using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Data-driven roguelike config (DataLE/Roguelike/Settings.json). Defaults applied when
    // the file or a key is missing, so the mod runs without it.
    static class RoguelikeSettings
    {
        static Dictionary<string, object> _cache;

        public static Dictionary<string, object> Load(string dataDirectory)
        {
            if (_cache != null) return _cache;
            string p = Path.Combine(dataDirectory, "Roguelike", "Settings.json");
            Dictionary<string, object> d = null;
            if (File.Exists(p))
            {
                try { d = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>; }
                catch { d = null; }
            }
            _cache = d ?? new Dictionary<string, object>();
            return _cache;
        }

        public static string Layout(Dictionary<string, object> s) => Utils.GetValue<string>(s, "layout", "slay_the_spire");
        public static int Floors(Dictionary<string, object> s) => Utils.GetValue<int>(s, "floors", 8);
        public static int Width(Dictionary<string, object> s) => Utils.GetValue<int>(s, "width", 4);
        public static int Paths(Dictionary<string, object> s) => Utils.GetValue<int>(s, "paths", 6);

        // type -> weight (duel forced on floor 0, boss fixed on top — not in this table).
        public static Dictionary<string, object> TypeWeights(Dictionary<string, object> s)
        {
            Dictionary<string, object> w = Utils.GetValue<Dictionary<string, object>>(s, "typeWeights");
            if (w != null && w.Count > 0) return w;
            return new Dictionary<string, object>
            {
                { "duel", 0.6 }, { "elite", 0.12 }, { "event", 0.12 }, { "shop", 0.08 }, { "reward", 0.08 },
            };
        }

        // Row zones with their own weight tables: [{ from, to, weights:{...} }]. Empty = none.
        public static List<object> Bands(Dictionary<string, object> s)
        {
            return Utils.GetValue<List<object>>(s, "bands") ?? new List<object>();
        }

        // Per-type floor locks: { "<type>": { minFloor, maxFloor } } (negatives = from top). Empty = none.
        public static Dictionary<string, object> TypeRules(Dictionary<string, object> s)
        {
            return Utils.GetValue<Dictionary<string, object>>(s, "typeRules") ?? new Dictionary<string, object>();
        }

        // Whole-row overrides: { "<row>": "<type>" } (row may be negative). Empty = none.
        public static Dictionary<string, object> ForcedRows(Dictionary<string, object> s)
        {
            return Utils.GetValue<Dictionary<string, object>>(s, "forcedRows") ?? new Dictionary<string, object>();
        }

        // ----- combat HP -----

        // Player run HP: start value and cap (heals never exceed it).
        public static int PlayerMaxHp(Dictionary<string, object> s) => Utils.GetValue<int>(s, "playerMaxHp", 8000);

        // Heal applied after a won combat, as a fraction of PlayerMaxHp (default 10%).
        public static double HealPercent(Dictionary<string, object> s)
        {
            object v;
            if (s != null && s.TryGetValue("healPercentPerCombat", out v))
            {
                try { return Convert.ToDouble(v); } catch { }
            }
            return 0.10;
        }

        // Enemy starting LP for a combat node: enemyHp[<type>] else enemyHp["default"] else 2000.
        public static int EnemyHpFor(Dictionary<string, object> s, string type)
        {
            Dictionary<string, object> e = Utils.GetValue<Dictionary<string, object>>(s, "enemyHp");
            int fallback = 2000;
            if (e != null)
            {
                object v;
                if (!string.IsNullOrEmpty(type) && e.TryGetValue(type, out v)) { try { return Convert.ToInt32(v); } catch { } }
                if (e.TryGetValue("default", out v)) { try { fallback = Convert.ToInt32(v); } catch { } }
            }
            return fallback;
        }
    }
}
