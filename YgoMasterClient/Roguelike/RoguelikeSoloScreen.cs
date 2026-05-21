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
        static IntPtr _soloModeType;
        static bool _ready;

        static RoguelikeSoloScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class portal = asm.GetClass("SoloPortalViewController", "YgomGame.Solo");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, portal.GetMethod("OnCreatedView"));
                _soloModeType = asm.GetClass("SoloModeViewController", "YgomGame.Solo").IL2Typeof();
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] soloscreen init EX: " + ex); }
        }

        // Pop the SoloMode VC (which takes its child SoloPortal with it) so the stack returns
        // to Home. Popping only SoloPortal leaves an orphan SoloMode.
        public static void Close()
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            IntPtr soloMode = YgomSystem.UI.ViewControllerManager.GetViewController(manager, _soloModeType);
            if (soloMode != IntPtr.Zero) YgomSystem.UI.ViewControllerManager.PopChildViewController(manager, soloMode);
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
