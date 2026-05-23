using System;
using System.Collections.Generic;

namespace YgoMaster
{
    partial class GameServer
    {
        // Piggyback run state into a response so it lands at $.Roguelike client-side.
        void WriteRoguelikeState(GameServerWebRequest request)
        {
            WriteRun(request, RoguelikeRun.Load(GetPlayerDirectory(request.Player)));
        }

        // Send the run to the client. deckOffers is persisted as bare file paths (source of
        // truth = the deck JSONs); we expand them into full deck-list items only on the wire.
        void WriteRun(GameServerWebRequest request, RoguelikeRun run)
        {
            Dictionary<string, object> dto = run.ToDictionary();
            dto["deckOffers"] = ExpandDeckOffers(run.DeckOffers);
            request.Response["Roguelike"] = dto;
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
                DeckOffers = RollDeckOfferFiles(seed, 3),
                Deck = null,
            };
            run.Save(GetPlayerDirectory(request.Player));
            WriteRun(request, run);
        }

        // Roll up to `count` distinct starter-deck files (seeded). Returns relative paths only;
        // the deck data lives in the files and is read on demand (choose/expand).
        List<object> RollDeckOfferFiles(int seed, int count)
        {
            List<string> files = RoguelikeDeckPool.ListFiles(dataDirectory);
            List<object> result = new List<object>();
            if (files.Count == 0)
            {
                Console.WriteLine("[Roguelike] no starter decks in Roguelike/StartingDecks");
                return result;
            }
            Random rng = new Random(seed);
            for (int i = files.Count - 1; i > 0; i--) // Fisher-Yates
            {
                int j = rng.Next(i + 1);
                string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
            }
            int take = Math.Min(count, files.Count);
            for (int i = 0; i < take; i++) result.Add(RelPath(files[i]));
            return result;
        }

        // Expand persisted offer file paths into deck-list items the client renders natively
        // (deck_id + name + accessory + pick_cards, all in the game's shape).
        List<object> ExpandDeckOffers(List<object> files)
        {
            List<object> offers = new List<object>();
            if (files == null) return offers;
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i] as string;
                if (string.IsNullOrEmpty(rel)) continue;
                try
                {
                    RoguelikeDeckPool.StarterDeck d = RoguelikeDeckPool.LoadOne(System.IO.Path.Combine(dataDirectory, rel));
                    if (d == null) continue;
                    offers.Add(new Dictionary<string, object>
                    {
                        { "deck_id", 90000001 + i },
                        { "name", d.Name },
                        { "status", 0 },
                        { "ct", Utils.GetValue<long>(d.Json, "ct") },
                        { "et", Utils.GetValue<long>(d.Json, "et") },
                        { "regulation_id", Utils.GetValue<int>(d.Json, "regulation_id") },
                        { "accessory", Utils.GetValue<Dictionary<string, object>>(d.Json, "accessory") },
                        { "pick_cards", Utils.GetValue<Dictionary<string, object>>(d.Json, "pick_cards") },
                        { "main", IdsOf(d.Json, "m") },
                        { "extra", IdsOf(d.Json, "e") },
                        { "file", rel }, { "description", d.Description },
                    });
                }
                catch (Exception ex) { Console.WriteLine("[Roguelike] expand offer EX: " + ex.Message); }
            }
            return offers;
        }

        string RelPath(string fullPath)
        {
            return fullPath.StartsWith(dataDirectory)
                ? fullPath.Substring(dataDirectory.Length).TrimStart('\\', '/')
                : fullPath;
        }

        // Flat id list of a player-format deck section ("m"/"e"/"s") for the deck viewer.
        static List<object> IdsOf(Dictionary<string, object> deckJson, string section)
        {
            Dictionary<string, object> sec = Utils.GetValue<Dictionary<string, object>>(deckJson, section);
            List<object> ids = sec != null ? Utils.GetValue<List<object>>(sec, "ids") : null;
            return ids ?? new List<object>();
        }

        void Act_RoguelikeChooseDeck(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            if (run.Active && !run.DeckChosen && run.DeckOffers != null)
            {
                int index = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "index", -1) : -1;
                if (index >= 0 && index < run.DeckOffers.Count)
                {
                    string rel = run.DeckOffers[index] as string;
                    if (!string.IsNullOrEmpty(rel))
                    {
                        RoguelikeDeckPool.StarterDeck d =
                            RoguelikeDeckPool.LoadOne(System.IO.Path.Combine(dataDirectory, rel));
                        if (d != null)
                        {
                            run.Deck = new Dictionary<string, object>
                            {
                                { "name", d.Name }, { "bossCard", d.BossCard },
                                { "description", d.Description }, { "deck", d.Json },
                            };
                            run.DeckChosen = true;
                            run.DeckOffers = new List<object>();
                            Dictionary<string, object> settings = RoguelikeSettings.Load(dataDirectory);
                            RoguelikeMapLayout layout = RoguelikeMapLayout.Create(RoguelikeSettings.Layout(settings));
                            run.Map = layout.Build((int)run.Seed, settings).ToDictionary();
                            run.Position = -1;
                            run.Visited = new List<object>();
                            run.MaxHp = RoguelikeSettings.PlayerMaxHp(settings);
                            run.Hp = run.MaxHp; // start the run at full HP
                            run.Save(GetPlayerDirectory(request.Player));
                        }
                    }
                }
            }
            WriteRun(request, run);
        }

        void Act_RoguelikeMove(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            if (run.Active && run.DeckChosen && run.Map != null)
            {
                int target = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "nodeId", -1) : -1;
                if (IsReachable(run, target))
                {
                    run.Position = target;
                    if (run.Visited == null) run.Visited = new List<object>();
                    if (!run.Visited.Contains(target)) run.Visited.Add(target);
                    // Combat node -> set up the duel; the client launches it and reports the result.
                    if (IsCombat(NodeType(run, target)) && BuildRoguelikeDuel(run, request, target))
                        run.PendingDuelNode = target;
                    else
                        run.PendingDuelNode = -1;
                    run.Save(GetPlayerDirectory(request.Player));
                }
            }
            WriteRun(request, run);
        }

        // Resume an unfinished combat: rebuild the same (deterministic) duel for the pending node so
        // the client can re-launch it. No state change — the duel only clears via duel_result.
        void Act_RoguelikeResumeDuel(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            if (run.Active && run.PendingDuelNode >= 0)
                BuildRoguelikeDuel(run, request, run.PendingDuelNode);
            WriteRun(request, run);
        }

        // Win: carry the player's remaining LP into run HP (+ heal, clamped to max), credit currency.
        // Loss: HP hits 0, run ends. Clears the pending duel either way.
        void Act_RoguelikeDuelResult(GameServerWebRequest request)
        {
            RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
            bool win = request.ActParams != null && Utils.GetValue<bool>(request.ActParams, "win", false);
            int playerLp = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "playerLp", 0) : 0;
            if (run.Active && run.PendingDuelNode >= 0)
            {
                Dictionary<string, object> settings = RoguelikeSettings.Load(dataDirectory);
                if (run.MaxHp <= 0) run.MaxHp = RoguelikeSettings.PlayerMaxHp(settings); // lazy init (old saves)
                if (win)
                {
                    int heal = (int)(run.MaxHp * RoguelikeSettings.HealPercent(settings));
                    run.Hp = Math.Min(run.MaxHp, Math.Max(0, playerLp) + heal);
                    run.Currency += RewardFor(NodeType(run, run.PendingDuelNode));
                    if (run.Hp <= 0) run.Active = false; // safety: a 0-HP "win" is still death
                }
                else
                {
                    run.Hp = 0;
                    run.Active = false;
                }
                run.PendingDuelNode = -1;
                run.Save(GetPlayerDirectory(request.Player));
            }
            WriteRun(request, run);
        }

        static bool IsCombat(string type) => type == "duel" || type == "elite" || type == "boss";

        static int RewardFor(string type)
        {
            switch (type) { case "boss": return 1000; case "elite": return 250; default: return 100; }
        }

        static string NodeType(RoguelikeRun run, int id)
        {
            List<object> nodes = run.Map != null ? Utils.GetValue<List<object>>(run.Map, "nodes") : null;
            if (nodes == null) return "";
            foreach (object o in nodes)
            {
                Dictionary<string, object> n = o as Dictionary<string, object>;
                if (n != null && Utils.GetValue<int>(n, "id", -999) == id)
                    return Utils.GetValue<string>(n, "type", "");
            }
            return "";
        }

        // Build a custom duel (run deck vs opponent) for a combat node and put it in Response["Duel"]
        // so the client's Solo-start flow launches it. Everything random — opponent pick, who goes
        // first, the engine RNG seed — derives from (run seed, node) so an unfinished duel resumes
        // identically: the player can't quit to re-roll their draws. Mirrors the CustomDuel pattern.
        bool BuildRoguelikeDuel(RoguelikeRun run, GameServerWebRequest request, int nodeId)
        {
            Dictionary<string, object> deckDict = run.Deck != null ? Utils.GetValue<Dictionary<string, object>>(run.Deck, "deck") : null;
            if (deckDict == null) return false;
            DeckInfo player = new DeckInfo();
            player.FromDictionary(deckDict, false);
            player.Name = Utils.GetValue<string>(run.Deck, "name", "Run Deck");

            List<string> opps = RoguelikeDeckPool.ListOpponentFiles(dataDirectory);
            if (opps.Count == 0) { Console.WriteLine("[Roguelike] no opponents in Roguelike/Opponents"); return false; }
            opps.Sort(StringComparer.Ordinal); // stable order so the seeded pick is reproducible

            Random duelRng = new Random(DuelRngSeed(run.Seed, nodeId));
            string oppFile = opps[duelRng.Next(opps.Count)];
            DeckInfo opp = new DeckInfo();
            opp.File = oppFile;
            opp.Load();
            if (string.IsNullOrEmpty(opp.Name)) opp.Name = System.IO.Path.GetFileNameWithoutExtension(oppFile);

            DuelSettings ds = new DuelSettings();
            ds.Deck[DuelSettings.PlayerIndex] = player;
            ds.Deck[DuelSettings.CpuIndex] = opp;
            ds.IsCustomDuel = true;
            ds.SetRequiredDefaults();
            // Starting LP: player = current run HP, enemy = per-type config (set after defaults so
            // they aren't zeroed; non-zero so they survive serialization and the engine's defaulting).
            Dictionary<string, object> hpSettings = RoguelikeSettings.Load(dataDirectory);
            if (run.MaxHp <= 0) run.MaxHp = RoguelikeSettings.PlayerMaxHp(hpSettings); // lazy init (old saves)
            int playerLife = run.Hp > 0 ? run.Hp : run.MaxHp;
            ds.life[DuelSettings.PlayerIndex] = playerLife;
            ds.life[DuelSettings.CpuIndex] = RoguelikeSettings.EnemyHpFor(hpSettings, NodeType(run, nodeId));
            ds.chapter = 0;
            ds.FirstPlayer = duelRng.Next(2);
            // Engine RNG seed (shuffle/draws/dice). Must be nonzero so Act_DuelBegin keeps it
            // instead of randomizing; deterministic so a resumed duel deals the same cards.
            ds.RandSeed = (uint)duelRng.Next(1, int.MaxValue);
            // $.Duel drives the production visuals (cosmetics only). The full settings ride along
            // as duelStarterData so the client can forward them in Duel.begin — there's no chapter
            // for the server to build from, so the custom-duel path is the only way in.
            Dictionary<string, object> duelDto = ds.ToDictionaryForSoloStart();
            duelDto["duelStarterData"] = ds.ToDictionary();
            request.Response["Duel"] = duelDto;
            return true;
        }

        // Deterministic per-(run, node) seed for everything random about a combat duel.
        static int DuelRngSeed(long runSeed, int nodeId)
        {
            unchecked { return (int)((uint)(runSeed * 2654435761L) ^ (uint)(nodeId * 40503 + 0x9E3779B9)); }
        }

        // Reachable = entry (-1) -> any node in row 0; otherwise the current node's `next`.
        static bool IsReachable(RoguelikeRun run, int target)
        {
            List<object> nodes = Utils.GetValue<List<object>>(run.Map, "nodes");
            if (nodes == null) return false;
            Dictionary<string, object> targetNode = null, currentNode = null;
            foreach (object o in nodes)
            {
                Dictionary<string, object> n = o as Dictionary<string, object>;
                if (n == null) continue;
                int id = Utils.GetValue<int>(n, "id", -999);
                if (id == target) targetNode = n;
                if (id == run.Position) currentNode = n;
            }
            if (targetNode == null) return false;
            if (run.Position < 0) return Utils.GetValue<int>(targetNode, "row", -1) == 0;
            if (currentNode == null) return false;
            List<object> next = Utils.GetValue<List<object>>(currentNode, "next");
            if (next == null) return false;
            foreach (object v in next) { try { if (Convert.ToInt32(v) == target) return true; } catch { } }
            return false;
        }

        void Act_RoguelikeAbandonRun(GameServerWebRequest request)
        {
            RoguelikeRun.Delete(GetPlayerDirectory(request.Player));
            WriteRun(request, new RoguelikeRun { Active = false });
        }
    }
}
