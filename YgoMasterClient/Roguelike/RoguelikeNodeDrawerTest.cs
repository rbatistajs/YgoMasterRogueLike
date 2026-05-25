using IL2CPP;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace YgoMasterClient
{
    // TEST / WIP: node detail as a native side menu (reuses the game's HomeSubMenu). Kept for
    // reference; not wired to map clicks (the active drawer is the ActionSheet-based RoguelikeNodeDrawer).
    // Only OnHomeSubMenuCreated is still invoked from the HomeSubMenuViewController hook, and it no-ops
    // unless Open() was called (which nothing does now).
    static unsafe class RoguelikeNodeDrawerTest
    {
        static int _pendingNodeId = -1;
        static int _actionNodeId = -1;
        static readonly Action _onAction = OnActionClick;

        static IL2Method _baseOnCreatedView;
        static IL2Method _layoutRebuild;
        static IL2Field _scrollViewField;
        static IntPtr _rectType, _imageType, _rawImageType, _graphicType, _layoutElementType;
        static IL2Property _anchorMin, _anchorMax, _pivot, _sizeDelta, _offsetMin, _offsetMax, _anchoredPos3D, _localEuler, _graphicColor, _imageSprite, _rawTexture, _leMinHeight, _lePreferredHeight;
        static IntPtr _tmpType;

        static RoguelikeNodeDrawerTest()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class sub = asm.GetClass("SubMenuViewController", "YgomGame.SubMenu");
                _baseOnCreatedView = sub.GetMethod("OnCreatedView");
                _scrollViewField = sub.GetField("m_InfinityScrollView");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _pivot = rect.GetProperty("pivot");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _anchoredPos3D = rect.GetProperty("anchoredPosition3D");
                _localEuler = core.GetClass("Transform", "UnityEngine").GetProperty("localEulerAngles"); // base class prop
                _imageType = ui.GetClass("Image", "UnityEngine.UI").IL2Typeof();
                _imageSprite = ui.GetClass("Image", "UnityEngine.UI").GetProperty("sprite");
                IL2Class rawImg = ui.GetClass("RawImage", "UnityEngine.UI");
                _rawImageType = rawImg.IL2Typeof();
                _rawTexture = rawImg.GetProperty("texture");
                IL2Class graphic = ui.GetClass("Graphic", "UnityEngine.UI");
                _graphicType = graphic.IL2Typeof();
                _graphicColor = graphic.GetProperty("color");
                IL2Class le = ui.GetClass("LayoutElement", "UnityEngine.UI");
                _layoutElementType = le.IL2Typeof();
                _leMinHeight = le.GetProperty("minHeight");
                _lePreferredHeight = le.GetProperty("preferredHeight");
                _layoutRebuild = ui.GetClass("LayoutRebuilder", "UnityEngine.UI").GetMethod("ForceRebuildLayoutImmediate");
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest init EX: " + ex); }
        }

        public static void Open(int nodeId)
        {
            try
            {
                if (FindNode(nodeId) == null) return;
                IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
                if (manager == IntPtr.Zero) return;
                _pendingNodeId = nodeId;
                YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "Home/HomeSubMenu");
            }
            catch (Exception ex) { _pendingNodeId = -1; Console.WriteLine("[Roguelike] drawerTest Open EX: " + ex); }
        }

        public static void Close() { }

        // Piggyback from HomeSubMenuViewController.OnCreatedView. Returns true when it built the
        // roguelike node detail (caller must then skip the home items + Original).
        public static bool OnHomeSubMenuCreated(IntPtr thisPtr)
        {
            if (_pendingNodeId < 0) return false;
            int nodeId = _pendingNodeId; _pendingNodeId = -1;
            try
            {
                RoguelikeApi.MapNode node = FindNode(nodeId);
                if (node != null)
                {
                    YgomGame.SubMenu.SubMenuViewController.SetTitleText(thisPtr, BuildTitle(node));
                    if (IsReachable(nodeId))
                    {
                        _actionNodeId = nodeId;
                        string label = IsCombat(node.Type)
                            ? RoguelikeLabels.Get("node.action.duel", "Duelar")
                            : RoguelikeLabels.Get("node.action.move", "Avançar");
                        YgomGame.SubMenu.SubMenuViewController.AddMenuItem(thisPtr, label, _onAction);
                    }

                    if (_baseOnCreatedView != null) _baseOnCreatedView.Invoke(thisPtr); // setup/animation binds the scroll view

                    IntPtr panel = FindPanel(thisPtr); // read AFTER base (scroll view now bound)
                    if (panel != IntPtr.Zero) BuildCardBlock(panel, node);
                    else
                        foreach (string line in InfoLines(node)) // fallback
                            YgomGame.SubMenu.SubMenuViewController.AddMenuItem(thisPtr, line, NoOp);
                }
                else if (_baseOnCreatedView != null) _baseOnCreatedView.Invoke(thisPtr);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest build EX: " + ex); }
            return true;
        }

        static void NoOp() { }

        static void OnActionClick()
        {
            int id = _actionNodeId; _actionNodeId = -1;
            PopMenu();
            try { if (id >= 0) RoguelikeApi.Move(id); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest action EX: " + ex); }
        }

        static void PopMenu()
        {
            try
            {
                IntPtr m = YgomGame.Menu.ContentViewControllerManager.GetManager();
                if (m != IntPtr.Zero) YgomSystem.UI.ViewControllerManager.PopChildViewController(m);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest pop EX: " + ex); }
        }

        static IntPtr FindPanel(IntPtr thisPtr)
        {
            try
            {
                if (_scrollViewField == null) return IntPtr.Zero;
                IL2Object sv = _scrollViewField.GetValue(thisPtr);
                if (sv == null || sv.ptr == IntPtr.Zero) return IntPtr.Zero;
                IntPtr svGo = Component.GetGameObject(sv.ptr);
                if (svGo == IntPtr.Zero) return IntPtr.Zero;
                if (UnityObject.GetName(svGo) == "Content") return svGo;
                IntPtr content = GameObject.FindGameObjectByName(svGo, "Content", false, true);
                if (content == IntPtr.Zero)
                {
                    IntPtr parent = Transform.GetParent(GameObject.GetTransform(svGo));
                    if (parent != IntPtr.Zero) content = GameObject.FindGameObjectByName(Component.GetGameObject(parent), "Content", false, true);
                }
                return content;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest FindPanel EX: " + ex); return IntPtr.Zero; }
        }

        static void BuildCardBlock(IntPtr panel, RoguelikeApi.MapNode node)
        {
            try
            {
                IntPtr existing = GameObject.FindGameObjectByName(panel, "RgCardBlock", false, false);
                if (existing != IntPtr.Zero) UnityObject.Destroy(existing);

                IntPtr block = GameObject.New();
                UnityObject.SetName(block, "RgCardBlock");
                GameObject.AddComponent(block, _rectType);
                Transform.SetParent(GameObject.GetTransform(block), GameObject.GetTransform(panel));
                IntPtr bt = GameObject.GetTransform(block);
                ResetXform(bt);
                SetVec(bt, _anchorMin, new AssetHelper.Vector2(0, 1));
                SetVec(bt, _anchorMax, new AssetHelper.Vector2(0, 1));
                SetVec(bt, _pivot, new AssetHelper.Vector2(0.5f, 0.5f));
                SetVec(bt, _sizeDelta, new AssetHelper.Vector2(540, 260));
                Vector3 bpos = new Vector3(320, -210, 0);
                _anchoredPos3D.GetSetMethod().Invoke(bt, new IntPtr[] { new IntPtr(&bpos) });
                Transform.SetSiblingIndex(bt, 1);

                if (!string.IsNullOrEmpty(node.IconImage))
                {
                    IntPtr art = GameObject.New();
                    UnityObject.SetName(art, "Art");
                    GameObject.AddComponent(art, _rectType);
                    Transform.SetParent(GameObject.GetTransform(art), bt);
                    IntPtr at = GameObject.GetTransform(art);
                    ResetXform(at);
                    SetVec(at, _anchorMin, new AssetHelper.Vector2(0, 0.5f));
                    SetVec(at, _anchorMax, new AssetHelper.Vector2(0, 0.5f));
                    SetVec(at, _pivot, new AssetHelper.Vector2(0, 0.5f));
                    SetVec(at, _sizeDelta, new AssetHelper.Vector2(230, 230));
                    Vector3 ap = new Vector3(16, 0, 0);
                    _anchoredPos3D.GetSetMethod().Invoke(at, new IntPtr[] { new IntPtr(&ap) });
                    if (node.IconImage.StartsWith("profile_"))
                    {
                        IntPtr sp = RoguelikeMapScreen.FindIcon("ProfileIcon" + node.IconImage.Substring(8) + "_L");
                        if (sp != IntPtr.Zero) { IntPtr img = GameObject.AddComponent(art, _imageType); _imageSprite.GetSetMethod().Invoke(img, new IntPtr[] { sp }); }
                    }
                    else
                    {
                        IntPtr tex = RoguelikeMapScreen.ResolveArtTexture(node.IconImage);
                        if (tex != IntPtr.Zero) { IntPtr ri = GameObject.AddComponent(art, _rawImageType); _rawTexture.GetSetMethod().Invoke(ri, new IntPtr[] { tex }); }
                    }
                }

                IntPtr tmpl = RoguelikeMapScreen.LabelTemplate();
                if (tmpl != IntPtr.Zero)
                {
                    IntPtr lbl = UnityObject.Instantiate(tmpl, bt);
                    UnityObject.SetName(lbl, "Info");
                    GameObject.SetActive(lbl, true);
                    IntPtr lt = GameObject.GetTransform(lbl);
                    ResetXform(lt);
                    SetVec(lt, _anchorMin, new AssetHelper.Vector2(0, 0));
                    SetVec(lt, _anchorMax, new AssetHelper.Vector2(1, 1));
                    SetVec(lt, _offsetMin, new AssetHelper.Vector2(262, 10));
                    SetVec(lt, _offsetMax, new AssetHelper.Vector2(-10, -10));
                    IntPtr tmp = GameObject.GetComponent(lbl, _tmpType);
                    if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, string.Join("\n", InfoLines(node).ToArray()));
                }

                ForceRebuild(panel);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest card EX: " + ex); }
        }

        static List<string> InfoLines(RoguelikeApi.MapNode node)
        {
            List<string> lines = new List<string>();
            if (IsCombat(node.Type))
            {
                if (node.EnemyLp >= 0) lines.Add(RoguelikeLabels.Get("node.stat.enemyLp", "LP do inimigo: {0}", node.EnemyLp));
                if (node.Reward >= 0) lines.Add(RoguelikeLabels.Get("node.stat.reward", "Recompensa: {0}", node.Reward));
            }
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
                        lines.Add(RoguelikeLabels.Get("node.mod." + side + "." + key, side + " " + key + " {0}", v));
                    }
                }
            return lines;
        }

        static void SetVec(IntPtr t, IL2Property prop, AssetHelper.Vector2 v)
        {
            prop.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&v) });
        }

        static void ResetXform(IntPtr t)
        {
            Transform.SetLocalScale(t, new Vector3(1, 1, 1));
            Vector3 z = new Vector3(0, 0, 0);
            if (_localEuler != null) _localEuler.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&z) });
        }

        static void ForceRebuild(IntPtr content)
        {
            try
            {
                if (_layoutRebuild == null) return;
                IntPtr rt = GameObject.GetComponent(content, _rectType);
                if (rt != IntPtr.Zero) _layoutRebuild.Invoke(new IntPtr[] { rt });
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] drawerTest ForceRebuild EX: " + ex); }
        }

        static string BuildTitle(RoguelikeApi.MapNode node)
        {
            string type = RoguelikeLabels.Get("node.type." + node.Type, node.Type);
            return string.IsNullOrEmpty(node.Name) ? type : type + " · " + node.Name;
        }

        static RoguelikeApi.MapNode FindNode(int id)
        {
            foreach (RoguelikeApi.MapNode n in RoguelikeApi.GetMapNodes()) if (n.Id == id) return n;
            return null;
        }

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
