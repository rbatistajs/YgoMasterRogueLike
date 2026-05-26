using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YgoMasterClient
{
    // Node detail popup via the game's native ActionSheetViewController (OpenCustomSheet). The card art
    // is injected into the sheet's TitleArea (see InjectTitleArt); the enemy LP / reward / modifiers go
    // in the message body. The only sheet entry is the action ("Duelar"/"Avançar"/"Fechar").
    static unsafe class RoguelikeNodeAction
    {
        const float BaseDimFactor = 0.45f;   // dim the original Base overlay (post-clone) so the art reads through

        static int _openNodeId = -1;
        static int _actionIndex = -1;
        static string _pendingIcon;          // icon of the node being shown (for the deferred title-art inject)
        static readonly Action<IntPtr, int> _onSelect = OnSelect;
        static readonly Action<IntPtr> _injectArt = InjectTitleArt;

        static IntPtr _imageType, _rectMask2DType, _aspectFitterType;
        static IL2Property _imageSprite, _graphicColor, _anchorMin, _anchorMax, _pivot, _sizeDelta, _anchoredPos, _aspectMode, _aspectRatio;
        static IL2Method _texW, _texH;

        static RoguelikeNodeAction()
        {
            try
            {
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _pivot = rect.GetProperty("pivot");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _anchoredPos = rect.GetProperty("anchoredPosition");
                IL2Class image = ui.GetClass("Image", "UnityEngine.UI");
                _imageType = image.IL2Typeof();
                _imageSprite = image.GetProperty("sprite");
                _graphicColor = ui.GetClass("Graphic", "UnityEngine.UI").GetProperty("color");
                _rectMask2DType = ui.GetClass("RectMask2D", "UnityEngine.UI").IL2Typeof();
                IL2Class arf = ui.GetClass("AspectRatioFitter", "UnityEngine.UI");
                _aspectFitterType = arf.IL2Typeof();
                _aspectMode = arf.GetProperty("aspectMode");
                _aspectRatio = arf.GetProperty("aspectRatio");
                IL2Class tex = core.GetClass("Texture", "UnityEngine");
                _texW = tex.GetProperty("width").GetGetMethod();
                _texH = tex.GetProperty("height").GetGetMethod();
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] node action init EX: " + ex); }
        }

        public static void Open(int nodeId)
        {
            try
            {
                RoguelikeApi.MapNode node = FindNode(nodeId);
                if (node == null) return;
                _openNodeId = nodeId;
                _pendingIcon = node.IconImage;

                // LP / reward / modifiers go in the message body; the card art goes in TitleArea.Base.
                List<string> info = StatLines(node);
                info.AddRange(ModLines(node));
                string message = info.Count > 0 ? string.Join("\n", info.ToArray()) : "";

                bool reachable = IsReachable(nodeId);
                string action = IsCombat(node.Type) ? RoguelikeLabels.Get("node.action.duel", "Duelar") : RoguelikeLabels.Get("node.action.move", "Avançar");
                string[] entries = reachable ? new[] { action } : new[] { RoguelikeLabels.Get("node.action.close", "Fechar") };
                _actionIndex = reachable ? 0 : -1;

                // Node detail is always dismissible (cancel + tap-outside); only the M5 action prompt is modal.
                YgomGame.Menu.ActionSheetViewController.OpenCustomSheet(BuildTitle(node), message, entries, _onSelect, false, _injectArt);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] node action Open EX: " + ex); }
        }

        // Clone the title bar's Base twice: the outer clone is the mask (inherits Base's full-width rect
        // and clips), the inner clone reuses Base's own Image with the card sprite swapped in. The inner
        // art fills the width via WidthControlsHeight (keeps the sprite's aspect), centered vertically, so
        // the vertical overflow is cropped by the mask. Placed before Base so Base's frame stays on top.
        static void InjectTitleArt(IntPtr titleArea)
        {
            try
            {
                IntPtr baseGo = GameObject.FindGameObjectByPath(titleArea, "Base");
                if (baseGo == IntPtr.Zero) return;
                float aspect;
                IntPtr sprite = BuildSprite(_pendingIcon, out aspect);
                if (sprite == IntPtr.Zero) return;

                IntPtr mask = UnityObject.Instantiate(baseGo, GameObject.GetTransform(titleArea));
                UnityObject.SetName(mask, "RgTitleArt");
                GameObject.SetActive(mask, true);
                GameObject.AddComponent(mask, _rectMask2DType); // clip the art to the title bar
                int baseIdx = Transform.GetSiblingIndex(GameObject.GetTransform(baseGo));
                Transform.SetSiblingIndex(GameObject.GetTransform(mask), baseIdx); // Base renders above the art

                IntPtr art = UnityObject.Instantiate(baseGo, GameObject.GetTransform(mask));
                UnityObject.SetName(art, "Art");
                GameObject.SetActive(art, true);
                IntPtr img = GameObject.GetComponent(art, _imageType);
                if (img != IntPtr.Zero)
                {
                    _imageSprite.GetSetMethod().Invoke(img, new IntPtr[] { sprite });
                    if (_graphicColor != null) { RoguelikeMapScreen.Col white = new RoguelikeMapScreen.Col { r = 1f, g = 1f, b = 1f, a = 1f }; _graphicColor.GetSetMethod().Invoke(img, new IntPtr[] { new IntPtr(&white) }); }
                }
                IntPtr fitter = GameObject.AddComponent(art, _aspectFitterType);
                float ratio = aspect; _aspectRatio.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&ratio) });
                int mode = 1; _aspectMode.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&mode) }); // WidthControlsHeight
                IntPtr at = GameObject.GetTransform(art);
                SetVec(at, _anchorMin, new AssetHelper.Vector2(0f, 1f)); // full width, top-aligned
                SetVec(at, _anchorMax, new AssetHelper.Vector2(1f, 1f));
                SetVec(at, _pivot, new AssetHelper.Vector2(0.5f, 1f));
                SetVec(at, _anchoredPos, new AssetHelper.Vector2(0f, 0f));
                SetVec(at, _sizeDelta, new AssetHelper.Vector2(0f, 0f));

                // Fade the original Base overlay (it renders above the art) so the card reads through.
                IntPtr baseImg = GameObject.GetComponent(baseGo, _imageType);
                if (baseImg != IntPtr.Zero && _graphicColor != null)
                {
                    RoguelikeMapScreen.Col c = _graphicColor.GetGetMethod().Invoke(baseImg).GetValueRef<RoguelikeMapScreen.Col>();
                    c.a *= BaseDimFactor;
                    _graphicColor.GetSetMethod().Invoke(baseImg, new IntPtr[] { new IntPtr(&c) });
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] title art EX: " + ex); }
        }

        public static void Close() { }

        static void OnSelect(IntPtr thisPtr, int index)
        {
            try { if (index == _actionIndex && _openNodeId >= 0) RoguelikeApi.Move(_openNodeId); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] node action select EX: " + ex); }
        }

        // card_<cid> -> Texture2D -> Sprite (+ aspect from the texture); profile_<id> -> ProfileIcon sprite.
        static IntPtr BuildSprite(string iconImage, out float aspect)
        {
            aspect = 0.71f;
            if (string.IsNullOrEmpty(iconImage)) return IntPtr.Zero;
            if (iconImage.StartsWith("profile_"))
            {
                aspect = 1f;
                return RoguelikeMapScreen.FindIcon("ProfileIcon" + iconImage.Substring(8) + "_L");
            }
            IntPtr tex = RoguelikeMapScreen.ResolveArtTexture(iconImage);
            if (tex == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                int w = _texW.Invoke(tex).GetValueRef<int>();
                int h = _texH.Invoke(tex).GetValueRef<int>();
                if (h > 0) aspect = (float)w / h;
            }
            catch { }
            return AssetHelper.SpriteFromTexture(tex, "rg_node_art_" + iconImage);
        }

        // Enemy LP + reward: shown over the banner's top corners.
        static List<string> StatLines(RoguelikeApi.MapNode node)
        {
            List<string> rows = new List<string>();
            if (IsCombat(node.Type))
            {
                if (node.EnemyLp >= 0) rows.Add(RoguelikeLabels.Get("node.stat.enemyLp", "LP {0}", node.EnemyLp));
                if (node.Reward >= 0) rows.Add(RoguelikeLabels.Get("node.stat.reward", "Recompensa: {0}", node.Reward));
            }
            return rows;
        }

        // Active modifiers (enemy/player): shown as the text line under the title.
        static List<string> ModLines(RoguelikeApi.MapNode node)
        {
            List<string> rows = new List<string>();
            if (node.Modifiers != null)
                foreach (string side in new[] { "enemy", "player" })
                {
                    Dictionary<string, object> s = node.Modifiers.ContainsKey(side) ? node.Modifiers[side] as Dictionary<string, object> : null;
                    if (s == null) continue;
                    foreach (string key in new[] { "extraLp", "extraHand", "monsters", "spellTraps", "hand" })
                    {
                        if (!s.ContainsKey(key)) continue;
                        int v; try { v = Convert.ToInt32(s[key]); } catch { continue; }
                        if (v == 0) continue;
                        rows.Add(RoguelikeLabels.Get("node.mod." + side + "." + key, side + " " + key + " {0}", v));
                    }
                }
            return rows;
        }

        static string BuildTitle(RoguelikeApi.MapNode node)
        {
            string type = RoguelikeLabels.Get("node.type." + node.Type, node.Type);
            return string.IsNullOrEmpty(node.Name) ? type : type + " · " + node.Name;
        }

        static void SetVec(IntPtr t, IL2Property prop, AssetHelper.Vector2 v) { prop.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&v) }); }

        static RoguelikeApi.MapNode FindNode(int id)
        {
            foreach (RoguelikeApi.MapNode n in RoguelikeApi.GetMapNodes()) if (n.Id == id) return n;
            return null;
        }

        // Reachable (and not the current spot): row-0 nodes from the entry, else a Next of the current node.
        static bool IsReachable(int id)
        {
            int pos = RoguelikeApi.Position();
            if (id == pos) return false;
            List<RoguelikeApi.MapNode> nodes = RoguelikeApi.GetMapNodes();
            if (pos < 0)
            {
                foreach (RoguelikeApi.MapNode n in nodes) if (n.Id == id) return n.Row == 0;
                return false;
            }
            foreach (RoguelikeApi.MapNode n in nodes) if (n.Id == pos) return n.Next.Contains(id);
            return false;
        }

        static bool IsCombat(string t) { return t == "duel" || t == "elite" || t == "boss"; }
    }
}
