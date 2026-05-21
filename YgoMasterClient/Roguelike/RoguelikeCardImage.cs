using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Loads card artwork by card id (stock AssetHelper: custom PNG in ClientData, else the
    // game's bundle which has every card) and attaches it to a UI parent. Sprites are cached
    // + gc-anchored so Unity doesn't collect them.
    static unsafe class RoguelikeCardImage
    {
        static IntPtr _imageType;
        static IL2Method _imageSetSprite;
        static IL2Property _imagePreserveAspect;
        static IntPtr _rectType;
        static IL2Property _anchorMin, _anchorMax, _offsetMin, _offsetMax;
        static IntPtr _rawImageType;
        static IL2Property _rawImageTexture;
        static IL2Property _behaviourEnabled;
        static readonly Dictionary<int, IntPtr> _cache = new Dictionary<int, IntPtr>();
        static readonly Dictionary<int, IntPtr> _texCache = new Dictionary<int, IntPtr>();
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
                IL2Class rawImage = ui.GetClass("RawImage", "UnityEngine.UI");
                _rawImageType = rawImage.IL2Typeof();
                _rawImageTexture = rawImage.GetProperty("texture");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _behaviourEnabled = core.GetClass("Behaviour", "UnityEngine").GetProperty("enabled");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[RoguelikeCardImage] init EX: " + ex); }
        }

        // Raw card Texture2D by cid (game ResourceManager), cached + gc-anchored.
        public static IntPtr LoadTexture(int cid)
        {
            IntPtr cached;
            if (_texCache.TryGetValue(cid, out cached)) return cached;
            IntPtr tex = AssetHelper.LoadImmediateAsset("Card/Images/Illust/tcg/" + cid);
            if (tex != IntPtr.Zero) Import.Handler.il2cpp_gchandle_new(tex, true);
            _texCache[cid] = tex;
            return tex;
        }

        // Set a RawImage component's texture to card cid's art. `rawImageGo` is the GameObject
        // holding the RawImage. Returns false if no art / no RawImage.
        public static bool SetThumb(IntPtr rawImageGo, int cid)
        {
            if (!_ready || rawImageGo == IntPtr.Zero) return false;
            IntPtr tex = LoadTexture(cid);
            if (tex == IntPtr.Zero) { Console.WriteLine("[RoguelikeCardImage] no tex for cid " + cid); return false; }
            IntPtr raw = GameObject.GetComponent(rawImageGo, _rawImageType);
            if (raw == IntPtr.Zero) { Console.WriteLine("[RoguelikeCardImage] no RawImage on target"); return false; }
            _rawImageTexture.GetSetMethod().Invoke(raw, new IntPtr[] { tex });
            // The game's AutoReleaseCardIllust normally enables the RawImage; we disable that on
            // the clone, so enable the RawImage ourselves or the texture won't render.
            if (_behaviourEnabled != null) { csbool en = true; _behaviourEnabled.GetSetMethod().Invoke(raw, new IntPtr[] { new IntPtr(&en) }); }
            return true;
        }

        public static IntPtr LoadSprite(int cid)
        {
            IntPtr cached;
            if (_cache.TryGetValue(cid, out cached)) return cached;
            IntPtr tex = AssetHelper.LoadImmediateAsset("Card/Images/Illust/tcg/" + cid);
            IntPtr sprite = tex == IntPtr.Zero ? IntPtr.Zero
                : AssetHelper.SpriteFromTexture(tex, "rg_card_" + cid);
            if (sprite != IntPtr.Zero) Import.Handler.il2cpp_gchandle_new(sprite, true);
            _cache[cid] = sprite;
            return sprite;
        }

        // Create an Image child named `name` filling `parent`, showing card `cid` (aspect
        // preserved). Returns the image GameObject, or Zero on failure. Idempotent by name.
        public static IntPtr AttachFill(IntPtr parent, int cid, string name)
        {
            if (!_ready || parent == IntPtr.Zero) return IntPtr.Zero;
            IntPtr sprite = LoadSprite(cid);
            if (sprite == IntPtr.Zero) { Console.WriteLine("[RoguelikeCardImage] no art for cid " + cid); return IntPtr.Zero; }

            IntPtr existing = GameObject.FindGameObjectByName(parent, name, false, false);
            if (existing != IntPtr.Zero) GameObject.SetActive(existing, false);

            IntPtr go = GameObject.New();
            UnityObject.SetName(go, name);
            GameObject.AddComponent(go, _rectType);
            IntPtr img = GameObject.AddComponent(go, _imageType);
            IntPtr t = GameObject.GetTransform(go);
            Transform.SetParent(t, GameObject.GetTransform(parent));
            SetFull(t);
            Transform.SetAsLastSibling(t);
            Transform.SetLocalScale(t, new Vector3(1, 1, 1));
            _imageSetSprite.Invoke(img, new IntPtr[] { sprite });
            csbool preserve = true;
            _imagePreserveAspect.GetSetMethod().Invoke(img, new IntPtr[] { new IntPtr(&preserve) });
            return go;
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
