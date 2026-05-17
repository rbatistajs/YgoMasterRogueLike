// Custom chapter layout for Solo gates: reads per-chapter `grid_x` /
// `grid_y` from Solo.json and repositions each chapter's RectTransform
// on a fixed PADING grid. Recreates RootEdges as axis-aligned H/V
// connectors and scrolls the viewport to the active chapter via
// ExtendedScrollRect.ScrollByTargetPos.
//
// Wired through SoloVisualNovelChapterView.OnCreatedView (one-line
// callout — the detours backend allows only one Hook per method).

using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class SoloGateGridLayout
    {
        // Per-cell pixel spacing — matches ChapterMap.PADING_X / PADING_Y.
        const float PadingX = 244f;
        const float PadingY = 280f;

        // Half-chapter breathing room so border cells aren't clipped.
        const float MarginX = 200f;
        const float MarginY = 250f;

        const float EdgeThickness = 8f;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Color { public float r, g, b, a; }

        static IL2Field SoloVc_chapterMap;
        static IL2Field ChapterMap_chapterDataDic;
        static IL2Field ChapterMap_gateID;
        // 0 on a fresh gate; falls back to the root chapter then.
        static IL2Field ChapterMap_currentChapterID;
        static IL2Class Chapter_Class;
        static IL2Field Chapter_go;

        static IntPtr RectTransform_Type;
        static IL2Property RectTransform_anchoredPosition;
        static IL2Property RectTransform_sizeDelta;
        static IL2Property RectTransform_anchorMin;
        static IL2Property RectTransform_anchorMax;
        static IL2Property RectTransform_pivot;
        // Viewport stretches — sizeDelta lies; `rect` is the real size.
        static IL2Property RectTransform_rect;

        static IntPtr ExtendedScrollRect_Type;
        static IL2Method ExtendedScrollRect_ScrollByTargetPos;

        // Resolved once during Apply, reused by ApplyScrollToTarget.
        static IntPtr cachedExtScroll;
        static IntPtr cachedChapterMapGo;
        static IntPtr cachedViewportGo;

        // Konami's initial SelectedChapter fires BEFORE Apply against a
        // stale layout — suppress Original until we've laid out chapters.
        static bool wasApplied;

        static IL2Property Transform_localEulerAngles;

        static IntPtr Image_Type;
        static IL2Property Behaviour_enabled;
        static IL2Property Image_color;

        // Forces ScrollRect to re-read content bounds after we resize
        // ChapterMap, so vertical scroll reaches the new lower rows.
        static IL2Method LayoutRebuilder_ForceRebuildLayoutImmediate;

        delegate void Del_SelectedChapter(IntPtr thisPtr, int arg);
        static Hook<Del_SelectedChapter> hookSelectedChapter;

        static bool enabled;

        static SoloGateGridLayout()
        {
            try
            {
                IL2Assembly coreModule = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class RectTransform_Class = coreModule.GetClass("RectTransform", "UnityEngine");
                RectTransform_Type = RectTransform_Class.IL2Typeof();
                RectTransform_anchoredPosition = RectTransform_Class.GetProperty("anchoredPosition");
                RectTransform_sizeDelta = RectTransform_Class.GetProperty("sizeDelta");
                RectTransform_anchorMin = RectTransform_Class.GetProperty("anchorMin");
                RectTransform_anchorMax = RectTransform_Class.GetProperty("anchorMax");
                RectTransform_pivot = RectTransform_Class.GetProperty("pivot");
                RectTransform_rect = RectTransform_Class.GetProperty("rect");

                IL2Class Transform_Class = coreModule.GetClass("Transform", "UnityEngine");
                if (Transform_Class != null)
                    Transform_localEulerAngles = Transform_Class.GetProperty("localEulerAngles");

                IL2Class Behaviour_Class = coreModule.GetClass("Behaviour", "UnityEngine");
                if (Behaviour_Class != null)
                    Behaviour_enabled = Behaviour_Class.GetProperty("enabled");

                IL2Assembly uiAssembly2 = Assembler.GetAssembly("UnityEngine.UI");
                if (uiAssembly2 != null)
                {
                    IL2Class Image_Class = uiAssembly2.GetClass("Image", "UnityEngine.UI");
                    if (Image_Class != null) Image_Type = Image_Class.IL2Typeof();
                    // `color` lives on Graphic (Image's base); GetProperty
                    // doesn't walk inheritance, so resolve from the base.
                    IL2Class Graphic_Class = uiAssembly2.GetClass("Graphic", "UnityEngine.UI");
                    if (Graphic_Class != null) Image_color = Graphic_Class.GetProperty("color");
                }

                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class SoloVc_Class = assembly.GetClass("SoloSelectChapterViewController", "YgomGame.Solo");
                SoloVc_chapterMap = SoloVc_Class.GetField("chapterMap");

                IL2Class ChapterMap_Class = SoloVc_Class.GetNestedType("ChapterMap");
                ChapterMap_chapterDataDic = ChapterMap_Class.GetField("chapterDataDic");
                ChapterMap_gateID = ChapterMap_Class.GetField("gateID");
                ChapterMap_currentChapterID = ChapterMap_Class.GetField("currentChapterID");

                Chapter_Class = SoloVc_Class.GetNestedType("Chapter");
                Chapter_go = Chapter_Class.GetField("go");

                IL2Assembly uiAssembly = Assembler.GetAssembly("UnityEngine.UI");
                if (uiAssembly != null)
                {
                    IL2Class LayoutRebuilder_Class = uiAssembly.GetClass("LayoutRebuilder", "UnityEngine.UI");
                    if (LayoutRebuilder_Class != null)
                        LayoutRebuilder_ForceRebuildLayoutImmediate = LayoutRebuilder_Class.GetMethod("ForceRebuildLayoutImmediate");
                }

                IL2Class ExtendedScrollRect_Class = assembly.GetClass("ExtendedScrollRect", "YgomSystem.UI");
                if (ExtendedScrollRect_Class != null)
                {
                    ExtendedScrollRect_Type = ExtendedScrollRect_Class.IL2Typeof();
                    ExtendedScrollRect_ScrollByTargetPos = ExtendedScrollRect_Class.GetMethod("ScrollByTargetPos");
                }

                IL2Method selectedChapterMethod = ChapterMap_Class.GetMethod("SelectedChapter");
                if (selectedChapterMethod != null)
                    hookSelectedChapter = new Hook<Del_SelectedChapter>(SelectedChapter, selectedChapterMethod);

                enabled = true;
            }
            catch (Exception ex)
            {
                Utils.LogWarning("SoloGateGridLayout init failed: " + ex);
            }
        }

        public static void OnCreatedView(IntPtr thisPtr)
        {
            if (!enabled) return;
            try { Apply(thisPtr); }
            catch (Exception ex) { Utils.LogWarning("SoloGateGridLayout apply EX: " + ex); }
        }

        // Runs BEFORE Konami's Original so the SelectedChapter hook
        // suppresses scroll on the first call of every new gate display.
        public static void OnPreCreatedView(IntPtr thisPtr)
        {
            if (!enabled) return;
            wasApplied = false;
        }

        static void Apply(IntPtr thisPtr)
        {
            IL2Object chapterMapObj = SoloVc_chapterMap.GetValue(thisPtr);
            if (chapterMapObj == null) return;
            IntPtr chapterMap = chapterMapObj.ptr;

            int gateId = ChapterMap_gateID.GetValue(chapterMap).GetValueRef<int>();
            Dictionary<string, object> chaptersData =
                YgomSystem.Utility.ClientWork.GetDict("$.Master.Solo.chapter." + gateId);
            if (chaptersData == null) return;

            IL2DictionaryExplicit chapterDataDict = new IL2DictionaryExplicit(
                ChapterMap_chapterDataDic.GetValue(chapterMap).ptr,
                IL2SystemClass.Int32, Chapter_Class);

            // Collect placements + grid bounds.
            List<int> chapterIds = new List<int>();
            List<int> gridXs = new List<int>();
            List<int> gridYs = new List<int>();
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (KeyValuePair<string, object> entry in chaptersData)
            {
                Dictionary<string, object> chapterData = entry.Value as Dictionary<string, object>;
                int chapterId;
                if (!int.TryParse(entry.Key, out chapterId)) continue;
                if (chapterData == null) continue;
                if (!chapterData.ContainsKey("grid_x") || !chapterData.ContainsKey("grid_y")) continue;
                if (!chapterDataDict.ContainsKey(chapterId)) continue;

                int gridX = Utils.GetValue<int>(chapterData, "grid_x");
                int gridY = Utils.GetValue<int>(chapterData, "grid_y");
                chapterIds.Add(chapterId);
                gridXs.Add(gridX);
                gridYs.Add(gridY);
                if (gridX < minX) minX = gridX;
                if (gridX > maxX) maxX = gridX;
                if (gridY < minY) minY = gridY;
                if (gridY > maxY) maxY = gridY;
            }
            if (chapterIds.Count == 0) return;

            // Resize ChapterMap to fit the layout (+ margins) and pin its
            // X pivot/anchor to 0 so chapter offsets-from-center are
            // predictable. Y axis is left to SoloGateScrollEnabler (top).
            float layoutWidth = (maxX - minX) * PadingX + 2f * MarginX;
            float layoutHeight = (maxY - minY) * PadingY + 2f * MarginY;
            IntPtr chapterMapGo = GameObject.FindGameObjectByName(
                Component.GetGameObject(thisPtr), "ChapterMap");
            if (chapterMapGo != IntPtr.Zero)
            {
                IntPtr mapRt = GameObject.GetComponent(chapterMapGo, RectTransform_Type);
                if (mapRt != IntPtr.Zero)
                {
                    AssetHelper.Vector2 prevAnchorMin = RectTransform_anchorMin.GetGetMethod().Invoke(mapRt).GetValueRef<AssetHelper.Vector2>();
                    AssetHelper.Vector2 prevAnchorMax = RectTransform_anchorMax.GetGetMethod().Invoke(mapRt).GetValueRef<AssetHelper.Vector2>();
                    AssetHelper.Vector2 prevPivot = RectTransform_pivot.GetGetMethod().Invoke(mapRt).GetValueRef<AssetHelper.Vector2>();
                    AssetHelper.Vector2 anchorMin = new AssetHelper.Vector2(0f, prevAnchorMin.y);
                    AssetHelper.Vector2 anchorMax = new AssetHelper.Vector2(0f, prevAnchorMax.y);
                    AssetHelper.Vector2 pivot = new AssetHelper.Vector2(0f, prevPivot.y);
                    RectTransform_anchorMin.GetSetMethod().Invoke(mapRt, new IntPtr[] { new IntPtr(&anchorMin) });
                    RectTransform_anchorMax.GetSetMethod().Invoke(mapRt, new IntPtr[] { new IntPtr(&anchorMax) });
                    RectTransform_pivot.GetSetMethod().Invoke(mapRt, new IntPtr[] { new IntPtr(&pivot) });

                    AssetHelper.Vector2 newSize = new AssetHelper.Vector2(layoutWidth, layoutHeight);
                    RectTransform_sizeDelta.GetSetMethod().Invoke(
                        mapRt, new IntPtr[] { new IntPtr(&newSize) });

                    if (LayoutRebuilder_ForceRebuildLayoutImmediate != null)
                        LayoutRebuilder_ForceRebuildLayoutImmediate.Invoke(new IntPtr[] { mapRt });
                }
            }

            // Position each chapter. Anchor is (0.5, 0.5) so anchoredPosition
            // is offset from ChapterMap center.
            int repositioned = 0;
            Dictionary<int, AssetHelper.Vector2> chapterPositions =
                new Dictionary<int, AssetHelper.Vector2>(chapterIds.Count);
            for (int i = 0; i < chapterIds.Count; i++)
            {
                IntPtr chapterPtr = chapterDataDict[chapterIds[i]];
                IL2Object goObj = Chapter_go.GetValue(chapterPtr);
                if (goObj == null || goObj.ptr == IntPtr.Zero) continue;
                IntPtr rt = GameObject.GetComponent(goObj.ptr, RectTransform_Type);
                if (rt == IntPtr.Zero) continue;

                AssetHelper.Vector2 pos = new AssetHelper.Vector2(
                    (MarginX + (gridXs[i] - minX) * PadingX) - layoutWidth * 0.5f,
                    layoutHeight * 0.5f - (MarginY + (gridYs[i] - minY) * PadingY));
                RectTransform_anchoredPosition.GetSetMethod().Invoke(
                    rt, new IntPtr[] { new IntPtr(&pos) });
                chapterPositions[chapterIds[i]] = pos;
                repositioned++;
            }

            // Recreate edges — Konami's may be rotated and don't 1:1 match
            // our tree. Path-to-clear_chapter edges are colored green.
            HashSet<int> pathSet = ComputePathToClearChapter(gateId, chaptersData);
            int edgesCreated = RecreateEdges(chapterMapGo, chaptersData, chapterPositions, pathSet);

            // Cache GO hierarchy for ApplyScrollToTarget reuse.
            cachedChapterMapGo = chapterMapGo;
            cachedViewportGo = chapterMapGo != IntPtr.Zero
                ? Component.GetGameObject(Transform.GetParent(GameObject.GetTransform(chapterMapGo)))
                : IntPtr.Zero;
            IntPtr scrollViewGo = cachedViewportGo != IntPtr.Zero
                ? Component.GetGameObject(Transform.GetParent(GameObject.GetTransform(cachedViewportGo)))
                : IntPtr.Zero;
            cachedExtScroll = (scrollViewGo != IntPtr.Zero && ExtendedScrollRect_Type != IntPtr.Zero)
                ? GameObject.GetComponent(scrollViewGo, ExtendedScrollRect_Type)
                : IntPtr.Zero;

            Utils.LogWarning("SoloGateGridLayout: gate " + gateId
                + " repositioned " + repositioned + " chapters, " + edgesCreated + " edges");

            try { ApplyScrollToTarget(chapterMap); }
            catch (Exception ex) { Utils.LogWarning("SoloGateGridLayout post-Apply scroll EX: " + ex); }

            wasApplied = true;
        }

        static void SelectedChapter(IntPtr thisPtr, int arg)
        {
            int currentChapterID = -1;
            try
            {
                if (ChapterMap_currentChapterID != null)
                    currentChapterID = ChapterMap_currentChapterID.GetValue(thisPtr).GetValueRef<int>();
            }
            catch { }
            Utils.LogWarning("SoloGateGridLayout SelectedChapter called: arg=" + arg
                + " currentChapterID=" + currentChapterID
                + " wasApplied=" + wasApplied);
            if (!wasApplied) return;
            hookSelectedChapter.Original(thisPtr, arg);
        }

        // Scroll viewport to currentChapterID (or root). Computes the
        // ChapterMap.anchoredPosition that centers the chapter, clamps to
        // legal range, and hands off to Konami's ScrollByTargetPos.
        //   Ax = Vw/2 - Cw/2 - cx
        //   Ay = -Vh/2 + Ch/2 - cy
        //   Ax in [-(Cw-Vw), 0],  Ay in [0, Ch-Vh]
        static void ApplyScrollToTarget(IntPtr chapterMap)
        {
            if (chapterMap == IntPtr.Zero) return;
            if (cachedExtScroll == IntPtr.Zero || ExtendedScrollRect_ScrollByTargetPos == null) return;
            if (cachedChapterMapGo == IntPtr.Zero || cachedViewportGo == IntPtr.Zero) return;

            int gateId = ChapterMap_gateID.GetValue(chapterMap).GetValueRef<int>();
            Dictionary<string, object> chaptersData =
                YgomSystem.Utility.ClientWork.GetDict("$.Master.Solo.chapter." + gateId);
            if (chaptersData == null) return;

            int targetId = 0;
            if (ChapterMap_currentChapterID != null)
                targetId = ChapterMap_currentChapterID.GetValue(chapterMap).GetValueRef<int>();
            if (targetId == 0) targetId = ResolveRootChapter(chaptersData);
            if (targetId == 0) return;

            IL2DictionaryExplicit chapterDataDict = new IL2DictionaryExplicit(
                ChapterMap_chapterDataDic.GetValue(chapterMap).ptr,
                IL2SystemClass.Int32, Chapter_Class);
            if (!chapterDataDict.ContainsKey(targetId)) return;
            IL2Object goObj = Chapter_go.GetValue(chapterDataDict[targetId]);
            if (goObj == null || goObj.ptr == IntPtr.Zero) return;
            IntPtr chapterRt = GameObject.GetComponent(goObj.ptr, RectTransform_Type);
            if (chapterRt == IntPtr.Zero) return;
            AssetHelper.Vector2 chapterPos = RectTransform_anchoredPosition.GetGetMethod()
                .Invoke(chapterRt).GetValueRef<AssetHelper.Vector2>();

            IntPtr mapRt = GameObject.GetComponent(cachedChapterMapGo, RectTransform_Type);
            IntPtr viewRt = GameObject.GetComponent(cachedViewportGo, RectTransform_Type);
            if (mapRt == IntPtr.Zero || viewRt == IntPtr.Zero) return;
            AssetHelper.Vector2 mapSize = RectTransform_sizeDelta.GetGetMethod()
                .Invoke(mapRt).GetValueRef<AssetHelper.Vector2>();
            AssetHelper.Rect vRect = RectTransform_rect.GetGetMethod()
                .Invoke(viewRt).GetValueRef<AssetHelper.Rect>();
            float Cw = mapSize.x, Ch = mapSize.y;
            float Vw = vRect.m_Width, Vh = vRect.m_Height;
            if (Vw <= 0f || Vh <= 0f) return;

            float Ax = Vw * 0.5f - Cw * 0.5f - chapterPos.x;
            float Ay = -Vh * 0.5f + Ch * 0.5f - chapterPos.y;
            if (Cw > Vw)
            {
                if (Ax < -(Cw - Vw)) Ax = -(Cw - Vw);
                else if (Ax > 0f) Ax = 0f;
            }
            else Ax = 0f;
            if (Ch > Vh)
            {
                if (Ay < 0f) Ay = 0f;
                else if (Ay > (Ch - Vh)) Ay = (Ch - Vh);
            }
            else Ay = 0f;

            AssetHelper.Vector2 target = new AssetHelper.Vector2(Ax, Ay);
            Utils.LogWarning("SoloGateGridLayout scroll: chapterId=" + targetId
                + " chapterPos=(" + chapterPos.x + ", " + chapterPos.y + ")"
                + " target=(" + Ax + ", " + Ay + ")");
            ExtendedScrollRect_ScrollByTargetPos.Invoke(
                cachedExtScroll, new IntPtr[] { new IntPtr(&target) });
        }

        static int ResolveRootChapter(Dictionary<string, object> chaptersData)
        {
            foreach (KeyValuePair<string, object> entry in chaptersData)
            {
                Dictionary<string, object> chData = entry.Value as Dictionary<string, object>;
                if (chData == null) continue;
                if (Utils.GetValue<int>(chData, "parent_chapter") != 0) continue;
                int id;
                if (int.TryParse(entry.Key, out id)) return id;
            }
            return 0;
        }

        static HashSet<int> ComputePathToClearChapter(int gateId,
            Dictionary<string, object> chaptersData)
        {
            HashSet<int> path = new HashSet<int>();
            Dictionary<string, object> gateData =
                YgomSystem.Utility.ClientWork.GetDict("$.Master.Solo.gate." + gateId);
            if (gateData == null) return path;
            int clearChapter = Utils.GetValue<int>(gateData, "clear_chapter");
            if (clearChapter == 0) return path;
            // path.Add returns false on dup — also breaks malformed cycles.
            int cur = clearChapter;
            while (cur != 0 && path.Add(cur))
            {
                Dictionary<string, object> chapterData =
                    chaptersData.ContainsKey(cur.ToString())
                        ? chaptersData[cur.ToString()] as Dictionary<string, object>
                        : null;
                if (chapterData == null) break;
                cur = Utils.GetValue<int>(chapterData, "parent_chapter");
            }
            return path;
        }

        static int RecreateEdges(IntPtr chapterMapGo,
            Dictionary<string, object> chaptersData,
            Dictionary<int, AssetHelper.Vector2> chapterPositions,
            HashSet<int> pathSet)
        {
            if (chapterMapGo == IntPtr.Zero) return 0;
            IntPtr rootEdgesGo = GameObject.FindGameObjectByName(chapterMapGo, "RootEdges");
            if (rootEdgesGo == IntPtr.Zero) return 0;

            List<IntPtr> existingEdges = GameObject.GetChildren(rootEdgesGo);
            if (existingEdges == null || existingEdges.Count == 0) return 0;

            // Snapshot template BEFORE DestroyChildObjects — Destroy is
            // deferred to end-of-frame, so the pointer stays valid here.
            IntPtr template = existingEdges[0];
            IntPtr rootEdgesTransform = GameObject.GetTransform(rootEdgesGo);

            // Sample green from the template (Konami paints it for path edges).
            Color greenColor = new Color { r = 0.6f, g = 1.0f, b = 0.3f, a = 1.0f };
            if (Image_Type != IntPtr.Zero && Image_color != null)
            {
                IntPtr templateImage = GameObject.GetComponent(template, Image_Type);
                if (templateImage != IntPtr.Zero)
                {
                    IL2Object colorObj = Image_color.GetGetMethod().Invoke(templateImage);
                    if (colorObj != null)
                        greenColor = colorObj.GetValueRef<Color>();
                }
            }
            Color whiteColor = new Color { r = 1f, g = 1f, b = 1f, a = 1f };

            GameObject.DestroyChildObjects(rootEdgesGo);

            List<int> sortedIds = new List<int>();
            foreach (string key in chaptersData.Keys)
            {
                int id;
                if (int.TryParse(key, out id)) sortedIds.Add(id);
            }
            sortedIds.Sort();

            int created = 0;
            foreach (int childId in sortedIds)
            {
                Dictionary<string, object> chapterData = chaptersData[childId.ToString()] as Dictionary<string, object>;
                if (chapterData == null) continue;
                int parentId = Utils.GetValue<int>(chapterData, "parent_chapter");
                if (parentId <= 0) continue;
                AssetHelper.Vector2 childPos, parentPos;
                if (!chapterPositions.TryGetValue(childId, out childPos)) continue;
                if (!chapterPositions.TryGetValue(parentId, out parentPos)) continue;

                IntPtr clone = UnityObject.Instantiate(template, rootEdgesTransform);
                if (clone == IntPtr.Zero) continue;

                // Reset rotation — Instantiate inherits template's euler.
                if (Transform_localEulerAngles != null)
                {
                    IntPtr cloneTransform = GameObject.GetTransform(clone);
                    if (cloneTransform != IntPtr.Zero)
                    {
                        Vector3 zero = new Vector3(0f, 0f, 0f);
                        Transform_localEulerAngles.GetSetMethod().Invoke(
                            cloneTransform, new IntPtr[] { new IntPtr(&zero) });
                    }
                }

                // Force-enable Image (template may be disabled) + recolor:
                // green on path-to-clear, white off-path.
                if (Image_Type != IntPtr.Zero)
                {
                    IntPtr image = GameObject.GetComponent(clone, Image_Type);
                    if (image != IntPtr.Zero)
                    {
                        if (Behaviour_enabled != null)
                        {
                            csbool yes = true;
                            Behaviour_enabled.GetSetMethod().Invoke(
                                image, new IntPtr[] { new IntPtr(&yes) });
                        }
                        if (Image_color != null)
                        {
                            Color c = pathSet.Contains(childId) ? greenColor : whiteColor;
                            Image_color.GetSetMethod().Invoke(
                                image, new IntPtr[] { new IntPtr(&c) });
                        }
                    }
                }

                IntPtr cloneRt = GameObject.GetComponent(clone, RectTransform_Type);
                if (cloneRt == IntPtr.Zero) continue;

                // Edge pivot is (0, 0.5): anchoredPosition.x = left end,
                // anchoredPosition.y = vert center. Compensate per orientation.
                float midY = (parentPos.y + childPos.y) * 0.5f;
                float dx = Math.Abs(childPos.x - parentPos.x);
                float dy = Math.Abs(childPos.y - parentPos.y);
                AssetHelper.Vector2 anchored;
                AssetHelper.Vector2 size;
                if (dx >= dy)
                {
                    anchored = new AssetHelper.Vector2(Math.Min(parentPos.x, childPos.x), midY);
                    size = new AssetHelper.Vector2(dx, EdgeThickness);
                }
                else
                {
                    anchored = new AssetHelper.Vector2(parentPos.x - EdgeThickness * 0.5f, midY);
                    size = new AssetHelper.Vector2(EdgeThickness, dy);
                }

                RectTransform_anchoredPosition.GetSetMethod().Invoke(
                    cloneRt, new IntPtr[] { new IntPtr(&anchored) });
                RectTransform_sizeDelta.GetSetMethod().Invoke(
                    cloneRt, new IntPtr[] { new IntPtr(&size) });
                created++;
            }
            return created;
        }
    }
}
