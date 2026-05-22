namespace YgoMasterClient
{
    // Run-flow reactions: drives the unified run screen from completed Roguelike acts
    // (called from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        public static void OnNetworkComplete(string cmd)
        {
            // Home is still active here; grab the PlayerIcon now (it goes inactive under the run screen).
            RoguelikeMapScreen.CaptureMarkerSource();
            if (cmd == "Roguelike.start_run")
                RoguelikeRunScreen.Open();                 // new run -> deck-choice screen
            else if (cmd == "Roguelike.choose_deck")
                RoguelikeRunScreen.OnDeckChosen();         // close choice -> open the map
            else if (cmd == "Roguelike.move")
                RoguelikeRunScreen.Refresh();              // map nav -> re-render in place
        }
    }
}
