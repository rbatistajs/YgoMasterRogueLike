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
        static readonly Dictionary<int, IntPtr> _cache = new Dictionary<int, IntPtr>();
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
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[RoguelikeCardImage] init EX: " + ex); }
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
