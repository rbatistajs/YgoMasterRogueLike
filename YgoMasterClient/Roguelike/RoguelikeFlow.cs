namespace YgoMasterClient
{
    // Run-flow reactions: opens the deck-select screen and handles the async responses of
    // Roguelike acts (driven from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        // Open the deck-select screen (3 deck tiles).
        public static void OpenDeckSelect()
        {
            RoguelikeDeckSelectScreen.Open();
        }

        // Called from DuelStarter.Complete for every completed network command.
        public static void OnNetworkComplete(string cmd)
        {
            if (cmd == "Roguelike.start_run")
                OpenDeckSelect();
            else if (cmd == "Roguelike.choose_deck")
                RoguelikeMapScreen.Open();   // deck chosen → the map is ready, go straight to it
        }
    }
}
