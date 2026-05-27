using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Roguelike run deck editor: opens the game's DeckEdit and customizes it for the run. The
    // DeckEditViewController2.OnCreatedView hook is owned by DeckEditorUtils (MinHook = one hook per
    // method), so customization is driven from there via OnDeckEditCreated, gated to the VC we push.
    static class RoguelikeDeckEditScreen
    {
        static IntPtr _rgVc;
        static bool _pending;
        static bool _loaded;
        static bool _dirty;       // user added/removed a card since the last load/save
        static string _savedHave; // player's real $.Cards.have, restored on close

        public static void Open()
        {
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) { Console.WriteLine("[Roguelike] DeckEdit: no manager"); return; }
            _pending = true;
            _loaded = false;
            _dirty = false;
            _rgVc = IntPtr.Zero;
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "DeckEdit/DeckEdit");
            Console.WriteLine("[Roguelike] pushed DeckEdit/DeckEdit");
        }

        // Driven from DeckEditViewController2.InitializeView (post-Original). Customizes only the
        // DeckEdit we pushed for the run. Card loading happens later (see OnSortDeckViewCards) —
        // InitializeView is too early, the game's async deck-load re-shows the empty placeholder.
        public static void OnDeckEditCreated(IntPtr thisPtr)
        {
            if (_pending) { _pending = false; _rgVc = thisPtr; }
            if (thisPtr != _rgVc) return; // only the DeckEdit we pushed for the run
            try
            {
                IntPtr go = Component.GetGameObject(thisPtr);
                Hide(go, "DeckEditUI(Clone).DeckEditOverHeader");                            // craft-points (CP N/R/SR/UR) bar
                Hide(go, "DeckEditUI(Clone).DeckEditHeader.Root.Window.Header.ButtonMenu");   // ☰ submenu (craft/options)
                HideCardDetailExtras(go);
                SwapInRunCollection();                                                       // owned counts/limits = run collection
                YgomGame.DeckEditViewController2.SetRegulation(RoguelikeApi.RegulationId());  // run pool banlist
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckedit customize EX: " + ex); }
        }

        // Replace $.Cards.have with the run's collection so the deck editor's owned counts and
        // add-limits reflect run-owned cards (mirrors TradeUtils' collection swap). Restored on close.
        static void SwapInRunCollection()
        {
            string runCards = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.Cards");
            if (string.IsNullOrEmpty(runCards) || runCards == "{}") return;
            _savedHave = YgomSystem.Utility.ClientWork.SerializePath("$.Cards.have");
            YgomSystem.Utility.ClientWork.DeleteByJsonPath("Cards.have");
            YgomSystem.Utility.ClientWork.UpdateJson("{\"Cards\":{\"have\":" + runCards + "}}");
        }

        static void RestorePlayerCollection()
        {
            if (_savedHave == null) return;
            YgomSystem.Utility.ClientWork.DeleteByJsonPath("Cards.have");
            if (_savedHave != "" && _savedHave != "{}")
                YgomSystem.Utility.ClientWork.UpdateJson("{\"Cards\":{\"have\":" + _savedHave + "}}");
            _savedHave = null;
        }

        // Driven from DeckEditViewController2.NotificationStackRemove. Restore the player's collection
        // and clear run-deck state when our deck editor closes.
        public static void OnClose(IntPtr thisPtr)
        {
            if (thisPtr != _rgVc) return;
            try { RestorePlayerCollection(); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckedit close EX: " + ex); }
            _rgVc = IntPtr.Zero;
            _loaded = false;
        }

        // Driven from DeckEditViewController2.SortDeckViewCards — the post-load point Trade uses to
        // inject cards reliably. One-shot per push (gated to our VC), so re-sorts don't re-load.
        public static void OnSortDeckViewCards(IntPtr thisPtr)
        {
            if (thisPtr != _rgVc || _loaded) return;
            _loaded = true;
            try { LoadRunDeck(); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckedit load EX: " + ex); }
        }

        // True for the DeckEdit we pushed for the run — gates the save overrides in DeckEditorUtils
        // (NeedSave / OnClickSaveButton) so only our deck editor uses our save flow.
        public static bool IsRunDeckEdit(IntPtr thisPtr)
        {
            return _rgVc != IntPtr.Zero && thisPtr == _rgVc;
        }

        // Unsaved-changes flag for the back-confirm: set by the user's add/remove handlers
        // (DeckEditorUtils), cleared on load/save. Our programmatic SetCards goes through DeckView
        // (not those handlers), so loading the run deck doesn't trip it.
        public static void MarkDirty() { _dirty = true; }
        public static bool HasUnsavedChanges() { return _dirty; }

        // Save button (DeckEditViewController2.OnClickSaveButton): validate + persist.
        public static void OnClickSave(IntPtr thisPtr)
        {
            TrySaveDeck();
        }

        // Driven from the ShowModifiedDialog hook (native back/ESC). The native dirty flag is tripped by
        // our programmatic SetCards, so it fires even with no real edit — we pop straight to the map
        // when the deck still matches the saved run deck. If the deck is invalid it can't be saved, so
        // we drop the save option and warn: Descartar alterações (leave) or Editar (keep editing).
        public static void HandleBack()
        {
            if (!HasUnsavedChanges())
            {
                YgomGame.DeckEditViewController2.PopViewController();
                return;
            }
            int mainCount, extraCount, min, maxMain, maxExtra;
            if (IsCurrentDeckValid(out mainCount, out extraCount, out min, out maxMain, out maxExtra))
            {
                YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog(
                    RoguelikeLabels.Get("deckedit.back.title", "Retornar ao mapa"),
                    RoguelikeLabels.Get("deckedit.back.msg", "Você tem alterações não salvas no deck. Deseja salvar antes de voltar?"),
                    SaveAndReturn,    // Salvar e retornar
                    DiscardAndReturn, // Descartar e retornar
                    null,
                    RoguelikeLabels.Get("deckedit.back.save", "Salvar e retornar"),
                    RoguelikeLabels.Get("deckedit.back.discard", "Descartar e retornar"));
            }
            else
            {
                string msg;
                if (extraCount > maxExtra)
                    msg = RoguelikeLabels.Get("deckedit.invalid.back.extra",
                        "O Deck Adicional pode ter no máximo {0} cards (atual: {1}). Não é possível salvar.", maxExtra, extraCount);
                else
                    msg = RoguelikeLabels.Get("deckedit.invalid.back.msg",
                        "O Deck Principal deve ter entre {0} e {1} cards (atual: {2}). Não é possível salvar.", min, maxMain, mainCount);
                YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog(
                    RoguelikeLabels.Get("deckedit.invalid.title", "Deck inválido"),
                    msg,
                    DiscardAndReturn, // Descartar alterações (sai da edição)
                    null,             // Editar (continua editando)
                    null,
                    RoguelikeLabels.Get("deckedit.invalid.back.discard", "Descartar alterações"),
                    RoguelikeLabels.Get("deckedit.invalid.back.edit", "Editar"));
            }
        }

        // Save button flow: validate + persist. Invalid -> Resetar (revert to the saved run deck) or
        // Cancelar (keep editing). Returns true when the deck was valid and saved.
        static bool TrySaveDeck()
        {
            try
            {
                int mainCount, extraCount, min, maxMain, maxExtra;
                if (!IsCurrentDeckValid(out mainCount, out extraCount, out min, out maxMain, out maxExtra))
                {
                    string msg;
                    if (extraCount > maxExtra)
                        msg = RoguelikeLabels.Get("deckedit.invalid.save.extra",
                            "O Deck Adicional pode ter no máximo {0} cards (atual: {1}).", maxExtra, extraCount);
                    else
                        msg = RoguelikeLabels.Get("deckedit.invalid.save.msg",
                            "O Deck Principal deve ter entre {0} e {1} cards (atual: {2}).", min, maxMain, mainCount);
                    YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog(
                        RoguelikeLabels.Get("deckedit.invalid.title", "Deck inválido"),
                        msg,
                        ResetToSavedDeck, // Resetar
                        null,             // Cancelar
                        null,
                        RoguelikeLabels.Get("deckedit.invalid.save.reset", "Resetar"),
                        RoguelikeLabels.Get("deckedit.invalid.save.cancel", "Cancelar"));
                    return false;
                }
                DeckInfo deck = YgomGame.DeckEditViewController2.GetDeckInfo();
                RoguelikeApi.SaveDeck(deck.ToDictionary());
                _dirty = false;
                Console.WriteLine("[Roguelike] deckedit save: " + mainCount + " main, " + extraCount + " extra");
                return true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckedit save EX: " + ex); return false; }
        }

        // True when the editor's deck is a legal run deck. min is the run's configured minimum
        // (or the run-collection total if it owns fewer cards than the config asks for, so the
        // early-game player can still save). max comes from the run config (defaults 60/15).
        static bool IsCurrentDeckValid(out int mainCount, out int extraCount, out int min, out int maxMain, out int maxExtra)
        {
            mainCount = 0; extraCount = 0;
            int cfgMin = RoguelikeApi.DeckMinCards();
            maxMain = RoguelikeApi.DeckMaxMainCards();
            maxExtra = RoguelikeApi.DeckMaxExtraCards();
            int collectionTotal = RoguelikeApi.RunOwnedCardTotal();
            min = collectionTotal < cfgMin ? collectionTotal : cfgMin;
            DeckInfo deck = YgomGame.DeckEditViewController2.GetDeckInfo();
            if (deck == null) return false;
            mainCount = deck.MainDeckCards.GetCollection().Count;
            extraCount = deck.ExtraDeckCards.GetCollection().Count;
            return mainCount >= min && mainCount <= maxMain && extraCount <= maxExtra;
        }

        // Back-confirm callbacks (static = captureless, required by the dialog's UnityAction).
        static void SaveAndReturn() { if (TrySaveDeck()) YgomGame.DeckEditViewController2.PopViewController(); }
        static void DiscardAndReturn() { YgomGame.DeckEditViewController2.PopViewController(); }

        // Revert the editor to the deck currently saved on the run ($.Roguelike.deck).
        static void ResetToSavedDeck()
        {
            try { LoadRunDeck(); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] reset deck EX: " + ex); }
        }

        // Populate the deck view with the active run's deck (currentInstance set in InitializeView).
        static void LoadRunDeck()
        {
            List<int> main = RoguelikeApi.RunDeckMain();
            List<int> extra = RoguelikeApi.RunDeckExtra();
            if (main.Count == 0 && extra.Count == 0) return;
            DeckInfo deck = new DeckInfo();
            foreach (int cid in main) deck.MainDeckCards.Add(cid);
            foreach (int cid in extra) deck.ExtraDeckCards.Add(cid);
            YgomGame.DeckEditViewController2.SetCards(deck.MainDeckCards, deck.ExtraDeckCards);
            _dirty = false; // programmatic load/reset is not a user edit
            Console.WriteLine("[Roguelike] deckedit loaded run deck: " + main.Count + " main, " + extra.Count + " extra");
        }

        // Card-detail panel: hide "Como obter" (HowToGetButton) and the craft area (CraftGroup). Paths
        // are full direct-child chains because FindGameObjectByPath isn't recursive. Driven from the
        // initializeDetailView hook too, since the panel re-shows its buttons on each card.
        public static void HideCardDetailExtras(IntPtr go)
        {
            const string menu = "DeckEditUI(Clone).CardDetail.Root.Window.DescriptionArea.MenuAreaRoot.MenuArea.";
            Hide(go, menu + "MenuGroup.HowToGetButton");
            Hide(go, menu + "CraftGroup");
        }

        static void Hide(IntPtr go, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(go, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }
    }
}
