using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Loads card artwork by card id and overlays it onto a UI parent as OUR OWN sprite
    // (new Sprite via SpriteFromTexture, gc-anchored + DontDestroyOnLoad), so it survives
    // the game releasing shared card textures (e.g. when the deck viewer closes). Mirrors
    // Goat's SoloChapterCardImage (not reused): a RectMask2D container + an Img child with
    // preserveAspect.
    static unsafe class RoguelikeCardImage
    {
        static IntPtr _imageType;
        static IL2Method _imageSetSprite;
        static IL2Property _imagePreserveAspect;
        static IntPtr _rectType;
        static IL2Property _anchorMin, _anchorMax, _offsetMin, _offsetMax, _pivot, _sizeDelta, _anchoredPos3D;
        static IntPtr _rectMaskType;
        static IntPtr _canvasGroupType;
        static IntPtr _aspectFitterType;
        static IL2Property _aspectFitterMode;
        static IL2Property _aspectFitterRatio;
        static IL2Property _texWidth;
        static IL2Property _texHeight;
        static readonly Dictionary<int, IntPtr> _spriteCache = new Dictionary<int, IntPtr>();
        static readonly Dictionary<int, float> _aspectCache = new Dictionary<int, float>();
        static IntPtr _dontDestroyRoot;
        static bool _ready;

        static RoguelikeCardImage()
        {
            try
            {
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class image = ui.GetClass("Image", "UnityEngine.UI");
                _imageType = image.IL2Typeof();
                _imageSetSprite = image.GetProperty("sprite").GetSetMethod();
                _imagePreserveAspect = image.GetProperty("preserveAspect");
                _rectMaskType = ui.GetClass("RectMask2D", "UnityEngine.UI").IL2Typeof();
                IL2Class fitter = ui.GetClass("AspectRatioFitter", "UnityEngine.UI");
                _aspectFitterType = fitter.IL2Typeof();
                _aspectFitterMode = fitter.GetProperty("aspectMode");
                _aspectFitterRatio = fitter.GetProperty("aspectRatio");

                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class texture = core.GetClass("Texture", "UnityEngine");
                _texWidth = texture.GetProperty("width");
                _texHeight = texture.GetProperty("height");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _pivot = rect.GetProperty("pivot");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _anchoredPos3D = rect.GetProperty("anchoredPosition3D");

                _canvasGroupType = Assembler.GetAssembly("UnityEngine.UIModule")
                    .GetClass("CanvasGroup", "UnityEngine").IL2Typeof();
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[RoguelikeCardImage] init EX: " + ex); }
        }

        // New sprite for a card id, gc-anchored + parked under a DontDestroyOnLoad holder so
        // Unity won't collect it (the underlying illust is the game's shared bundle asset).
        static IntPtr LoadSprite(int cid)
        {
            IntPtr cached;
            if (_spriteCache.TryGetValue(cid, out cached)) return cached;
            IntPtr tex = AssetHelper.LoadImmediateAsset("Card/Images/Illust/tcg/" + cid);
            if (tex != IntPtr.Zero)
            {
                int w = _texWidth.GetGetMethod().Invoke(tex).GetValueRef<int>();
                int h = _texHeight.GetGetMethod().Invoke(tex).GetValueRef<int>();
                _aspectCache[cid] = h != 0 ? (float)w / h : 1f;
            }
            IntPtr sprite = tex == IntPtr.Zero ? IntPtr.Zero
                : AssetHelper.SpriteFromTexture(tex, "rg_card_" + cid);
            if (sprite != IntPtr.Zero)
            {
                Import.Handler.il2cpp_gchandle_new(sprite, true);
                UnityObject.DontDestroyOnLoad(sprite);
                if (_dontDestroyRoot == IntPtr.Zero)
                {
                    _dontDestroyRoot = GameObject.New();
                    UnityObject.SetName(_dontDestroyRoot, "RgCardImagesDontDestroy");
                    UnityObject.DontDestroyOnLoad(_dontDestroyRoot);
                }
                IntPtr keep = GameObject.New();
                UnityObject.SetName(keep, "rg_card_" + cid);
                IntPtr keepImg = GameObject.AddComponent(keep, _imageType);
                _imageSetSprite.Invoke(keepImg, new IntPtr[] { sprite });
                Transform.SetParent(GameObject.GetTransform(keep), GameObject.GetTransform(_dontDestroyRoot));
            }
            _spriteCache[cid] = sprite;
            return sprite;
        }

        // Overlay the card illust filling `parent`, aspect preserved. Idempotent by `name`.
        public static IntPtr AttachCardImage(IntPtr parent, int cid, string name)
        {
            if (!_ready || parent == IntPtr.Zero) return IntPtr.Zero;
            if (GameObject.FindGameObjectByName(parent, name, false, false) != IntPtr.Zero) return IntPtr.Zero;
            IntPtr sprite = LoadSprite(cid);
            if (sprite == IntPtr.Zero) { Console.WriteLine("[RoguelikeCardImage] no art for cid " + cid); return IntPtr.Zero; }

            Vector3 zero = new Vector3(0, 0, 0);
            Vector3 one = new Vector3(1, 1, 1);

            // Container with a RectMask2D so the image is clipped to the slot.
            IntPtr container = GameObject.New();
            UnityObject.SetName(container, name);
            GameObject.AddComponent(container, _canvasGroupType);
            GameObject.AddComponent(container, _rectType);
            GameObject.AddComponent(container, _rectMaskType);
            IntPtr ct = GameObject.GetTransform(container);
            Transform.SetParent(ct, GameObject.GetTransform(parent));
            Transform.SetAsLastSibling(ct);
            SetFull(ct);
            _anchoredPos3D.GetSetMethod().Invoke(ct, new IntPtr[] { new IntPtr(&zero) });
            Transform.SetLocalScale(ct, one);

            // Image child that ENVELOPES the container (fills 100%, overflow clipped by the
            // container's RectMask2D) via an AspectRatioFitter — same as the native slot.
            IntPtr imgGo = GameObject.New();
            UnityObject.SetName(imgGo, "Img");
            GameObject.AddComponent(imgGo, _rectType);
            IntPtr img = GameObject.AddComponent(imgGo, _imageType);
            IntPtr fitter = GameObject.AddComponent(imgGo, _aspectFitterType);
            IntPtr it = GameObject.GetTransform(imgGo);
            Transform.SetParent(it, ct);
            _imageSetSprite.Invoke(img, new IntPtr[] { sprite });
            csbool preserve = false;
            _imagePreserveAspect.GetSetMethod().Invoke(img, new IntPtr[] { new IntPtr(&preserve) });
            SetCenter(it);
            _anchoredPos3D.GetSetMethod().Invoke(it, new IntPtr[] { new IntPtr(&zero) });
            Transform.SetLocalScale(it, one);
            float aspect;
            if (!_aspectCache.TryGetValue(cid, out aspect) || aspect <= 0f) aspect = 1f;
            int envelopeParent = 4; // AspectRatioFitter.AspectMode.EnvelopeParent
            _aspectFitterMode.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&envelopeParent) });
            _aspectFitterRatio.GetSetMethod().Invoke(fitter, new IntPtr[] { new IntPtr(&aspect) });
            return container;
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

        static void SetCenter(IntPtr t)
        {
            AssetHelper.Vector2 c = new AssetHelper.Vector2(0.5f, 0.5f);
            _anchorMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) });
            _anchorMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) });
            _pivot.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) });
        }
    }
}
