using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster.Builder;
using YgoMaster.Layout;
using YgoMaster.Rewards;

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
                string source = cfg.Manual ? "manual cells" : ("C# " + cfg.Format + " generator");
                Console.WriteLine("[RuntimeGateGenerator] gate " + cfg.GateId
                    + " (" + cfg.Format + ", " + cfg.DuelType + "): "
                    + source + ", " + pool.Count + " decks in pool '"
                    + cfg.DeckPool + "'");
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
                Chapters       = new Dictionary<int, Dictionary<string, object>>(),
                SoloDuels      = new Dictionary<int, DuelSettings>(),
                SoloDuelDicts  = new Dictionary<int, Dictionary<string, object>>(),
                ItemDrops      = new Dictionary<int, int[]>(),
            };

            List<DeckPoolLoader.LoadedDeck> pool;
            if (!deckPools.TryGetValue(cfg.GateId, out pool) || pool.Count == 0)
            {
                Console.WriteLine("[RuntimeGateGenerator] gate " + cfg.GateId
                    + ": empty deck pool — generating empty gate");
                gate.BossChapterId = cfg.FallbackBossChapterId;
                return gate;
            }

            // Build a fresh layout from the C# generators. Manual gates
            // use their hand-authored cells; everything else runs the
            // matching format generator. Returns null if the format isn't
            // recognized — fall back to the simple linear chain in that
            // case so the gate still functions.
            LayoutGenerator.Result layout = BuildLayout(cfg, rng);
            if (layout != null)
            {
                gate.BossChapterId = layout.BossChapterId;
                GenerateFromLayout(cfg, layout, gate, pool, rng);
            }
            else
            {
                gate.BossChapterId = cfg.FallbackBossChapterId;
                GenerateLinearFallback(cfg, gate, pool, rng);
            }
            RollItemDrops(cfg, gate, rng);
            return gate;
        }

        // Pra cada chapter gerado, rola item drop pelo chance configurado
        // no `rewards` da gate. Resultados ficam em gate.ItemDrops e são
        // persistidos no JSON do player — drop é estável até a próxima
        // regen (player pode abrir/fechar o menu sem o drop mudar).
        static void RollItemDrops(RuntimeGateConfig cfg, GeneratedGate gate, Random rng)
        {
            if (cfg.Rewards == null || !cfg.Rewards.AnyDrop) return;
            foreach (KeyValuePair<int, Dictionary<string, object>> kv in gate.Chapters)
            {
                Dictionary<string, object> goat =
                    Utils.GetValue<Dictionary<string, object>>(kv.Value, "goat");
                string chapterType = goat != null
                    ? Utils.GetValue<string>(goat, "type") ?? "duel" : "duel";
                double chance = cfg.Rewards.DropChanceFor(chapterType);
                Tuple<int, int> drop = RewardPicker.Roll(rng, chance, cfg.Rewards);
                if (drop != null)
                {
                    gate.ItemDrops[kv.Key] = new[] { drop.Item1, drop.Item2 };
                }
            }
        }

        // Bridge from the cfg dicts (raw JSON shape) to a GenerationContext
        // the layout pipeline can consume. Mirrors `_precompute_runtime_layout`'s
        // param parsing in build_grid_gate_procedural.py.
        static LayoutGenerator.Result BuildLayout(RuntimeGateConfig cfg, Random rng)
        {
            string format = cfg.Manual ? "manual" : cfg.Format;
            if (format != "hourglass" && format != "dungeon"
                && format != "tower" && format != "manual") return null;

            Dictionary<string, object> gp = cfg.GenericParams ?? new Dictionary<string, object>();
            GenerationContext ctx = new GenerationContext
            {
                GateId         = cfg.GateId,
                Format         = format,
                Rng            = rng,
                FormatParams   = cfg.FormatParams,
                ManualCells    = cfg.ManualCells,
                ManualBossPos  = cfg.ManualBossPos,
                EliteCount     = GetIntOr(gp, "elite_count",     2),
                LockCount      = GetIntOr(gp, "lock_count",      0),
                RewardCount    = GetIntOr(gp, "reward_count",    3),
                TreasureCount  = GetIntOr(gp, "treasure_count",  2),
                DuelLevel      = GetIntOr(gp, "duel_level",      3),
                EliteLevel     = GetIntOr(gp, "elite_level",     2),
                BossLevel      = GetIntOr(gp, "boss_level",      1),
                DifficultyMode = Utils.GetValue<string>(gp, "difficulty_curve") ?? "default",
            };
            return LayoutGenerator.Generate(ctx);
        }

        static int GetIntOr(Dictionary<string, object> d, string key, int fallback)
        {
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        // Iterate the C#-generated chapter dicts. Each non-passive chapter
        // (npc_id=1) gets a per-player random deck + a DuelSettings built
        // from the relevant modifier template.
        static void GenerateFromLayout(RuntimeGateConfig cfg, LayoutGenerator.Result layout,
                                       GeneratedGate gate,
                                       List<DeckPoolLoader.LoadedDeck> pool, Random rng)
        {
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in layout.Chapters)
            {
                int chapterId;
                if (!int.TryParse(kv.Key, out chapterId)) continue;
                Dictionary<string, object> chapterDict = kv.Value;

                string chapterType = GetChapterMeta(layout, chapterId, "type", "duel");
                int chapterLevel = GetChapterMetaInt(layout, chapterId, "level", 3);
                bool isDuel = Utils.GetValue<int>(chapterDict, "npc_id") == 1;
                if (isDuel)
                {
                    DeckPoolLoader.LoadedDeck deck = pool[rng.Next(pool.Count)];
                    // Image: usa `card_image` (cardId direto) lido pelo
                    // SoloChapterCardImage do client. Sem fallback p1/p2 —
                    // o sistema novo substitui o antigo de atlas.
                    if (deck.BossCard > 0) chapterDict["card_image"] = deck.BossCard;
                    chapterDict["goat"] = new Dictionary<string, object>
                    {
                        { "type",      chapterType },
                        { "level",     chapterLevel },
                        { "deck_file", deck.Name },
                        { "runtime",   true },
                    };
                    Dictionary<string, object> duelDict =
                        BuildDuelDict(cfg, chapterId, chapterType, deck, rng);
                    gate.SoloDuelDicts[chapterId] = duelDict;
                    gate.SoloDuels[chapterId]     = HydrateDuelSettings(duelDict, chapterId);
                }
                else
                {
                    chapterDict["goat"] = new Dictionary<string, object>
                    {
                        { "type",    chapterType },
                        { "level",   chapterLevel },
                        { "runtime", true },
                    };
                }
                gate.Chapters[chapterId] = chapterDict;
            }
        }

        static string GetChapterMeta(LayoutGenerator.Result layout, int chapterId,
                                     string key, string fallback)
        {
            Dictionary<string, object> meta;
            if (layout.ChapterMeta != null
                && layout.ChapterMeta.TryGetValue(chapterId.ToString(), out meta))
            {
                return Utils.GetValue<string>(meta, key) ?? fallback;
            }
            return fallback;
        }

        static int GetChapterMetaInt(LayoutGenerator.Result layout, int chapterId,
                                     string key, int fallback)
        {
            Dictionary<string, object> meta;
            if (layout.ChapterMeta != null
                && layout.ChapterMeta.TryGetValue(chapterId.ToString(), out meta))
            {
                object v;
                if (meta.TryGetValue(key, out v) && v != null)
                {
                    try { return Convert.ToInt32(v); } catch { }
                }
            }
            return fallback;
        }

        // Simple linear chain — used when the gate's format isn't one of
        // the C# generators (e.g. `linear`, or unknown). Same N-chapter
        // shape as the original MVP runtime gate.
        static void GenerateLinearFallback(RuntimeGateConfig cfg, GeneratedGate gate,
                                            List<DeckPoolLoader.LoadedDeck> pool, Random rng)
        {
            int chapterCount = cfg.FallbackChapterCount;
            for (int i = 0; i < chapterCount; i++)
            {
                int chapterId = cfg.ChapterIdBase + i;
                bool isBoss = (i == chapterCount - 1);
                DeckPoolLoader.LoadedDeck deck = pool[rng.Next(pool.Count)];

                gate.Chapters[chapterId] = BuildChapterDict(cfg, chapterId, i, isBoss, deck);
                Dictionary<string, object> duelDict = BuildDuelDict(
                    cfg, chapterId, isBoss ? "boss" : "duel", deck, rng);
                gate.SoloDuelDicts[chapterId] = duelDict;
                gate.SoloDuels[chapterId] = HydrateDuelSettings(duelDict, chapterId);
            }
        }

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
            Dictionary<string, object> ch = new Dictionary<string, object>
            {
                { "parent_chapter", index == 0 ? 0 : cfg.ChapterIdBase + index - 1 },
                { "grid_x",         0 },
                { "grid_y",         index },
                { "begin_sn",       "" },
                { "mydeck_set_id",  chapterId },
                { "set_id",         0 },
                { "unlock_id",      0 },
                { "npc_id",         1 },
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
            // Card image: lido pelo SoloChapterCardImage do client.
            if (deck.BossCard > 0) ch["card_image"] = deck.BossCard;
            return ch;
        }

        // (Defaults visuais movidos pra SoloDuelBuilder — esse caller
        // só pede `CosmeticMode.Vanilla` ou `Random` lá.)

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

        // Builds the duel dict in the same shape `_solo_helpers.build_soloduel`
        // emits. Both sides use the same deck — the engine swaps in the
        // player's MyDeck at start. `chapterType` seleciona o modifier
        // default (boss / elite / duel). `rng` é usado pelo cosmetic
        // random e pelo ModifierApplier (markers de random_specs).
        //
        // ModifierApplier (C#) é chamado INLINE — não precisa mais de
        // template pre-compilado pelo Python. life/hnum/cmds/random_specs
        // saem todos do mesmo lugar que o bake path usa.
        static Dictionary<string, object> BuildDuelDict(
            RuntimeGateConfig cfg, int chapterId, string chapterType,
            DeckPoolLoader.LoadedDeck deck, Random rng)
        {
            Dictionary<string, object> modifier = cfg.ModifierFor(chapterType);
            Dictionary<string, object>[] layers = modifier != null
                ? new[] { modifier }
                : new Dictionary<string, object>[0];

            return SoloDuelBuilder.BuildInner(
                chapterId:      chapterId,
                deckSection:    deck.SoloDuelDeck,
                cpuName:        deck.Name,
                duelType:       cfg.DuelType,
                cosmeticMode:   cfg.CosmeticMode,
                rng:            rng,
                modifierLayers: layers);
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
                        Dictionary<string, object> rewardBlock = new Dictionary<string, object>
                        {
                            // category 1 → { item 1 (gem) → count }
                            { "1", new Dictionary<string, object> { { "1", DefaultGemReward } } }
                        };
                        // Adiciona item drop se houver (categoria != 1).
                        int[] drop;
                        if (gate.ItemDrops != null && gate.ItemDrops.TryGetValue(kv.Key, out drop))
                        {
                            string catKey = drop[0].ToString();
                            string itemKey = drop[1].ToString();
                            Dictionary<string, object> catBucket;
                            if (rewardBlock.TryGetValue(catKey, out object existing)
                                && existing is Dictionary<string, object> existingBucket)
                            {
                                catBucket = existingBucket;
                            }
                            else
                            {
                                catBucket = new Dictionary<string, object>();
                                rewardBlock[catKey] = catBucket;
                            }
                            catBucket[itemKey] = 1;
                        }
                        rewardRoot[rewardKey.ToString()] = rewardBlock;
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
