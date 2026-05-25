using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace YgoMasterClient
{
    // Node detail popup via the game's native ActionSheetViewController. The embed object (passed to
    // OpenWithEmbedObject -> EmbedObjectArea) is a vertical stack: the active modifiers as a text line
    // right under the title, then a full-width card-art banner with the enemy LP / reward overlaid on
    // its top corners. The only sheet entry is the action ("Duelar"/"Avançar").
    static unsafe class RoguelikeNodeAction
    {
        const float ArtHeight = 420f;
        const float StatW = 240f;
        const float StatH = 60f;
        const float StatPad = 16f;
        const float ModFontSize = 28f;
        const float ModLineH = 38f;
        const float ModTextPad = 8f;
        const float Spacing = 10f;
        const string HeaderButtonPath =
            "DeckSelectUI(Clone).Root.Window.TitleSafeArea.HeaderButtonArea.HeaderButtonGroup.ButtonStructureDeckView";

        static int _openNodeId = -1;
        static int _actionIndex = -1;
        static readonly Action<IntPtr, int> _onSelect = OnSelect;

        static IntPtr _rectType, _imageType, _rectMask2DType, _aspectFitterType, _layoutElementType, _vlgType, _selButtonType, _bindingTextType, _tmpType, _colorContainerType;
        static IL2Property _imageSprite, _anchorMin, _anchorMax, _pivot, _sizeDelta, _anchoredPos, _aspectMode, _aspectRatio,
            _leMinHeight, _lePrefHeight, _leFlexWidth, _vlgSpacing, _vlgForceW, _vlgForceH, _vlgControlW, _vlgControlH,
            _tmpFontSize, _tmpColor, _tmpHAlign, _behaviourEnabled;
        static IL2Field _selOnClick;
        static IL2Method _texW, _texH, _ueRemoveAll;

        static RoguelikeNodeAction()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _pivot = rect.GetProperty("pivot");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _anchoredPos = rect.GetProperty("anchoredPosition");
                IL2Class image = ui.GetClass("Image", "UnityEngine.UI");
                _imageType = image.IL2Typeof();
                _imageSprite = image.GetProperty("sprite");
                _rectMask2DType = ui.GetClass("RectMask2D", "UnityEngine.UI").IL2Typeof();
                IL2Class arf = ui.GetClass("AspectRatioFitter", "UnityEngine.UI");
                _aspectFitterType = arf.IL2Typeof();
                _aspectMode = arf.GetProperty("aspectMode");
                _aspectRatio = arf.GetProperty("aspectRatio");
                IL2Class le = ui.GetClass("LayoutElement", "UnityEngine.UI");
                _layoutElementType = le.IL2Typeof();
                _leMinHeight = le.GetProperty("minHeight");
                _lePrefHeight = le.GetProperty("preferredHeight");
                _leFlexWidth = le.GetProperty("flexibleWidth");
                _vlgType = ui.GetClass("VerticalLayoutGroup", "UnityEngine.UI").IL2Typeof();
                IL2Class hov = ui.GetClass("HorizontalOrVerticalLayoutGroup", "UnityEngine.UI");
                _vlgSpacing = hov.GetProperty("spacing");
                _vlgForceW = hov.GetProperty("childForceExpandWidth");
                _vlgForceH = hov.GetProperty("childForceExpandHeight");
                _vlgControlW = hov.GetProperty("childControlWidth");
                _vlgControlH = hov.GetProperty("childControlHeight");
                IL2Class selBtn = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selButtonType = selBtn.IL2Typeof();
                _selOnClick = selBtn.GetField("onClick");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                _colorContainerType = asm.GetClass("ColorContainerGraphic", "YgomSystem.UI").IL2Typeof();
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                IL2Class tmpText = Assembler.GetAssembly("Unity.TextMeshPro").GetClass("TMP_Text", "TMPro");
                _tmpFontSize = tmpText.GetProperty("fontSize");
                _tmpColor = tmpText.GetProperty("color"); // TMP override of Graphic.color (updates the mesh)
                _tmpHAlign = tmpText.GetProperty("horizontalAlignment"); // HorizontalAlignmentOptions: Left=1
                _behaviourEnabled = core.GetClass("Behaviour", "UnityEngine").GetProperty("enabled");
                _ueRemoveAll = core.GetClass("UnityEventBase", "UnityEngine.Events").GetMethod("RemoveAllListeners");
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

                List<string> stats = StatLines(node);
                List<string> mods = ModLines(node);
                bool reachable = IsReachable(nodeId);
                string[] entries = reachable
                    ? new[] { IsCombat(node.Type) ? RoguelikeLabels.Get("node.action.duel", "Duelar") : RoguelikeLabels.Get("node.action.move", "Avançar") }
                    : new string[0];
                _actionIndex = reachable ? 0 : -1;

                IntPtr embed = BuildEmbed(node.IconImage, stats, mods);
                if (embed != IntPtr.Zero)
                    YgomGame.Menu.ActionSheetViewController.OpenWithEmbedObject(BuildTitle(node), embed, entries, _onSelect);
                else // no art -> fall back to info as plain entries
                {
                    List<string> all = new List<string>(stats); all.AddRange(mods);
                    if (reachable) { _actionIndex = all.Count; all.Add(entries[0]); }
                    YgomGame.Menu.ActionSheetViewController.Open(BuildTitle(node), all.ToArray(), _onSelect);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] node action Open EX: " + ex); }
        }

        public static void Close() { }

        static void OnSelect(IntPtr thisPtr, int index)
        {
            try { if (index == _actionIndex && _openNodeId >= 0) RoguelikeApi.Move(_openNodeId); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] node action select EX: " + ex); }
        }

        // Vertical stack embed: modifier text (under the title) + art banner (with LP/reward overlay).
        static IntPtr BuildEmbed(string iconImage, List<string> stats, List<string> mods)
        {
            float aspect;
            IntPtr sprite = BuildSprite(iconImage, out aspect);
            if (sprite == IntPtr.Zero) return IntPtr.Zero;
            IntPtr template = FindHeaderButton();

            IntPtr embed = GameObject.New();
            UnityObject.SetName(embed, "RgNodeArt");
            GameObject.AddComponent(embed, _rectType);
            IntPtr vlg = GameObject.AddComponent(embed, _vlgType);
            SetFloat(_vlgSpacing, vlg, Spacing);
            SetBool(_vlgForceW, vlg, true);
            SetBool(_vlgForceH, vlg, false);
            SetBool(_vlgControlW, vlg, true);
            SetBool(_vlgControlH, vlg, true);
            bool hasMods = template != IntPtr.Zero && mods.Count > 0;
            float modTextH = hasMods ? mods.Count * ModLineH + ModTextPad : 0;
            float total = ArtHeight + (hasMods ? modTextH + Spacing : 0);
            float flex = 1f;
            IntPtr ele = GameObject.AddComponent(embed, _layoutElementType);
            _lePrefHeight.GetSetMethod().Invoke(ele, new IntPtr[] { new IntPtr(&total) });
            _leMinHeight.GetSetMethod().Invoke(ele, new IntPtr[] { new IntPtr(&total) });
            _leFlexWidth.GetSetMethod().Invoke(ele, new IntPtr[] { new IntPtr(&flex) });

            BuildBanner(embed, sprite, aspect, template, stats);
            if (hasMods) BuildModText(embed, template, mods); // below the banner
            return embed;
        }

        // Modifiers as a single centered multi-line text, cloned from the header button's TMP label.
        static void BuildModText(IntPtr embed, IntPtr template, List<string> mods)
        {
            IntPtr src = GameObject.FindGameObjectByPath(template, "ImageOn.TextTMP");
            if (src == IntPtr.Zero) return;
            IntPtr o = UnityObject.Instantiate(src, GameObject.GetTransform(embed));
            UnityObject.SetName(o, "RgModText");
            GameObject.SetActive(o, true);
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero && _behaviourEnabled != null) { csbool off = false; _behaviourEnabled.GetSetMethod().Invoke(binding, new IntPtr[] { new IntPtr(&off) }); }
            DisableColorContainers(o); // stop the theme palette from forcing the text color
            IntPtr tmp = GameObject.GetComponent(o, _tmpType);
            if (tmp != IntPtr.Zero)
            {
                if (_tmpFontSize != null) SetFloat(_tmpFontSize, tmp, ModFontSize);
                if (_tmpColor != null) { RoguelikeMapScreen.Col white = new RoguelikeMapScreen.Col { r = 1f, g = 1f, b = 1f, a = 1f }; _tmpColor.GetSetMethod().Invoke(tmp, new IntPtr[] { new IntPtr(&white) }); }
                if (_tmpHAlign != null) { int left = 1; _tmpHAlign.GetSetMethod().Invoke(tmp, new IntPtr[] { new IntPtr(&left) }); }
                TMPro.TMP_Text.SetText(tmp, "• " + string.Join("\n• ", mods.ToArray()));
            }
            IntPtr le = GameObject.GetComponent(o, _layoutElementType);
            if (le == IntPtr.Zero) le = GameObject.AddComponent(o, _layoutElementType);
            float h = mods.Count * ModLineH + ModTextPad, flex = 1f;
            _lePrefHeight.GetSetMethod().Invoke(le, new IntPtr[] { new IntPtr(&h) });
            _leMinHeight.GetSetMethod().Invoke(le, new IntPtr[] { new IntPtr(&h) });
            _leFlexWidth.GetSetMethod().Invoke(le, new IntPtr[] { new IntPtr(&flex) });
        }

        static void BuildBanner(IntPtr embed, IntPtr sprite, float aspect, IntPtr template, List<string> stats)
        {
            IntPtr banner = GameObject.New();
            UnityObject.SetName(banner, "Banner");
            GameObject.AddComponent(banner, _rectType);
            GameObject.AddComponent(banner, _rectMask2DType);
            IntPtr ble = GameObject.AddComponent(banner, _layoutElementType);
            float h = ArtHeight;
            _lePrefHeight.GetSetMethod().Invoke(ble, new IntPtr[] { new IntPtr(&h) });
            _leMinHeight.GetSetMethod().Invoke(ble, new IntPtr[] { new IntPtr(&h) });
            Transform.SetParent(GameObject.GetTransform(banner), GameObject.GetTransform(embed));

            IntPtr art = GameObject.New();
            UnityObject.SetName(art, "Art");
            GameObject.AddComponent(art, _rectType);
            IntPtr img = GameObject.AddComponent(art, _imageType);
            _imageSprite.GetSetMethod().Invoke(img, new IntPtr[] { sprite });
            IntPtr fitter = GameObject.AddComponent(art, _aspectFitterType);
            float ratio = aspect; _aspectRatio.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&ratio) });
            int mode = 4; _aspectMode.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&mode) }); // EnvelopeParent
            Transform.SetParent(GameObject.GetTransform(art), GameObject.GetTransform(banner));
            IntPtr at = GameObject.GetTransform(art);
            SetVec(at, _anchorMin, new AssetHelper.Vector2(0.5f, 1f)); // top-aligned crop
            SetVec(at, _anchorMax, new AssetHelper.Vector2(0.5f, 1f));
            SetVec(at, _pivot, new AssetHelper.Vector2(0.5f, 1f));

            // LP / reward as small buttons pinned to the top corners, over the art.
            if (template != IntPtr.Zero && stats != null)
                for (int i = 0; i < stats.Count; i++) BuildStat(banner, template, stats[i], i == 0);
        }

        // A small label button anchored to a top corner of the banner (left for i==0, right otherwise).
        static void BuildStat(IntPtr banner, IntPtr template, string text, bool left)
        {
            IntPtr s = CloneLabel(banner, template, text, "RgStat");
            IntPtr t = GameObject.GetTransform(s);
            AssetHelper.Vector2 corner = left ? new AssetHelper.Vector2(0f, 1f) : new AssetHelper.Vector2(1f, 1f);
            SetVec(t, _anchorMin, corner);
            SetVec(t, _anchorMax, corner);
            SetVec(t, _pivot, corner);
            SetVec(t, _sizeDelta, new AssetHelper.Vector2(StatW, StatH));
            SetVec(t, _anchoredPos, new AssetHelper.Vector2(left ? StatPad : -StatPad, -StatPad));
        }

        // Clone the header button as a non-clickable label (text on both on/off states, no shortcut icon).
        static IntPtr CloneLabel(IntPtr parent, IntPtr template, string text, string name)
        {
            IntPtr o = UnityObject.Instantiate(template, GameObject.GetTransform(parent));
            UnityObject.SetName(o, name);
            GameObject.SetActive(o, true);
            IntPtr sel = GameObject.GetComponent(o, _selButtonType);
            if (sel != IntPtr.Zero)
            {
                IL2Object onClick = _selOnClick.GetValue(sel);
                if (onClick != null && _ueRemoveAll != null) _ueRemoveAll.Invoke(onClick.ptr);
            }
            SetRowText(o, "ImageOn", text); HideChild(o, "ImageOn.ShortcutIconGroupOn");
            SetRowText(o, "ImageOff", text); HideChild(o, "ImageOff.ShortcutIconGroupOff");
            return o;
        }

        // The TextTMP has a BindingTextMeshProUGUI that re-applies its own text on enable; disable it
        // and set the TMP text directly so our label sticks.
        static void SetRowText(IntPtr row, string state, string text)
        {
            IntPtr t = GameObject.FindGameObjectByPath(row, state + ".TextTMP");
            if (t == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(t, _bindingTextType);
            if (binding != IntPtr.Zero && _behaviourEnabled != null) { csbool off = false; _behaviourEnabled.GetSetMethod().Invoke(binding, new IntPtr[] { new IntPtr(&off) }); }
            IntPtr tmp = GameObject.GetComponent(t, _tmpType);
            if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, text);
        }

        static void HideChild(IntPtr root, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        // Disable every ColorContainerGraphic on a GameObject so our Graphic.color sticks.
        static void DisableColorContainers(IntPtr go)
        {
            if (go == IntPtr.Zero || _colorContainerType == IntPtr.Zero || _behaviourEnabled == null) return;
            IntPtr[] comps = GameObject.GetComponents(go, _colorContainerType);
            if (comps == null) return;
            csbool off = false;
            foreach (IntPtr c in comps) _behaviourEnabled.GetSetMethod().Invoke(c, new IntPtr[] { new IntPtr(&off) });
        }

        static IntPtr FindHeaderButton()
        {
            IntPtr root = RoguelikeMapScreen.Root();
            return root == IntPtr.Zero ? IntPtr.Zero : GameObject.FindGameObjectByPath(root, HeaderButtonPath);
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
        static void SetFloat(IL2Property prop, IntPtr o, float v) { prop.GetSetMethod().Invoke(o, new IntPtr[] { new IntPtr(&v) }); }
        static void SetBool(IL2Property prop, IntPtr o, bool b) { csbool v = b; prop.GetSetMethod().Invoke(o, new IntPtr[] { new IntPtr(&v) }); }

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
