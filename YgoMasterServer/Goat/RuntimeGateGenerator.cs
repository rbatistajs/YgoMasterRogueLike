using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Goat: generates solo gates on the fly per player and injects them
    // into the request response without touching the global Solo data.
    //
    // Flow:
    //   1. Boot: Init() loads RuntimeGates.json configs + deck pools.
    //   2. Client request: WrapSoloData() runs in Act_SoloInfo. It
    //      detects missing/stale per-player generations, regenerates,
    //      persists, and clones SoloData with the generated chapters
    //      injected for the requesting player.
    //   3. Per chapter click: TryGetSoloDuel() runs in
    //      GetSoloDuelSettings. Returns the runtime-generated DuelSettings
    //      so the duel engine sees what the player saw on the map.
    //
    // Regeneration trigger: player has cleared the gate's boss chapter
    // (player.SoloChapters[bossChapterId] == COMPLETE). Re-entering the
    // solo screen rebuilds the gate from a fresh seed.
    static class RuntimeGateGenerator
    {
        static Dictionary<int, RuntimeGateConfig> configs;
        static Dictionary<int, List<DeckPoolLoader.LoadedDeck>> deckPools;   // per gateId
        // Cache of per-player runtime state. Lazy-loaded from disk the
        // first time the player makes a request that needs it.
        static Dictionary<uint, Dictionary<int, GeneratedGate>> playerStates;
        static readonly object stateLock = new object();
        static string dataDirectory;

        public static void Init(string dataDir)
        {
            dataDirectory = dataDir;
            playerStates = new Dictionary<uint, Dictionary<int, GeneratedGate>>();
            deckPools = new Dictionary<int, List<DeckPoolLoader.LoadedDeck>>();

            string configPath = Path.Combine(dataDir, "RuntimeGates.json");
            configs = RuntimeGateConfig.LoadAll(configPath);
            if (configs.Count == 0)
            {
                Console.WriteLine("[RuntimeGateGenerator] no RuntimeGates.json (or empty) — disabled");
                return;
            }

            foreach (RuntimeGateConfig cfg in configs.Values)
            {
                List<DeckPoolLoader.LoadedDeck> pool = DeckPoolLoader.LoadAll(dataDir, cfg.DeckPool);
                deckPools[cfg.GateId] = pool;
                Console.WriteLine("[RuntimeGateGenerator] gate " + cfg.GateId
                    + ": " + cfg.ChapterCount + " chapters, "
                    + pool.Count + " decks in pool '" + cfg.DeckPool + "'");
            }
        }

        public static bool HasAnyGates()
        {
            return configs != null && configs.Count > 0;
        }

        // Wraps `soloData` with the player's runtime-generated chapters.
        // Returns the original dict (no clone) when there's nothing to
        // inject — keeps the request hot-path cheap for vanilla flows.
        public static Dictionary<string, object> WrapSoloData(
            Dictionary<string, object> soloData, Player player, string playerDir)
        {
            if (!HasAnyGates()) return soloData;
            EnsureGeneratedForPlayer(player, playerDir);

            Dictionary<int, GeneratedGate> playerGates;
            lock (stateLock)
            {
                if (!playerStates.TryGetValue(player.Code, out playerGates) || playerGates.Count == 0)
                {
                    return soloData;
                }
            }
            return InjectIntoSoloData(soloData, playerGates);
        }

        // Used by GetSoloDuelSettings: returns the runtime DuelSettings
        // for a chapter, or null if the chapter isn't from a runtime gate
        // (so the caller falls back to the static SoloDuels dict).
        public static DuelSettings TryGetSoloDuel(Player player, int chapterId)
        {
            if (!HasAnyGates()) return null;
            lock (stateLock)
            {
                Dictionary<int, GeneratedGate> playerGates;
                if (!playerStates.TryGetValue(player.Code, out playerGates)) return null;
                foreach (GeneratedGate gate in playerGates.Values)
                {
                    DuelSettings ds;
                    if (gate.SoloDuels.TryGetValue(chapterId, out ds)) return ds;
                }
            }
            return null;
        }

        // ----- generation -----
        static void EnsureGeneratedForPlayer(Player player, string playerDir)
        {
            lock (stateLock)
            {
                Dictionary<int, GeneratedGate> playerGates;
                if (!playerStates.TryGetValue(player.Code, out playerGates))
                {
                    playerGates = new Dictionary<int, GeneratedGate>();
                    playerStates[player.Code] = playerGates;
                }

                foreach (RuntimeGateConfig cfg in configs.Values)
                {
                    GeneratedGate gate;
                    if (!playerGates.TryGetValue(cfg.GateId, out gate))
                    {
                        gate = RuntimeGateStorage.Load(playerDir, cfg.GateId);
                        if (gate != null) playerGates[cfg.GateId] = gate;
                    }

                    bool needsGen = gate == null || IsBossCleared(player, gate);
                    if (!needsGen) continue;

                    gate = Generate(cfg, NextSeed());
                    playerGates[cfg.GateId] = gate;
                    RuntimeGateStorage.Save(playerDir, gate);
                    // Reset cleared status for the old chapter so the client
                    // doesn't render the new boss already as "done".
                    ResetClearedFor(player, gate);
                    Console.WriteLine("[RuntimeGateGenerator] regenerated gate "
                        + cfg.GateId + " for player " + player.Code);
                }
            }
        }

        static bool IsBossCleared(Player player, GeneratedGate gate)
        {
            ChapterStatus status;
            return player.SoloChapters != null
                && player.SoloChapters.TryGetValue(gate.BossChapterId, out status)
                && status == ChapterStatus.COMPLETE;
        }

        static void ResetClearedFor(Player player, GeneratedGate gate)
        {
            if (player.SoloChapters == null) return;
            foreach (int chapterId in gate.Chapters.Keys)
            {
                player.SoloChapters.Remove(chapterId);
            }
        }

        static long NextSeed()
        {
            return DateTime.UtcNow.Ticks;
        }

        static GeneratedGate Generate(RuntimeGateConfig cfg, long seed)
        {
            Random rng = new Random((int)(seed & 0x7FFFFFFF));
            GeneratedGate gate = new GeneratedGate
            {
                GateId         = cfg.GateId,
                Seed           = seed,
                GeneratedAt    = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                BossChapterId  = cfg.BossChapterId(),
                Chapters       = new Dictionary<int, Dictionary<string, object>>(),
                SoloDuels      = new Dictionary<int, DuelSettings>(),
            };

            List<DeckPoolLoader.LoadedDeck> pool;
            if (!deckPools.TryGetValue(cfg.GateId, out pool) || pool.Count == 0)
            {
                Console.WriteLine("[RuntimeGateGenerator] gate " + cfg.GateId
                    + ": empty deck pool — generating empty gate");
                return gate;
            }

            for (int i = 0; i < cfg.ChapterCount; i++)
            {
                int chapterId = cfg.ChapterIdAt(i);
                bool isBoss = (i == cfg.ChapterCount - 1);
                DeckPoolLoader.LoadedDeck deck = pool[rng.Next(pool.Count)];

                gate.Chapters[chapterId] = BuildChapterDict(cfg, chapterId, i, isBoss, deck);
                gate.SoloDuels[chapterId] = BuildDuelSettings(cfg, chapterId, isBoss, deck);
            }
            return gate;
        }

        // Linear layout for MVP: each chapter is one row below the previous,
        // grid_x fixed at 0. parent_chapter chains them.
        static Dictionary<string, object> BuildChapterDict(
            RuntimeGateConfig cfg, int chapterId, int index, bool isBoss,
            DeckPoolLoader.LoadedDeck deck)
        {
            return new Dictionary<string, object>
            {
                { "parent_chapter", index == 0 ? 0 : cfg.ChapterIdAt(index - 1) },
                { "grid_x",         0 },
                { "grid_y",         index },
                { "begin_sn",       "" },
                { "mydeck_set_id",  chapterId },
                { "set_id",         0 },
                { "unlock_id",      0 },
                { "npc_id",         1 },
                { "p1_img",         "" },
                { "p2_img",         "" },
                { "goat", new Dictionary<string, object>
                    {
                        { "type",      isBoss ? "boss" : "duel" },
                        { "level",     0 },
                        { "deck_file", deck.Name },
                        { "runtime",   true },
                    }
                },
            };
        }

        // Builds a minimal DuelSettings by going through FromDictionary
        // (so all the upstream reflection-based field/property init
        // happens). Both sides use the same deck — the engine swaps in
        // the player's MyDeck at start.
        static DuelSettings BuildDuelSettings(
            RuntimeGateConfig cfg, int chapterId, bool isBoss,
            DeckPoolLoader.LoadedDeck deck)
        {
            Dictionary<string, object> duel = new Dictionary<string, object>
            {
                { "Deck",            new List<object> { deck.SoloDuelDeck, deck.SoloDuelDeck } },
                { "chapter",         chapterId },
                { "name",            new List<object> { "", deck.Name } },
                { "dialog_intro",    "" },
                { "dialog_outro",    "" },
                { "cpu",             100 },
                { "cpuflag",         "None" },
                { "regulation_name", cfg.RegulationName ?? "" },
            };
            // TODO (post-MVP): merge cfg.Modifiers / cfg.BossModifiers here
            // (same shape Python `apply_modifiers` produces — random_specs +
            // cmds + life + hnum).
            DuelSettings ds = new DuelSettings();
            ds.FromDictionary(duel);
            return ds;
        }

        // ----- injection -----
        // Clones SoloData (shallow at the top, deep on the `chapter` and
        // `gate` dicts we touch) and writes the player's generated chapters
        // + the dynamically resolved clear_chapter into the copy.
        static Dictionary<string, object> InjectIntoSoloData(
            Dictionary<string, object> soloData, Dictionary<int, GeneratedGate> playerGates)
        {
            Dictionary<string, object> wrapped = new Dictionary<string, object>(soloData);

            // Per-gate clones of `Solo.chapter[<gateId>]` and `Solo.gate[<gateId>]`
            // so we can override entries without mutating the global SoloData.
            Dictionary<string, object> chapterRoot = wrapped.TryGetValue("chapter", out object cObj)
                ? new Dictionary<string, object>(cObj as Dictionary<string, object>)
                : new Dictionary<string, object>();
            Dictionary<string, object> gateRoot = wrapped.TryGetValue("gate", out object gObj)
                ? new Dictionary<string, object>(gObj as Dictionary<string, object>)
                : new Dictionary<string, object>();

            foreach (GeneratedGate gate in playerGates.Values)
            {
                Dictionary<string, object> chaptersDict = new Dictionary<string, object>();
                foreach (KeyValuePair<int, Dictionary<string, object>> kv in gate.Chapters)
                {
                    chaptersDict[kv.Key.ToString()] = kv.Value;
                }
                chapterRoot[gate.GateId.ToString()] = chaptersDict;

                // Patch the gate entry's clear_chapter to point at the
                // dynamically generated boss.
                object gateMetaObj;
                if (gateRoot.TryGetValue(gate.GateId.ToString(), out gateMetaObj))
                {
                    Dictionary<string, object> gateMeta = new Dictionary<string, object>(
                        gateMetaObj as Dictionary<string, object>);
                    gateMeta["clear_chapter"] = gate.BossChapterId;
                    gateRoot[gate.GateId.ToString()] = gateMeta;
                }
            }

            wrapped["chapter"] = chapterRoot;
            wrapped["gate"]    = gateRoot;
            return wrapped;
        }
    }
}
