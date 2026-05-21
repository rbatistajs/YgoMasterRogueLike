using IL2CPP;
using System;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Dev spike (throwaway): proves we can render our own content onto a pushed game
    // screen. Injects a full-screen background + a centered card image into the CURRENT
    // top view controller. Triggered by the `rgproof [cid]` console command (open the base
    // screen first, e.g. `rgpush Solo/SoloPortal`). Reuses core AssetHelper card loading;
    // RectTransform/Image setup mirrors Goat's SoloChapterCardImage (not reused).
    static unsafe class RoguelikeUiProof
    {
        static IntPtr _imageType;
        static IL2Method _imageSetSprite;
        static IL2Property _imageColor;
        static IL2Property _imagePreserveAspect;
        static IntPtr _rectType;
        static IL2Property _anchorMin, _anchorMax, _offsetMin, _offsetMax, _pivot, _sizeDelta, _anchoredPos3D;
        static bool _ready;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ProofColor { public float r, g, b, a; public ProofColor(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; } }

        static RoguelikeUiProof()
        {
            try
            {
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class image = ui.GetClass("Image", "UnityEngine.UI");
                _imageType = image.IL2Typeof();
                _imageSetSprite = image.GetProperty("sprite").GetSetMethod();
                _imagePreserveAspect = image.GetProperty("preserveAspect");
                // `color` lives on the base Graphic class, not Image.
                _imageColor = ui.GetClass("Graphic", "UnityEngine.UI").GetProperty("color");

                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _pivot = rect.GetProperty("pivot");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _anchoredPos3D = rect.GetProperty("anchoredPosition3D");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[rgproof] init EX: " + ex); }
        }

        public static void Run(int cid)
        {
            if (!_ready) { Console.WriteLine("[rgproof] not ready"); return; }
            try
            {
                IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
                if (manager == IntPtr.Zero) { Console.WriteLine("[rgproof] no manager"); return; }
                IntPtr top = YgomSystem.UI.ViewControllerManager.GetStackTopViewController(manager);
                if (top == IntPtr.Zero) { Console.WriteLine("[rgproof] no top VC"); return; }
                IntPtr vcGo = Component.GetGameObject(top);
                IntPtr vcTransform = GameObject.GetTransform(vcGo);

                IntPtr old = GameObject.FindGameObjectByName(vcGo, "RgProof", false, false);
                if (old != IntPtr.Zero) GameObject.SetActive(old, false);

                Vector3 zero = new Vector3(0, 0, 0);
                Vector3 one = new Vector3(1, 1, 1);

                IntPtr root = GameObject.New();
                UnityObject.SetName(root, "RgProof");
                GameObject.AddComponent(root, _rectType);
                IntPtr rootT = GameObject.GetTransform(root);
                Transform.SetParent(rootT, vcTransform);
                SetFull(rootT);
                Transform.SetAsLastSibling(rootT);
                Transform.SetLocalScale(rootT, one);
                _anchoredPos3D.GetSetMethod().Invoke(rootT, new IntPtr[] { new IntPtr(&zero) });

                // Full-screen dark background (covers the transparent base screen).
                IntPtr bg = GameObject.New();
                UnityObject.SetName(bg, "Bg");
                GameObject.AddComponent(bg, _rectType);
                IntPtr bgImg = GameObject.AddComponent(bg, _imageType);
                IntPtr bgT = GameObject.GetTransform(bg);
                Transform.SetParent(bgT, rootT);
                SetFull(bgT);
                Transform.SetLocalScale(bgT, one);
                _anchoredPos3D.GetSetMethod().Invoke(bgT, new IntPtr[] { new IntPtr(&zero) });
                ProofColor dark = new ProofColor(0.05f, 0.06f, 0.10f, 0.95f);
                _imageColor.GetSetMethod().Invoke(bgImg, new IntPtr[] { new IntPtr(&dark) });

                // Centered card image (portrait), preserving aspect.
                IntPtr sprite = LoadCardSprite(cid);
                if (sprite != IntPtr.Zero)
                {
                    IntPtr card = GameObject.New();
                    UnityObject.SetName(card, "Card");
                    GameObject.AddComponent(card, _rectType);
                    IntPtr cardImg = GameObject.AddComponent(card, _imageType);
                    IntPtr cardT = GameObject.GetTransform(card);
                    Transform.SetParent(cardT, rootT);
                    AssetHelper.Vector2 half = new AssetHelper.Vector2(0.5f, 0.5f);
                    _anchorMin.GetSetMethod().Invoke(cardT, new IntPtr[] { new IntPtr(&half) });
                    _anchorMax.GetSetMethod().Invoke(cardT, new IntPtr[] { new IntPtr(&half) });
                    _pivot.GetSetMethod().Invoke(cardT, new IntPtr[] { new IntPtr(&half) });
                    AssetHelper.Vector2 size = new AssetHelper.Vector2(420, 610);
                    _sizeDelta.GetSetMethod().Invoke(cardT, new IntPtr[] { new IntPtr(&size) });
                    _anchoredPos3D.GetSetMethod().Invoke(cardT, new IntPtr[] { new IntPtr(&zero) });
                    Transform.SetLocalScale(cardT, one);
                    _imageSetSprite.Invoke(cardImg, new IntPtr[] { sprite });
                    csbool preserve = true;
                    _imagePreserveAspect.GetSetMethod().Invoke(cardImg, new IntPtr[] { new IntPtr(&preserve) });
                    Console.WriteLine("[rgproof] card " + cid + " shown on " + UnityObject.GetName(vcGo));
                }
                else Console.WriteLine("[rgproof] no sprite for cid " + cid + " (bg only)");
            }
            catch (Exception ex) { Console.WriteLine("[rgproof] EX: " + ex); }
        }

        static IntPtr LoadCardSprite(int cid)
        {
            string path = "Card/Images/Illust/tcg/" + cid;
            IntPtr tex = AssetHelper.LoadImmediateAsset(path);
            if (tex == IntPtr.Zero) return IntPtr.Zero;
            IntPtr sprite = AssetHelper.SpriteFromTexture(tex, "rgproof_" + cid);
            if (sprite != IntPtr.Zero) Import.Handler.il2cpp_gchandle_new(sprite, true);
            return sprite;
        }

        static void SetFull(IntPtr t)
        {
            AssetHelper.Vector2 min = new AssetHelper.Vector2(0, 0);
            AssetHelper.Vector2 max = new AssetHelper.Vector2(1, 1);
            AssetHelper.Vector2 zero = new AssetHelper.Vector2(0, 0);
            _anchorMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&min) });
            _anchorMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&max) });
            _offsetMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&zero) });
            _offsetMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&zero) });
        }
    }
}
