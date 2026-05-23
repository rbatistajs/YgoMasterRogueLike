using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Owns roguelike node-type selection: per-zone weight tables (bands), global per-type floor
    // ranges (rules), and whole-row overrides (forced rows). Built once per map from Settings.json;
    // negative row indices are normalized to absolute floors here. Topology lives in the layout —
    // this only answers "what type is the node at floor r?".
    class RoguelikeTypePolicy
    {
        class Band { public int From, To; public List<KeyValuePair<string, double>> Weights; }
        class Rule { public int Min, Max; } // inclusive; Max = int.MaxValue when open-ended

        readonly int _floors;
        List<KeyValuePair<string, double>> _globalWeights = new List<KeyValuePair<string, double>>();
        readonly List<Band> _bands = new List<Band>();
        readonly Dictionary<string, Rule> _rules = new Dictionary<string, Rule>();
        readonly Dictionary<int, string> _forcedRows = new Dictionary<int, string>();

        RoguelikeTypePolicy(int floors) { _floors = Math.Max(1, floors); }

        public static RoguelikeTypePolicy FromSettings(Dictionary<string, object> settings, int floors)
        {
            RoguelikeTypePolicy p = new RoguelikeTypePolicy(floors);
            p._globalWeights = NormalizeWeights(RoguelikeSettings.TypeWeights(settings));

            foreach (object o in RoguelikeSettings.Bands(settings))
            {
                Dictionary<string, object> b = o as Dictionary<string, object>;
                if (b == null) continue;
                int from = p.Norm(Utils.GetValue<int>(b, "from", 0));
                int to = p.Norm(Utils.GetValue<int>(b, "to", floors - 1));
                if (from > to) { int t = from; from = to; to = t; }
                p._bands.Add(new Band
                {
                    From = from, To = to,
                    Weights = NormalizeWeights(Utils.GetValue<Dictionary<string, object>>(b, "weights")),
                });
            }

            foreach (KeyValuePair<string, object> kv in RoguelikeSettings.TypeRules(settings))
            {
                Dictionary<string, object> r = kv.Value as Dictionary<string, object>;
                if (r == null) continue;
                p._rules[kv.Key] = new Rule
                {
                    Min = r.ContainsKey("minFloor") ? p.Norm(ToInt(r["minFloor"])) : 0,
                    Max = r.ContainsKey("maxFloor") ? p.Norm(ToInt(r["maxFloor"])) : int.MaxValue,
                };
            }

            foreach (KeyValuePair<string, object> kv in RoguelikeSettings.ForcedRows(settings))
            {
                int row;
                if (!int.TryParse(kv.Key, out row)) continue;
                string type = Convert.ToString(kv.Value);
                if (!string.IsNullOrEmpty(type)) p._forcedRows[p.Norm(row)] = type;
            }
            return p;
        }

        // Type for the node at floor r: forced row > band weights (first match) > global weights,
        // then filtered by per-type floor rules; weighted random over what's left.
        public string PickType(int floor, Random rng)
        {
            string forced;
            if (_forcedRows.TryGetValue(floor, out forced)) return forced;

            List<KeyValuePair<string, double>> weights = null;
            foreach (Band b in _bands)
                if (floor >= b.From && floor <= b.To && b.Weights.Count > 0) { weights = b.Weights; break; }

            // Floor 0 funnels combat by default; only an explicit band/forcedRow changes it.
            if (floor == 0 && weights == null) return "duel";
            if (weights == null) weights = _globalWeights;

            List<KeyValuePair<string, double>> allowed = new List<KeyValuePair<string, double>>();
            double total = 0;
            foreach (KeyValuePair<string, double> kv in weights)
            {
                Rule rule;
                if (_rules.TryGetValue(kv.Key, out rule) && (floor < rule.Min || floor > rule.Max)) continue;
                allowed.Add(kv); total += kv.Value;
            }
            if (allowed.Count == 0 || total <= 0) return "duel";

            double roll = rng.NextDouble() * total;
            foreach (KeyValuePair<string, double> kv in allowed) { roll -= kv.Value; if (roll <= 0) return kv.Key; }
            return allowed[0].Key;
        }

        // Negative row index -> absolute floor (floors + n), clamped to [0, floors-1].
        int Norm(int idx)
        {
            int r = idx < 0 ? _floors + idx : idx;
            if (r < 0) r = 0;
            if (r > _floors - 1) r = _floors - 1;
            return r;
        }

        static int ToInt(object v) { try { return Convert.ToInt32(v); } catch { return 0; } }

        static List<KeyValuePair<string, double>> NormalizeWeights(Dictionary<string, object> w)
        {
            List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            if (w == null) return list;
            foreach (KeyValuePair<string, object> kv in w)
            {
                double v; try { v = Convert.ToDouble(kv.Value); } catch { v = 0; }
                if (v > 0) list.Add(new KeyValuePair<string, double>(kv.Key, v));
            }
            return list;
        }
    }
}
