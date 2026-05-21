using IL2CPP;
using System;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Unified roguelike screen on DeckEdit/DeckSelect (DeckSelectViewController2). Shows the
    // deck choice OR the map depending on run state, swapping content in-place (no flicker).
    // Reuses the DeckBox tile from the deck grid. Replaces the SoloMode-based screens.
    static unsafe class RoguelikeRunScreen
    {
        const string Ui = "DeckSelectUI(Clone).Root.Window";

        static IL2Class _vcClass;
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hook;
        static IntPtr _rectType;
        static IL2Property _anchorMin, _anchorMax, _offsetMin, _offsetMax;
        static IntPtr _content;
        static bool _pending, _ready;

        static RoguelikeRunScreen()
        {
            try
            {
                _vcClass = Assembler.GetAssembly("Assembly-CSharp").GetClass("DeckSelectViewController2", "YgomGame");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, _vcClass.GetMethod("OnCreatedView"));
                IL2Class rect = Assembler.GetAssembly("UnityEngine.CoreModule").GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] runscreen init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            _pending = true;
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "DeckEdit/DeckSelect");
        }

        // Re-render in place (called after choose_deck / move complete — the screen stays up).
        public static void Refresh()
        {
            if (_content != IntPtr.Zero) Render();
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            _hook.Original(thisPtr);
            if (!_pending) return;
            _pending = false;
            try { Customize(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] runscreen customize EX: " + ex); }
        }

        static void Customize(IntPtr vcPtr)
        {
            IntPtr go = Component.GetGameObject(vcPtr);
            Hide(go, "DeckOverview2(Clone)");
            Hide(go, Ui + ".MainArea.DeckArea.DeckGroup");
            Hide(go, Ui + ".MainArea.FooterArea");

            IntPtr main = GameObject.FindGameObjectByPath(go, Ui + ".MainArea");
            if (main == IntPtr.Zero) { Console.WriteLine("[Roguelike] runscreen MainArea not found"); return; }

            IntPtr content = GameObject.New();
            UnityObject.SetName(content, "RgContent");
            GameObject.AddComponent(content, _rectType);
            IntPtr ct = GameObject.GetTransform(content);
            Transform.SetParent(ct, GameObject.GetTransform(main));
            SetFull(ct);
            Transform.SetAsLastSibling(ct);
            Transform.SetLocalScale(ct, new Vector3(1, 1, 1));
            _content = content;
            Render();
            Console.WriteLine("[Roguelike] runscreen customized");
        }

        static void Render()
        {
            bool chosen = RoguelikeApi.IsDeckChosen();
            Console.WriteLine("[Roguelike] runscreen mode=" + (chosen ? "map" : "choice"));
            // deck-choice / map content built in the next steps.
        }

        static void Hide(IntPtr go, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(go, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        static void SetFull(IntPtr t)
        {
            AssetHelper.Vector2 min = new AssetHelper.Vector2(0, 0), max = new AssetHelper.Vector2(1, 1), zero = new AssetHelper.Vector2(0, 0);
            _anchorMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&min) });
            _anchorMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&max) });
            _offsetMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&zero) });
            _offsetMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&zero) });
        }
    }
}
