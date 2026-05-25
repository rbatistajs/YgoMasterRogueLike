using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Curated enemy pool (DataLE/Roguelike/Encounters.json), keyed by node type. Each type holds an
    // array of encounters that name a deck (file in Roguelike/Opponents) plus optional gating
    // (act/floor/ascension ranges) and overrides (enemy LP, reward, who goes first). Cached once
    // (restart the server to apply); affects new picks only. Mirrors RoguelikeSettings.
    static class RoguelikeEncounters
    {
        public class Range
        {
            public int? Min, Max;
            public bool Contains(int v) { return (!Min.HasValue || v >= Min.Value) && (!Max.HasValue || v <= Max.Value); }
        }

        public class Encounter
        {
            public string Id;
            public string Name;
            public string Text = "";
            public string Deck;
            public string IconImage; // "card_<cid>" or "profile_<id>" — node art (baked types only)
            public Range Act = new Range();
            public Range Floor = new Range();
            public Range Ascension = new Range();
            public double Weight = 1.0;
            public int? EnemyLp;
            public int? Reward;
            public int? FirstPlayer;
            public int? CpuRate;    // AI strength -100..100 (default = Settings, 100 = max)
            public string CpuFlag;  // DuelCpuParam name (None/Def/Fool/Light/...); null = Settings default
            public Dictionary<string, object> Modifiers; // { player, enemy } board spec (RoguelikeModifiers)
        }

        static Dictionary<string, List<Encounter>> _cache;

        public static Dictionary<string, List<Encounter>> Load(string dataDirectory)
        {
            if (_cache != null) return _cache;
            _cache = new Dictionary<string, List<Encounter>>();
            string p = Path.Combine(dataDirectory, "Roguelike", "Encounters.json");
            if (!File.Exists(p)) { Console.WriteLine("[Roguelike] no Encounters.json (combat nodes will error)"); return _cache; }
            Dictionary<string, object> doc = null;
            try { doc = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>; }
            catch (Exception ex) { Console.WriteLine("[Roguelike] Encounters.json parse EX: " + ex.Message); }
            if (doc == null) return _cache;
            foreach (KeyValuePair<string, object> kv in doc)
            {
                List<object> arr = kv.Value as List<object>;
                if (arr == null) continue;
                List<Encounter> list = new List<Encounter>();
                foreach (object o in arr)
                {
                    Encounter e = Parse(o as Dictionary<string, object>);
                    if (e != null) list.Add(e);
                }
                _cache[kv.Key] = list;
            }
            return _cache;
        }

        static Encounter Parse(Dictionary<string, object> d)
        {
            if (d == null) return null;
            string id = Utils.GetValue<string>(d, "id", null);
            string deck = Utils.GetValue<string>(d, "deck", null);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(deck))
            {
                Console.WriteLine("[Roguelike] encounter missing id/deck, skipped");
                return null;
            }
            Encounter e = new Encounter
            {
                Id = id,
                Deck = deck,
                Name = Utils.GetValue<string>(d, "name", null),
                Text = Utils.GetValue<string>(d, "text", ""),
                IconImage = Utils.GetValue<string>(d, "icon_image", null),
                Act = ParseRange(Utils.GetValue<Dictionary<string, object>>(d, "act")),
                Floor = ParseRange(Utils.GetValue<Dictionary<string, object>>(d, "floor")),
                Ascension = ParseRange(Utils.GetValue<Dictionary<string, object>>(d, "ascension")),
                EnemyLp = OptInt(d, "enemyLp"),
                Reward = OptInt(d, "reward"),
                FirstPlayer = OptInt(d, "firstPlayer"),
                CpuRate = OptInt(d, "cpuRate"),
                CpuFlag = ValidCpuFlag(Utils.GetValue<string>(d, "cpuFlag", null), id),
                Modifiers = Utils.GetValue<Dictionary<string, object>>(d, "modifiers"),
            };
            object w; if (d.TryGetValue("weight", out w)) { try { e.Weight = Convert.ToDouble(w); } catch { } }
            if (string.IsNullOrEmpty(e.Name)) e.Name = Path.GetFileNameWithoutExtension(deck);
            return e;
        }

        // Keep a cpuFlag only if it names a real DuelCpuParam; otherwise warn and fall back to default.
        static string ValidCpuFlag(string s, string id)
        {
            if (string.IsNullOrEmpty(s)) return null;
            DuelCpuParam p;
            if (Enum.TryParse<DuelCpuParam>(s, out p)) return s;
            Console.WriteLine("[Roguelike] encounter '" + id + "' invalid cpuFlag '" + s + "' (using default)");
            return null;
        }

        static Range ParseRange(Dictionary<string, object> d)
        {
            Range r = new Range();
            if (d == null) return r;
            r.Min = OptInt(d, "min");
            r.Max = OptInt(d, "max");
            return r;
        }

        static int? OptInt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) { try { return Convert.ToInt32(v); } catch { } }
            return null;
        }

        // Encounters of `type` whose act/floor/ascension ranges all contain the given values.
        public static List<Encounter> Eligible(string dataDirectory, string type, int act, int floor, int ascension)
        {
            List<Encounter> result = new List<Encounter>();
            List<Encounter> pool;
            if (!Load(dataDirectory).TryGetValue(type, out pool) || pool == null) return result;
            foreach (Encounter e in pool)
                if (e.Act.Contains(act) && e.Floor.Contains(floor) && e.Ascension.Contains(ascension))
                    result.Add(e);
            return result;
        }

        // Weighted pick over the eligible set; null when none are eligible (strict — caller errors).
        public static Encounter Pick(string dataDirectory, string type, int act, int floor, int ascension, Random rng)
        {
            List<Encounter> elig = Eligible(dataDirectory, type, act, floor, ascension);
            double total = 0; Encounter last = null;
            foreach (Encounter e in elig) if (e.Weight > 0) { total += e.Weight; last = e; }
            if (total <= 0) return null;
            double roll = rng.NextDouble() * total;
            foreach (Encounter e in elig) { if (e.Weight <= 0) continue; roll -= e.Weight; if (roll <= 0) return e; }
            return last; // float drift fallback
        }

        public static Encounter ById(string dataDirectory, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (KeyValuePair<string, List<Encounter>> kv in Load(dataDirectory))
                foreach (Encounter e in kv.Value)
                    if (e.Id == id) return e;
            return null;
        }
    }
}
