using System;
using System.Collections.Generic;

namespace YgoMaster
{
    partial class GameServer
    {
        // Piggyback run state into a response so it lands at $.Roguelike client-side.
        void WriteRoguelikeState(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            request.Response["Roguelike"] = run.ToDictionary();
            request.Remove("Roguelike");
        }

        void Act_RoguelikeStartRun(GameServerWebRequest request)
        {
            RoguelikeRun run = new RoguelikeRun
            {
                Active = true,
                GameType = Utils.GetValue<string>(request.ActParams, "gameType", "base_deck"),
                Seed = new Random().Next(),
                CreatedAt = DateTime.UtcNow.ToString("o"),
            };
            run.Save(GetPlayerDirectory(request.Player));
            request.Response["Roguelike"] = run.ToDictionary();
            request.Remove("Roguelike");
        }

        void Act_RoguelikeAbandonRun(GameServerWebRequest request)
        {
            RoguelikeRun.Delete(GetPlayerDirectory(request.Player));
            request.Response["Roguelike"] = new RoguelikeRun { Active = false }.ToDictionary();
            request.Remove("Roguelike");
        }
    }
}
