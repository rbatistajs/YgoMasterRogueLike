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
            dto["maxAscension"] = RoguelikeMeta.Load(GetPlayerDirectory(request.Player)).MaxAscension;
            dto["acts"] = RoguelikeSettings.Acts(RoguelikeSettings.Load(dataDirectory));
            request.Response["Roguelike"] = dto;
            request.Remove("Roguelike");
        }

        void Act_RoguelikeStartRun(GameServerWebRequest request)
        {
            string gameType = "base_deck";
            int ascension = 0;
            if (request.ActParams != null)
            {
                gameType = Utils.GetValue<string>(request.ActParams, "gameType", "base_deck");
                ascension = Utils.GetValue<int>(request.ActParams, "ascension", 0);
            }
            int maxAsc = RoguelikeMeta.Load(GetPlayerDirectory(request.Player)).MaxAscension;
            if (ascension < 0) ascension = 0;
            if (ascension > maxAsc) ascension = maxAsc; // never trust the client
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
                Act = 0,
                Ascension = ascension,
                Won = false,
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
                            run.Act = 0;
                            Dictionary<string, object> eff = RoguelikeSettings.Effective(
                                RoguelikeSettings.Load(dataDirectory), run.Act, run.Ascension);
                            run.Map = BuildActMap(run, eff);
                            run.Position = -1;
                            run.Visited = new List<object>();
                            run.MaxLp = RoguelikeSettings.PlayerMaxLp(eff);
                            run.Lp = run.MaxLp; // start the run at full LP
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

        // Win: carry remaining LP into run LP (+ combat heal), credit currency. Boss win advances to
        // the next act (regenerate map, inter-act heal) or, on the final act, wins the run and unlocks
        // the next ascension. Loss: LP hits 0, run ends. Clears the pending duel either way.
        void Act_RoguelikeDuelResult(GameServerWebRequest request)
        {
            string dir = GetPlayerDirectory(request.Player);
            RoguelikeRun run = RoguelikeRun.Load(dir);
            bool win = request.ActParams != null && Utils.GetValue<bool>(request.ActParams, "win", false);
            int playerLp = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "playerLp", 0) : 0;
            if (run.Active && run.PendingDuelNode >= 0)
            {
                Dictionary<string, object> baseS = RoguelikeSettings.Load(dataDirectory);
                Dictionary<string, object> eff = RoguelikeSettings.Effective(baseS, run.Act, run.Ascension);
                if (run.MaxLp <= 0) run.MaxLp = RoguelikeSettings.PlayerMaxLp(eff); // lazy init (old saves)
                string nodeType = NodeType(run, run.PendingDuelNode);
                RoguelikeEncounters.Encounter enc = RoguelikeEncounters.ById(dataDirectory, run.PendingEncounterId);
                run.PendingDuelNode = -1;
                run.PendingEncounterId = "";
                if (win)
                {
                    int heal = (int)(run.MaxLp * RoguelikeSettings.HealPercent(eff));
                    run.Lp = Math.Min(run.MaxLp, Math.Max(0, playerLp) + heal);
                    run.Currency += (enc != null && enc.Reward.HasValue) ? enc.Reward.Value : RewardFor(nodeType);
                    if (run.Lp <= 0) run.Active = false; // safety: a 0-LP "win" is still death
                    else if (nodeType == "boss") AdvanceActOrWin(run, baseS, dir);
                }
                else
                {
                    run.Lp = 0;
                    run.Active = false;
                }
                run.Save(dir);
            }
            WriteRun(request, run);
        }

        // Boss beaten: go to the next act (new map + inter-act heal) or win the run + unlock ascension.
        void AdvanceActOrWin(RoguelikeRun run, Dictionary<string, object> baseS, string dir)
        {
            if (run.Act < RoguelikeSettings.Acts(baseS) - 1)
            {
                run.Act++;
                Dictionary<string, object> next = RoguelikeSettings.Effective(baseS, run.Act, run.Ascension);
                run.Map = BuildActMap(run, next);
                run.Position = -1;
                run.Visited = new List<object>();
                int interHeal = (int)(run.MaxLp * RoguelikeSettings.InterActHealPercent(next));
                run.Lp = Math.Min(run.MaxLp, run.Lp + interHeal);
            }
            else
            {
                run.Won = true;
                run.Active = false;
                RoguelikeMeta meta = RoguelikeMeta.Load(dir);
                int unlocked = Math.Min(RoguelikeSettings.Ascensions(baseS) - 1, Math.Max(meta.MaxAscension, run.Ascension + 1));
                if (unlocked > meta.MaxAscension) { meta.MaxAscension = unlocked; meta.Save(dir); }
            }
        }

        // Deterministic per-act map seed (each act of a run gets a distinct, reproducible map).
        static int ActSeed(long runSeed, int act)
        {
            unchecked { return (int)((uint)(runSeed * 31) ^ (uint)(act * 2654435761)); }
        }

        // Build the act map and bake the boss encounter onto its node, so the map can name the boss
        // ahead of the fight. duel/elite nodes pick their encounter lazily at arrival instead.
        Dictionary<string, object> BuildActMap(RoguelikeRun run, Dictionary<string, object> eff)
        {
            RoguelikeMapLayout layout = RoguelikeMapLayout.Create(RoguelikeSettings.Layout(eff));
            RoguelikeMap map = layout.Build(ActSeed(run.Seed, run.Act), eff);
            BakeBossEncounter(run, map);
            return map.ToDictionary();
        }

        // Pick a boss encounter (seeded by the boss node) and store its id + display name on the node.
        void BakeBossEncounter(RoguelikeRun run, RoguelikeMap map)
        {
            MapNode boss = null;
            foreach (MapNode n in map.Nodes) if (n.Type == "boss") { boss = n; break; }
            if (boss == null) return;
            Random rng = new Random(DuelRngSeed(run.Seed, run.Act, boss.Id));
            RoguelikeEncounters.Encounter e = RoguelikeEncounters.Pick(
                dataDirectory, "boss", run.Act, boss.Row, run.Ascension, rng);
            if (e == null)
            {
                Console.WriteLine("[Roguelike] no boss encounter for act " + run.Act + " asc " + run.Ascension);
                return;
            }
            boss.Encounter = e.Id;
            boss.Name = e.Name;
        }

        static bool IsCombat(string type) => type == "duel" || type == "elite" || type == "boss";

        static int RewardFor(string type)
        {
            switch (type) { case "boss": return 1000; case "elite": return 250; default: return 100; }
        }

        static Dictionary<string, object> FindNode(RoguelikeRun run, int id)
        {
            List<object> nodes = run.Map != null ? Utils.GetValue<List<object>>(run.Map, "nodes") : null;
            if (nodes == null) return null;
            foreach (object o in nodes)
            {
                Dictionary<string, object> n = o as Dictionary<string, object>;
                if (n != null && Utils.GetValue<int>(n, "id", -999) == id) return n;
            }
            return null;
        }

        static string NodeType(RoguelikeRun run, int id)
        {
            Dictionary<string, object> n = FindNode(run, id);
            return n != null ? Utils.GetValue<string>(n, "type", "") : "";
        }

        static int NodeRow(RoguelikeRun run, int id)
        {
            Dictionary<string, object> n = FindNode(run, id);
            return n != null ? Utils.GetValue<int>(n, "row", -1) : -1;
        }

        static string NodeEncounter(RoguelikeRun run, int id)
        {
            Dictionary<string, object> n = FindNode(run, id);
            return n != null ? Utils.GetValue<string>(n, "encounter", "") : "";
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

            string nodeType = NodeType(run, nodeId);
            int floor = NodeRow(run, nodeId);
            Random duelRng = new Random(DuelRngSeed(run.Seed, run.Act, nodeId));

            // Boss encounter is baked on the node at map-gen; duel/elite are picked here (lazy). The
            // pick is the first RNG draw, so a resumed duel re-derives the same enemy and draws.
            RoguelikeEncounters.Encounter enc = nodeType == "boss"
                ? RoguelikeEncounters.ById(dataDirectory, NodeEncounter(run, nodeId))
                : RoguelikeEncounters.Pick(dataDirectory, nodeType, run.Act, floor, run.Ascension, duelRng);
            if (enc == null)
            {
                Console.WriteLine("[Roguelike] no encounter for type " + nodeType + " act " + run.Act +
                    " floor " + floor + " asc " + run.Ascension);
                return false;
            }

            string oppFile = RoguelikeDeckPool.ResolveOpponentDeck(dataDirectory, enc.Deck);
            if (oppFile == null) { Console.WriteLine("[Roguelike] encounter deck not found: " + enc.Deck); return false; }
            DeckInfo opp = new DeckInfo();
            opp.File = oppFile;
            opp.Load();
            opp.Name = !string.IsNullOrEmpty(enc.Name) ? enc.Name : System.IO.Path.GetFileNameWithoutExtension(oppFile);

            DuelSettings ds = new DuelSettings();
            ds.Deck[DuelSettings.PlayerIndex] = player;
            ds.Deck[DuelSettings.CpuIndex] = opp;
            ds.IsCustomDuel = true;
            ds.SetRequiredDefaults();
            // Starting LP: player = current run LP, enemy = encounter override else per-type config
            // (set after defaults so they aren't zeroed; non-zero so they survive the engine's defaulting).
            Dictionary<string, object> lpSettings = RoguelikeSettings.Effective(
                RoguelikeSettings.Load(dataDirectory), run.Act, run.Ascension);
            if (run.MaxLp <= 0) run.MaxLp = RoguelikeSettings.PlayerMaxLp(lpSettings); // lazy init (old saves)
            int playerLife = run.Lp > 0 ? run.Lp : run.MaxLp;
            ds.life[DuelSettings.PlayerIndex] = playerLife;
            ds.life[DuelSettings.CpuIndex] = enc.EnemyLp ?? RoguelikeSettings.EnemyLpFor(lpSettings, nodeType);
            ds.chapter = 0;
            ds.FirstPlayer = enc.FirstPlayer ?? duelRng.Next(2);
            ds.cpu = enc.CpuRate ?? RoguelikeSettings.CpuRate(lpSettings);     // AI strength (default 100 = max)
            ds.cpuflag = enc.CpuFlag ?? RoguelikeSettings.CpuFlag(lpSettings); // AI behavior flag (default None)
            // Engine RNG seed (shuffle/draws/dice). Must be nonzero so Act_DuelBegin keeps it
            // instead of randomizing; deterministic so a resumed duel deals the same cards.
            ds.RandSeed = (uint)duelRng.Next(1, int.MaxValue);

            run.PendingEncounterId = enc.Id; // for the reward lookup at duel_result
            // Full settings ride along as duelStarterData so the client forwards them in Duel.begin —
            // there's no chapter for the server to build from. Modifiers (per-type defaults +
            // encounter) compile into cmds (starting board) and LP/hand deltas on top of the base.
            Dictionary<string, object> starter = ds.ToDictionary();
            string duelType = ds.Type == 4 ? "Rush" : "Normal";
            RoguelikeModifiers.Resolver resolver = new RoguelikeModifiers.Resolver
            {
                Rng = duelRng, Decks = new DeckInfo[] { player, opp }, DataDir = dataDirectory,
            };
            RoguelikeModifiers.Apply(starter, duelType, resolver,
                RoguelikeSettings.ModifierDefaults(lpSettings, nodeType), enc.Modifiers);
            // $.Duel drives the production visuals (cosmetics only).
            Dictionary<string, object> duelDto = ds.ToDictionaryForSoloStart();
            duelDto["duelStarterData"] = starter;
            request.Response["Duel"] = duelDto;
            return true;
        }

        // Deterministic per-(run, node) seed for everything random about a combat duel.
        static int DuelRngSeed(long runSeed, int act, int nodeId)
        {
            unchecked { return (int)((uint)(runSeed * 2654435761L) ^ (uint)(act * 0x85EBCA77) ^ (uint)(nodeId * 40503 + 0x9E3779B9)); }
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
