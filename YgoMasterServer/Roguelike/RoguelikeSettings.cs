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
    }
}
