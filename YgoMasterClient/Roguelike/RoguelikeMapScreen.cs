using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // The run map, rendered on the DeckEdit/DeckSelect screen (deck already chosen). Does NOT
    // hook the VC itself (MinHook = one hook per method; RoguelikeRunScreen owns the shared
    // DeckSelectViewController2 hooks and delegates here). Builds an RgMap container in the
    // Viewport with one node per map node (a clone of the deck-box "Template" frame), placed by
    // row/col; reachable nodes are clickable -> Move.
    static unsafe class RoguelikeMapScreen
    {
        const string Ui = "DeckSelectUI(Clone).Root.Window";
        const string DeckGroup = Ui + ".MainArea.DeckArea.DeckGroup";
        const string ScrollView = DeckGroup + ".Scroll View2";
        const string DeckContent = ScrollView + ".Viewport.Content";
        const string RgScrollViewPath = DeckGroup + ".RgScrollView";
        const string RgMapPath = RgScrollViewPath + ".RgViewport.RgMap";

        const float EdgeThickness = 6f;
        const float ColStep = 180f, RowStep = 180f; // 1:1 cell spacing

        static IntPtr _rectType, _rectMask2DType;
        static IL2Property _anchorMin, _anchorMax, _pivot, _anchoredPos3D, _sizeDelta, _localEuler, _rectRect;
        static IL2Method _scrollByTargetPos;
        static float _scrollTargetY;
        static bool _hasScrollTarget;
        static IntPtr _selectionButtonType, _colorContainerType;
        static IL2Field _selBtnOnClick;
        static IL2Property _behaviourEnabled;

        // Node frame colors by state (Out = outline, Bg = rounded fill).
        static readonly Col OutDisabled = new Col { r = 0.30f, g = 0.30f, b = 0.36f, a = 1f };
        static readonly Col OutVisited  = new Col { r = 0.82f, g = 0.85f, b = 0.95f, a = 1f };
        static readonly Col OutActive   = new Col { r = 0.90f, g = 0.95f, b = 1.00f, a = 1f };
        static readonly Col BgDisabled  = new Col { r = 0.04f, g = 0.04f, b = 0.06f, a = 0.90f };
        static readonly Col BgVisited   = new Col { r = 0.11f, g = 0.13f, b = 0.18f, a = 0.88f };
        static readonly Col BgActive    = new Col { r = 0.15f, g = 0.19f, b = 0.27f, a = 0.92f };
        static IL2Method _ueAddListener, _ueRemoveAll;
        static IntPtr _scrollRectType, _extScrollRectType, _imageType;
        static IL2Property _scrollRectContent, _scrollViewport, _scrollHorizontal, _scrollVertical, _scrollSensitivity, _graphicColor;
        static IL2Field _extDragScrollEnabled;
        static IntPtr _tweenScaleType;
        static IL2Field _tsFrom, _tsTo, _tStyle, _tDuration, _tTarget;
        static IL2Method _tPlay;
        static IntPtr _tweenPositionType;
        static IL2Field _tpRtrans, _tpFrom, _tpTo;
        static IntPtr _go;       // the current map VC GameObject (for in-place refresh)
        static IntPtr _extScroll; // our RgScrollView's ExtendedScrollRect (identity for the Start hook)
        static IntPtr _markerSource; // cached PlayerIcon clone (Home is inactive once a run is open)
        static bool _ready;

        // ExtendedScrollRect.Start()/Initialize() resets dragScrollEnabled to false after our
        // OnCreatedView set, so re-assert it post-Start. Global hook (one per method); only our
        // instance is forced.
        delegate void Del_Start(IntPtr thisPtr);
        static Hook<Del_Start> _hookExtStart;

        struct Col { public float r, g, b, a; }

        // Map-click dispatch: CreateUnityAction only bridges captureless delegates, so each
        // reachable node wires a fixed slot lambda; slot->nodeId is rebuilt per render.
        const int MapSlots = 16;
        static readonly int[] _slotNodeId = new int[MapSlots];
        static readonly Action[] _slotActions =
        {
            () => OnSlot(0), () => OnSlot(1), () => OnSlot(2), () => OnSlot(3),
            () => OnSlot(4), () => OnSlot(5), () => OnSlot(6), () => OnSlot(7),
            () => OnSlot(8), () => OnSlot(9), () => OnSlot(10), () => OnSlot(11),
            () => OnSlot(12), () => OnSlot(13), () => OnSlot(14), () => OnSlot(15),
        };

        static RoguelikeMapScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Assembly ui = Assembler.GetAssembly("UnityEngine.UI");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rect.IL2Typeof();
                _anchorMin = rect.GetProperty("anchorMin");
                _anchorMax = rect.GetProperty("anchorMax");
                _pivot = rect.GetProperty("pivot");
                _anchoredPos3D = rect.GetProperty("anchoredPosition3D");
                _sizeDelta = rect.GetProperty("sizeDelta");
                _rectRect = rect.GetProperty("rect");
                _localEuler = core.GetClass("Transform", "UnityEngine").GetProperty("localEulerAngles");
                IL2Class selBtn = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selectionButtonType = selBtn.IL2Typeof();
                _selBtnOnClick = selBtn.GetField("onClick");
                _colorContainerType = asm.GetClass("ColorContainerGraphic", "YgomSystem.UI").IL2Typeof();
                _ueAddListener = core.GetClass("UnityEvent", "UnityEngine.Events").GetMethod("AddListener");
                _ueRemoveAll = core.GetClass("UnityEventBase", "UnityEngine.Events").GetMethod("RemoveAllListeners");
                _behaviourEnabled = core.GetClass("Behaviour", "UnityEngine").GetProperty("enabled");
                IL2Class scrollRect = ui.GetClass("ScrollRect", "UnityEngine.UI");
                _scrollRectType = scrollRect.IL2Typeof();
                _scrollRectContent = scrollRect.GetProperty("content");
                _scrollViewport = scrollRect.GetProperty("viewport");
                _scrollHorizontal = scrollRect.GetProperty("horizontal");
                _scrollVertical = scrollRect.GetProperty("vertical");
                _scrollSensitivity = scrollRect.GetProperty("scrollSensitivity");
                IL2Class extScroll = asm.GetClass("ExtendedScrollRect", "YgomSystem.UI");
                _extScrollRectType = extScroll.IL2Typeof();
                _extDragScrollEnabled = extScroll.GetField("dragScrollEnabled");
                _scrollByTargetPos = extScroll.GetMethod("ScrollByTargetPos");
                _hookExtStart = new Hook<Del_Start>(OnExtScrollStart, extScroll.GetMethod("Start"));
                IL2Class tweenScale = asm.GetClass("TweenScale", "YgomSystem.UI");
                _tweenScaleType = tweenScale.IL2Typeof();
                _tsFrom = tweenScale.GetField("from");
                _tsTo = tweenScale.GetField("to");
                IL2Class tween = asm.GetClass("Tween", "YgomSystem.UI");
                _tStyle = tween.GetField("style");
                _tDuration = tween.GetField("duration");
                _tTarget = tween.GetField("target");
                _tPlay = tween.GetMethod("Play");
                IL2Class tweenPos = asm.GetClass("TweenPosition", "YgomSystem.UI");
                _tweenPositionType = tweenPos.IL2Typeof();
                _tpRtrans = tweenPos.GetField("rtrans");
                _tpFrom = tweenPos.GetField("from");
                _tpTo = tweenPos.GetField("to");
                _rectMask2DType = ui.GetClass("RectMask2D", "UnityEngine.UI").IL2Typeof();
                _imageType = ui.GetClass("Image", "UnityEngine.UI").IL2Typeof();
                _graphicColor = ui.GetClass("Graphic", "UnityEngine.UI").GetProperty("color");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] mapscreen init EX: " + ex); }
        }

        // Clone the Home PlayerIcon while Home is still active (it gets deactivated once a run
        // screen is open, so GameObject.Find can't reach it at map-build time). Call from the home
        // context (e.g. when start_run completes). Idempotent; the clone persists for the session.
        public static void CaptureMarkerSource()
        {
            if (!_ready || _markerSource != IntPtr.Zero) return;
            try
            {
                IntPtr home = GameObject.Find("Home");
                IntPtr src = home != IntPtr.Zero ? GameObject.FindGameObjectByName(home, "PlayerIcon") : IntPtr.Zero;
                if (src == IntPtr.Zero) return;
                IntPtr clone = UnityObject.Instantiate(src);
                UnityObject.SetName(clone, "RgMarkerSource");
                UnityObject.DontDestroyOnLoad(clone);
                GameObject.SetActive(clone, false);
                _markerSource = clone;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] CaptureMarkerSource EX: " + ex); }
        }

        // Called by the host (RoguelikeRunScreen) once the DeckSelect view is created in map mode.
        public static void Build(IntPtr go)
        {
            if (!_ready) return;
            _go = go;
            try { SetupMap(go); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] map build EX: " + ex); }
        }

        // Re-render after a move. Keep the scroll container (so the scroll position is preserved
        // and ScrollByTargetPos animates from where the player was, not from a rebuilt top);
        // only the nodes/edges inside RgMap are rebuilt.
        public static void Refresh()
        {
            if (!_ready || _go == IntPtr.Zero) return;
            try
            {
                IntPtr sv = GameObject.FindGameObjectByPath(_go, RgScrollViewPath);
                IntPtr ctGo = GameObject.FindGameObjectByPath(_go, RgMapPath);
                IntPtr srComp = sv != IntPtr.Zero ? GameObject.GetComponent(sv, _extScrollRectType) : IntPtr.Zero;
                IntPtr content = GameObject.FindGameObjectByPath(_go, DeckContent);
                IntPtr template = content != IntPtr.Zero ? GameObject.FindGameObjectByName(content, "Template") : IntPtr.Zero;
                IntPtr deckGroup = GameObject.FindGameObjectByPath(_go, DeckGroup);
                if (ctGo == IntPtr.Zero || srComp == IntPtr.Zero || template == IntPtr.Zero || deckGroup == IntPtr.Zero)
                {
                    if (sv != IntPtr.Zero) UnityObject.Destroy(sv);
                    SetupMap(_go);
                    return;
                }
                DestroyChildrenExcept(ctGo, "RgPlayerMarker");        // keep the marker so it can animate
                RenderMap(template, GameObject.GetTransform(ctGo), srComp, deckGroup); // RenderMap takes the Transform
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] map refresh EX: " + ex); }
        }

        // Runs for every ExtendedScrollRect; re-assert dragScrollEnabled + scroll to the current
        // node only on ours (Start/Initialize clears drag and resets the scroll position after our
        // OnCreatedView build).
        static void OnExtScrollStart(IntPtr thisPtr)
        {
            _hookExtStart.Original(thisPtr);
            if (thisPtr != _extScroll) return;
            if (_extDragScrollEnabled != null)
            {
                csbool drag = true;
                _extDragScrollEnabled.SetValue(thisPtr, new IntPtr(&drag));
            }
            ApplyScroll(thisPtr);
        }

        static void OnSlot(int slot)
        {
            if (slot < 0 || slot >= MapSlots) return;
            int id = _slotNodeId[slot];
            if (id >= 0) RoguelikeApi.Move(id);
        }

        // Build OUR OWN scroll: RgScrollView > RgViewport > RgMap inside the DeckGroup, and hide
        // the game's Scroll View2 (its ExtendedScrollRect/InfinityScroll resize our content). A
        // plain Unity ScrollRect we fully control scrolls RgMap (one node per map node).
        static void SetupMap(IntPtr go)
        {
            IntPtr content = GameObject.FindGameObjectByPath(go, DeckContent);
            IntPtr template = content != IntPtr.Zero ? GameObject.FindGameObjectByName(content, "Template") : IntPtr.Zero;
            if (template == IntPtr.Zero) { Console.WriteLine("[Roguelike] map: template not found"); return; }
            IntPtr deckGroup = GameObject.FindGameObjectByPath(go, DeckGroup);
            if (deckGroup == IntPtr.Zero) { Console.WriteLine("[Roguelike] map: DeckGroup not found"); return; }

            Hide(go, ScrollView); // hide the game's scroll (its InfinityScroll resized our content)

            // RgScrollView (fills DeckGroup) + ScrollRect.
            IntPtr sv = GameObject.New();
            UnityObject.SetName(sv, "RgScrollView");
            GameObject.AddComponent(sv, _rectType);
            IntPtr svt = GameObject.GetTransform(sv);
            Transform.SetParent(svt, GameObject.GetTransform(deckGroup));
            SetFill(svt);
            Transform.SetLocalScale(svt, new Vector3(1, 1, 1));
            // Transparent Image on the root = raycast target so drag/wheel scrolls (matches the
            // game's Scroll View2, which has its Image on the root, not the viewport).
            IntPtr svImg = GameObject.AddComponent(sv, _imageType);
            Col clear = new Col { r = 1, g = 1, b = 1, a = 0 };
            _graphicColor.GetSetMethod().Invoke(svImg, new IntPtr[] { new IntPtr(&clear) });
            IntPtr srComp = GameObject.AddComponent(sv, _extScrollRectType); // game's ScrollRect subclass
            _extScroll = srComp; // identity for the Start hook (re-asserts dragScrollEnabled)

            // RgViewport (fills RgScrollView) + RectMask2D to clip.
            IntPtr vp = GameObject.New();
            UnityObject.SetName(vp, "RgViewport");
            GameObject.AddComponent(vp, _rectType);
            GameObject.AddComponent(vp, _rectMask2DType);
            IntPtr vpt = GameObject.GetTransform(vp);
            Transform.SetParent(vpt, svt);
            SetFill(vpt);
            Transform.SetLocalScale(vpt, new Vector3(1, 1, 1));

            // RgMap (content): full width, top-anchored. Height + children come from RenderMap.
            IntPtr container = GameObject.New();
            UnityObject.SetName(container, "RgMap");
            GameObject.AddComponent(container, _rectType);
            IntPtr ct = GameObject.GetTransform(container);
            Transform.SetParent(ct, vpt);
            SetVec(ct, _anchorMin, new AssetHelper.Vector2(0, 1));
            SetVec(ct, _anchorMax, new AssetHelper.Vector2(1, 1));
            SetVec(ct, _pivot, new AssetHelper.Vector2(0.5f, 1));
            Vector3 z0 = new Vector3(0, 0, 0);
            _anchoredPos3D.GetSetMethod().Invoke(ct, new IntPtr[] { new IntPtr(&z0) });
            Transform.SetLocalScale(ct, new Vector3(1, 1, 1));

            // Wire the ScrollRect (vertical only).
            _scrollViewport.GetSetMethod().Invoke(srComp, new IntPtr[] { vpt });
            _scrollRectContent.GetSetMethod().Invoke(srComp, new IntPtr[] { ct });
            SetBoolProp(srComp, _scrollHorizontal, false);
            SetBoolProp(srComp, _scrollVertical, true);
            float sens = 60f; // match the game's Scroll View2 (default 1 = imperceptible wheel)
            _scrollSensitivity.GetSetMethod().Invoke(srComp, new IntPtr[] { new IntPtr(&sens) });
            if (_extDragScrollEnabled != null) { csbool drag = true; _extDragScrollEnabled.SetValue(srComp, new IntPtr(&drag)); }
            else Console.WriteLine("[Roguelike] map: ExtendedScrollRect.dragScrollEnabled field not found");

            RenderMap(template, ct, srComp, deckGroup);
        }

        // (Re)build the nodes/edges/marker inside RgMap from the current run state and scroll to the
        // current node. Container is left intact so the scroll position carries across moves.
        static void RenderMap(IntPtr template, IntPtr ct, IntPtr srComp, IntPtr deckGroup)
        {
            List<RoguelikeApi.MapNode> nodes = RoguelikeApi.GetMapNodes();
            int pos = RoguelikeApi.Position();
            HashSet<int> reachable = Reachable(nodes, pos);
            HashSet<int> visited = RoguelikeApi.Visited();
            int rows = 0, cols = 0;
            foreach (RoguelikeApi.MapNode n in nodes)
            {
                if (n.Row + 1 > rows) rows = n.Row + 1;
                if (n.Col + 1 > cols) cols = n.Col + 1;
            }
            float contentHeight = rows * RowStep + RowStep * 2; // padding top+bottom
            SetVec(ct, _sizeDelta, new AssetHelper.Vector2(0, contentHeight));

            DrawEdges(ct, nodes, rows, cols, visited);

            int slot = 0;
            float curX = 0, curY = RowY(-1, rows); // default: entry, below row 0
            foreach (RoguelikeApi.MapNode n in nodes)
            {
                IntPtr node = UnityObject.Instantiate(template, ct);
                UnityObject.SetName(node, "RgNode" + n.Id);
                GameObject.SetActive(node, true);
                KeepFrame(node);
                float x = ColX(n.Col, cols);
                float y = RowY(n.Row, rows);
                PlaceNode(node, x, y, new AssetHelper.Vector2(120, 120));

                bool isCurrent = n.Id == pos;
                bool isVisited = visited.Contains(n.Id);
                bool isOpen = reachable.Contains(n.Id) && slot < MapSlots;
                if (isCurrent) { curX = x; curY = y; }
                if (isOpen)
                {
                    _slotNodeId[slot] = n.Id;
                    WireNodeClick(node, _slotActions[slot]);
                    slot++;
                }
                StyleNode(node, isOpen, isVisited);
            }

            // First build creates the marker at the spot; later renders keep it and animate it over.
            IntPtr marker = GameObject.FindGameObjectByName(Component.GetGameObject(ct), "RgPlayerMarker");
            if (marker == IntPtr.Zero) PlacePlayerMarker(ct, curX, curY);
            else AnimateMarker(marker, curX, curY);

            float vh = RectHeight(deckGroup);
            _scrollTargetY = ScrollTargetY(contentHeight, curY, vh);
            _hasScrollTarget = vh > 0;
            ApplyScroll(srComp); // re-applied from the Start hook on first build (post Initialize)

            Console.WriteLine("[Roguelike] map nodes=" + nodes.Count + " rows=" + rows + " contentH=" + contentHeight + " pos=" + pos + " open=" + slot);
        }

        // Looping ping-pong scale on the choosable (reachable) nodes so the next moves stand out.
        static void PulseNode(IntPtr node)
        {
            if (_tweenScaleType == IntPtr.Zero || _tPlay == null) return;
            IntPtr tw = GameObject.GetComponent(node, _tweenScaleType); // reuse so re-styling doesn't stack tweens
            if (tw == IntPtr.Zero) tw = GameObject.AddComponent(node, _tweenScaleType);
            if (tw == IntPtr.Zero) return;
            Vector3 from = new Vector3(1, 1, 1);
            Vector3 to = new Vector3(1.12f, 1.12f, 1.12f);
            if (_tsFrom != null) _tsFrom.SetValue(tw, new IntPtr(&from));
            if (_tsTo != null) _tsTo.SetValue(tw, new IntPtr(&to));
            float dur = 0.6f;
            if (_tDuration != null) _tDuration.SetValue(tw, new IntPtr(&dur));
            int style = 3; // Tween.Style.PingPongLoop
            if (_tStyle != null) _tStyle.SetValue(tw, new IntPtr(&style));
            if (_tTarget != null) _tTarget.SetValue(tw, node);
            _tPlay.Invoke(tw);
        }

        // Color the Out outline + BG fill by state, after disabling the tile's ColorContainerGraphic
        // (which would otherwise re-apply the theme color over ours). Reachable nodes also pulse and
        // keep their SelectionButton; the rest are disabled so hover/click can't recolor them.
        static void StyleNode(IntPtr node, bool isOpen, bool isVisited)
        {
            IntPtr outFrame = GameObject.FindGameObjectByPath(node, "Body.Out");
            IntPtr bg = GameObject.FindGameObjectByPath(node, "Body.BG");
            DisableColorContainers(outFrame);
            DisableColorContainers(bg);
            if (isOpen)
            {
                PulseNode(node);
                SetImageColor(outFrame, OutActive);
                SetImageColor(bg, BgActive);
            }
            else
            {
                DisableButton(node);
                SetImageColor(outFrame, isVisited ? OutVisited : OutDisabled);
                SetImageColor(bg, isVisited ? BgVisited : BgDisabled);
            }
        }

        // Turn off a node's SelectionButton so hover/click can't play its recolor tweens.
        static void DisableButton(IntPtr node)
        {
            if (_behaviourEnabled == null) return;
            IntPtr body = GameObject.FindGameObjectByPath(node, "Body");
            IntPtr sel = body != IntPtr.Zero ? GameObject.GetComponent(body, _selectionButtonType) : IntPtr.Zero;
            if (sel == IntPtr.Zero) return;
            csbool off = false;
            _behaviourEnabled.GetSetMethod().Invoke(sel, new IntPtr[] { new IntPtr(&off) });
        }

        // Disable every ColorContainerGraphic on a GameObject so our Graphic.color stops being
        // overwritten by the theme palette.
        static void DisableColorContainers(IntPtr go)
        {
            if (go == IntPtr.Zero || _colorContainerType == IntPtr.Zero || _behaviourEnabled == null) return;
            IntPtr[] comps = GameObject.GetComponents(go, _colorContainerType);
            if (comps == null) return;
            csbool off = false;
            foreach (IntPtr c in comps) _behaviourEnabled.GetSetMethod().Invoke(c, new IntPtr[] { new IntPtr(&off) });
        }

        static void SetImageColor(IntPtr go, Col c)
        {
            IntPtr img = go != IntPtr.Zero ? GameObject.GetComponent(go, _imageType) : IntPtr.Zero;
            if (img != IntPtr.Zero) _graphicColor.GetSetMethod().Invoke(img, new IntPtr[] { new IntPtr(&c) });
        }

        static float RectHeight(IntPtr go)
        {
            IntPtr rt = GameObject.GetComponent(go, _rectType);
            if (rt == IntPtr.Zero || _rectRect == null) return 0;
            AssetHelper.Rect r = _rectRect.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Rect>();
            return r.m_Height;
        }

        // Content anchoredPosition.y that centers nodeY in a viewport of height vh (content is
        // top-anchored, pivot top), clamped to the scrollable range. Mirrors the game's own math.
        static float ScrollTargetY(float contentHeight, float nodeY, float vh)
        {
            if (vh <= 0) return 0;
            float y = (contentHeight / 2f - nodeY) - vh / 2f;
            if (contentHeight <= vh) return 0;
            if (y < 0) return 0;
            if (y > contentHeight - vh) return contentHeight - vh;
            return y;
        }

        static void ApplyScroll(IntPtr scroll)
        {
            if (!_hasScrollTarget || _scrollByTargetPos == null || scroll == IntPtr.Zero) return;
            AssetHelper.Vector2 target = new AssetHelper.Vector2(0, _scrollTargetY);
            _scrollByTargetPos.Invoke(scroll, new IntPtr[] { new IntPtr(&target) });
        }

        static float ColX(int col, int cols) { return (col - (cols - 1) / 2f) * ColStep; }
        static float RowY(int row, int rows) { return (row - (rows - 1) / 2f) * RowStep; }

        // One thin Image per node->Next link, drawn behind the nodes (called before the node loop).
        // Links between two visited nodes are the traveled path -> highlighted.
        static void DrawEdges(IntPtr ct, List<RoguelikeApi.MapNode> nodes, int rows, int cols, HashSet<int> visited)
        {
            Dictionary<int, RoguelikeApi.MapNode> byId = new Dictionary<int, RoguelikeApi.MapNode>();
            foreach (RoguelikeApi.MapNode n in nodes) byId[n.Id] = n;
            Col idle = new Col { r = 0.8f, g = 0.82f, b = 0.88f, a = 0.5f };
            Col walked = new Col { r = 0.35f, g = 0.85f, b = 0.45f, a = 0.95f };
            foreach (RoguelikeApi.MapNode n in nodes)
            {
                float x1 = ColX(n.Col, cols);
                float y1 = RowY(n.Row, rows);
                foreach (int nextId in n.Next)
                {
                    RoguelikeApi.MapNode m;
                    if (!byId.TryGetValue(nextId, out m)) continue;
                    bool onPath = visited.Contains(n.Id) && visited.Contains(m.Id);
                    MakeEdge(ct, x1, y1, ColX(m.Col, cols), RowY(m.Row, rows), onPath ? walked : idle);
                }
            }
        }

        // A line between two points = a thin Image at the midpoint, rotated to the angle.
        static void MakeEdge(IntPtr ct, float x1, float y1, float x2, float y2, Col c)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;
            float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);

            IntPtr edge = GameObject.New();
            UnityObject.SetName(edge, "RgEdge");
            GameObject.AddComponent(edge, _rectType);
            IntPtr img = GameObject.AddComponent(edge, _imageType);
            _graphicColor.GetSetMethod().Invoke(img, new IntPtr[] { new IntPtr(&c) });

            IntPtr t = GameObject.GetTransform(edge);
            Transform.SetParent(t, ct);
            AssetHelper.Vector2 center = new AssetHelper.Vector2(0.5f, 0.5f);
            _anchorMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&center) });
            _anchorMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&center) });
            _pivot.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&center) });
            AssetHelper.Vector2 size = new AssetHelper.Vector2(len, EdgeThickness);
            _sizeDelta.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&size) });
            Vector3 mid = new Vector3((x1 + x2) / 2f, (y1 + y2) / 2f, 0);
            _anchoredPos3D.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&mid) });
            Vector3 euler = new Vector3(0, 0, ang);
            _localEuler.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&euler) });
            Transform.SetLocalScale(t, new Vector3(1, 1, 1));
        }

        // Clone the Home view's PlayerIcon (renders the player's profile icon via
        // BindingProfileFrameIcon) and drop it on the current node as the position marker.
        static void PlacePlayerMarker(IntPtr ct, float x, float y)
        {
            IntPtr src = _markerSource;
            if (src == IntPtr.Zero)
            {
                IntPtr home = GameObject.Find("Home"); // fallback (only works while Home is active)
                src = home != IntPtr.Zero ? GameObject.FindGameObjectByName(home, "PlayerIcon") : IntPtr.Zero;
            }
            if (src == IntPtr.Zero) { Console.WriteLine("[Roguelike] map: PlayerIcon source not found"); return; }
            IntPtr icon = UnityObject.Instantiate(src, ct);
            UnityObject.SetName(icon, "RgPlayerMarker");
            GameObject.SetActive(icon, true);
            PlaceNode(icon, x, y, new AssetHelper.Vector2(96, 96));
            Transform.SetAsLastSibling(GameObject.GetTransform(icon)); // draw above nodes
        }

        // Slide the (preserved) player marker from where it sits to the new current node.
        static void AnimateMarker(IntPtr marker, float toX, float toY)
        {
            Transform.SetAsLastSibling(GameObject.GetTransform(marker)); // keep above the rebuilt nodes
            IntPtr rt = GameObject.GetComponent(marker, _rectType);
            if (rt == IntPtr.Zero) return;
            Vector3 to = new Vector3(toX, toY, 0);
            if (_tweenPositionType == IntPtr.Zero || _tPlay == null)
            {
                _anchoredPos3D.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&to) }); // no tween: snap
                return;
            }
            Vector3 from = _anchoredPos3D.GetGetMethod().Invoke(rt).GetValueRef<Vector3>();
            IntPtr tw = GameObject.GetComponent(marker, _tweenPositionType); // reuse to avoid stacking tweens
            if (tw == IntPtr.Zero) tw = GameObject.AddComponent(marker, _tweenPositionType);
            if (tw == IntPtr.Zero) return;
            if (_tpRtrans != null) _tpRtrans.SetValue(tw, rt);
            if (_tpFrom != null) _tpFrom.SetValue(tw, new IntPtr(&from));
            if (_tpTo != null) _tpTo.SetValue(tw, new IntPtr(&to));
            float dur = 0.35f;
            if (_tDuration != null) _tDuration.SetValue(tw, new IntPtr(&dur));
            int style = 0; // Tween.Style.Once
            if (_tStyle != null) _tStyle.SetValue(tw, new IntPtr(&style));
            _tPlay.Invoke(tw);
        }

        static void DestroyChildrenExcept(IntPtr go, string keepName)
        {
            IntPtr t = GameObject.GetTransform(go);
            int n = Transform.GetChildCount(t);
            for (int i = n - 1; i >= 0; i--)
            {
                IntPtr child = Transform.GetChild(t, i);
                if (child == IntPtr.Zero) continue;
                IntPtr cgo = Component.GetGameObject(child);
                if (UnityObject.GetName(cgo) != keepName) UnityObject.Destroy(cgo);
            }
        }

        // Stretch a RectTransform to fill its parent.
        static void SetFill(IntPtr t)
        {
            SetVec(t, _anchorMin, new AssetHelper.Vector2(0, 0));
            SetVec(t, _anchorMax, new AssetHelper.Vector2(1, 1));
            SetVec(t, _sizeDelta, new AssetHelper.Vector2(0, 0));
            Vector3 z = new Vector3(0, 0, 0);
            _anchoredPos3D.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&z) });
        }

        static void SetBoolProp(IntPtr comp, IL2Property p, bool v)
        {
            if (p == null) return;
            csbool b = v;
            p.GetSetMethod().Invoke(comp, new IntPtr[] { new IntPtr(&b) });
        }

        // Nodes the player may move to from `pos` (-1 = entry, before row 0).
        static HashSet<int> Reachable(List<RoguelikeApi.MapNode> nodes, int pos)
        {
            HashSet<int> set = new HashSet<int>();
            if (pos < 0)
            {
                foreach (RoguelikeApi.MapNode n in nodes) if (n.Row == 0) set.Add(n.Id);
            }
            else
            {
                foreach (RoguelikeApi.MapNode n in nodes)
                    if (n.Id == pos) { foreach (int next in n.Next) set.Add(next); break; }
            }
            return set;
        }

        static void WireNodeClick(IntPtr node, Action action)
        {
            IntPtr body = GameObject.FindGameObjectByPath(node, "Body");
            IntPtr sel = body == IntPtr.Zero ? IntPtr.Zero : GameObject.GetComponent(body, _selectionButtonType);
            if (sel == IntPtr.Zero) return;
            IL2Object onClickObj = _selBtnOnClick.GetValue(sel);
            if (onClickObj == null) return;
            if (_ueRemoveAll != null) _ueRemoveAll.Invoke(onClickObj.ptr);
            IntPtr cb = UnityEngine.Events._UnityAction.CreateUnityAction(action);
            _ueAddListener.Invoke(onClickObj.ptr, new IntPtr[] { cb });
        }

        // Keep the "Out" outline + "BG" rounded fill of a cloned deck-box tile (hide case, text,
        // icons, ...). BG gives the node a colored background per state.
        static void KeepFrame(IntPtr node)
        {
            IntPtr body = GameObject.FindGameObjectByPath(node, "Body");
            if (body == IntPtr.Zero) return;
            IntPtr bt = GameObject.GetTransform(body);
            int n = Transform.GetChildCount(bt);
            for (int i = 0; i < n; i++)
            {
                IntPtr child = Transform.GetChild(bt, i);
                if (child == IntPtr.Zero) continue;
                IntPtr cgo = Component.GetGameObject(child);
                string name = UnityObject.GetName(cgo);
                GameObject.SetActive(cgo, name == "Out" || name == "BG");
            }
        }

        static void PlaceNode(IntPtr node, float x, float y, AssetHelper.Vector2 size)
        {
            IntPtr t = GameObject.GetTransform(node);
            AssetHelper.Vector2 c = new AssetHelper.Vector2(0.5f, 0.5f);
            _anchorMin.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) });
            _anchorMax.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) });
            _pivot.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&c) }); // center pivot so (x,y) = node center
            _sizeDelta.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&size) });
            Vector3 p = new Vector3(x, y, 0);
            _anchoredPos3D.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&p) });
            Transform.SetLocalScale(t, new Vector3(1, 1, 1));
        }

        static void SetVec(IntPtr t, IL2Property prop, AssetHelper.Vector2 v)
        {
            prop.GetSetMethod().Invoke(t, new IntPtr[] { new IntPtr(&v) });
        }

        static void Hide(IntPtr go, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(go, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }
    }
}
