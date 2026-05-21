using IL2CPP;
using System;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Roguelike map screen. Reuses Solo/SoloMode (via RoguelikeSoloScreen) and renders our
    // own nodes/edges into a full-screen content panel. Nodes/edges added in later tasks.
    static unsafe class RoguelikeMapScreen
    {
        static IntPtr _tmpType, _bindingTextType, _rectType, _imageType;
        static IL2Property _anchorMin, _anchorMax, _offsetMin, _offsetMax;
        static IntPtr _content;
        static bool _ready;

        static RoguelikeMapScreen()
        {
            try
            {
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _offsetMin = rect.GetProperty("offsetMin");
                _offsetMax = rect.GetProperty("offsetMax");
                _imageType = Assembler.GetAssembly("UnityEngine.UI").GetClass("Image", "UnityEngine.UI").IL2Typeof();
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] map init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) return;
            RoguelikeSoloScreen.Open(Customize);
        }

        static void Customize(IntPtr portalPtr)
        {
            IntPtr root = RoguelikeSoloScreen.PortalRoot(portalPtr);
            if (root == IntPtr.Zero) { Console.WriteLine("[Roguelike] map: portal root not found"); return; }
            SetText(root, "TitleSafeArea.TitleGroup.NameText", "Mapa");
            Hide(root, "ButtonArea.MainGroup");
            Hide(root, "ButtonArea.GateListGroup");

            IntPtr content = GameObject.New();
            UnityObject.SetName(content, "RgMapContent");
            GameObject.AddComponent(content, _rectType);
            IntPtr ct = GameObject.GetTransform(content);
            Transform.SetParent(ct, GameObject.GetTransform(root));
            SetFull(ct);
            Transform.SetAsLastSibling(ct);
            Transform.SetLocalScale(ct, new Vector3(1, 1, 1));
            _content = content;
            Console.WriteLine("[Roguelike] map customized");
        }

        static void Hide(IntPtr root, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        static void SetText(IntPtr root, string path, string text)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero) { YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(binding, text); return; }
            IntPtr tmp = GameObject.GetComponent(o, _tmpType);
            if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, text);
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
