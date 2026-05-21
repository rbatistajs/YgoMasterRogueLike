using IL2CPP;
using System;
using UnityEngine;

namespace YgoMasterClient
{
    // Injects a "Roguelike" entry button into the Home menu by cloning an existing menu
    // button (ButtonShop) and placing it one slot below. HomeViewController.UpdateHome is
    // already hooked by HomeViewTweaks (MinHook allows one hook per target), so this is
    // driven from there via OnHomeUpdate.
    unsafe static class RoguelikeHomeButton
    {
        const string RootMenuPath = "HomeUI_Console(Clone).Root.SafeAreaMenu.RootMenu";
        const string TemplateButton = "ButtonShop";   // bottom menu button; clone sits below it
        const string AboveButton = "ButtonQuest";      // the button just above ButtonShop (for row spacing)
        const string Label = "ROGUELIKE";
        const string Description = "Enfrente a dungeon e derrote o boss final.";

        static IntPtr _selectionButtonType;
        static IL2Field _selectionButton_onClick;
        static IL2Method _unityEvent_AddListener;
        static IntPtr _tmpType;
        static IntPtr _bindingTextType;
        static IntPtr _rectTransformType;
        static IL2Property _rectAnchoredPos;
        static readonly Action _onClick = OnClick;
        static bool _ready;
        static bool _menuRunActive;

        static RoguelikeHomeButton()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                IL2Class selectionButton = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selectionButtonType = selectionButton.IL2Typeof();
                _selectionButton_onClick = selectionButton.GetField("onClick");
                _unityEvent_AddListener = core.GetClass("UnityEvent", "UnityEngine.Events").GetMethod("AddListener");
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                IL2Class rect = core.GetClass("RectTransform", "UnityEngine");
                _rectTransformType = rect.IL2Typeof();
                _rectAnchoredPos = rect.GetProperty("anchoredPosition");
                _ready = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Roguelike] home init EX: " + ex);
            }
        }

        public static void OnHomeUpdate(IntPtr homeViewPtr)
        {
            if (!_ready) return;
            try
            {
                IntPtr home = Component.GetGameObject(homeViewPtr);
                IntPtr rootMenu = GameObject.FindGameObjectByPath(home, RootMenuPath);
                if (rootMenu == IntPtr.Zero) return;
                if (GameObject.FindGameObjectByName(rootMenu, "ButtonRoguelike") != IntPtr.Zero) return; // idempotent

                IntPtr template = GameObject.FindGameObjectByName(rootMenu, TemplateButton);
                if (template == IntPtr.Zero) { Console.WriteLine("[Roguelike] template not found"); return; }

                IntPtr clone = UnityObject.Instantiate(template, GameObject.GetTransform(rootMenu));
                UnityObject.SetName(clone, "ButtonRoguelike");

                SetText(clone, "Out.Text", Label);
                SetText(clone, "Out.TextShadow", Label);
                SetText(clone, "Over.Mask.TextOver", Label);
                SetBoundText(clone, "GroupExplain.TextExplain", Description);

                PlaceBelow(clone, template, GameObject.FindGameObjectByName(rootMenu, AboveButton));

                IntPtr sel = GameObject.GetComponent(clone, _selectionButtonType);
                if (sel != IntPtr.Zero)
                {
                    IntPtr onClick = _selectionButton_onClick.GetValue(sel).ptr;
                    IntPtr cb = UnityEngine.Events._UnityAction.CreateUnityAction(_onClick);
                    _unityEvent_AddListener.Invoke(onClick, new IntPtr[] { cb });
                }
                Console.WriteLine("[Roguelike] button injected");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Roguelike] inject EX: " + ex);
            }
        }

        // Place `clone` one row below `bottom`, using (bottom - above) as the row height.
        // Only Y matters (the menu's TweenPositionTarget animates X only).
        static void PlaceBelow(IntPtr clone, IntPtr bottom, IntPtr above)
        {
            if (above == IntPtr.Zero) return;
            AssetHelper.Vector2 bottomPos = GetAnchored(bottom);
            AssetHelper.Vector2 abovePos = GetAnchored(above);
            float rowH = bottomPos.y - abovePos.y; // negative (each row is lower)
            SetAnchored(clone, new AssetHelper.Vector2(bottomPos.x, bottomPos.y + rowH));
        }

        static AssetHelper.Vector2 GetAnchored(IntPtr go)
        {
            IntPtr rt = GameObject.GetComponent(go, _rectTransformType);
            return _rectAnchoredPos.GetGetMethod().Invoke(rt).GetValueRef<AssetHelper.Vector2>();
        }

        static void SetAnchored(IntPtr go, AssetHelper.Vector2 pos)
        {
            IntPtr rt = GameObject.GetComponent(go, _rectTransformType);
            _rectAnchoredPos.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&pos) });
        }

        static void SetText(IntPtr root, string path, string text)
        {
            IntPtr go = GameObject.FindGameObjectByPath(root, path);
            if (go == IntPtr.Zero) return;
            IntPtr comp = GameObject.GetComponent(go, _tmpType);
            if (comp != IntPtr.Zero) TMPro.TMP_Text.SetText(comp, text);
        }

        // For data-bound texts (BindingTextMeshProUGUI) a literal string renders verbatim.
        static void SetBoundText(IntPtr root, string path, string text)
        {
            IntPtr go = GameObject.FindGameObjectByPath(root, path);
            if (go == IntPtr.Zero) return;
            IntPtr comp = GameObject.GetComponent(go, _bindingTextType);
            if (comp != IntPtr.Zero) YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(comp, text);
        }

        static void OnClick()
        {
            _menuRunActive = RoguelikeApi.IsRunActive();
            string[] entries = _menuRunActive
                ? new string[] { "Continuar Run", "Abandonar Run" }
                : new string[] { "Nova Run" };
            YgomGame.Menu.ActionSheetViewController.Open("Roguelike", entries, OnMenuSelect);
        }

        static void OnMenuSelect(IntPtr ctx, int index)
        {
            if (_menuRunActive)
            {
                if (index == 0) // Continuar Run
                {
                    if (!RoguelikeApi.IsDeckChosen())
                        RoguelikeFlow.OpenDeckSelect();
                    else
                        RoguelikeMapScreen.Open();
                }
                else if (index == 1) // Abandonar Run
                {
                    YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog("Abandonar Run",
                        "Tem certeza? A run atual sera perdida.", () => { RoguelikeApi.AbandonRun(); }, () => { });
                }
            }
            else if (index == 0) // Nova Run
            {
                RoguelikeApi.StartRun(); // deck-select sheet opens on start_run completion
            }
        }
    }
}
