using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Host for the roguelike screens built on DeckEdit/DeckSelect (DeckSelectViewController2).
    // Owns the shared VC hooks (one hook per method — MinHook) and dispatches by run state:
    //  - no deck chosen -> deck-choice (this class): replaces m_Decks with our offers and wires
    //    the click to a Ver deck / Selecionar drawer.
    //  - deck chosen    -> RoguelikeMapScreen.Build (the map).
    // The footer "Abandonar Run" is shared (set up here for both modes).
    static unsafe class RoguelikeRunScreen
    {
        const string Ui = "DeckSelectUI(Clone).Root.Window";
        const string HeaderName = Ui + ".TitleSafeArea.HeaderButtonArea.HeaderButtonGroup.NameText";
        const string DeckContent = Ui + ".MainArea.DeckArea.DeckGroup.Scroll View2.Viewport.Content";

        static IL2Class _vcClass;
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hookCreated;
        delegate void Del_UpdateTemplateList(IntPtr thisPtr);
        static Hook<Del_UpdateTemplateList> _hookTemplate;
        delegate void Del_OnClick(IntPtr thisPtr, IntPtr arg);
        static Hook<Del_OnClick> _hookClick;
        static IL2Field _decksField, _templateField, _pickupBtnField, _selBtnOnClick;
        static IntPtr _selectionButtonType, _bindingTextType;
        static IL2Method _ueInvoke, _ueAddListener, _ueRemoveAll;
        static IL2Class _deckRefClass;
        static IL2Method _deckRefCtor;
        static IL2Field _fName, _fDeckId, _fDeckType, _fCaseId, _fProtectorId, _fFieldId, _fObjectId, _fPickUp, _fPickDecos, _fFixedPick, _fFixedAcc;
        static IL2Class _dictClass;
        static IL2Method _dictCtor;
        static IL2Class _objectClass, _boolClass;
        static List<RoguelikeApi.DeckOffer> _offers;
        static int _drawerIndex;
        static IntPtr _tmpType;
        static IntPtr _rgVc;     // the specific DeckSelect VC instance we opened for roguelike
        static bool _mapMode;    // true when a deck is already chosen -> delegate to the map
        static bool _pending, _ready;

        static RoguelikeRunScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                _vcClass = asm.GetClass("DeckSelectViewController2", "YgomGame");
                _hookCreated = new Hook<Del_OnCreatedView>(OnCreatedView, _vcClass.GetMethod("OnCreatedView"));
                _hookTemplate = new Hook<Del_UpdateTemplateList>(UpdateTemplateList, _vcClass.GetMethod("UpdateTemplateList"));
                _hookClick = new Hook<Del_OnClick>(OnClick, _vcClass.GetMethod("OnClick"));
                _decksField = _vcClass.GetField("m_Decks");
                _templateField = _vcClass.GetField("m_TemplateList");
                _pickupBtnField = _vcClass.GetField("m_PickupCardButton");
                IL2Class selBtn = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selectionButtonType = selBtn.IL2Typeof();
                _selBtnOnClick = selBtn.GetField("onClick");
                IL2Class ueClass = Assembler.GetAssembly("UnityEngine.CoreModule").GetClass("UnityEvent", "UnityEngine.Events");
                _ueInvoke = ueClass.GetMethod("Invoke", x => x.GetParameters().Length == 0);
                _ueAddListener = ueClass.GetMethod("AddListener");
                _ueRemoveAll = Assembler.GetAssembly("UnityEngine.CoreModule")
                    .GetClass("UnityEventBase", "UnityEngine.Events").GetMethod("RemoveAllListeners");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                _deckRefClass = _vcClass.GetNestedType("DeckReference");
                _deckRefCtor = _deckRefClass.GetMethod(".ctor", x => x.GetParameters().Length == 0);
                _fName = _deckRefClass.GetField("name");
                _fDeckId = _deckRefClass.GetField("deckID");
                _fDeckType = _deckRefClass.GetField("deckType");
                _fCaseId = _deckRefClass.GetField("caseID");
                _fProtectorId = _deckRefClass.GetField("protectorID");
                _fFieldId = _deckRefClass.GetField("fieldID");
                _fObjectId = _deckRefClass.GetField("objectID");
                _fPickUp = _deckRefClass.GetField("pickUpIDs");
                _fPickDecos = _deckRefClass.GetField("pickUpDecos");
                _fFixedPick = _deckRefClass.GetField("isFixedPickCards");
                _fFixedAcc = _deckRefClass.GetField("isFixedAccessories");
                _dictClass = IL2Dictionary<string, object>.Instance_Class;
                _dictCtor = _dictClass.GetMethod(".ctor", x => x.GetParameters().Length == 0);
                IL2Assembly mscorlib = Assembler.GetAssembly("mscorlib");
                _objectClass = mscorlib.GetClass("Object", "System");
                _boolClass = mscorlib.GetClass("Boolean", "System");
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] runscreen init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            // GameMode 9 = the "my deck" variant that omits the "+" add-deck cell. Args must be
            // typed IL2CPP (Int32, not the JSON round-trip's Int64) or the VC hard-cast crashes.
            IntPtr args = NewDict();
            DictAdd(args, "GameMode", BoxInt(9));
            _pending = true;
            YgomSystem.UI.ViewControllerManager.PushChildViewControllerArgs(manager, "DeckEdit/DeckSelect", args);
        }

        // choose_deck completed: swap the choice screen for the map in one op (no pop-to-home
        // flicker). The new instance reports a chosen deck -> map mode.
        public static void OnDeckChosen()
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            IntPtr args = NewDict();
            DictAdd(args, "GameMode", BoxInt(9));
            _pending = true;
            _rgVc = IntPtr.Zero;
            YgomSystem.UI.ViewControllerManager.SwapTopChildViewControllerArgs(manager, "DeckEdit/DeckSelect", args);
        }

        // After a move: re-render the map in place.
        public static void Refresh()
        {
            if (_mapMode) RoguelikeMapScreen.Refresh();
        }

        // After a duel: defer the map refresh until it's visible again (it's hidden under the duel).
        public static void MarkMapDirty()
        {
            if (_mapMode) RoguelikeMapScreen.MarkDirty();
        }

        // Launch the duel the server queued for a combat node. The settings already live in
        // $.Duel (from the move response), so this mirrors the vanilla non-live Solo.start
        // launch: hide the loading wheel and push the production VC over the map.
        public static bool LaunchPendingDuel()
        {
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) { Console.WriteLine("[Roguelike] LaunchPendingDuel: no manager"); return false; }
            if (!RoguelikeDuel.Arm()) return false;   // capture settings before the production mutates $.Duel
            Console.WriteLine("[Roguelike] launching Solo/SoloStartProduction");
            YgomGame.Menu.ProfileViewController.HideLoading();
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "Solo/SoloStartProduction");
            return true;
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            // Mark this instance BEFORE Original: the deck list (and UpdateTemplateList) is
            // built inside Original, so _rgVc must already be set for our changes to apply.
            if (_pending) { _pending = false; _rgVc = thisPtr; _mapMode = RoguelikeApi.IsDeckChosen(); }
            _hookCreated.Original(thisPtr);
            if (thisPtr != _rgVc) return;
            try
            {
                IntPtr go = Component.GetGameObject(thisPtr);
                SetText(go, HeaderName, _mapMode ? "Mapa da Run" : "Selecionar Deck Inicial");
                if (_mapMode)
                {
                    RoguelikeMapScreen.Build(go);
                    // Unfinished combat? Re-launch it (same seed) instead of letting the player roam.
                    if (RoguelikeApi.PendingDuelNode() >= 0)
                    {
                        Console.WriteLine("[Roguelike] pending duel on map open -> resume");
                        RoguelikeApi.ResumeDuel();
                    }
                }
                else
                {
                    HideRegulationIcons(thisPtr);
                    ShowPickupCards(thisPtr); // invoke the toggle's onClick before hiding it
                    HideHeaderClutter(thisPtr, go);
                }
                SetupFooter(go); // Abandonar Run in both modes
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] runscreen customize EX: " + ex); }
        }

        static void UpdateTemplateList(IntPtr thisPtr)
        {
            _hookTemplate.Original(thisPtr);
            if (thisPtr != _rgVc || _mapMode) return; // leave normal DeckSelect / map untouched
            try { PopulateDecks(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] populate decks EX: " + ex); }
        }

        // Intercept deck selection for our instance: the arg is the clicked DeckReference. Map it
        // to its offer index and open our drawer (Ver deck / Selecionar), blocking the default.
        static void OnClick(IntPtr thisPtr, IntPtr deckRef)
        {
            if (thisPtr != _rgVc || _mapMode) { _hookClick.Original(thisPtr, deckRef); return; }
            int idx = IndexOfDeck(thisPtr, deckRef);
            if (idx >= 0) OpenDrawer(idx);
        }

        static void OpenDrawer(int index)
        {
            _offers = RoguelikeApi.GetDeckOffers();
            if (index < 0 || index >= _offers.Count) return;
            _drawerIndex = index;
            RoguelikeApi.DeckOffer o = _offers[index];
            string title = o.Name + (string.IsNullOrEmpty(o.Description) ? "" : "\n" + o.Description);
            YgomGame.Menu.ActionSheetViewController.Open(title, new[] { "Ver deck", "Selecionar" }, OnDrawerSelect);
        }

        static void OnDrawerSelect(IntPtr ctx, int choice)
        {
            if (_offers == null || _drawerIndex < 0 || _drawerIndex >= _offers.Count) return;
            if (choice == 0) ShowDeck(_offers[_drawerIndex]);
            else if (choice == 1) RoguelikeApi.ChooseDeck(_drawerIndex);
        }

        // Open the game's native read-only deck viewer (DeckBrowser) with the offer's cards. Args
        // are typed IL2CPP (Int32, not the JSON round-trip's Int64) since the VC hard-casts.
        static void ShowDeck(RoguelikeApi.DeckOffer offer)
        {
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            try
            {
                IntPtr args = NewDict();
                DictAdd(args, "name", new IL2String(offer.Name).ptr);
                DictAdd(args, "deckNameInit", BoxBool(true));
                DictAdd(args, "iconDeckId", BoxInt(1081001));
                DictAdd(args, "mcards", CardDict(offer.Main));
                DictAdd(args, "ecards", CardDict(offer.Extra));
                DictAdd(args, "numMainCards", BoxInt(offer.Main.Count));
                DictAdd(args, "numExtraCards", BoxInt(offer.Extra.Count));
                DictAdd(args, "regulationMonochromeEnable", BoxBool(false));
                YgomSystem.UI.ViewControllerManager.PushChildViewControllerArgs(manager, "DeckBrowser", args);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] ShowDeck EX: " + ex); }
        }

        static int IndexOfDeck(IntPtr thisPtr, IntPtr deckRef)
        {
            IL2Object decksObj = _decksField.GetValue(thisPtr);
            if (decksObj == null) return -1;
            IL2ListExplicit decks = new IL2ListExplicit(decksObj.ptr, _deckRefClass);
            for (int i = 0; i < decks.Count; i++) if (decks[i] == deckRef) return i;
            return -1;
        }

        static IntPtr NewDict()
        {
            IntPtr p = Import.Object.il2cpp_object_new(_dictClass.ptr);
            _dictCtor.Invoke(p);
            return p;
        }

        static void DictAdd(IntPtr dict, string key, IntPtr valueObj)
        {
            new IL2Dictionary<string, object>(dict).Add(new IL2String(key).ptr, valueObj);
        }

        static IntPtr BoxInt(int v) { return Import.Object.CreateNewObject<int>(v, IL2SystemClass.Int32); }
        static IntPtr BoxBool(bool v) { return Import.Object.CreateNewObject<bool>(v, _boolClass); }

        // Player-format deck section { ids:[...], r:[...] } as List<object> of boxed Int32.
        static IntPtr CardDict(List<int> ids)
        {
            IL2ListExplicit idsList = new IL2ListExplicit(IntPtr.Zero, _objectClass, true);
            IL2ListExplicit rList = new IL2ListExplicit(IntPtr.Zero, _objectClass, true);
            foreach (int id in ids) { idsList.Add(BoxInt(id)); rList.Add(BoxInt(1)); }
            IntPtr d = NewDict();
            DictAdd(d, "ids", idsList.ptr);
            DictAdd(d, "r", rList.ptr);
            return d;
        }

        // Replace m_Decks with DeckReference instances built from the run's deck offers, and
        // size m_TemplateList to match — the scroll then renders our offers as native tiles.
        static void PopulateDecks(IntPtr thisPtr)
        {
            List<RoguelikeApi.DeckOffer> offers = RoguelikeApi.GetDeckOffers();
            if (offers.Count == 0) return; // no pending offers (e.g. deck already chosen)

            IL2Object decksObj = _decksField.GetValue(thisPtr);
            IL2Object templateObj = _templateField.GetValue(thisPtr);
            if (decksObj == null || templateObj == null || _deckRefClass == null) return;

            IL2ListExplicit decks = new IL2ListExplicit(decksObj.ptr, _deckRefClass);
            decks.Clear();
            foreach (RoguelikeApi.DeckOffer o in offers) decks.Add(BuildDeckRef(o));

            IL2List<int> template = new IL2List<int>(templateObj.ptr);
            template.Clear();
            for (int i = 0; i < offers.Count; i++) { int z = 0; template.Add(new IntPtr(&z)); }
            HideRegulationIcons(thisPtr);
            Console.WriteLine("[Roguelike] populated " + offers.Count + " offer decks");
        }

        // Turn on the "show pick cards" mode by default by invoking the header toggle button's
        // onClick (replicates the user's click exactly — the named method only flips state).
        static void ShowPickupCards(IntPtr thisPtr)
        {
            if (_pickupBtnField == null || _selBtnOnClick == null || _ueInvoke == null) return;
            IL2Object btn = _pickupBtnField.GetValue(thisPtr);
            if (btn == null) return;
            IL2Object onClick = _selBtnOnClick.GetValue(btn.ptr);
            if (onClick != null) _ueInvoke.Invoke(onClick.ptr);
        }

        // Hide the (blank) regulation badge on every cell — our decks have no regulation.
        static void HideRegulationIcons(IntPtr thisPtr)
        {
            IntPtr content = GameObject.FindGameObjectByPath(Component.GetGameObject(thisPtr), DeckContent);
            if (content == IntPtr.Zero) return;
            IntPtr ct = GameObject.GetTransform(content);
            int n = Transform.GetChildCount(ct);
            for (int i = 0; i < n; i++)
            {
                IntPtr child = Transform.GetChild(ct, i);
                if (child == IntPtr.Zero) continue;
                IntPtr logo = GameObject.FindGameObjectByPath(Component.GetGameObject(child), "Body.RegulationLogo");
                if (logo != IntPtr.Zero) GameObject.SetActive(logo, false);
            }
        }

        // A fresh DeckReference carrying just what the deck-box tile renders: name, the deck-case
        // accessory, and the 3 cover (pick) cards. deckID is synthetic (we intercept the click).
        static IntPtr BuildDeckRef(RoguelikeApi.DeckOffer o)
        {
            IntPtr p = Import.Object.il2cpp_object_new(_deckRefClass.ptr);
            _deckRefCtor.Invoke(p);
            if (_fName != null) _fName.SetValue(p, new IL2String(o.Name ?? "Deck").ptr);
            SetInt(_fDeckId, p, o.DeckId);
            SetInt(_fDeckType, p, 1); // DeckEventType.MyDeck (real decks use 1, not 0)
            SetInt(_fCaseId, p, o.Box);
            SetInt(_fProtectorId, p, o.Sleeve);
            SetInt(_fFieldId, p, o.Field);
            SetInt(_fObjectId, p, o.Object);
            SetBool(_fFixedAcc, p, true);
            SetBool(_fFixedPick, p, true);
            if (_fPickUp != null && o.PickCards.Count > 0)
            {
                _fPickUp.SetValue(p, IntArray(o.PickCards).ptr);
                // pickUpDecos must match pickUpIDs in length; default to 1 (Normal) when absent.
                List<int> decos = o.PickDecos.Count == o.PickCards.Count ? o.PickDecos : Ones(o.PickCards.Count);
                if (_fPickDecos != null) _fPickDecos.SetValue(p, IntArray(decos).ptr);
            }
            return p;
        }

        static IL2Array<int> IntArray(List<int> values)
        {
            IL2Array<int> arr = new IL2Array<int>(values.Count, IL2SystemClass.Int32);
            for (int i = 0; i < values.Count; i++) arr[i] = values[i];
            return arr;
        }

        static List<int> Ones(int n)
        {
            List<int> r = new List<int>(n);
            for (int i = 0; i < n; i++) r.Add(1);
            return r;
        }

        static void SetInt(IL2Field f, IntPtr obj, int v) { if (f != null) f.SetValue(obj, new IntPtr(&v)); }
        static void SetBool(IL2Field f, IntPtr obj, bool v) { if (f != null) { csbool b = v; f.SetValue(obj, new IntPtr(&b)); } }

        // Hide header bits irrelevant to roguelike: the deck count "N/999" and the pick-cards
        // toggle button (we already enabled its mode programmatically).
        static void HideHeaderClutter(IntPtr thisPtr, IntPtr go)
        {
            const string group = Ui + ".TitleSafeArea.HeaderButtonArea.HeaderButtonGroup";
            Hide(go, group + ".DeckNum");
            Hide(go, group + ".TournamentDeckNum");
            if (_pickupBtnField != null)
            {
                IL2Object btn = _pickupBtnField.GetValue(thisPtr);
                if (btn != null) GameObject.SetActive(Component.GetGameObject(btn.ptr), false);
            }
        }

        // Re-show the footer (GameMode 9 hides it) and repurpose its first button as "Abandonar
        // Run"; hide the other game buttons.
        static void SetupFooter(IntPtr go)
        {
            const string footer = Ui + ".MainArea.FooterArea";
            const string row = footer + ".FooterGroup.BaseAll.BaseRight";
            IntPtr fa = GameObject.FindGameObjectByPath(go, footer);
            if (fa == IntPtr.Zero) { Console.WriteLine("[Roguelike] footer not found"); return; }
            GameObject.SetActive(fa, true);

            Hide(go, row + ".ButtonStructureInfo");
            Hide(go, row + ".ButtonDeleteExecution");

            IntPtr btn = GameObject.FindGameObjectByPath(go, row + ".ButtonOpenDeckSearch");
            if (btn == IntPtr.Zero) { Console.WriteLine("[Roguelike] footer button not found"); return; }
            GameObject.SetActive(btn, true);
            SetBindingText(btn, "TextTMP", RoguelikeLabels.Get("run.abandon.button", "Abandonar Run"));
            WireButton(btn, OnAbandonClick);
        }

        static void OnAbandonClick()
        {
            YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog(
                RoguelikeLabels.Get("run.abandon.title", "Abandonar Run"),
                RoguelikeLabels.Get("run.abandon.msg", "Tem certeza? Todo o progresso da run atual será perdido."),
                OnAbandonConfirmed);
        }

        static void OnAbandonConfirmed()
        {
            RoguelikeApi.AbandonRun();
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager != IntPtr.Zero) YgomSystem.UI.ViewControllerManager.PopChildViewController(manager);
        }

        // Replace a SelectionButton's onClick with our captureless action.
        static void WireButton(IntPtr buttonGo, Action action)
        {
            IntPtr sel = GameObject.GetComponent(buttonGo, _selectionButtonType);
            if (sel == IntPtr.Zero) return;
            IL2Object onClickObj = _selBtnOnClick.GetValue(sel);
            if (onClickObj == null) return;
            if (_ueRemoveAll != null) _ueRemoveAll.Invoke(onClickObj.ptr);
            IntPtr cb = UnityEngine.Events._UnityAction.CreateUnityAction(action);
            _ueAddListener.Invoke(onClickObj.ptr, new IntPtr[] { cb });
        }

        // Set a binding-bound TMP label verbatim (renders the literal, no IDS lookup).
        static void SetBindingText(IntPtr root, string path, string text)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero) YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(binding, text);
        }

        static void SetText(IntPtr go, string path, string text)
        {
            IntPtr o = GameObject.FindGameObjectByPath(go, path);
            if (o == IntPtr.Zero) { Console.WriteLine("[Roguelike] text not found: " + path); return; }
            IntPtr tmp = GameObject.GetComponent(o, _tmpType);
            if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, text);
        }

        static void Hide(IntPtr go, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(go, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }
    }
}
