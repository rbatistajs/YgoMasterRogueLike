// Enables vertical scroll + drag-pan on the Solo gate map so we can
// ship gates with more than the native ~3 lanes.
//
// At OnCreatedView we:
//  1. Re-anchor ChapterMap to TOP (vertical drag would snap back with
//     a centered pivot).
//  2. Set ScrollRect.vertical = true and movementType = Clamped.
//  3. Force ExtendedScrollRect.dragScrollEnabled = true and re-apply
//     it every frame from Update() — Konami keeps resetting it.
//  4. Clone the horizontal scrollbar into a vertical one on the right
//     edge, wired into m_VerticalScrollbar + barObjVertical. Idempotent.
//
// Hierarchy: Root/RootGate/TemplateGate/Scroll View/Viewport/ChapterMap
// (walked by name — TemplateGate's Clone suffix varies per scene).
//
// Wired via a callout from SoloVisualNovelChapterView.OnCreatedView (one
// Hook per method limit). Update() rides on TradeUtils.NetworkMain.Update.

using IL2CPP;
using System;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class SoloGateScrollEnabler
    {
        // Native gate ships ~896 (3 lanes); 1500 fits 5 with breathing room.
        const float ChapterMapHeight = 1500f;

        static IntPtr RectTransform_Type;
        static IL2Property RectTransform_anchorMin;
        static IL2Property RectTransform_anchorMax;
        static IL2Property RectTransform_pivot;
        static IL2Property RectTransform_sizeDelta;
        static IL2Property RectTransform_anchoredPosition;

        static IntPtr ScrollRect_Type;
        static IL2Property ScrollRect_vertical;
        static IL2Property ScrollRect_horizontal;
        static IL2Property ScrollRect_movementType;
        static IL2Field ScrollRect_m_VerticalScrollbar;

        // Direction enum: 0=L→R, 1=R→L, 2=B→T, 3=T→B.
        static IntPtr Scrollbar_Type;
        static IL2Property Scrollbar_direction;

        static IntPtr ExtendedScrollRect_Type;
        static IL2Field ExtendedScrollRect_dragScrollEnabled;
        static IL2Field ExtendedScrollRect_barObjHorizontal;
        static IL2Field ExtendedScrollRect_barObjVertical;

        // Reserved for future features that need direct gate-GO access.
        static IL2Field SoloSelectChapterViewController_chapterMap;
        static IL2Field ChapterMap_gateGO;

        // Per-frame re-asserted because Konami resets it after our hook.
        static IntPtr cachedExtScroll;

        static bool enabled;

        static SoloGateScrollEnabler()
        {
            try
            {
                IL2Assembly coreModule = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class RectTransform_Class = coreModule.GetClass("RectTransform", "UnityEngine");
                RectTransform_Type = RectTransform_Class.IL2Typeof();
                RectTransform_anchorMin = RectTransform_Class.GetProperty("anchorMin");
                RectTransform_anchorMax = RectTransform_Class.GetProperty("anchorMax");
                RectTransform_pivot = RectTransform_Class.GetProperty("pivot");
                RectTransform_sizeDelta = RectTransform_Class.GetProperty("sizeDelta");
                RectTransform_anchoredPosition = RectTransform_Class.GetProperty("anchoredPosition");

                IL2Assembly uiAssembly = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class ScrollRect_Class = uiAssembly != null
                    ? uiAssembly.GetClass("ScrollRect", "UnityEngine.UI") : null;
                if (ScrollRect_Class != null)
                {
                    ScrollRect_Type = ScrollRect_Class.IL2Typeof();
                    ScrollRect_vertical = ScrollRect_Class.GetProperty("vertical");
                    ScrollRect_horizontal = ScrollRect_Class.GetProperty("horizontal");
                    ScrollRect_movementType = ScrollRect_Class.GetProperty("movementType");
                    ScrollRect_m_VerticalScrollbar = ScrollRect_Class.GetField("m_VerticalScrollbar");
                }

                IL2Class Scrollbar_Class = uiAssembly != null
                    ? uiAssembly.GetClass("Scrollbar", "UnityEngine.UI") : null;
                if (Scrollbar_Class != null)
                {
                    Scrollbar_Type = Scrollbar_Class.IL2Typeof();
                    Scrollbar_direction = Scrollbar_Class.GetProperty("direction");
                }

                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class ExtendedScrollRect_Class = assembly.GetClass("ExtendedScrollRect", "YgomSystem.UI");
                if (ExtendedScrollRect_Class != null)
                {
                    ExtendedScrollRect_Type = ExtendedScrollRect_Class.IL2Typeof();
                    ExtendedScrollRect_dragScrollEnabled = ExtendedScrollRect_Class.GetField("dragScrollEnabled");
                    ExtendedScrollRect_barObjHorizontal = ExtendedScrollRect_Class.GetField("barObjHorizontal");
                    ExtendedScrollRect_barObjVertical = ExtendedScrollRect_Class.GetField("barObjVertical");
                }

                IL2Class SoloVc_Class = assembly.GetClass("SoloSelectChapterViewController", "YgomGame.Solo");
                if (SoloVc_Class != null)
                {
                    SoloSelectChapterViewController_chapterMap = SoloVc_Class.GetField("chapterMap");
                    IL2Class ChapterMap_Class = SoloVc_Class.GetNestedType("ChapterMap");
                    if (ChapterMap_Class != null)
                        ChapterMap_gateGO = ChapterMap_Class.GetField("gateGO");
                }

                enabled = true;
            }
            catch (Exception ex)
            {
                Utils.LogWarning("SoloGateScrollEnabler init failed: " + ex);
            }
        }

        public static void OnCreatedView(IntPtr thisPtr)
        {
            if (!enabled) return;
            try { Apply(thisPtr); }
            catch (Exception ex) { Utils.LogWarning("SoloGateScrollEnabler apply EX: " + ex); }
        }

        static void Apply(IntPtr thisPtr)
        {
            IntPtr controllerGo = Component.GetGameObject(thisPtr);
            if (controllerGo == IntPtr.Zero) return;

            IntPtr chapterMapGo = GameObject.FindGameObjectByName(controllerGo, "ChapterMap");
            IntPtr scrollViewGo = GameObject.FindGameObjectByName(controllerGo, "Scroll View");
            if (chapterMapGo == IntPtr.Zero || scrollViewGo == IntPtr.Zero) return;

            // 1. Re-anchor ChapterMap to top (preserve X-axis values).
            IntPtr rt = GameObject.GetComponent(chapterMapGo, RectTransform_Type);
            if (rt != IntPtr.Zero)
            {
                AssetHelper.Vector2 curAnchorMin = RectTransform_anchorMin.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Vector2>();
                AssetHelper.Vector2 curAnchorMax = RectTransform_anchorMax.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Vector2>();
                AssetHelper.Vector2 curPivot = RectTransform_pivot.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Vector2>();
                AssetHelper.Vector2 curSize = RectTransform_sizeDelta.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Vector2>();

                AssetHelper.Vector2 anchorMin = new AssetHelper.Vector2(curAnchorMin.x, 1f);
                AssetHelper.Vector2 anchorMax = new AssetHelper.Vector2(curAnchorMax.x, 1f);
                AssetHelper.Vector2 pivot = new AssetHelper.Vector2(curPivot.x, 1f);
                AssetHelper.Vector2 sizeDelta = new AssetHelper.Vector2(curSize.x, ChapterMapHeight);
                AssetHelper.Vector2 anchoredPos = new AssetHelper.Vector2(0f, 0f);

                RectTransform_anchorMin.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&anchorMin) });
                RectTransform_anchorMax.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&anchorMax) });
                RectTransform_pivot.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&pivot) });
                RectTransform_sizeDelta.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&sizeDelta) });
                RectTransform_anchoredPosition.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&anchoredPos) });
            }

            // 2. Both axes on, Clamped movement (Elastic would drag past
            //    our new top anchor).
            if (ScrollRect_Type != IntPtr.Zero)
            {
                IntPtr scrollRect = GameObject.GetComponent(scrollViewGo, ScrollRect_Type);
                if (scrollRect != IntPtr.Zero)
                {
                    csbool yes = true;
                    if (ScrollRect_vertical != null)
                        ScrollRect_vertical.GetSetMethod().Invoke(scrollRect, new IntPtr[] { new IntPtr(&yes) });
                    if (ScrollRect_horizontal != null)
                        ScrollRect_horizontal.GetSetMethod().Invoke(scrollRect, new IntPtr[] { new IntPtr(&yes) });
                    if (ScrollRect_movementType != null)
                    {
                        int clamped = 2; // ScrollRect.MovementType.Clamped
                        ScrollRect_movementType.GetSetMethod().Invoke(scrollRect, new IntPtr[] { new IntPtr(&clamped) });
                    }
                }
            }

            // 3. Master toggle — drag handler returns early without it.
            if (ExtendedScrollRect_Type != IntPtr.Zero && ExtendedScrollRect_dragScrollEnabled != null)
            {
                IntPtr extScroll = GameObject.GetComponent(scrollViewGo, ExtendedScrollRect_Type);
                if (extScroll != IntPtr.Zero)
                {
                    csbool yes = true;
                    ExtendedScrollRect_dragScrollEnabled.SetValue(extScroll, new IntPtr(&yes));
                    cachedExtScroll = extScroll;
                    EnsureVerticalScrollbar(extScroll, scrollViewGo);
                }
            }
        }

        static void EnsureVerticalScrollbar(IntPtr extScroll, IntPtr scrollViewGo)
        {
            if (Scrollbar_Type == IntPtr.Zero || Scrollbar_direction == null) return;
            if (ScrollRect_m_VerticalScrollbar == null) return;
            if (ExtendedScrollRect_barObjVertical == null) return;

            // Idempotent: if barObjVertical is already wired OR we already
            // injected our clone, this gate is done.
            IL2Object existingV = ExtendedScrollRect_barObjVertical.GetValue(extScroll);
            if (existingV != null && existingV.ptr != IntPtr.Zero) return;
            if (GameObject.FindGameObjectByName(scrollViewGo, "Scrollbar Vertical (Goat)") != IntPtr.Zero) return;

            // Source for the clone: prefer Konami's `barObjHorizontal`
            // field (when populated, it points to the right thing). If
            // Konami didn't wire it for this gate (common — they rely on
            // Unity's standard `ScrollRect.horizontalScrollbar` instead),
            // fall back to the "ScrollBar" child GameObject by name —
            // visible in the hierarchy as Scroll View/ScrollBar.
            IntPtr hBar = IntPtr.Zero;
            if (ExtendedScrollRect_barObjHorizontal != null)
            {
                IL2Object hBarObj = ExtendedScrollRect_barObjHorizontal.GetValue(extScroll);
                if (hBarObj != null) hBar = hBarObj.ptr;
            }
            if (hBar == IntPtr.Zero)
                hBar = GameObject.FindGameObjectByName(scrollViewGo, "ScrollBar");
            if (hBar == IntPtr.Zero)
            { Utils.LogWarning("EnsureVerticalScrollbar: no source ScrollBar GameObject found"); return; }

            IntPtr parentTransform = Transform.GetParent(GameObject.GetTransform(hBar));
            if (parentTransform == IntPtr.Zero) return;

            IntPtr vBar = UnityObject.Instantiate(hBar, parentTransform);
            if (vBar == IntPtr.Zero) return;
            UnityObject.SetName(vBar, "Scrollbar Vertical (Goat)");

            IntPtr vScrollbarComp = GameObject.GetComponent(vBar, Scrollbar_Type);
            if (vScrollbarComp != IntPtr.Zero)
            {
                int dirBottomToTop = 2;
                Scrollbar_direction.GetSetMethod().Invoke(
                    vScrollbarComp, new IntPtr[] { new IntPtr(&dirBottomToTop) });
            }

            // Stick to right edge, full viewport height.
            IntPtr vRt = GameObject.GetComponent(vBar, RectTransform_Type);
            if (vRt != IntPtr.Zero)
            {
                AssetHelper.Vector2 anchorMin = new AssetHelper.Vector2(1f, 0f);
                AssetHelper.Vector2 anchorMax = new AssetHelper.Vector2(1f, 1f);
                AssetHelper.Vector2 pivot = new AssetHelper.Vector2(1f, 0.5f);
                AssetHelper.Vector2 anchoredPos = new AssetHelper.Vector2(0f, 0f);
                AssetHelper.Vector2 sizeDelta = new AssetHelper.Vector2(8f, 0f);
                RectTransform_anchorMin.GetSetMethod().Invoke(vRt, new IntPtr[] { new IntPtr(&anchorMin) });
                RectTransform_anchorMax.GetSetMethod().Invoke(vRt, new IntPtr[] { new IntPtr(&anchorMax) });
                RectTransform_pivot.GetSetMethod().Invoke(vRt, new IntPtr[] { new IntPtr(&pivot) });
                RectTransform_anchoredPosition.GetSetMethod().Invoke(vRt, new IntPtr[] { new IntPtr(&anchoredPos) });
                RectTransform_sizeDelta.GetSetMethod().Invoke(vRt, new IntPtr[] { new IntPtr(&sizeDelta) });
            }

            // Wire into both: ScrollRect (drag/wheel) + ExtendedScrollRect
            // (Konami's show/hide/fade plumbing).
            if (vScrollbarComp != IntPtr.Zero)
                ScrollRect_m_VerticalScrollbar.SetValue(extScroll, vScrollbarComp);
            ExtendedScrollRect_barObjVertical.SetValue(extScroll, vBar);
        }

        // Re-asserts dragScrollEnabled every frame — Konami resets it
        // somewhere after OnCreatedView (likely Scroll View's OnEnable).
        public static void Update()
        {
            if (!enabled) return;
            if (cachedExtScroll == IntPtr.Zero || ExtendedScrollRect_dragScrollEnabled == null) return;
            try
            {
                csbool yes = true;
                ExtendedScrollRect_dragScrollEnabled.SetValue(cachedExtScroll, new IntPtr(&yes));
            }
            catch
            {
                // Cache stale (scene change) — next OnCreatedView repopulates.
                cachedExtScroll = IntPtr.Zero;
            }
        }
    }
}
