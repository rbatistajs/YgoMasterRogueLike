using IL2CPP;
using System;
using UnityEngine;

namespace YgoMasterClient
{
    // Roguelike deck-select screen. Reuses the game's Solo/SoloMode screen (animated bg +
    // header) and repurposes SoloPortal's RecommendGroup tiles as the deck choices. The
    // OnCreatedView hook only customizes when _pending is set by Open() — normal Solo mode
    // is untouched.
    static unsafe class RoguelikeDeckSelectScreen
    {
        const string PortalRoot = "SoloPortalUI(Clone).Root";

        static IL2Class _portalClass;
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hook;
        static IntPtr _tmpType;
        static IntPtr _bindingTextType;
        static bool _pending;
        static bool _ready;

        static RoguelikeDeckSelectScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                _portalClass = asm.GetClass("SoloPortalViewController", "YgomGame.Solo");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, _portalClass.GetMethod("OnCreatedView"));
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckselect init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) { Console.WriteLine("[Roguelike] deckselect not ready"); return; }
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            _pending = true;
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "Solo/SoloMode");
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            _hook.Original(thisPtr);
            if (!_pending) return;
            _pending = false;
            try { Customize(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckselect customize EX: " + ex); }
        }

        static void Customize(IntPtr portalPtr)
        {
            IntPtr go = Component.GetGameObject(portalPtr);
            IntPtr root = GameObject.FindGameObjectByPath(go, PortalRoot);
            if (root == IntPtr.Zero) { Console.WriteLine("[Roguelike] portal root not found"); return; }

            SetText(root, "TitleSafeArea.TitleGroup.NameText", "Escolha seu Deck");
            Hide(root, "ButtonArea.MainGroup.LastPlayGroup");
            Hide(root, "ButtonArea.GateListGroup");
            Console.WriteLine("[Roguelike] deckselect customized");
        }

        static void Hide(IntPtr root, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        // Binding-aware: a BindingTextMeshProUGUI overwrites a plain TMP_Text.SetText, so set
        // the literal through the binding (renders verbatim) when present; else set TMP text.
        static void SetText(IntPtr root, string path, string text)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero) { YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(binding, text); return; }
            IntPtr tmp = GameObject.GetComponent(o, _tmpType);
            if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, text);
        }
    }
}
