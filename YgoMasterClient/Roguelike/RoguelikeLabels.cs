using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMasterClient
{
    // Custom roguelike UI strings, loaded from DataLE/Roguelike/Labels.json so they can be edited
    // without recompiling. A missing key (or file) falls back to the in-code default passed by the
    // caller. Loaded once; restart the client to pick up edits.
    static class RoguelikeLabels
    {
        static Dictionary<string, object> _labels;
        static bool _loaded;

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string p = Path.Combine(Program.DataDir, "Roguelike", "Labels.json");
                if (File.Exists(p))
                    _labels = MiniJSON.Json.Deserialize(File.ReadAllText(p)) as Dictionary<string, object>;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] Labels.json load EX: " + ex); }
        }

        public static string Get(string key, string fallback)
        {
            EnsureLoaded();
            object v;
            if (_labels != null && _labels.TryGetValue(key, out v) && v != null)
            {
                string s = Convert.ToString(v);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return fallback;
        }

        // Same as Get, then string.Format with args (for labels with {0}/{1} placeholders).
        public static string Get(string key, string fallback, params object[] args)
        {
            return string.Format(Get(key, fallback), args);
        }
    }
}
