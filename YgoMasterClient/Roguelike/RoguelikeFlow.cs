namespace YgoMasterClient
{
    // Run-flow reactions: drives the unified run screen from completed Roguelike acts
    // (called from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        public static void OnNetworkComplete(string cmd)
        {
            if (cmd == "Roguelike.start_run")
                RoguelikeRunScreen.Open();                 // new run -> open screen (deck choice)
            else if (cmd == "Roguelike.choose_deck" || cmd == "Roguelike.move")
                RoguelikeRunScreen.Refresh();              // in-place: choice -> map, or map nav
        }
    }
}
