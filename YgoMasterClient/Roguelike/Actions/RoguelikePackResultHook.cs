using IL2CPP;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YgoMasterClient
{
    // Customizes CardPackOpenResultViewController when the pack belongs to us (PendingPack active).
    // Keep mode: relabel OK -> Confirm; left-click sends pack_finalize and closes the VC.
    // Pick mode: each CardPict is selectable; OK is gated on |sel| == pick.
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

        // Pick-mode per-view state.
        static HashSet<int> _selected;       // indices into PendingPack.cards
        static int _pickRequired;
        static IntPtr _okBtn;
        static List<IntPtr> _pictGos;        // GameObjects of each CardPict, in index order

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
                Console.WriteLine("[Roguelike] pack result hook installed");
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack result hook init EX: " + ex); }
        }

        public static void EnsureRegistered() { /* trigger cctor */ }

        static void OnCreatedView(IntPtr thisPtr)
        {
            _hook.Original(thisPtr);
            try
            {
                RoguelikeApi.PendingPack p = RoguelikeApi.GetPendingPack();
                if (p == null) return; // vanilla shop pack: untouched

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
                IntPtr content = obtainedRoot != IntPtr.Zero
                    ? GameObject.FindGameObjectByPath(obtainedRoot, "CardAreaGroup.CardsScrollView.Viewport.Content")
                    : IntPtr.Zero;
                _pictGos = new List<IntPtr>();
                EnumerateCardPicts(content, _pictGos);

                for (int i = 0; i < _pictGos.Count; i++)
                {
                    int idx = i;
                    IntPtr pict = _pictGos[i];
                    WireButton(pict, () => ToggleCard(idx));
                    SetSelectVisual(pict, false);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack result hook EX: " + ex); }
        }

        // Only fires on legitimate left-click (right-click/ESC bindings were stripped).
        static void OnOkClick()
        {
            RoguelikeApi.PendingPack p = RoguelikeApi.GetPendingPack();
            if (p == null) return;
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
            RoguelikeApi.PackFinalize(p.Token, picks);
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

        static void EnumerateCardPicts(IntPtr content, List<IntPtr> outList)
        {
            // Content -> PackCardGroupTemplate(Clone) (N) -> PackCardTemplate(Clone) (M) -> CardPict
            IntPtr ct = GameObject.GetTransform(content);
            int n = Transform.GetChildCount(ct);
            for (int i = 0; i < n; i++)
            {
                IntPtr group = GameObject.GetTransform(Transform.GetChild(ct, i));
                if (group == IntPtr.Zero) continue;
                int m = Transform.GetChildCount(group);
                for (int j = 0; j < m; j++)
                {
                    IntPtr card = GameObject.GetTransform(Transform.GetChild(group, j));
                    if (card == IntPtr.Zero) continue;
                    IntPtr cardGo = Component.GetGameObject(card);
                    string name = UnityObject.GetName(cardGo);
                    if (!name.StartsWith("PackCardTemplate")) continue;
                    IntPtr pict = GameObject.FindGameObjectByPath(cardGo, "CardPict");
                    if (pict != IntPtr.Zero) outList.Add(pict);
                }
            }
        }

        static void ToggleCard(int idx)
        {
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
