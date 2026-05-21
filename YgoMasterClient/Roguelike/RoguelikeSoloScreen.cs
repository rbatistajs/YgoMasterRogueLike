using IL2CPP;
using System;
using UnityEngine;

namespace YgoMasterClient
{
    // Owns the single SoloPortalViewController.OnCreatedView hook (MinHook = one hook per
    // target). Open(customize) pushes Solo/SoloMode and runs `customize(portalPtr)` once when
    // the portal view is created. Both the deck-select and map screens reuse this.
    static unsafe class RoguelikeSoloScreen
    {
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hook;
        static Action<IntPtr> _pending;
        static bool _ready;

        static RoguelikeSoloScreen()
        {
            try
            {
                IL2Class portal = Assembler.GetAssembly("Assembly-CSharp").GetClass("SoloPortalViewController", "YgomGame.Solo");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, portal.GetMethod("OnCreatedView"));
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] soloscreen init EX: " + ex); }
        }

        public static void Open(Action<IntPtr> customize)
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            _pending = customize;
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "Solo/SoloMode");
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            _hook.Original(thisPtr);
            Action<IntPtr> c = _pending;
            _pending = null;
            if (c == null) return;
            try { c(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] soloscreen customize EX: " + ex); }
        }

        // The SoloPortal content root GameObject (under it: TitleSafeArea, ButtonArea, ...).
        public static IntPtr PortalRoot(IntPtr portalPtr)
        {
            return GameObject.FindGameObjectByPath(Component.GetGameObject(portalPtr), "SoloPortalUI(Clone).Root");
        }
    }
}
