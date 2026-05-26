using System;
using System.Collections.Generic;
using System.Linq;
using IL2CPP;
using UnityEngine;

namespace YgoMasterClient
{
    // Customizes CardPackOpenResultViewController when the pack belongs to us (the unified action
    // is an openpack). Keep mode: relabel OK -> Confirm; left-click sends ActionRespondPicks and
    // closes the VC. Pick mode: each CardPict is selectable; OK is gated on |sel| == pick.
    // Right-click and ESC are neutralized by clearing the SelectionButton's shortcut bindings,
    // so the only way to dismiss the result is via the OK button (left-click).
    static unsafe class RoguelikePackResultHook
    {
        static IL2Class _vcClass;
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hook;
        static IntPtr _selectionButtonType, _bindingTextType;
        static IL2Field _selBtnOnClick;
        static IL2Property _selBtnInteractable;
        static IL2Method _ueAddListener, _ueRemoveAll;
        static IL2Method _selBtnRemoveMouseDown, _selBtnRemoveMouseRelease;
        static IL2Method _selBtnRemoveKeyDown, _selBtnRemoveKeyRelease;
        // m_DrawDatas reorder: reflect into CardPackOpenResultViewController and the
        // IL2Dictionary<string, object> indexer so we can read "mrk"/"rarity" from each entry
        // and rewrite the list in our desired order BEFORE the native VC builds clones from it.
        static IL2Field _drawDatasField;
        static IL2Class _objectClass;
        static IL2Method _dictItemGet;

        // Pick-mode per-view state. _selected holds the index that matches both visual click
        // order AND server _cards order — because OnCreatedView reorders m_DrawDatas to mirror
        // the server sort BEFORE the native VC instantiates clones from it.
        static HashSet<int> _selected;
        static int _pickRequired;
        static IntPtr _okBtn;
        static List<IntPtr> _pictGos;        // GameObjects of each CardPict, in visual index order
        // Deferred wiring: OnCreatedView fires before the vanilla VC instantiates
        // PackCardGroupTemplate(Clone). We capture Content + the pick request here, and
        // Poll() (driven from RoguelikeMapScreen.Update) retries each frame until clones
        // appear, then wires them and clears the flag.
        static IntPtr _pendingContent;
        static bool _wirePending;

        // Pre-built Action[] of pick handlers — each entry is a closure-free lambda over a
        // literal index, so the C# compiler emits a cacheable STATIC method (no display class,
        // no captured `this`). This avoids the IL2CPP delegate-marshalling crash that closures
        // over local vars trigger: _UnityAction.CreateUnityAction routes to the "static method,
        // no this" path in DelegateHelper, which silently drops any captured target.
        // Cap chosen above any realistic pack size (5 in our smokes; bump if you ever roll more).
        const int MaxPickHandlers = 32;
        static readonly Action[] _pictHandlers = new Action[]
        {
            () => ToggleCard(0),  () => ToggleCard(1),  () => ToggleCard(2),  () => ToggleCard(3),
            () => ToggleCard(4),  () => ToggleCard(5),  () => ToggleCard(6),  () => ToggleCard(7),
            () => ToggleCard(8),  () => ToggleCard(9),  () => ToggleCard(10), () => ToggleCard(11),
            () => ToggleCard(12), () => ToggleCard(13), () => ToggleCard(14), () => ToggleCard(15),
            () => ToggleCard(16), () => ToggleCard(17), () => ToggleCard(18), () => ToggleCard(19),
            () => ToggleCard(20), () => ToggleCard(21), () => ToggleCard(22), () => ToggleCard(23),
            () => ToggleCard(24), () => ToggleCard(25), () => ToggleCard(26), () => ToggleCard(27),
            () => ToggleCard(28), () => ToggleCard(29), () => ToggleCard(30), () => ToggleCard(31),
        };

        static RoguelikePackResultHook()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                _vcClass = asm.GetClass("CardPackOpenResultViewController", "YgomGame.CardPack.OpenResult");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, _vcClass.GetMethod("OnCreatedView"));
                IL2Class selBtn = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selectionButtonType = selBtn.IL2Typeof();
                _selBtnOnClick = selBtn.GetField("onClick");
                _selBtnInteractable = selBtn.GetProperty("interactable");
                _selBtnRemoveMouseDown = selBtn.GetMethod("RemoveClickShortCutMouseDown");
                _selBtnRemoveMouseRelease = selBtn.GetMethod("RemoveClickShortCutMouseRelease");
                _selBtnRemoveKeyDown = selBtn.GetMethod("RemoveClickShortCutKeyDown", x => x.GetParameters().Length == 1);
                _selBtnRemoveKeyRelease = selBtn.GetMethod("RemoveClickShortCutKeyRelease", x => x.GetParameters().Length == 1);
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class ue = core.GetClass("UnityEvent", "UnityEngine.Events");
                _ueAddListener = ue.GetMethod("AddListener");
                _ueRemoveAll = core.GetClass("UnityEventBase", "UnityEngine.Events").GetMethod("RemoveAllListeners");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                _drawDatasField = _vcClass.GetField("m_DrawDatas");
                IL2Assembly mscor = Assembler.GetAssembly("mscorlib");
                _objectClass = mscor.GetClass("Object", "System");
                // Dictionary<string, object> indexer (get_Item) — resolve via generic dict class.
                IL2Class stringClass = mscor.GetClass("String", "System");
                IL2Class dictGeneric = mscor.GetClass("Dictionary`2", "System.Collections.Generic")
                    .MakeGenericType(new IntPtr[] { stringClass.IL2Typeof(), _objectClass.IL2Typeof() });
                _dictItemGet = dictGeneric.GetProperty("Item").GetGetMethod();
                Console.WriteLine("[Roguelike] pack result hook installed");
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack result hook init EX: " + ex); }
        }

        public static void EnsureRegistered() { /* trigger cctor */ }

        // Driven from RoguelikeMapScreen.Update once per frame. While we're in pick mode waiting
        // for the vanilla VC to instantiate PackCardGroupTemplate(Clone) -> PackCardTemplate(Clone)*
        // under Content, retry the enumeration. Wire on the first frame we find them and clear the
        // pending flag. Visual order matches server _cards order (we sorted m_DrawDatas in
        // OnCreatedView), so the visual click index doubles as the server pick index.
        public static void Poll()
        {
            if (!_wirePending || _pendingContent == IntPtr.Zero) return;
            _pictGos = new List<IntPtr>();
            EnumerateCardPicts(_pendingContent, _pictGos);
            if (_pictGos.Count == 0) return; // not populated yet, try next frame
            Console.WriteLine("[Roguelike] pack pick: wiring " + _pictGos.Count + " CardPicts");
            for (int i = 0; i < _pictGos.Count; i++)
            {
                IntPtr pict = _pictGos[i];
                if (i < MaxPickHandlers) WireButton(pict, _pictHandlers[i]);
                else Console.WriteLine("[Roguelike] pack pick: card " + i + " > MaxPickHandlers (" + MaxPickHandlers + "), not selectable");
                SetSelectVisual(pict, false);
            }
            _wirePending = false;
        }

        // Re-sort the VC's m_DrawDatas list (List<Dictionary<string, object>>) in place by
        // (rarity DESC, mrk ASC) so visual order == server _cards order. We treat the list as
        // an IL2ListExplicit of System.Object (the field's declared type), grab each entry's
        // raw IntPtr, read mrk/rarity via the Dictionary indexer, sort a parallel array of
        // (mrk, rarity, ptr), then write the ptrs back at the same positions.
        static void ReorderDrawDatas(IntPtr vcPtr)
        {
            if (_drawDatasField == null || _dictItemGet == null || _objectClass == null) return;
            IL2Object listObj = _drawDatasField.GetValue(vcPtr);
            if (listObj == null || listObj.ptr == IntPtr.Zero) return;
            IL2ListExplicit list = new IL2ListExplicit(listObj.ptr, _objectClass);
            int count = list.Count;
            if (count <= 1) return;
            int[] mrks = new int[count];
            int[] rars = new int[count];
            IntPtr[] ptrs = new IntPtr[count];
            for (int i = 0; i < count; i++)
            {
                ptrs[i] = list[i];
                mrks[i] = ReadDictInt(ptrs[i], "mrk");
                rars[i] = ReadDictInt(ptrs[i], "rarity");
            }
            int[] order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                if (rars[a] != rars[b]) return rars[b].CompareTo(rars[a]); // rarity DESC
                return mrks[a].CompareTo(mrks[b]);                          // mrk ASC tiebreaker
            });
            for (int i = 0; i < count; i++) list[i] = ptrs[order[i]];
            Console.WriteLine("[Roguelike] m_DrawDatas reordered (" + count + " entries) rarity DESC + mrk ASC");
        }

        // Read an int field from an IL2 Dictionary<string, object> by key. "mrk"/"rarity" arrive
        // as Int64-boxed (System.Object); GetValueRef<long> + downcast handles that.
        static int ReadDictInt(IntPtr dictPtr, string key)
        {
            if (dictPtr == IntPtr.Zero) return 0;
            IL2Object v = _dictItemGet.Invoke(dictPtr, new IntPtr[] { new IL2String(key).ptr, _dictItemGet.ptr });
            if (v == null || v.ptr == IntPtr.Zero) return 0;
            // Boxed long (MiniJSON deserializes ints as long). Unbox to long, cast to int.
            try { return (int)v.GetValueRef<long>(); } catch { }
            try { return v.GetValueRef<int>(); } catch { }
            return 0;
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            // Before the native VC consumes m_DrawDatas to instantiate PackCardGroupTemplate(Clone)
            // children, re-sort the list rarity DESC + mrk ASC. Server _cards is sorted the same
            // way, so visual click indices line up 1:1 with the server's _cards[] — no per-pict
            // m_Cardid lookup needed.
            try { ReorderDrawDatas(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack result hook reorder EX: " + ex); }
            _hook.Original(thisPtr);
            try
            {
                RoguelikeApi.PendingAction p = RoguelikeApi.GetPendingAction();
                if (p == null || p.Type != "openpack") return; // vanilla shop pack: untouched

                IntPtr go = Component.GetGameObject(thisPtr);

                // (1) Optional override of the "Cards obtidos" sub-label (UpperInfo>LabelRoot>LabelText).
                string title = p.Mode == "pick" ? p.TextLabels.TitlePick : p.TextLabels.TitleKeep;
                if (!string.IsNullOrEmpty(title))
                {
                    IntPtr upperInfo = GameObject.FindGameObjectByName(go, "UpperInfo");
                    IntPtr subLabel = upperInfo != IntPtr.Zero
                        ? GameObject.FindGameObjectByPath(upperInfo, "LabelRoot.LabelText")
                        : IntPtr.Zero;
                    if (subLabel != IntPtr.Zero) SetBindingText(subLabel, null, title);
                }

                // (2) Wire OK button: only left-click can fire OnOkClick. Right-click and ESC
                //     are neutralized by removing the SelectionButton's mouse + key shortcut bindings.
                IntPtr okBtn = GameObject.FindGameObjectByName(go, "OKButton");
                if (okBtn != IntPtr.Zero)
                {
                    if (!string.IsNullOrEmpty(p.TextLabels.Confirm))
                        SetBindingText(okBtn, "TextTMP", p.TextLabels.Confirm);
                    WireButton(okBtn, OnOkClick);
                    StripShortCuts(okBtn);
                }

                if (p.Mode != "pick") return;

                _selected = new HashSet<int>();
                _pickRequired = p.Pick;
                _okBtn = okBtn;
                SetButtonInteractable(okBtn, false);

                IntPtr obtainedRoot = GameObject.FindGameObjectByName(go, "ObtainedCardsRoot");
                _pendingContent = obtainedRoot != IntPtr.Zero
                    ? GameObject.FindGameObjectByPath(obtainedRoot, "CardAreaGroup.CardsScrollView.Viewport.Content")
                    : IntPtr.Zero;
                _pictGos = new List<IntPtr>();
                _wirePending = _pendingContent != IntPtr.Zero;
                if (!_wirePending) Console.WriteLine("[Roguelike] pack pick: no Content GO under ObtainedCardsRoot");
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack result hook EX: " + ex); }
        }

        // Only fires on legitimate left-click (right-click/ESC bindings were stripped).
        static void OnOkClick()
        {
            RoguelikeApi.PendingAction p = RoguelikeApi.GetPendingAction();
            if (p == null || p.Type != "openpack") return;
            int[] picks;
            if (p.Mode == "pick")
            {
                if (_selected == null || _selected.Count != _pickRequired) return; // gated; should not happen
                picks = _selected.OrderBy(i => i).ToArray();
            }
            else
            {
                picks = Enumerable.Range(0, p.Size).ToArray();
            }
            Console.WriteLine("[Roguelike] OK clicked, finalize (" + p.Mode + ") picks=[" + string.Join(",", picks) + "]");
            RoguelikeApi.ActionRespondPicks(p.Token, picks);
        }

        // Remove all mouse + key shortcut bindings on a SelectionButton so right-click,
        // ESC, and gamepad-B can't trigger its onClick.
        static void StripShortCuts(IntPtr buttonGo)
        {
            IntPtr sel = GameObject.GetComponent(buttonGo, _selectionButtonType);
            if (sel == IntPtr.Zero || _selBtnRemoveMouseDown == null) return;
            for (int mt = 0; mt < 8; mt++)
            {
                int v = mt;
                _selBtnRemoveMouseDown.Invoke(sel, new IntPtr[] { new IntPtr(&v) });
                _selBtnRemoveMouseRelease.Invoke(sel, new IntPtr[] { new IntPtr(&v) });
            }
            for (int kt = 0; kt < 64; kt++)
            {
                int v = kt;
                if (_selBtnRemoveKeyDown != null) _selBtnRemoveKeyDown.Invoke(sel, new IntPtr[] { new IntPtr(&v) });
                if (_selBtnRemoveKeyRelease != null) _selBtnRemoveKeyRelease.Invoke(sel, new IntPtr[] { new IntPtr(&v) });
            }
        }

        // Walk Content > PackCardGroupTemplate(Clone) > PackCardTemplate(Clone) > CardPict and collect
        // the CardPict GameObjects in order. Skip inactive template prefabs (`...Template` without
        // `(Clone)` suffix) — vanilla keeps them parented under Content as hidden source nodes.
        //
        // Type discipline: Transform.GetChild returns a Transform, NOT a GameObject. Never pass it
        // back through GameObject.GetTransform — that invokes GameObject.transform's getter on a
        // Transform pointer (UB in IL2CPP → crash). Use Component.GetGameObject to hop from Transform
        // to its owning GameObject.
        // Walk Content > PackCardGroupTemplate(Clone) > PackCardTemplate(Clone) > CardPict and
        // collect the active CardPict GameObjects. Skips: inactive template prefabs (no "(Clone)"
        // suffix), the SecretPulldownRoot sibling inside the group, and inactive PackCardTemplates
        // (the vanilla pool keeps spares inactive). Quiet on retry — only logs once we find > 0.
        static void EnumerateCardPicts(IntPtr content, List<IntPtr> outList)
        {
            if (content == IntPtr.Zero) return;
            IntPtr contentTf = GameObject.GetTransform(content);
            if (contentTf == IntPtr.Zero) return;
            int n = Transform.GetChildCount(contentTf);
            for (int i = 0; i < n; i++)
            {
                IntPtr groupTf = Transform.GetChild(contentTf, i);
                if (groupTf == IntPtr.Zero) continue;
                IntPtr groupGo = Component.GetGameObject(groupTf);
                if (groupGo == IntPtr.Zero) continue;
                string groupName = UnityObject.GetName(groupGo);
                // Only the cloned group holds the cards; the template prefab has no children of interest.
                if (!groupName.StartsWith("PackCardGroupTemplate") || !groupName.Contains("(Clone)")) continue;
                if (!GameObject.IsActive(groupGo)) continue;

                int m = Transform.GetChildCount(groupTf);
                for (int j = 0; j < m; j++)
                {
                    IntPtr cardTf = Transform.GetChild(groupTf, j);
                    if (cardTf == IntPtr.Zero) continue;
                    IntPtr cardGo = Component.GetGameObject(cardTf);
                    if (cardGo == IntPtr.Zero) continue;
                    string cardName = UnityObject.GetName(cardGo);
                    // Filter out SecretPulldownRoot (sibling under the group) + the inactive template.
                    if (!cardName.StartsWith("PackCardTemplate") || !cardName.Contains("(Clone)")) continue;
                    if (!GameObject.IsActive(cardGo)) continue;
                    IntPtr pict = GameObject.FindGameObjectByPath(cardGo, "CardPict");
                    if (pict != IntPtr.Zero) outList.Add(pict);
                }
            }
        }

        static void ToggleCard(int idx)
        {
            // idx is both the visual click index and the server _cards index — m_DrawDatas
            // was reordered in OnCreatedView to match server order before the clones were built.
            if (idx < 0 || _pictGos == null || idx >= _pictGos.Count) return;
            if (_selected.Contains(idx))
            {
                _selected.Remove(idx);
                SetSelectVisual(_pictGos[idx], false);
            }
            else
            {
                if (_selected.Count >= _pickRequired) return; // ignore clicks above limit (defensive)
                _selected.Add(idx);
                SetSelectVisual(_pictGos[idx], true);
            }
            SetButtonInteractable(_okBtn, _selected.Count == _pickRequired);
        }

        static void SetSelectVisual(IntPtr pict, bool on)
        {
            IntPtr pcCursor = GameObject.FindGameObjectByPath(pict, "SelectCursorCardForPC");
            if (pcCursor != IntPtr.Zero) GameObject.SetActive(pcCursor, on);
            IntPtr consoleCursor = GameObject.FindGameObjectByPath(pict, "SelectCursorForConsole");
            if (consoleCursor != IntPtr.Zero) GameObject.SetActive(consoleCursor, on);
        }

        static void SetButtonInteractable(IntPtr btn, bool on)
        {
            IntPtr sel = GameObject.GetComponent(btn, _selectionButtonType);
            if (sel == IntPtr.Zero || _selBtnInteractable == null) return;
            csbool v = on;
            _selBtnInteractable.GetSetMethod().Invoke(sel, new IntPtr[] { new IntPtr(&v) });
        }

        // Replace ALL existing listeners with `action`. Native onClick lambda is removed -
        // ours is the only handler, and shortcut bindings are stripped so only left-click fires it.
        static void WireButton(IntPtr buttonGo, Action action)
        {
            IntPtr sel = GameObject.GetComponent(buttonGo, _selectionButtonType);
            if (sel == IntPtr.Zero) return;
            IL2Object onClick = _selBtnOnClick.GetValue(sel);
            if (onClick == null) return;
            if (_ueRemoveAll != null) _ueRemoveAll.Invoke(onClick.ptr);
            IntPtr cb = UnityEngine.Events._UnityAction.CreateUnityAction(action);
            _ueAddListener.Invoke(onClick.ptr, new IntPtr[] { cb });
        }

        static void SetBindingText(IntPtr root, string path, string text)
        {
            IntPtr o = string.IsNullOrEmpty(path) ? root : GameObject.FindGameObjectByPath(root, path);
            if (o == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero) YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(binding, text);
        }
    }
}
