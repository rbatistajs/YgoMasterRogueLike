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
            string gameType = "base_deck";
            if (request.ActParams != null)
                gameType = Utils.GetValue<string>(request.ActParams, "gameType", "base_deck");
            int seed = new Random().Next();
            RoguelikeRun run = new RoguelikeRun
            {
                Active = true,
                GameType = gameType,
                Seed = seed,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                DeckChosen = false,
                DeckOffers = RollDeckOffers(seed, 3),
                Deck = null,
            };
            run.Save(GetPlayerDirectory(request.Player));
            request.Response["Roguelike"] = run.ToDictionary();
            request.Remove("Roguelike");
        }

        // Pick up to `count` distinct starter decks (seeded), returning display offers
        // ({name, bossCard, file}) with `file` relative to dataDirectory.
        List<object> RollDeckOffers(int seed, int count)
        {
            List<string> files = RoguelikeDeckPool.ListFiles(dataDirectory);
            List<object> offers = new List<object>();
            if (files.Count == 0)
            {
                Console.WriteLine("[Roguelike] no starter decks in Roguelike/StartingDecks");
                return offers;
            }
            Random rng = new Random(seed);
            for (int i = files.Count - 1; i > 0; i--) // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
            }
            int take = Math.Min(count, files.Count);
            for (int i = 0; i < take; i++)
            {
                try
                {
                    RoguelikeDeckPool.StarterDeck d = RoguelikeDeckPool.LoadOne(files[i]);
                    if (d == null) continue;
                    string rel = files[i].StartsWith(dataDirectory)
                        ? files[i].Substring(dataDirectory.Length).TrimStart('\\', '/')
                        : files[i];
                    offers.Add(new Dictionary<string, object>
                    {
                        { "name", d.Name }, { "bossCard", d.BossCard }, { "file", rel },
                    });
                }
                catch (Exception ex) { Console.WriteLine("[Roguelike] offer load EX: " + ex.Message); }
            }
            return offers;
        }

        void Act_RoguelikeChooseDeck(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            if (run.Active && !run.DeckChosen && run.DeckOffers != null)
            {
                int index = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "index", -1) : -1;
                if (index >= 0 && index < run.DeckOffers.Count)
                {
                    Dictionary<string, object> offer = run.DeckOffers[index] as Dictionary<string, object>;
                    string rel = offer != null ? Utils.GetValue<string>(offer, "file") : null;
                    if (!string.IsNullOrEmpty(rel))
                    {
                        RoguelikeDeckPool.StarterDeck d =
                            RoguelikeDeckPool.LoadOne(System.IO.Path.Combine(dataDirectory, rel));
                        if (d != null)
                        {
                            run.Deck = new Dictionary<string, object>
                            {
                                { "name", d.Name }, { "bossCard", d.BossCard }, { "deck", d.Json },
                            };
                            run.DeckChosen = true;
                            run.DeckOffers = new List<object>();
                            run.Save(GetPlayerDirectory(request.Player));
                        }
                    }
                }
            }
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
