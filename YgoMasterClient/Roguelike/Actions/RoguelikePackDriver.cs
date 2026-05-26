using System;
using System.Collections.Generic;

namespace YgoMasterClient
{
    // Renders pending packs from the server (run.PendingPack). Pumped from RoguelikeMapScreen.Update
    // — only fires while the map is visible (same pattern as the M5 action prompt).
    static class RoguelikePackDriver
    {
        static int _shownToken = -1;

        public static void Pump()
        {
            try
            {
                RoguelikeApi.PendingPack p = RoguelikeApi.GetPendingPack();
                if (p == null) { _shownToken = -1; return; }
                if (p.Token == _shownToken) return; // already opened
                _shownToken = p.Token;
                IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
                Console.WriteLine("[Roguelike] pack pump: token=" + p.Token + " mode=" + p.Mode + " size=" + p.Size + " mgr=" + (manager != IntPtr.Zero));
                if (manager == IntPtr.Zero) return;
                IntPtr args = NewArgs();
                YgomSystem.UI.ViewControllerManager.PushChildViewControllerArgs(manager, "CardPack/CardPackOpen", args);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] pack pump EX: " + ex); }
        }

        static IntPtr NewArgs()
        {
            // { "ForwardResultArgs": null }
            return YgomMiniJSON.Json.Deserialize(MiniJSON.Json.Serialize(
                new Dictionary<string, object> { { "ForwardResultArgs", null } }));
        }
    }
}
