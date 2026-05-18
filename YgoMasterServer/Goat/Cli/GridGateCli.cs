using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YgoMaster.Builder;
using YgoMaster.Cosmetics;
using YgoMaster.Layout;
using YgoMaster.Modifiers;
using YgoMaster.Rewards;

namespace YgoMaster.Cli
{
    // CLI plugado no Main: `YgoMaster.exe --grid-gate <command> [args]`.
    //
    // Mesma intenção do `scripts/build_grid_gate_procedural.py` mas
    // rodando no .exe — caminho pra eventualmente aposentar o builder
    // Python. Hoje (fase 1) só implementa comandos read-only que
    // exercitam o LayoutGenerator que já existe:
    //
    //   list                  — lista todas as entries da GridGates.json
    //   preview <gateId>      — gera o layout em memória e imprime ASCII
    //
    // Comandos write (`add` / `edit` / `delete` / `gen` / boot auto-gen)
    // dependem de portar `_modifiers` + `_solo_helpers` + a pipeline
    // `emit_gate` — virão em fases seguintes (ver RUNTIME_GATE_BACKLOG.md).
    static class GridGateCli
    {
        // Returns the exit code that Main should propagate. -1 = help shown
        // or unknown subcommand; 0 = ok; >0 = error.
        public static int Run(string[] subargs)
        {
            if (subargs == null || subargs.Length == 0)
            {
                PrintUsage();
                return -1;
            }
            string dataDir = Utils.GetDataDirectory(true);
            string gridPath = Path.Combine(dataDir, "GridGates.json");
            if (!File.Exists(gridPath))
            {
                Console.WriteLine("GridGates.json not found at: " + gridPath);
                return 2;
            }

            switch (subargs[0].ToLowerInvariant())
            {
                case "list":            return CmdList(gridPath);
                case "preview":         return CmdPreview(gridPath, subargs);
                case "test-modifiers":  return CmdTestModifiers(gridPath, subargs);
                case "test-cosmetics":  return CmdTestCosmetics(dataDir, subargs);
                case "test-rewards":    return CmdTestRewards(dataDir, gridPath, subargs);
                case "gen":             return CmdGen(dataDir, gridPath, subargs);
                case "bake-missing":    return CmdBakeMissing(dataDir);
                case "delete":          return CmdDelete(dataDir, gridPath, subargs);
                case "export-manual":   return CmdExportManual(gridPath, subargs);
                case "sync-ids":        return CmdSyncIds(dataDir);
                default:
                    Console.WriteLine("Unknown subcommand: " + subargs[0]);
                    PrintUsage();
                    return -1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: YgoMaster.exe --grid-gate <command> [args]");
            Console.WriteLine();
            Console.WriteLine("Read-only commands:");
            Console.WriteLine("  list                       List every gate in GridGates.json");
            Console.WriteLine("  preview <gateId>           Generate the layout in-memory and print ASCII");
            Console.WriteLine("  test-modifiers <gateId>    Roda ModifierApplier (C#) e compara com");
            Console.WriteLine("                             runtime_templates pre-baked pelo Python.");
            Console.WriteLine("  test-cosmetics [n]         Sorteia N cosmetic sets via CosmeticsPicker");
            Console.WriteLine("                             (default n=5) — útil pra ver a variedade.");
            Console.WriteLine("  test-rewards <gateId> [n]  Rola item drops da gate por chapter type");
            Console.WriteLine("                             (default n=20) usando RewardConfig.");
            Console.WriteLine();
            Console.WriteLine("Write commands:");
            Console.WriteLine("  gen <gateId>               Bake uma gate (Solo.json + SoloDuels +");
            Console.WriteLine("                             IDS + cover card). Skip gates `runtime: true`.");
            Console.WriteLine("  gen --all                  Bake todas as gates non-runtime do registry.");
            Console.WriteLine("  bake-missing               Bake só as gates cujo hash mudou ou que");
            Console.WriteLine("                             sumiram de Solo.json (= o que o server roda no boot).");
            Console.WriteLine("  delete <gateId>            Remove a gate de GridGates.json + Solo.json");
            Console.WriteLine("                             + SoloDuels/. SoloGateCards.txt e IDS_SOLO");
            Console.WriteLine("                             ficam (rebuild no próximo bake-missing).");
            Console.WriteLine("  export-manual <gateId>     Snapshot do layout procedural pra `manual_cells`");
            Console.WriteLine("                             + flag manual=true. Ponto de partida pro editor visual.");
            Console.WriteLine("  sync-ids                   Reconstrói o bloco IDS_SOLO.txt (gates names +");
            Console.WriteLine("                             chapter labels). Use após editar nome de runtime gate.");
            Console.WriteLine();
            Console.WriteLine("CRUD (add/edit/delete) — vem na próxima fase.");
        }

        // ---------- list ----------
        static int CmdList(string gridPath)
        {
            List<Dictionary<string, object>> gates = ReadGates(gridPath);
            Console.WriteLine("Gates in GridGates.json:");
            Console.WriteLine();
            Console.WriteLine("  {0,5}  {1,-8}  {2,-10}  {3,-7}  {4}", "ID", "TYPE", "FORMAT", "MODE", "NAME");
            Console.WriteLine("  -----  --------  ----------  -------  ----");
            foreach (Dictionary<string, object> e in gates)
            {
                int gateId    = Utils.GetValue<int>(e, "gate_id");
                string duel   = Utils.GetValue<string>(e, "duel_type")  ?? "?";
                string format = Utils.GetValue<string>(e, "format")     ?? "?";
                bool runtime  = Utils.GetValue<bool>(e, "runtime");
                bool manual   = Utils.GetValue<bool>(e, "manual");
                string mode   = runtime ? "runtime" : (manual ? "manual" : "baked");
                string name   = Utils.GetValue<string>(e, "name") ?? "";
                Console.WriteLine("  {0,5}  {1,-8}  {2,-10}  {3,-7}  {4}",
                    gateId, duel, format, mode, name);
            }
            Console.WriteLine();
            Console.WriteLine(gates.Count + " gate(s) total.");
            return 0;
        }

        // ---------- preview ----------
        static int CmdPreview(string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("preview requires <gateId>");
                return 1;
            }
            int gateId;
            if (!int.TryParse(subargs[1], out gateId))
            {
                Console.WriteLine("preview: '" + subargs[1] + "' is not a valid gate id");
                return 1;
            }
            Dictionary<string, object> entry = FindGate(ReadGates(gridPath), gateId);
            if (entry == null)
            {
                Console.WriteLine("Gate " + gateId + " not in GridGates.json");
                return 2;
            }

            // Use a fresh seed so repeated previews show variance —
            // matches the runtime path's per-call behavior.
            int seed = Environment.TickCount;
            Random rng = new Random(seed);
            LayoutGenerator.Result layout = BuildLayout(entry, rng);
            if (layout == null)
            {
                Console.WriteLine("Format '" + Utils.GetValue<string>(entry, "format")
                    + "' is not supported by the C# generators (yet).");
                return 3;
            }

            string name = Utils.GetValue<string>(entry, "name") ?? ("Gate " + gateId);
            string format = Utils.GetValue<string>(entry, "format") ?? "?";
            Console.WriteLine("Preview of gate " + gateId + " (" + format + "): " + name);
            Console.WriteLine("Seed: " + seed + "  Chapters: " + layout.Chapters.Count
                              + "  Boss: " + layout.BossChapterId);
            Console.WriteLine();
            Console.WriteLine(RenderAscii(layout, entry));
            return 0;
        }

        // ---------- sync-ids ----------
        // Regenera o bloco IDS_SOLO.txt completo. Útil quando o user
        // edita nome/blurb de uma RUNTIME gate (que não dispara bake) e
        // precisa propagar pra IDS. Bake já chama isso por baixo dos
        // panos, então comando dedicado é só pro caso runtime-only.
        static int CmdSyncIds(string dataDir)
        {
            GridGateBaker.SyncIdsBlock(dataDir);
            Console.WriteLine("IDS_SOLO block reconstruído.");
            return 0;
        }

        // ---------- export-manual ----------
        // Roda o LayoutGenerator com os params da entry, converte as
        // nodes pra schema `manual_cells`, salva como `manual: true` no
        // GridGates.json. NÃO bakeia — o bake fica pro caller (a GUI
        // chama `gen` depois pra refletir o snapshot no Solo.json).
        //
        // Formato da entry após export:
        //   manual: true
        //   manual_cells: [ {grid_x, grid_y, parent_pos, type, level,
        //                    deck_file, gems_override, card_rewards,
        //                    force_special, modifiers}, ... ]
        //   manual_boss_pos: "x,y"
        // Format string original fica intacta — o user pode `clear-manual`
        // depois pra reverter pro procedural (futuro).
        static int CmdExportManual(string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("export-manual requer <gateId>");
                return 1;
            }
            int gateId;
            if (!int.TryParse(subargs[1], out gateId))
            {
                Console.WriteLine("export-manual: '" + subargs[1] + "' não é gate id válido");
                return 1;
            }

            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(gridPath)) as Dictionary<string, object>;
            List<object> gates = doc != null ? Utils.GetValue<List<object>>(doc, "gates") : null;
            if (gates == null)
            {
                Console.WriteLine("GridGates.json malformed");
                return 2;
            }
            Dictionary<string, object> entry = null;
            foreach (object o in gates)
            {
                Dictionary<string, object> e = o as Dictionary<string, object>;
                if (e != null && Utils.GetValue<int>(e, "gate_id") == gateId) { entry = e; break; }
            }
            if (entry == null)
            {
                Console.WriteLine("Gate " + gateId + " not in GridGates.json");
                return 2;
            }
            if (Utils.GetValue<bool>(entry, "manual"))
            {
                Console.WriteLine("Gate " + gateId + " já tem snapshot manual.");
                return 0;
            }

            // Mesmo seed-derivation que o GridGateBaker usa pra que o
            // snapshot bata com o que `gen` produziria.
            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(entry, "generic_params") ?? new Dictionary<string, object>();
            string duelType = Utils.GetValue<string>(entry, "duel_type") ?? "Normal";
            string format = Utils.GetValue<string>(entry, "format") ?? "linear";
            long seed = 0;
            object seedObj;
            if (gp.TryGetValue("seed", out seedObj) && seedObj != null)
            {
                try { seed = Convert.ToInt64(seedObj); } catch { }
            }
            if (seed == 0)
            {
                seed = ((long)(gateId.GetHashCode() ^ duelType.GetHashCode() ^ format.GetHashCode())) & 0xFFFFFFFFL;
            }
            Random rng = new Random((int)(seed & 0x7FFFFFFF));

            GenerationContext ctx = new GenerationContext
            {
                GateId         = gateId,
                Format         = format,
                Rng            = rng,
                FormatParams   = Utils.GetValue<Dictionary<string, object>>(entry, "format_params"),
                EliteCount     = GetIntOr(gp, "elite_count", 2),
                LockCount      = GetIntOr(gp, "lock_count", 0),
                RewardCount    = GetIntOr(gp, "reward_count", 3),
                TreasureCount  = GetIntOr(gp, "treasure_count", 2),
                DuelLevel      = GetIntOr(gp, "duel_level", 3),
                EliteLevel     = GetIntOr(gp, "elite_level", 2),
                BossLevel      = GetIntOr(gp, "boss_level", 1),
                DifficultyMode = Utils.GetValue<string>(gp, "difficulty_curve") ?? "default",
            };
            LayoutGenerator.NodesResult nr = LayoutGenerator.GenerateNodes(ctx);
            if (nr == null)
            {
                Console.WriteLine("Format '" + format + "' não suportado");
                return 3;
            }

            // Converte cada node pra dict no shape `manual_cells`.
            List<object> cells = new List<object>();
            foreach (LayoutNode n in nr.Nodes)
            {
                Dictionary<string, object> cell = new Dictionary<string, object>
                {
                    { "grid_x",        n.X },
                    { "grid_y",        n.Y },
                    { "parent_pos",    n.Parent != null ? (n.Parent.X + "," + n.Parent.Y) : null },
                    { "type",          n.ChapterType },
                    { "level",         n.Level },
                    { "deck_file",     null },   // deck selection é runtime/bake-time
                    { "gems_override", null },
                    { "card_rewards",  new List<object>() },
                    { "force_special", null },
                    { "modifiers",     null },
                };
                cells.Add(cell);
            }
            entry["manual"] = true;
            entry["manual_cells"] = cells;
            entry["manual_boss_pos"] = nr.Boss.X + "," + nr.Boss.Y;
            // Hash atual fica obsoleto (entry mudou) — auto-bake detecta.
            entry.Remove("last_bake_hash");

            File.WriteAllText(gridPath, MiniJSON.Json.Serialize(doc));
            Console.WriteLine("Gate " + gateId + ": snapshotted " + cells.Count + " cells "
                + "(format '" + format + "' preservado, manual=true).");
            return 0;
        }

        // ---------- delete ----------
        // Remove a gate completamente: tira da registry (GridGates.json),
        // limpa o gate inteiro de Solo.json (meta + chapters + rewards +
        // unlocks no range), apaga SoloDuels do range. IDS_SOLO bloco
        // não é mexido aqui — sai naturalmente no próximo bake-missing
        // (que regenera o bloco do zero com as gates restantes).
        static int CmdDelete(string dataDir, string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("delete requer <gateId>");
                return 1;
            }
            int gateId;
            if (!int.TryParse(subargs[1], out gateId))
            {
                Console.WriteLine("delete: '" + subargs[1] + "' não é gate id válido");
                return 1;
            }

            // 1) Remove a entry de GridGates.json.
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(gridPath)) as Dictionary<string, object>;
            bool registryHit = false;
            if (doc != null)
            {
                List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
                if (gates != null)
                {
                    for (int i = gates.Count - 1; i >= 0; i--)
                    {
                        Dictionary<string, object> e = gates[i] as Dictionary<string, object>;
                        if (e != null && Utils.GetValue<int>(e, "gate_id") == gateId)
                        {
                            gates.RemoveAt(i);
                            registryHit = true;
                        }
                    }
                    if (registryHit) File.WriteAllText(gridPath, MiniJSON.Json.Serialize(doc));
                }
            }
            // 2) Limpa de Solo.json + SoloDuels (mesma rotina usada por upserts).
            SoloJsonPatcher.DeleteGate(dataDir, gateId);
            Console.WriteLine("gate " + gateId + " deleted: "
                + "registry=" + (registryHit ? "removed" : "(not present)")
                + ", Solo.json scrubbed, SoloDuels purged.");
            return 0;
        }

        // ---------- bake-missing ----------
        // Mesma pipeline que o server roda no boot. Útil pra debug —
        // confirma quais gates seriam re-bakeadas sem subir o server.
        static int CmdBakeMissing(string dataDir)
        {
            ItemID.Load(dataDir);
            List<GridGateBaker.Summary> results = GridGateBaker.BakeMissing(dataDir);
            Console.WriteLine();
            if (results.Count == 0)
            {
                Console.WriteLine("Nothing to bake.");
            }
            else
            {
                Console.WriteLine("Baked " + results.Count + " gate(s):");
                foreach (GridGateBaker.Summary s in results)
                {
                    Console.WriteLine("  gate " + s.GateId + " (" + s.Format + "): "
                        + s.ChaptersCount + " chapters, " + s.DuelFilesWritten + " SoloDuels, "
                        + "boss=" + s.BossChapterId + ", seed=" + s.Seed);
                }
            }
            return 0;
        }

        // ---------- gen ----------
        // Bake uma gate (ou todas) usando GridGateBaker. Skips entries
        // com `runtime: true` (essas são geradas runtime pelo server,
        // não fazem sentido em bake estático).
        static int CmdGen(string dataDir, string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("gen requer <gateId> ou --all");
                return 1;
            }
            // ItemID precisa estar carregado pra cosmetic random + reward picker.
            ItemID.Load(dataDir);

            List<Dictionary<string, object>> entries = ReadGates(gridPath);
            List<Dictionary<string, object>> targets = new List<Dictionary<string, object>>();

            if (subargs[1] == "--all")
            {
                foreach (Dictionary<string, object> e in entries)
                {
                    if (Utils.GetValue<bool>(e, "runtime")) continue;
                    targets.Add(e);
                }
                if (targets.Count == 0)
                {
                    Console.WriteLine("Nenhuma gate não-runtime no registry. Nothing to bake.");
                    return 0;
                }
            }
            else
            {
                int gateId;
                if (!int.TryParse(subargs[1], out gateId))
                {
                    Console.WriteLine("gen: '" + subargs[1] + "' não é gate id válido");
                    return 1;
                }
                Dictionary<string, object> entry = FindGate(entries, gateId);
                if (entry == null)
                {
                    Console.WriteLine("Gate " + gateId + " not in GridGates.json");
                    return 2;
                }
                if (Utils.GetValue<bool>(entry, "runtime"))
                {
                    Console.WriteLine("Gate " + gateId + " é runtime — não precisa de bake.");
                    return 0;
                }
                targets.Add(entry);
            }

            List<GridGateBaker.Summary> results = GridGateBaker.BakeMany(dataDir, targets);
            Console.WriteLine();
            Console.WriteLine("Baked " + results.Count + " gate(s):");
            foreach (GridGateBaker.Summary s in results)
            {
                Console.WriteLine("  gate " + s.GateId + " (" + s.Format + "): "
                    + s.ChaptersCount + " chapters, " + s.DuelFilesWritten + " SoloDuels written, "
                    + "boss=" + s.BossChapterId + " (" + (s.BossDeck ?? "?") + "), seed=" + s.Seed);
            }
            return 0;
        }

        // ---------- test-rewards ----------
        // Lê o bloco `rewards` da gate, rola N drops por chapter type e
        // imprime distribuição. Útil pra calibrar drop_chance + weights.
        static int CmdTestRewards(string dataDir, string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("test-rewards requires <gateId>");
                return 1;
            }
            int gateId;
            if (!int.TryParse(subargs[1], out gateId))
            {
                Console.WriteLine("test-rewards: '" + subargs[1] + "' is not a valid gate id");
                return 1;
            }
            int n = 20;
            if (subargs.Length >= 3 && int.TryParse(subargs[2], out int parsed) && parsed > 0) n = parsed;

            Dictionary<string, object> entry = FindGate(ReadGates(gridPath), gateId);
            if (entry == null)
            {
                Console.WriteLine("Gate " + gateId + " not in GridGates.json");
                return 2;
            }
            ItemID.Load(dataDir);
            RewardConfig cfg = RewardConfig.Parse(entry);
            Console.WriteLine("Gate " + gateId + " reward config:");
            Console.WriteLine("  boss   chance = " + cfg.BossDropChance);
            Console.WriteLine("  elite  chance = " + cfg.EliteDropChance);
            Console.WriteLine("  reward chance = " + cfg.RewardDropChance);
            Console.WriteLine("  duel   chance = " + cfg.DuelDropChance);
            Console.WriteLine("  category_weights = " + (cfg.CategoryWeights.Count == 0
                ? "(empty = all categories peso 1)"
                : MiniJSON.Json.Serialize(cfg.CategoryWeights)));
            Console.WriteLine();

            if (!cfg.AnyDrop)
            {
                Console.WriteLine("Nenhum drop chance > 0 — gate não dropa items.");
                return 0;
            }

            Random rng = new Random();
            string[] types = { "boss", "elite", "reward", "duel" };
            foreach (string t in types)
            {
                double chance = cfg.DropChanceFor(t);
                Console.WriteLine("--- " + t + " (chance " + chance + ") ---");
                int hits = 0;
                Dictionary<int, int> byCategory = new Dictionary<int, int>();
                for (int i = 0; i < n; i++)
                {
                    Tuple<int, int> drop = RewardPicker.Roll(rng, chance, cfg);
                    if (drop == null) continue;
                    hits++;
                    if (!byCategory.ContainsKey(drop.Item1)) byCategory[drop.Item1] = 0;
                    byCategory[drop.Item1]++;
                }
                Console.WriteLine("  drops: " + hits + "/" + n);
                foreach (KeyValuePair<int, int> kv in byCategory)
                {
                    Console.WriteLine("    cat " + kv.Key + " → " + kv.Value);
                }
                Console.WriteLine();
            }
            return 0;
        }

        // ---------- test-cosmetics ----------
        // Sorteia N sets via CosmeticsPicker.PickSet e imprime. Carrega
        // ItemID.json sob demanda (no boot do server isso roda dentro de
        // GameServer.State.Init — aqui ainda não rodou).
        static int CmdTestCosmetics(string dataDir, string[] subargs)
        {
            int n = 5;
            if (subargs.Length >= 2 && int.TryParse(subargs[1], out int parsed) && parsed > 0)
                n = parsed;

            ItemID.Load(dataDir);
            int catCount = 0;
            int totalItems = 0;
            foreach (KeyValuePair<ItemID.Category, int[]> kv in ItemID.Values)
            {
                if (kv.Value.Length > 0)
                {
                    catCount++;
                    totalItems += kv.Value.Length;
                }
            }
            Console.WriteLine("ItemID.json loaded — " + catCount + " populated categories ("
                + totalItems + " total items).");
            Console.WriteLine();

            Random rng = new Random();
            for (int i = 0; i < n; i++)
            {
                Console.WriteLine("--- set " + (i + 1) + " ---");
                Dictionary<string, object> picked = CosmeticsPicker.PickSet(rng);
                foreach (KeyValuePair<string, object> kv in picked)
                {
                    Console.WriteLine("  {0,-12} = {1}", kv.Key, MiniJSON.Json.Serialize(kv.Value));
                }
                Console.WriteLine();
            }
            return 0;
        }

        // ---------- test-modifiers ----------
        // Roda ModifierApplier (C#) sobre os mesmos modifier_defaults
        // que o Python já compilou em runtime_templates, e compara
        // resultado-a-resultado. Diff = bug no porte (espera-se zero).
        static int CmdTestModifiers(string gridPath, string[] subargs)
        {
            if (subargs.Length < 2)
            {
                Console.WriteLine("test-modifiers requires <gateId>");
                return 1;
            }
            int gateId;
            if (!int.TryParse(subargs[1], out gateId))
            {
                Console.WriteLine("test-modifiers: '" + subargs[1] + "' is not a valid gate id");
                return 1;
            }
            Dictionary<string, object> entry = FindGate(ReadGates(gridPath), gateId);
            if (entry == null)
            {
                Console.WriteLine("Gate " + gateId + " not in GridGates.json");
                return 2;
            }

            string duelType = Utils.GetValue<string>(entry, "duel_type") ?? "Normal";
            Dictionary<string, object> gp = Utils.GetValue<Dictionary<string, object>>(entry, "generic_params");
            Dictionary<string, object> md = gp != null
                ? Utils.GetValue<Dictionary<string, object>>(gp, "modifier_defaults")
                : null;
            Dictionary<string, Dictionary<string, object>> pythonTemplates = ParseNestedDict(entry, "runtime_templates");
            if (md == null || md.Count == 0)
            {
                Console.WriteLine("Gate " + gateId + " has no modifier_defaults — nothing to test.");
                return 0;
            }

            Console.WriteLine("Comparing ModifierApplier (C#) vs runtime_templates (Python) for gate "
                + gateId + " (" + duelType + "):");
            Console.WriteLine();

            int diffs = 0;
            foreach (KeyValuePair<string, object> kv in md)
            {
                string chapterType = kv.Key;
                Dictionary<string, object> modifier = kv.Value as Dictionary<string, object>;
                if (modifier == null || modifier.Count == 0) continue;

                // Roda o porte C# isolado.
                Dictionary<string, object> cs = new Dictionary<string, object>();
                ModifierApplier.ApplyFullPipeline(cs, duelType, modifier);

                Dictionary<string, object> py = null;
                if (pythonTemplates != null)
                {
                    pythonTemplates.TryGetValue(chapterType, out py);
                }

                Console.WriteLine("  [" + chapterType + "]");
                if (py == null)
                {
                    Console.WriteLine("    Python output: (no runtime_templates entry — skipping diff)");
                    PrintTemplateSummary("C# ", cs);
                    continue;
                }
                bool same = CompareTemplates(py, cs);
                Console.WriteLine("    " + (same ? "✓ MATCH" : "✗ DIFFER"));
                PrintTemplateSummary("Python", py);
                PrintTemplateSummary("C#    ", cs);
                if (!same) diffs++;
                Console.WriteLine();
            }
            Console.WriteLine(diffs == 0
                ? "All chapter types match ✓"
                : diffs + " chapter type(s) differ ✗");
            return diffs == 0 ? 0 : 4;
        }

        // Compara cmds / random_specs / life / hnum via JSON re-serializado.
        // Não é byte-perfect (ordem de chaves pode variar) mas suficiente
        // pra detectar drift do encoder.
        static bool CompareTemplates(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            string[] fields = { "cmds", "random_specs", "life", "hnum" };
            foreach (string f in fields)
            {
                object av, bv;
                a.TryGetValue(f, out av);
                b.TryGetValue(f, out bv);
                string aj = av == null ? "null" : MiniJSON.Json.Serialize(av);
                string bj = bv == null ? "null" : MiniJSON.Json.Serialize(bv);
                if (aj != bj) return false;
            }
            return true;
        }

        static void PrintTemplateSummary(string tag, Dictionary<string, object> tpl)
        {
            int cmds = (tpl != null && tpl.TryGetValue("cmds", out object c) && c is List<object> cl) ? cl.Count : 0;
            int specs = (tpl != null && tpl.TryGetValue("random_specs", out object s) && s is Dictionary<string, object> sd) ? sd.Count : 0;
            string life = (tpl != null && tpl.TryGetValue("life", out object l)) ? MiniJSON.Json.Serialize(l) : "(none)";
            string hnum = (tpl != null && tpl.TryGetValue("hnum", out object h)) ? MiniJSON.Json.Serialize(h) : "(none)";
            Console.WriteLine("    " + tag + ": cmds=" + cmds + " specs=" + specs
                + " life=" + life + " hnum=" + hnum);
        }

        // Mirror of RuntimeGateConfig.ParseNestedDict for the CLI context
        // (avoids depending on that file's static state).
        static Dictionary<string, Dictionary<string, object>> ParseNestedDict(
            Dictionary<string, object> entry, string key)
        {
            Dictionary<string, object> raw = Utils.GetValue<Dictionary<string, object>>(entry, key);
            if (raw == null) return null;
            Dictionary<string, Dictionary<string, object>> result =
                new Dictionary<string, Dictionary<string, object>>();
            foreach (KeyValuePair<string, object> kv in raw)
            {
                Dictionary<string, object> v = kv.Value as Dictionary<string, object>;
                if (v != null) result[kv.Key] = v;
            }
            return result;
        }

        // ---------- helpers ----------
        static List<Dictionary<string, object>> ReadGates(string gridPath)
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

        static Dictionary<string, object> FindGate(List<Dictionary<string, object>> gates, int gateId)
        {
            foreach (Dictionary<string, object> e in gates)
            {
                if (Utils.GetValue<int>(e, "gate_id") == gateId) return e;
            }
            return null;
        }

        static LayoutGenerator.Result BuildLayout(Dictionary<string, object> entry, Random rng)
        {
            string format = Utils.GetValue<bool>(entry, "manual")
                ? "manual"
                : (Utils.GetValue<string>(entry, "format") ?? "");
            if (format != "hourglass" && format != "dungeon"
                && format != "tower" && format != "manual") return null;

            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(entry, "generic_params")
                ?? new Dictionary<string, object>();
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

        static int GetIntOr(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        // ASCII renderer — Slay-the-Spire vibe. Each chapter prints as
        // a 1-char glyph (B=boss, E=elite, L=lock, R=reward, T=treasure,
        // ·=duel, S=start). Edges drawn between parent and child along
        // same row/col so the eye traces the topology.
        static string RenderAscii(LayoutGenerator.Result layout, Dictionary<string, object> entry)
        {
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            // Parse positions out of the emitted chapter dicts.
            Dictionary<int, int[]> pos = new Dictionary<int, int[]>();   // chapterId -> [x, y]
            Dictionary<int, int>   parentOf = new Dictionary<int, int>();
            Dictionary<int, char>  glyphOf  = new Dictionary<int, char>();
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in layout.Chapters)
            {
                int cid = int.Parse(kv.Key);
                int x   = Utils.GetValue<int>(kv.Value, "grid_x");
                int y   = Utils.GetValue<int>(kv.Value, "grid_y");
                int par = Utils.GetValue<int>(kv.Value, "parent_chapter");
                pos[cid] = new[] { x, y };
                parentOf[cid] = par;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in layout.ChapterMeta)
            {
                int cid = int.Parse(kv.Key);
                string type = Utils.GetValue<string>(kv.Value, "type") ?? "duel";
                glyphOf[cid] = GlyphFor(type);
            }

            // Spacing: 3 horizontal cells per grid step so corridors fit.
            int w = (maxX - minX + 1) * 3 + 1;
            int h = (maxY - minY + 1) * 2 + 1;
            char[,] canvas = new char[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++) canvas[i, j] = ' ';

            int CanvasX(int gx) => (gx - minX) * 3 + 1;
            int CanvasY(int gy) => (gy - minY) * 2;

            // Draw edges first so glyphs paint on top.
            foreach (KeyValuePair<int, int[]> p in pos)
            {
                int par = parentOf[p.Key];
                if (par == 0 || !pos.ContainsKey(par)) continue;
                int[] a = pos[par]; int[] b = p.Value;
                int ax = CanvasX(a[0]), ay = CanvasY(a[1]);
                int bx = CanvasX(b[0]), by = CanvasY(b[1]);
                if (a[0] == b[0])
                {
                    int y1 = Math.Min(ay, by), y2 = Math.Max(ay, by);
                    for (int yi = y1 + 1; yi < y2; yi++) canvas[ax, yi] = '|';
                }
                else if (a[1] == b[1])
                {
                    int x1 = Math.Min(ax, bx), x2 = Math.Max(ax, bx);
                    for (int xi = x1 + 1; xi < x2; xi++) canvas[xi, ay] = '-';
                }
            }
            // Paint glyphs.
            int rootCid = pos.Keys.First(c => parentOf[c] == 0);
            foreach (KeyValuePair<int, int[]> p in pos)
            {
                int cx = CanvasX(p.Value[0]);
                int cy = CanvasY(p.Value[1]);
                char g;
                if (!glyphOf.TryGetValue(p.Key, out g)) g = '?';
                if (p.Key == rootCid) g = 'S';
                canvas[cx, cy] = g;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Legend: S=start  B=boss  E=elite  L=lock  R=reward  T=treasure  o=duel");
            sb.AppendLine();
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) sb.Append(canvas[i, j]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static char GlyphFor(string type)
        {
            switch (type)
            {
                case "boss":     return 'B';
                case "elite":    return 'E';
                case "lock":     return 'L';
                case "reward":   return 'R';
                case "treasure": return 'T';
                default:         return 'o';
            }
        }
    }
}
