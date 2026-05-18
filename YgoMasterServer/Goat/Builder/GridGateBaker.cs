using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster.Layout;
using YgoMaster.Modifiers;
using YgoMaster.Rewards;

namespace YgoMaster.Builder
{
    // Port do pipeline `emit_gate` Python — bake completo de uma gate
    // do GridGates.json. Determinístico (usa seed de generic_params.seed
    // ou hash da identidade da gate).
    //
    // Outputs:
    //   - DataLE/Solo.json patchado (gate meta + chapter dict + rewards + unlocks)
    //   - DataLE/SoloDuels/<chapterId>.json — um por chapter duel
    //   - DataLE/ClientData/IDS/IDS_SOLO.txt — bloco BEGIN/END regenerado
    //   - DataLE/ClientData/SoloGateCards.txt — cover card do gate
    //
    // Pré-requisitos (sem isso o bake falha):
    //   - LayoutGenerator suporta o format da gate
    //   - decks/<duelType>/<level>/*.json populated pra pelo menos um level
    //   - Solo.json existe (será preservado fora do gate target)
    static class GridGateBaker
    {
        public class Summary
        {
            public int GateId;
            public string Format;
            public int ChaptersCount;
            public int DuelFilesWritten;
            public int BossChapterId;
            public string BossDeck;
            public long Seed;
            public string EntryHash;
            public List<string> IdsLines = new List<string>();
        }

        // Bake uma gate. Retorna summary com counts + ids_lines pra
        // patchar IDS_SOLO depois (o caller acumula de múltiplas gates
        // num bake all e patcha uma vez no final).
        public static Summary Bake(string dataDir, Dictionary<string, object> entry)
        {
            int gateId = Utils.GetValue<int>(entry, "gate_id");
            string duelType = Utils.GetValue<string>(entry, "duel_type") ?? "Normal";
            string format = Utils.GetValue<string>(entry, "format") ?? "linear";
            bool manual = Utils.GetValue<bool>(entry, "manual");

            // Seed: explicit em generic_params.seed, ou hash determinístico
            // da identidade do gate.
            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(entry, "generic_params") ?? new Dictionary<string, object>();
            long seed = GetSeed(entry, gateId, duelType, format, gp);
            Random rng = new Random((int)(seed & 0x7FFFFFFF));

            // Layout em memória.
            LayoutGenerator.Result layout = BuildLayout(entry, gp, manual ? "manual" : format, rng);
            if (layout == null)
                throw new InvalidOperationException("Format '" + format + "' não suportado pelos C# generators (use linear via runtime).");

            // Carrega pools por level (decks/<duelType>/0..6/).
            Dictionary<int, List<DeckPoolLoader.LoadedDeck>> deckPools =
                DeckPoolLoader.LoadByLevels(dataDir, duelType);
            if (deckPools.Count == 0)
                throw new InvalidOperationException("Nenhum deck encontrado em decks/" + duelType.ToLower() + "/<level>/");

            // Modifier defaults per chapter type (gate-level layer 1).
            Dictionary<string, Dictionary<string, object>> modifierDefaults =
                ParseModifierDefaults(gp);
            string regulationName = duelType == "Rush" ? "Rush Duel" : "Goat Format";

            // Reward + cosmetics config da entry (mesmo schema do runtime).
            RewardConfig rewardCfg = RewardConfig.Parse(entry);
            SoloDuelBuilder.CosmeticMode cosmeticMode = ParseCosmeticMode(entry);

            string soloDuelsDir = Path.Combine(dataDir, "SoloDuels");
            Directory.CreateDirectory(soloDuelsDir);

            // Cleanup: SoloDuels antigos do range.
            int lo = gateId * 10000;
            int hi = (gateId + 1) * 10000;
            foreach (string p in Directory.GetFiles(soloDuelsDir, gateId + "*.json"))
            {
                int cid;
                if (int.TryParse(Path.GetFileNameWithoutExtension(p), out cid)
                    && cid >= lo && cid < hi)
                {
                    try { File.Delete(p); } catch { }
                }
            }

            Summary summary = new Summary { GateId = gateId, Format = format, Seed = seed };
            Dictionary<string, Dictionary<string, object>> rewards = new Dictionary<string, Dictionary<string, object>>();
            Dictionary<string, Dictionary<string, object>> unlocks = new Dictionary<string, Dictionary<string, object>>();
            DeckPoolLoader.LoadedDeck bossDeck = null;

            // Itera chapters do layout, enriquece com goat metadata + deck
            // + card_image + rewards, escreve SoloDuels.
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in layout.Chapters)
            {
                int chapterId = int.Parse(kv.Key);
                Dictionary<string, object> ch = kv.Value;
                string chapterType = GetMetaString(layout, chapterId, "type", "duel");
                int level = GetMetaInt(layout, chapterId, "level", 3);
                bool isDuel = chapterType == "duel" || chapterType == "elite" || chapterType == "boss";

                // goat sub-dict (metadados de bake — não usado pelo engine,
                // mas útil pra debug + iteração futura).
                Dictionary<string, object> goat = new Dictionary<string, object>
                {
                    { "type",   chapterType },
                    { "level",  level },
                    { "seed",   seed },
                    { "manual", false },
                };
                ch["goat"] = goat;

                if (isDuel)
                {
                    List<DeckPoolLoader.LoadedDeck> pool = DeckPoolLoader.PoolForLevel(deckPools, level);
                    if (pool == null) continue;
                    DeckPoolLoader.LoadedDeck deck = pool[rng.Next(pool.Count)];
                    goat["deck_file"] = deck.Name;
                    if (deck.BossCard > 0) ch["card_image"] = deck.BossCard;

                    // Modifier layer: gate-level default pro chapter type.
                    // (Per-chapter override + special-deck embedded modifiers
                    //  ficam pra fase futura.)
                    Dictionary<string, object> modifier;
                    modifierDefaults.TryGetValue(chapterType, out modifier);
                    Dictionary<string, object>[] layers = modifier != null
                        ? new[] { modifier }
                        : new Dictionary<string, object>[0];

                    Dictionary<string, object> duelPayload = SoloDuelBuilder.Build(
                        chapterId:    chapterId,
                        deckSection:  deck.SoloDuelDeck,
                        cpuName:      deck.Name,
                        duelType:     duelType,
                        cosmeticMode: cosmeticMode,
                        rng:          rng,
                        modifierLayers: layers);
                    File.WriteAllText(Path.Combine(soloDuelsDir, chapterId + ".json"),
                                       MiniJSON.Json.Serialize(duelPayload));
                    summary.DuelFilesWritten++;

                    if (chapterId == layout.BossChapterId)
                    {
                        bossDeck = deck;
                        summary.BossDeck = deck.Name;
                    }
                }

                // Reward block: gems + opcional item drop.
                Dictionary<string, object> gemBlock = SoloRewards.GemRewardBlock(GemsFor(chapterType, level));
                Dictionary<string, object>[] toMerge = new Dictionary<string, object>[2];
                toMerge[0] = gemBlock;

                // Card reward pra elite/boss (deck.BossCard).
                if (isDuel && (chapterType == "elite" || chapterType == "boss"))
                {
                    int cardId = goat.ContainsKey("deck_file")
                        ? GetBossCardOf(deckPools, level, goat["deck_file"] as string) : 0;
                    if (cardId > 0)
                    {
                        int qty = chapterType == "boss" ? 3 : 1;
                        toMerge[1] = SoloRewards.CardRewardBlock(cardId, qty);
                    }
                }
                // Item drop opcional via RewardPicker.
                if (toMerge[1] == null)
                {
                    Tuple<int, int> drop = RewardPicker.Roll(rng, rewardCfg.DropChanceFor(chapterType), rewardCfg);
                    if (drop != null) toMerge[1] = RewardPicker.ItemRewardBlock(drop.Item1, drop.Item2);
                }

                Dictionary<string, object> merged = toMerge[1] != null
                    ? SoloRewards.Merge(toMerge)
                    : gemBlock;
                rewards[chapterId.ToString()] = merged;

                // IDS line per chapter — "L<level> <type>: <deck_or_label>"
                string label = goat.ContainsKey("deck_file")
                    ? (goat["deck_file"] as string ?? "").Replace(".json", "")
                    : chapterType.ToUpper();
                summary.IdsLines.Add("[IDS_SOLO.CHAPTER" + chapterId + "_EXPLANATION]");
                summary.IdsLines.Add("L" + level + " " + chapterType + ": " + label);
            }

            // Unlocks: lock chapters da layout viram { lock_cid: {"2": [key_cid]} }.
            // O LayoutGenerator já registrou em layout.Locks.
            foreach (KeyValuePair<string, string> kv in layout.Locks)
            {
                int lockCid; int keyCid;
                if (!int.TryParse(kv.Key, out lockCid)) continue;
                if (!int.TryParse(kv.Value, out keyCid)) continue;
                unlocks[lockCid.ToString()] = new Dictionary<string, object>
                {
                    { "2", new List<object> { keyCid } }
                };
            }

            // Gate meta.
            string gateName = Utils.GetValue<string>(entry, "name") ?? ("Gate " + gateId);
            string gateBlurb = Utils.GetValue<string>(entry, "blurb") ?? ("Generated gate (" + format + ").");
            Dictionary<string, object> gateMeta = new Dictionary<string, object>
            {
                { "priority",        gateId },
                { "parent_gate",     0 },
                { "view_gate",       0 },
                { "unlock_id",       0 },
                { "clear_chapter",   layout.BossChapterId },
                { "regulation_name", regulationName },
            };

            // Patch Solo.json.
            SoloJsonPatcher.UpsertGate(dataDir, gateId, gateMeta, layout.Chapters, rewards, unlocks);

            // Cover card = boss deck's boss_card.
            if (bossDeck != null && bossDeck.BossCard > 0)
            {
                BakedFilePatcher.SetGateCoverCard(dataDir, gateId, bossDeck.BossCard);
            }

            // IDS header.
            summary.IdsLines.Insert(0, "[IDS_SOLO.GATE" + gateId.ToString("D3") + "]");
            summary.IdsLines.Insert(1, gateName);
            summary.IdsLines.Insert(2, "[IDS_SOLO.GATE" + gateId.ToString("D3") + "_EXPLANATION]");
            summary.IdsLines.Insert(3, gateBlurb);
            summary.IdsLines.Insert(4, "");

            summary.ChaptersCount = layout.Chapters.Count;
            summary.BossChapterId = layout.BossChapterId;
            summary.EntryHash = EntryHasher.Compute(entry);
            return summary;
        }

        // Bake múltiplas gates e ao final patcha IDS_SOLO uma única vez
        // com TODOS os blocos juntos (preserva o resto do arquivo).
        // Também salva `last_bake_hash` em cada entry (commit no
        // GridGates.json) pra que o auto-bake do próximo boot saiba
        // o que já foi feito.
        public static List<Summary> BakeMany(string dataDir,
            List<Dictionary<string, object>> entries)
        {
            List<Summary> all = new List<Summary>();
            List<string> idsCombined = new List<string>();
            foreach (Dictionary<string, object> entry in entries)
            {
                Summary s;
                try { s = Bake(dataDir, entry); }
                catch (Exception ex)
                {
                    Console.WriteLine("[GridGateBaker] gate " + Utils.GetValue<int>(entry, "gate_id")
                        + " FAILED: " + ex.Message);
                    continue;
                }
                all.Add(s);
                entry["last_bake_hash"] = s.EntryHash;
                idsCombined.AddRange(s.IdsLines);
                idsCombined.Add("");   // separador
            }
            if (all.Count > 0) PersistHashes(dataDir, all);
            // Reconstrói o bloco IDS_SOLO inteiro do zero a partir do
            // estado em disco (baked = Solo.json + GridGates entry,
            // runtime = GridGates entry). Idempotente — sempre produz o
            // mesmo bloco pro mesmo estado, sem importar quem chamou.
            SyncIdsBlock(dataDir);
            return all;
        }

        // Pre-aloca esse número de chapter labels ("Stage N") por gate
        // runtime — mesmo que o Python builder reservava. Cobre o range
        // do gate (gateId * 10000 + 1..99).
        const int RuntimeIdsRange = 99;

        // Reconstrói o bloco IDS_SOLO inteiro lendo o estado atual de
        // disco. Usa GridGates.json pra nomes/blurbs/flag runtime, e
        // Solo.json pra labels dos chapters baked (lendo `goat.type/
        // level/deck_file` se presente). Idempotente. Chame depois de
        // qualquer mutação que afete nome de gate ou bake.
        public static void SyncIdsBlock(string dataDir)
        {
            string gridPath = Path.Combine(dataDir, "GridGates.json");
            if (!File.Exists(gridPath)) return;
            List<Dictionary<string, object>> entries = ReadAllEntries(gridPath);
            Dictionary<int, Dictionary<string, object>> soloChapters = ReadSoloChapters(dataDir);

            List<string> lines = new List<string>();
            foreach (Dictionary<string, object> entry in entries)
            {
                int gateId = Utils.GetValue<int>(entry, "gate_id");
                if (gateId == 0) continue;
                bool runtime = Utils.GetValue<bool>(entry, "runtime");
                string name = Utils.GetValue<string>(entry, "name") ?? ("Gate " + gateId);
                string blurb = Utils.GetValue<string>(entry, "blurb")
                    ?? (runtime ? "Procedurally generated." : "Generated gate.");

                lines.Add("[IDS_SOLO.GATE" + gateId.ToString("D3") + "]");
                lines.Add(name);
                lines.Add("[IDS_SOLO.GATE" + gateId.ToString("D3") + "_EXPLANATION]");
                lines.Add(blurb);
                lines.Add("");

                if (runtime)
                {
                    int baseCid = gateId * 10000 + 1;
                    for (int i = 0; i < RuntimeIdsRange; i++)
                    {
                        int cid = baseCid + i;
                        lines.Add("[IDS_SOLO.CHAPTER" + cid + "_EXPLANATION]");
                        lines.Add("Stage " + (i + 1));
                        lines.Add("");
                        lines.Add("A procedurally generated duel.");
                        lines.Add("");
                        lines.Add("VS Random Deck");
                        lines.Add("");
                    }
                }
                else
                {
                    // Baked: pega labels dos chapters em Solo.json (1 por
                    // chapter, ordenado por cid).
                    Dictionary<string, object> chMap;
                    if (!soloChapters.TryGetValue(gateId, out _))
                        continue;
                    soloChapters.TryGetValue(gateId, out chMap);
                    if (chMap == null) continue;
                    List<int> cids = new List<int>();
                    foreach (string k in chMap.Keys)
                    {
                        int cid; if (int.TryParse(k, out cid)) cids.Add(cid);
                    }
                    cids.Sort();
                    foreach (int cid in cids)
                    {
                        Dictionary<string, object> ch =
                            chMap[cid.ToString()] as Dictionary<string, object>;
                        if (ch == null) continue;
                        Dictionary<string, object> goat =
                            Utils.GetValue<Dictionary<string, object>>(ch, "goat");
                        string label;
                        if (goat != null)
                        {
                            string type = Utils.GetValue<string>(goat, "type") ?? "duel";
                            int level = Utils.GetValue<int>(goat, "level");
                            string deck = Utils.GetValue<string>(goat, "deck_file");
                            string suffix = !string.IsNullOrEmpty(deck)
                                ? deck.Replace(".json", "")
                                : type.ToUpper();
                            label = "L" + level + " " + type + ": " + suffix;
                        }
                        else
                        {
                            label = "Chapter " + cid;
                        }
                        lines.Add("[IDS_SOLO.CHAPTER" + cid + "_EXPLANATION]");
                        lines.Add(label);
                    }
                    lines.Add("");
                }
            }
            BakedFilePatcher.PatchIdsSoloBlock(dataDir, lines);
        }

        static Dictionary<int, Dictionary<string, object>> ReadSoloChapters(string dataDir)
        {
            Dictionary<int, Dictionary<string, object>> result =
                new Dictionary<int, Dictionary<string, object>>();
            string path = SoloJsonPatcher.SoloJsonPath(dataDir);
            if (!File.Exists(path)) return result;
            Dictionary<string, object> root = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            Dictionary<string, object> solo =
                Utils.GetValue<Dictionary<string, object>>(
                    Utils.GetValue<Dictionary<string, object>>(root, "Master") ?? new Dictionary<string, object>(),
                    "Solo");
            Dictionary<string, object> chMap =
                solo != null ? Utils.GetValue<Dictionary<string, object>>(solo, "chapter") : null;
            if (chMap == null) return result;
            foreach (KeyValuePair<string, object> kv in chMap)
            {
                int gid;
                if (!int.TryParse(kv.Key, out gid)) continue;
                Dictionary<string, object> inner = kv.Value as Dictionary<string, object>;
                if (inner != null) result[gid] = inner;
            }
            return result;
        }

        // Bake AUTOMÁTICO: pega todas entries não-runtime do GridGates.json
        // e bakeia as que (a) não estão em Solo.json ainda, ou (b) têm
        // hash != entry.last_bake_hash (= o user editou desde último bake).
        //
        // Pensado pra rodar no boot do server: self-healing — qualquer
        // edição manual no JSON propaga sem precisar de comando explícito.
        public static List<Summary> BakeMissing(string dataDir)
        {
            string gridPath = Path.Combine(dataDir, "GridGates.json");
            if (!File.Exists(gridPath)) return new List<Summary>();
            List<Dictionary<string, object>> all = ReadAllEntries(gridPath);
            HashSet<int> gatesInSolo = ReadSoloGateIds(dataDir);

            List<Dictionary<string, object>> needBake = new List<Dictionary<string, object>>();
            int upToDate = 0;
            foreach (Dictionary<string, object> entry in all)
            {
                if (Utils.GetValue<bool>(entry, "runtime")) continue;
                int gateId = Utils.GetValue<int>(entry, "gate_id");
                if (gateId == 0) continue;
                string currentHash = EntryHasher.Compute(entry);
                string savedHash = Utils.GetValue<string>(entry, "last_bake_hash");
                bool inSolo = gatesInSolo.Contains(gateId);
                if (inSolo && string.Equals(currentHash, savedHash, StringComparison.Ordinal))
                {
                    upToDate++;
                    continue;
                }
                Console.WriteLine("[GridGateBaker] gate " + gateId + " → "
                    + (!inSolo ? "missing from Solo.json"
                       : "entry changed (hash " + (savedHash ?? "(none)").Substring(0, Math.Min(8, (savedHash ?? "").Length))
                         + " → " + currentHash.Substring(0, 8) + ")"));
                needBake.Add(entry);
            }
            if (upToDate > 0)
                Console.WriteLine("[GridGateBaker] " + upToDate + " gate(s) up-to-date, skipping.");
            if (needBake.Count == 0)
            {
                // Nada pra bakear, mas sincroniza IDS_SOLO pra cobrir
                // edições em runtime gates (que não disparam bake).
                SyncIdsBlock(dataDir);
                return new List<Summary>();
            }
            return BakeMany(dataDir, needBake);
        }

        // Lê entries brutas do GridGates.json (sem o `gates` wrapper).
        // Usado tanto pelo CLI quanto pelo auto-bake.
        public static List<Dictionary<string, object>> ReadAllEntries(string gridPath)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(gridPath)) as Dictionary<string, object>;
            if (doc == null) return result;
            List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
            if (gates == null) return result;
            foreach (object g in gates)
            {
                Dictionary<string, object> e = g as Dictionary<string, object>;
                if (e != null) result.Add(e);
            }
            return result;
        }

        // Re-lê GridGates.json do disco, mescla os hashes recém-bakeados
        // (por gate_id), reescreve. Funciona mesmo se as entries que
        // foram bakeadas não forem as instances dentro do doc do disco
        // (CLI lê uma cópia, Bake muta a cópia — aqui sincronizamos).
        static void PersistHashes(string dataDir, List<Summary> summaries)
        {
            string gridPath = Path.Combine(dataDir, "GridGates.json");
            if (!File.Exists(gridPath)) return;
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(gridPath)) as Dictionary<string, object>;
            if (doc == null) return;
            List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
            if (gates == null) return;
            Dictionary<int, string> hashByGate = new Dictionary<int, string>();
            foreach (Summary s in summaries)
            {
                if (!string.IsNullOrEmpty(s.EntryHash)) hashByGate[s.GateId] = s.EntryHash;
            }
            bool dirty = false;
            foreach (object g in gates)
            {
                Dictionary<string, object> e = g as Dictionary<string, object>;
                if (e == null) continue;
                int gid = Utils.GetValue<int>(e, "gate_id");
                string newHash;
                if (hashByGate.TryGetValue(gid, out newHash))
                {
                    string oldHash = Utils.GetValue<string>(e, "last_bake_hash");
                    if (oldHash != newHash) { e["last_bake_hash"] = newHash; dirty = true; }
                }
            }
            if (dirty)
            {
                File.WriteAllText(gridPath, MiniJSON.Json.Serialize(doc));
            }
        }

        // Lê os gate ids presentes em Solo.json (pra decidir se uma
        // entry precisa de bake).
        static HashSet<int> ReadSoloGateIds(string dataDir)
        {
            HashSet<int> result = new HashSet<int>();
            string path = SoloJsonPatcher.SoloJsonPath(dataDir);
            if (!File.Exists(path)) return result;
            Dictionary<string, object> root = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) return result;
            Dictionary<string, object> master = Utils.GetValue<Dictionary<string, object>>(root, "Master");
            if (master == null) return result;
            Dictionary<string, object> solo = Utils.GetValue<Dictionary<string, object>>(master, "Solo");
            if (solo == null) return result;
            Dictionary<string, object> gateMap = Utils.GetValue<Dictionary<string, object>>(solo, "gate");
            if (gateMap == null) return result;
            foreach (string k in gateMap.Keys)
            {
                int v;
                if (int.TryParse(k, out v)) result.Add(v);
            }
            return result;
        }

        // ---------- helpers ----------
        static long GetSeed(Dictionary<string, object> entry, int gateId,
                            string duelType, string format, Dictionary<string, object> gp)
        {
            object seedObj;
            if (gp != null && gp.TryGetValue("seed", out seedObj) && seedObj != null)
            {
                try
                {
                    long n = Convert.ToInt64(seedObj);
                    if (n != 0) return n;
                }
                catch { }
            }
            // hash determinístico (mesmo padrão do Python).
            return ((long)(gateId.GetHashCode()
                ^ (duelType ?? "").GetHashCode()
                ^ (format ?? "").GetHashCode())) & 0xFFFFFFFFL;
        }

        static LayoutGenerator.Result BuildLayout(Dictionary<string, object> entry,
            Dictionary<string, object> gp, string format, Random rng)
        {
            GenerationContext ctx = new GenerationContext
            {
                GateId         = Utils.GetValue<int>(entry, "gate_id"),
                Format         = format,
                Rng            = rng,
                FormatParams   = Utils.GetValue<Dictionary<string, object>>(entry, "format_params"),
                ManualCells    = Utils.GetValue<List<object>>(entry, "manual_cells"),
                ManualBossPos  = Utils.GetValue<string>(entry, "manual_boss_pos"),
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

        static Dictionary<string, Dictionary<string, object>> ParseModifierDefaults(
            Dictionary<string, object> gp)
        {
            Dictionary<string, Dictionary<string, object>> result =
                new Dictionary<string, Dictionary<string, object>>();
            Dictionary<string, object> raw =
                Utils.GetValue<Dictionary<string, object>>(gp, "modifier_defaults");
            if (raw == null) return result;
            foreach (KeyValuePair<string, object> kv in raw)
            {
                Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                if (v != null) result[kv.Key] = v;
            }
            return result;
        }

        static SoloDuelBuilder.CosmeticMode ParseCosmeticMode(Dictionary<string, object> entry)
        {
            string mode = Utils.GetValue<string>(entry, "cosmetic_mode");
            if (string.Equals(mode, "random", StringComparison.OrdinalIgnoreCase))
                return SoloDuelBuilder.CosmeticMode.Random;
            return SoloDuelBuilder.CosmeticMode.Vanilla;
        }

        // Gems base por level, com bonus por chapter type (mirror do Python).
        static int GemsFor(string chapterType, int level)
        {
            int gems = SoloRewards.GemsForLevel(level);
            switch (chapterType)
            {
                case "elite":    return gems + 500;
                case "boss":     return Math.Max(gems, 5000);
                case "treasure": return gems * 3;
                case "reward":   return gems * 2;
                case "lock":     return gems * 4;
                default:         return gems;
            }
        }

        // Vasculha os deck pools pra achar a BossCard de um deck pelo nome.
        // Acontece raramente (1x por boss + 1x por elite) — busca linear OK.
        static int GetBossCardOf(Dictionary<int, List<DeckPoolLoader.LoadedDeck>> pools,
                                 int level, string deckFile)
        {
            if (string.IsNullOrEmpty(deckFile)) return 0;
            string name = deckFile.Replace(".json", "");
            // Tenta primeiro no level alvo.
            List<DeckPoolLoader.LoadedDeck> target;
            if (pools.TryGetValue(level, out target))
            {
                foreach (DeckPoolLoader.LoadedDeck d in target)
                {
                    if (d.Name == name || d.Name == deckFile) return d.BossCard;
                }
            }
            // Fallback: varre todos.
            foreach (List<DeckPoolLoader.LoadedDeck> pool in pools.Values)
            {
                foreach (DeckPoolLoader.LoadedDeck d in pool)
                {
                    if (d.Name == name || d.Name == deckFile) return d.BossCard;
                }
            }
            return 0;
        }

        static string GetMetaString(LayoutGenerator.Result layout, int chapterId,
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

        static int GetMetaInt(LayoutGenerator.Result layout, int chapterId,
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

        static int GetIntOr(Dictionary<string, object> d, string key, int fallback)
        {
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }
    }
}
