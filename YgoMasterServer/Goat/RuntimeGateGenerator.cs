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

            configs = RuntimeGateConfig.LoadAll(dataDir);
            if (configs.Count == 0)
            {
                Console.WriteLine("[RuntimeGateGenerator] no GridGates.json entries "
                    + "with `runtime: true` — disabled");
                return;
            }

            foreach (RuntimeGateConfig cfg in configs.Values)
            {
                List<DeckPoolLoader.LoadedDeck> pool = DeckPoolLoader.LoadAll(dataDir, cfg.DeckPool);
                deckPools[cfg.GateId] = pool;
                Console.WriteLine("[RuntimeGateGenerator] gate " + cfg.GateId
                    + " (" + cfg.Format + ", " + cfg.DuelType + "): "
                    + cfg.ChapterCount + " chapters, "
                    + pool.Count + " decks in pool '" + cfg.DeckPool + "'");
            }
        }

        public static bool HasAnyGates()
        {
            return configs != null && configs.Count > 0;
        }

        // Ensures the player has fresh-or-cached generated chapters and
        // splices them into `soloData` in place. We mutate the global dict
        // (rather than clone) so that any downstream lookup using SoloData
        // — most notably GetChapterSetIds, which decides MyDeck vs loaner
        // at chapter-start — also sees the runtime chapters. Returns the
        // same `soloData` reference for convenience.
        //
        // NOTE: this writes per-player chapters into a global dict. For
        // single-player offline (the common case) this is fine. For a
        // multi-player server, players would overwrite each other —
        // revisit before that scenario matters.
        public static Dictionary<string, object> WrapSoloData(
            Dictionary<string, object> soloData, Player player, string playerDir)
        {
            if (!HasAnyGates()) return soloData;
            try
            {
                Console.WriteLine("[RuntimeGate] WrapSoloData player=" + player.Code);
                EnsureGeneratedForPlayer(player, playerDir);

                Dictionary<int, GeneratedGate> playerGates;
                lock (stateLock)
                {
                    if (!playerStates.TryGetValue(player.Code, out playerGates) || playerGates.Count == 0)
                    {
                        return soloData;
                    }
                    InjectIntoSoloData(soloData, playerGates);
                }
                return soloData;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RuntimeGate] WrapSoloData FAILED for player " + player.Code
                    + ": " + ex);
                return soloData;
            }
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
                if (!playerStates.TryGetValue(player.Code, out playerGates))
                {
                    Console.WriteLine("[RuntimeGate] TryGetSoloDuel chapter=" + chapterId
                        + " player=" + player.Code + " → no player state (miss)");
                    return null;
                }
                foreach (GeneratedGate gate in playerGates.Values)
                {
                    DuelSettings ds;
                    if (gate.SoloDuels.TryGetValue(chapterId, out ds))
                    {
                        Console.WriteLine("[RuntimeGate] TryGetSoloDuel chapter=" + chapterId
                            + " → HIT (gate " + gate.GateId + ")");
                        return ds;
                    }
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
                    bool loadedFromDisk = false;
                    if (!playerGates.TryGetValue(cfg.GateId, out gate))
                    {
                        gate = RuntimeGateStorage.Load(playerDir, cfg.GateId);
                        if (gate != null)
                        {
                            playerGates[cfg.GateId] = gate;
                            loadedFromDisk = true;
                        }
                    }

                    bool needsGen = gate == null || IsBossCleared(player, gate);
                    if (!needsGen)
                    {
                        if (loadedFromDisk)
                        {
                            Console.WriteLine("[RuntimeGate] gate " + cfg.GateId
                                + " player=" + player.Code + " → loaded from disk ("
                                + gate.Chapters.Count + " chapters, boss=" + gate.BossChapterId + ")");
                        }
                        continue;
                    }

                    string reason = gate == null ? "no prior gen" : "boss cleared";
                    gate = Generate(cfg, NextSeed());
                    playerGates[cfg.GateId] = gate;
                    RuntimeGateStorage.Save(playerDir, gate);
                    ResetClearedFor(player, gate);
                    Console.WriteLine("[RuntimeGate] gate " + cfg.GateId
                        + " player=" + player.Code + " → GENERATED (" + reason + "): "
                        + gate.Chapters.Count + " chapters, boss=" + gate.BossChapterId
                        + ", seed=" + gate.Seed);
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
                BossChapterId  = cfg.BossChapterId,
                Chapters       = new Dictionary<int, Dictionary<string, object>>(),
                SoloDuels      = new Dictionary<int, DuelSettings>(),
                SoloDuelDicts  = new Dictionary<int, Dictionary<string, object>>(),
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
                int chapterId = cfg.ChapterIdBase + i;
                bool isBoss = (i == cfg.ChapterCount - 1);
                DeckPoolLoader.LoadedDeck deck = pool[rng.Next(pool.Count)];

                gate.Chapters[chapterId] = BuildChapterDict(cfg, chapterId, i, isBoss, deck);
                Dictionary<string, object> duelDict = BuildDuelDict(cfg, chapterId, isBoss, deck);
                gate.SoloDuelDicts[chapterId] = duelDict;
                gate.SoloDuels[chapterId] = HydrateDuelSettings(duelDict, chapterId);
            }
            return gate;
        }

        // Default boss portraits — valid IDs from the install. Used when
        // the picked deck has no entry in boss_portraits.json (yet).
        const string DefaultP1Img = "boss_21466326";
        const string DefaultP2Img = "boss_34124316";

        // Linear layout for MVP: each chapter is one row below the previous,
        // grid_x fixed at 0. parent_chapter chains them.
        //
        // Mirrors the field set the Python builder writes — including
        // `anime` (LoadSolo back-fills `anime: 0` on baked chapters; we
        // must include it explicitly since our chapters are injected
        // after that pass).
        static Dictionary<string, object> BuildChapterDict(
            RuntimeGateConfig cfg, int chapterId, int index, bool isBoss,
            DeckPoolLoader.LoadedDeck deck)
        {
            return new Dictionary<string, object>
            {
                { "parent_chapter", index == 0 ? 0 : cfg.ChapterIdBase + index - 1 },
                { "grid_x",         0 },
                { "grid_y",         index },
                { "begin_sn",       "" },
                { "mydeck_set_id",  chapterId },
                { "set_id",         0 },
                { "unlock_id",      0 },
                { "npc_id",         1 },
                { "p1_img",         DefaultP1Img },
                { "p2_img",         DefaultP2Img },
                { "anime",          0 },
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

        // Vanilla cosmetic defaults — mirror `_solo_helpers.VANILLA_COSMETICS`
        // on the Python side. P1's actual values come from MyDeck at duel
        // start; these are placeholders so the duel JSON has the same
        // shape the engine expects.
        static readonly List<object> DefaultMat        = new List<object> { 1090016, 1090016 };
        static readonly List<object> DefaultDuelObject = new List<object> { 1100016, 1100016 };
        static readonly List<object> DefaultAvatarHome = new List<object> { 1110016, 1110016 };
        static readonly List<object> DefaultSleeve     = new List<object> { 0,       1070052 };
        static readonly List<object> DefaultIcon       = new List<object> { 0,       1011047 };
        static readonly List<object> DefaultIconFrame  = new List<object> { 0,       1032001 };
        static readonly List<object> DefaultAvatar     = new List<object> { 0,       1000028 };
        static readonly List<object> DefaultLife       = new List<object> { 8000,    8000    };
        static readonly List<object> DefaultHnum       = new List<object> { 5,       5       };

        // Constructs the runtime DuelSettings from the dict the same way
        // LoadSoloDuels does for baked chapters: FromDictionary, assign
        // unique Deck IDs (otherwise Deck[i].Id stays 0 and the client's
        // deck-detail popup can't disambiguate), then SetRequiredDefaults
        // (back-fills cosmetics, life, names, etc.).
        //
        // Deck IDs are derived from chapter id offset into a high range
        // so they can't collide with sequential baked IDs (which start at
        // 1 in LoadSoloDuels).
        const int RuntimeDeckIdBase = 90000000;

        public static DuelSettings HydrateDuelSettings(Dictionary<string, object> duelDict, int chapterId)
        {
            DuelSettings ds = new DuelSettings();
            ds.FromDictionary(duelDict);
            for (int i = 0; i < ds.Deck.Length; i++)
            {
                if (ds.Deck[i] != null && ds.Deck[i].MainDeckCards.Count > 0)
                {
                    ds.Deck[i].Id = RuntimeDeckIdBase + chapterId * 10 + i;
                }
            }
            ds.SetRequiredDefaults();
            return ds;
        }

        // Builds the duel dict in the same shape the Python builder
        // (`_solo_helpers.build_soloduel`) emits. Both sides use the same
        // deck — the engine swaps in the player's MyDeck at start.
        static Dictionary<string, object> BuildDuelDict(
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
                { "mat",             DefaultMat },
                { "duel_object",     DefaultDuelObject },
                { "avatar_home",     DefaultAvatarHome },
                { "sleeve",          DefaultSleeve },
                { "icon",            DefaultIcon },
                { "icon_frame",      DefaultIconFrame },
                { "avatar",          DefaultAvatar },
                { "life",            DefaultLife },
                { "hnum",            DefaultHnum },
            };
            // TODO (post-MVP): pull modifiers from cfg.GenericParams.modifier_defaults
            // (per-chapter-type defaults) + per-chapter overrides — same
            // shape Python `apply_modifiers` produces (random_specs + cmds).
            return duel;
        }

        // Default gem reward per runtime chapter (category 1 = gems,
        // item id 1 = gem). Matches the baked-chapter format used by the
        // Python builder. Higher tiers / boss could vary in later phases.
        const int DefaultGemReward = 20;

        // ----- injection -----
        // Splices each player gate's chapters into `Solo.chapter[<gateId>]`,
        // patches the gate metadata's `clear_chapter`, and registers reward
        // entries for every generated chapter (SoloInfo loader logs a WARN
        // if a chapter's mydeck_set_id / set_id has no reward entry —
        // unconfirmed but suspected to also block deck selection on the
        // client side). Mutates the dict directly (see WrapSoloData NOTE).
        static void InjectIntoSoloData(
            Dictionary<string, object> soloData, Dictionary<int, GeneratedGate> playerGates)
        {
            Dictionary<string, object> chapterRoot = Utils.GetDictionary(soloData, "chapter");
            Dictionary<string, object> gateRoot    = Utils.GetDictionary(soloData, "gate");
            if (chapterRoot == null || gateRoot == null) return;

            Dictionary<string, object> rewardRoot = Utils.GetDictionary(soloData, "reward");
            if (rewardRoot == null)
            {
                rewardRoot = new Dictionary<string, object>();
                soloData["reward"] = rewardRoot;
            }

            foreach (GeneratedGate gate in playerGates.Values)
            {
                Dictionary<string, object> chaptersDict = new Dictionary<string, object>();
                foreach (KeyValuePair<int, Dictionary<string, object>> kv in gate.Chapters)
                {
                    chaptersDict[kv.Key.ToString()] = kv.Value;

                    // Register reward for whichever set id this chapter uses.
                    int mydeckSetId = Utils.GetValue<int>(kv.Value, "mydeck_set_id");
                    int loanerSetId = Utils.GetValue<int>(kv.Value, "set_id");
                    int rewardKey = mydeckSetId != 0 ? mydeckSetId : loanerSetId;
                    if (rewardKey != 0)
                    {
                        rewardRoot[rewardKey.ToString()] = new Dictionary<string, object>
                        {
                            // category 1 → { item 1 (gem) → count }
                            { "1", new Dictionary<string, object> { { "1", DefaultGemReward } } }
                        };
                    }
                }
                chapterRoot[gate.GateId.ToString()] = chaptersDict;

                Dictionary<string, object> gateMeta = Utils.GetDictionary(gateRoot, gate.GateId.ToString());
                if (gateMeta != null) gateMeta["clear_chapter"] = gate.BossChapterId;

                Console.WriteLine("[RuntimeGate] injected gate " + gate.GateId
                    + " into SoloData: " + chaptersDict.Count + " chapters + rewards, clear_chapter="
                    + gate.BossChapterId);
            }
        }
    }
}
