using System;
using System.Collections.Generic;

namespace YgoMasterClient
{
    // Run-flow reactions: opens the deck-select ActionSheet (offer names) and handles the
    // async responses of Roguelike acts (driven from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        // Open the "choose 1 of N" sheet from the current $.Roguelike offers.
        public static void OpenDeckSelect()
        {
            string[] names = RoguelikeApi.GetDeckOfferNames();
            if (names.Length == 0)
            {
                YgomGame.Menu.CommonDialogViewController.OpenAlertDialog("Roguelike",
                    "Nenhum deck disponivel no pool.", () => { });
                return;
            }
            YgomGame.Menu.ActionSheetViewController.Open("Escolha seu deck", names, OnDeckPicked);
        }

        static void OnDeckPicked(IntPtr ctx, int index)
        {
            RoguelikeApi.ChooseDeck(index);
        }

        // Called from DuelStarter.Complete for every completed network command.
        public static void OnNetworkComplete(string cmd)
        {
            if (cmd == "Roguelike.start_run")
            {
                OpenDeckSelect();
            }
            else if (cmd == "Roguelike.choose_deck")
            {
                YgomGame.Menu.CommonDialogViewController.OpenAlertDialog("Roguelike",
                    "Deck escolhido: " + ChosenDeckName(), () => { });
            }
        }

        // Reads $.Roguelike.deck.name (GetByJsonPath only does value types, so serialize
        // the deck object and pull the name).
        static string ChosenDeckName()
        {
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.deck");
                if (string.IsNullOrEmpty(json)) return "?";
                Dictionary<string, object> d = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("name")) return Convert.ToString(d["name"]);
            }
            catch { }
            return "?";
        }
    }
}
