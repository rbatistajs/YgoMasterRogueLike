using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Goat: per-gate config read from `DataLE/GridGates.json` — the same
    // file the Python builder writes when the user creates/edits a gate
    // through `build_grid_gate_procedural.py` / the gate editor GUI.
    //
    // The C# server picks up entries with `runtime: true` and regenerates
    // those gates per-player at duel start. Non-runtime gates ignored
    // (their baked SoloDuels/*.json drive the duel like before).
    //
    // Fields used today (see GridGates.json for the full shape):
    //   gate_id          int    — Solo.json gate key
    //   duel_type        string — "Normal" | "Rush" (drives deck pool default)
    //   format           string — "linear" | "hourglass" | "dungeon" | "tower"
    //   runtime          bool   — gate registered for per-player regen
    //   name / blurb     string — already in IDS_SOLO; unused server-side
    //   format_params    dict   — layout-specific knobs
    //   generic_params   dict   — chapter counts, levels, modifier defaults
    //
    // For MVP the server only honors a small subset (chapter_count derived
    // from generic_params; deck pool from duel_type). Richer layout / tier
    // scaling lives in RUNTIME_GATE_BACKLOG.md.
    class RuntimeGateConfig
    {
        public int GateId;
        public string DuelType;
        public string Format;
        public string RegulationName;
        public Dictionary<string, object> FormatParams;
        public Dictionary<string, object> GenericParams;
        // Pre-computed modifier templates per chapter type
        // ("boss"/"duel"/"elite"/...). Each value is a duel-dict fragment
        // with cmds / random_specs / life / hnum that the server merges
        // into the generated DuelSettings. Python (`_modifiers.apply_modifiers`)
        // computes these at gen time so the server doesn't need to know
        // how to encode markers/random_specs.
        public Dictionary<string, Dictionary<string, object>> RuntimeTemplates;
        // Pool of pre-computed layouts. Each entry is a fully-built layout
        // (chapters / chapter_meta / boss_chapter_id / locks) from one
        // seed variant. Server picks one per (player, regen) to give
        // session-to-session variety. Empty/null → use the linear default.
        public List<RuntimeLayoutVariant> RuntimeLayoutPool;

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
                    FormatParams   = Utils.GetValue<Dictionary<string, object>>(entry, "format_params"),
                    GenericParams  = Utils.GetValue<Dictionary<string, object>>(entry, "generic_params"),
                    RuntimeTemplates  = ParseNestedDict(entry, "runtime_templates"),
                    RuntimeLayoutPool = ParseLayoutPool(entry),
                };
            }
            return result;
        }

        public int ChapterCount
        {
            get
            {
                // MVP: pull from generic_params.duel_count if present, else 10.
                // The full Python pipeline derives count from layout params
                // (trunk_length / fan_count / etc.) — porting that to C# is
                // in the backlog.
                if (GenericParams != null)
                {
                    int n = Utils.GetValue<int>(GenericParams, "duel_count");
                    if (n > 0) return n;
                }
                return 10;
            }
        }

        public int ChapterIdBase => GateId * 10000 + 1;

        public int BossChapterId => ChapterIdBase + ChapterCount - 1;

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

        // Picks the modifier template for a chapter type ("boss" / "duel"
        // / "elite" / ...). Falls back to "duel" if the specific type
        // isn't configured. Returns null if no templates registered.
        public Dictionary<string, object> TemplateFor(string chapterType)
        {
            if (RuntimeTemplates == null || RuntimeTemplates.Count == 0) return null;
            Dictionary<string, object> tpl;
            if (RuntimeTemplates.TryGetValue(chapterType, out tpl)) return tpl;
            if (RuntimeTemplates.TryGetValue("duel",       out tpl)) return tpl;
            return null;
        }

        // Picks a layout from the pool. `variantIndex` wraps modulo the
        // pool size so callers can pass a monotonically increasing regen
        // counter without bothering with bounds.
        public RuntimeLayoutVariant LayoutVariant(int variantIndex)
        {
            if (RuntimeLayoutPool == null || RuntimeLayoutPool.Count == 0) return null;
            int n = RuntimeLayoutPool.Count;
            int idx = ((variantIndex % n) + n) % n;
            return RuntimeLayoutPool[idx];
        }

        // Helper: convert `Dictionary<string, object>` whose values are
        // themselves dicts (common shape for nested runtime tables).
        static Dictionary<string, Dictionary<string, object>> ParseNestedDict(
            Dictionary<string, object> entry, string key)
        {
            Dictionary<string, object> raw =
                Utils.GetValue<Dictionary<string, object>>(entry, key);
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

        static List<RuntimeLayoutVariant> ParseLayoutPool(Dictionary<string, object> entry)
        {
            List<object> raw = Utils.GetValue<List<object>>(entry, "runtime_layout_pool");
            if (raw == null || raw.Count == 0) return null;
            List<RuntimeLayoutVariant> pool = new List<RuntimeLayoutVariant>();
            foreach (object o in raw)
            {
                Dictionary<string, object> v = o as Dictionary<string, object>;
                if (v == null) continue;
                RuntimeLayoutVariant variant = new RuntimeLayoutVariant
                {
                    Chapters       = ParseNestedDict(v, "chapters"),
                    ChapterMeta    = ParseNestedDict(v, "chapter_meta"),
                    BossChapterId  = Utils.GetValue<int>(v, "boss_chapter_id"),
                };
                if (variant.Chapters != null && variant.Chapters.Count > 0)
                {
                    pool.Add(variant);
                }
            }
            return pool.Count > 0 ? pool : null;
        }
    }

    // One layout variant from the pool — fully-baked chapter dicts +
    // per-chapter metadata + boss id. Each gate ships several (different
    // seeds) so per-player regens get distinct maps.
    class RuntimeLayoutVariant
    {
        public Dictionary<string, Dictionary<string, object>> Chapters;
        public Dictionary<string, Dictionary<string, object>> ChapterMeta;
        public int BossChapterId;

        public string GetChapterType(int chapterId)
        {
            if (ChapterMeta == null) return "duel";
            Dictionary<string, object> meta;
            if (!ChapterMeta.TryGetValue(chapterId.ToString(), out meta)) return "duel";
            return Utils.GetValue<string>(meta, "type") ?? "duel";
        }

        public int GetChapterLevel(int chapterId)
        {
            if (ChapterMeta == null) return 3;
            Dictionary<string, object> meta;
            if (!ChapterMeta.TryGetValue(chapterId.ToString(), out meta)) return 3;
            return Utils.GetValue<int>(meta, "level");
        }
    }
}
