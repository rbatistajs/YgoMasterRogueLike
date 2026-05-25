namespace YgoMasterClient
{
    // Run-flow reactions: drives the unified run screen from completed Roguelike acts
    // (called from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        static bool _awaitingDuelResult;   // a combat duel is in progress -> report its result
        static bool _lastDuelWin;          // win/loss captured from the engine's Duel.end report
        static int _lastPlayerLp;          // player's remaining LP at Duel.end (run LP carry-over)

        public static void OnNetworkComplete(string cmd)
        {
            // Home is still active here; grab the PlayerIcon now (it goes inactive under the run screen).
            RoguelikeMapScreen.CaptureMarkerSource();
            if (cmd == "Roguelike.start_run")
                RoguelikeRunScreen.Open();                 // new run -> deck-choice screen
            else if (cmd == "Roguelike.choose_deck")
                RoguelikeRunScreen.OnDeckChosen();         // close choice -> open the map
            else if (cmd == "Roguelike.move")
            {
                // Combat node? The server queued a duel in $.Duel; launch it. Otherwise re-render.
                if (RoguelikeApi.PendingDuelNode() >= 0 && RoguelikeRunScreen.LaunchPendingDuel())
                    _awaitingDuelResult = true;
                else
                    RoguelikeRunScreen.Refresh();          // map nav -> re-render in place
            }
            else if (cmd == "Roguelike.resume_duel")
            {
                // Re-launch an unfinished combat (same seed) when re-entering the map.
                if (RoguelikeApi.PendingDuelNode() >= 0 && RoguelikeRunScreen.LaunchPendingDuel())
                    _awaitingDuelResult = true;
            }
            else if (cmd == "Duel.end" && _awaitingDuelResult)
            {
                _awaitingDuelResult = false;
                RoguelikeApi.SendDuelResult(_lastDuelWin, _lastPlayerLp); // server applies LP / currency / death
            }
            else if (cmd == "Roguelike.duel_result")
            {
                if (RoguelikeApi.Won())
                {
                    // Final boss down: the run is over. Show victory; the dead-run map underneath is
                    // harmless (home will offer a fresh run at the newly unlocked ascension).
                    YgomGame.Menu.CommonDialogViewController.OpenConfirmationDialog(
                        RoguelikeLabels.Get("run.victory.title", "Vitória!"),
                        RoguelikeLabels.Get("run.victory.msg", "Você completou a run! Ascensão máxima desbloqueada: {0}.", RoguelikeApi.MaxAscension()),
                        RoguelikeLabels.Get("common.ok", "OK"), OnVictoryAck);
                }
                else
                {
                    // Server applied the result. The map VC is deactivated under the duel screens
                    // now, so flag a refresh — it re-renders (fresh state, incl. a new act) on reappear.
                    RoguelikeRunScreen.MarkMapDirty();
                }
            }
            // Action prompts are pumped from RoguelikeMapScreen.Update (only while the map is visible),
            // so a pending action on re-entry waits for the map instead of opening over home/loading.
        }

        static void OnVictoryAck() { }

        // Win/loss + the player's remaining LP, captured from the engine's Duel.end report (fires
        // before the Duel.end completion above). Stored regardless of mode; only consumed for
        // combat duels (LP carries into run LP server-side).
        public static void OnDuelEnded(bool win, int playerLp) { _lastDuelWin = win; _lastPlayerLp = playerLp; }
    }
}
