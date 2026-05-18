using System;
using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Bag of params handed to every generator + post-processor. Built
    // from RuntimeGateConfig.FormatParams / GenericParams + a fresh
    // seed each call so each player+regen sees a different layout.
    class GenerationContext
    {
        public int GateId;
        public string Format;
        public Random Rng;

        // generic_params (post-processing knobs)
        public int RewardCount;
        public int TreasureCount;
        public int EliteCount;
        public int LockCount;
        public int DuelLevel;
        public int EliteLevel;
        public int BossLevel;
        public string DifficultyMode = "default";

        // format_params: raw dict — each generator picks what it needs.
        public Dictionary<string, object> FormatParams;
        // For manual gates: the verbatim cells list + boss anchor.
        public List<object> ManualCells;
        public string ManualBossPos;

        // ----- helpers for poking at format_params -----
        public string Str(string key, string fallback)
        {
            object v;
            if (FormatParams != null && FormatParams.TryGetValue(key, out v) && v is string)
                return (string)v;
            return fallback;
        }
        public int Int(string key, int fallback)
        {
            object v;
            if (FormatParams != null && FormatParams.TryGetValue(key, out v))
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return fallback;
        }
        public int? IntOpt(string key)
        {
            object v;
            if (FormatParams != null && FormatParams.TryGetValue(key, out v) && v != null)
            {
                try { return Convert.ToInt32(v); } catch { }
            }
            return null;
        }
        public double Dbl(string key, double fallback)
        {
            object v;
            if (FormatParams != null && FormatParams.TryGetValue(key, out v))
            {
                try { return Convert.ToDouble(v); } catch { }
            }
            return fallback;
        }
    }
}
