using System;

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
            {
                OpenDeckSelect();
            }
            else if (cmd == "Roguelike.choose_deck")
            {
                // The ActionSheet drawer has closed by now; pop the deck-select screen so
                // back from the map returns Home, then open the map.
                IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
                if (manager != IntPtr.Zero) YgomSystem.UI.ViewControllerManager.PopChildViewController(manager);
                RoguelikeMapScreen.Open();
            }
        }
    }
}
