using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster.Builder;
using YgoMaster.Rewards;

namespace YgoMaster
{
    // Goat: per-gate config read from `DataLE/GridGates.json` — the same
    // file the Python builder writes when the user creates/edits a gate
    // through `build_grid_gate_procedural.py` / the gate editor GUI.
    //
    // The C# server picks up entries with `runtime: true` and generates
    // those gates per-player at duel start using the layout generators
    // under YgoMaster.Layout (HourglassGenerator / DungeonGenerator /
    // TowerGenerator / ManualGenerator). Non-runtime gates ignored
    // (their baked SoloDuels/*.json drive the duel like before).
    //
    // Fields used today:
    //   gate_id          int    — Solo.json gate key
    //   duel_type        string — "Normal" | "Rush" (drives deck pool default)
    //   format           string — "linear" | "hourglass" | "dungeon" | "tower"
    //   runtime          bool   — gate registered for per-player regen
    //   manual           bool   — `manual_cells` is authoritative; format params ignored
    //   manual_cells     list   — verbatim cell list when `manual: true`
    //   manual_boss_pos  "x,y"  — explicit boss anchor for manual
    //   format_params    dict   — layout-specific knobs (size/branching, room_count, ...)
    //   generic_params   dict   — elite/lock/reward/treasure counts + levels + curve
    //   runtime_templates dict  — modifier templates pre-baked by Python
    class RuntimeGateConfig
    {
        public int GateId;
        public string DuelType;
        public string Format;
        public string RegulationName;
        public bool Manual;
        public Dictionary<string, object> FormatParams;
        public Dictionary<string, object> GenericParams;
        public List<object> ManualCells;
        public string ManualBossPos;
        // Cosmetics: "vanilla" (default) ou "random". Random sorteia
        // mat/sleeve/icon/etc por ItemID.Values em cada chapter — visual
        // único por duelo. Vanilla usa o baseline do chapter 40001.
        public SoloDuelBuilder.CosmeticMode CosmeticMode;
        // Drop chances + category weights per chapter type. Lê do bloco
        // `rewards` da entry. Default zerado (não dropa nada).
        public RewardConfig Rewards;
        // Modifier defaults per chapter type ("boss"/"duel"/"elite"/...).
        // Cada valor é um modifier dict alto-nível (fieldSpell/monsters/
        // spellTraps/hand/extraLife/extraHand). ModifierApplier compila
        // pra cmds + random_specs + life + hnum no momento do bake/runtime.
        //
        // Lê de `generic_params.modifier_defaults` (mesma source que o
        // baker baked usa).
        public Dictionary<string, Dictionary<string, object>> ModifierDefaults;

        // Loads every entry from GridGates.json filtered to `runtime: true`.
        // Returns a dict keyed by gate id — missing/malformed file → empty.
        public static Dictionary<int, RuntimeGateConfig> LoadAll(string dataDirectory)
        {
            Dictionary<int, RuntimeGateConfig> result = new Dictionary<int, RuntimeGateConfig>();
            string path = Path.Combine(dataDirectory, "GridGates.json");
            if (!File.Exists(path)) return result;

            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (doc == null) return result;
            List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
            if (gates == null) return result;

            foreach (object g in gates)
            {
                Dictionary<string, object> entry = g as Dictionary<string, object>;
                if (entry == null) continue;
                if (!Utils.GetValue<bool>(entry, "runtime")) continue;

                int gateId = Utils.GetValue<int>(entry, "gate_id");
                if (gateId == 0) continue;
                string duelType = Utils.GetValue<string>(entry, "duel_type") ?? "Normal";

                result[gateId] = new RuntimeGateConfig
                {
                    GateId         = gateId,
                    DuelType       = duelType,
                    Format         = Utils.GetValue<string>(entry, "format") ?? "linear",
                    RegulationName = duelType == "Rush" ? "Rush Duel" : "Goat Format",
                    Manual         = Utils.GetValue<bool>(entry, "manual"),
                    FormatParams   = Utils.GetValue<Dictionary<string, object>>(entry, "format_params"),
                    GenericParams  = Utils.GetValue<Dictionary<string, object>>(entry, "generic_params"),
                    ManualCells    = Utils.GetValue<List<object>>(entry, "manual_cells"),
                    ManualBossPos  = Utils.GetValue<string>(entry, "manual_boss_pos"),
                    ModifierDefaults = ParseModifierDefaults(entry),
                    CosmeticMode   = ParseCosmeticMode(entry),
                    Rewards        = RewardConfig.Parse(entry),
                };
            }
            return result;
        }

        // Fallback chapter count when the format isn't supported by the
        // C# generators (e.g. `linear`). The runtime generator pads a
        // simple vertical chain in that case.
        public int FallbackChapterCount
        {
            get
            {
                if (GenericParams != null)
                {
                    int n = Utils.GetValue<int>(GenericParams, "duel_count");
                    if (n > 0) return n;
                }
                return 10;
            }
        }

        public int ChapterIdBase => GateId * 10000 + 1;

        public int FallbackBossChapterId => ChapterIdBase + FallbackChapterCount - 1;

        public string DeckPool
        {
            get
            {
                // MVP default: mid-tier per duel type. Override slot left
                // open for a future `runtime_deck_pool` generic param.
                if (DuelType == "Rush") return "decks/rush/3";
                return "decks/normal/3";
            }
        }

        // Pega o modifier dict alto-nível pra um chapter type
        // ("boss"/"duel"/"elite"/...). Fallback pra "duel" se o type
        // específico não existir. Null se não houver modifier_defaults.
        // O caller (BuildDuelDict) passa pro ModifierApplier.ApplyFullPipeline.
        public Dictionary<string, object> ModifierFor(string chapterType)
        {
            if (ModifierDefaults == null || ModifierDefaults.Count == 0) return null;
            Dictionary<string, object> m;
            if (ModifierDefaults.TryGetValue(chapterType, out m)) return m;
            if (ModifierDefaults.TryGetValue("duel",       out m)) return m;
            return null;
        }

        // Lê `cosmetic_mode` da entry (string). Default = vanilla.
        // Aceita "random" ou "vanilla" (case-insensitive).
        static SoloDuelBuilder.CosmeticMode ParseCosmeticMode(Dictionary<string, object> entry)
        {
            string mode = Utils.GetValue<string>(entry, "cosmetic_mode");
            if (!string.IsNullOrEmpty(mode)
                && string.Equals(mode, "random", StringComparison.OrdinalIgnoreCase))
            {
                return SoloDuelBuilder.CosmeticMode.Random;
            }
            return SoloDuelBuilder.CosmeticMode.Vanilla;
        }

        // Lê `generic_params.modifier_defaults` → {chapter_type: modifier_dict}.
        // Vazio/null se não houver. Mesmo shape que o GridGateBaker usa.
        static Dictionary<string, Dictionary<string, object>> ParseModifierDefaults(
            Dictionary<string, object> entry)
        {
            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(entry, "generic_params");
            if (gp == null) return null;
            Dictionary<string, object> raw =
                Utils.GetValue<Dictionary<string, object>>(gp, "modifier_defaults");
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
    }
}
