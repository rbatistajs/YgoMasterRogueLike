using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Goat: per-gate runtime-generation config. Loaded once from
    // `DataLE/RuntimeGates.json`. Keyed by gateId (matches Solo.json).
    //
    // Schema (per gate):
    //   chapter_count     int     — total chapters (incl. boss)
    //   chapter_id_base   int     — first chapter id allocated (e.g. 900001)
    //   deck_pool         string  — directory of `.json` player decks
    //   regulation_name   string  — written into each generated DuelSettings
    //   layout            string  — "linear" (MVP); future: "hourglass" etc.
    //   modifiers         object  — optional, applied to each chapter's
    //                               DuelSettings (random_specs / cmds /
    //                               life / hnum — same shape Python builder
    //                               emits via apply_modifiers).
    //   boss_modifiers    object  — optional, override for the last chapter.
    //   boss_deck_pool    string  — optional, override deck dir for boss.
    //
    // Anything supported by the static gate pipeline should be reachable
    // here — runtime gates intentionally aren't a subset of capabilities.
    class RuntimeGateConfig
    {
        public int GateId;
        public int ChapterCount;
        public int ChapterIdBase;
        public string DeckPool;
        public string RegulationName;
        public string Layout;
        public Dictionary<string, object> Modifiers;       // optional
        public Dictionary<string, object> BossModifiers;   // optional
        public string BossDeckPool;                         // optional

        public static Dictionary<int, RuntimeGateConfig> LoadAll(string path)
        {
            Dictionary<int, RuntimeGateConfig> result = new Dictionary<int, RuntimeGateConfig>();
            if (!File.Exists(path)) return result;

            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (doc == null) return result;

            foreach (KeyValuePair<string, object> entry in doc)
            {
                int gateId;
                if (!int.TryParse(entry.Key, out gateId)) continue;
                Dictionary<string, object> cfg = entry.Value as Dictionary<string, object>;
                if (cfg == null) continue;

                result[gateId] = new RuntimeGateConfig
                {
                    GateId          = gateId,
                    ChapterCount    = Utils.GetValue<int>(cfg, "chapter_count", 10),
                    ChapterIdBase   = Utils.GetValue<int>(cfg, "chapter_id_base", gateId * 10000 + 1),
                    DeckPool        = Utils.GetValue<string>(cfg, "deck_pool"),
                    RegulationName  = Utils.GetValue<string>(cfg, "regulation_name"),
                    Layout          = Utils.GetValue<string>(cfg, "layout", "linear"),
                    Modifiers       = Utils.GetValue<Dictionary<string, object>>(cfg, "modifiers"),
                    BossModifiers   = Utils.GetValue<Dictionary<string, object>>(cfg, "boss_modifiers"),
                    BossDeckPool    = Utils.GetValue<string>(cfg, "boss_deck_pool"),
                };
            }
            return result;
        }

        public int ChapterIdAt(int index)
        {
            return ChapterIdBase + index;
        }

        public int BossChapterId()
        {
            return ChapterIdAt(ChapterCount - 1);
        }
    }
}
