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
        class Count { public int Min, Max; } // Max = int.MaxValue when open-ended

        const string Filler = "duel"; // demotion sink / promotion source for count enforcement

        readonly int _floors;
        List<KeyValuePair<string, double>> _globalWeights = new List<KeyValuePair<string, double>>();
        readonly List<Band> _bands = new List<Band>();
        readonly Dictionary<string, Rule> _rules = new Dictionary<string, Rule>();
        readonly Dictionary<int, string> _forcedRows = new Dictionary<int, string>();
        readonly Dictionary<string, Count> _counts = new Dictionary<string, Count>();

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

            foreach (KeyValuePair<string, object> kv in RoguelikeSettings.TypeCounts(settings))
            {
                Dictionary<string, object> c = kv.Value as Dictionary<string, object>;
                if (c == null) continue;
                p._counts[kv.Key] = new Count
                {
                    Min = c.ContainsKey("min") ? ToInt(c["min"]) : 0,
                    Max = c.ContainsKey("max") ? ToInt(c["max"]) : int.MaxValue,
                };
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

        // Post-generation count correction over the whole act map. Skips the boss node and any node
        // on a forced row. Enforces each type's max strictly (demote excess to the Filler), then min
        // best-effort (promote eligible Filler nodes whose floor satisfies the type's rule). The
        // Filler type itself is exempt from max (it is the demotion sink). Deterministic via rng.
        public void EnforceCounts(List<MapNode> nodes, Random rng)
        {
            if (_counts.Count == 0 || nodes == null) return;

            List<MapNode> pool = new List<MapNode>();
            foreach (MapNode n in nodes)
                if (n.Type != "boss" && !_forcedRows.ContainsKey(n.Row)) pool.Add(n);

            // max (strict): demote excess of each capped type to the filler.
            foreach (KeyValuePair<string, Count> kv in _counts)
            {
                if (kv.Value.Max == int.MaxValue || kv.Key == Filler) continue;
                List<MapNode> of = NodesOfType(pool, kv.Key);
                Shuffle(of, rng);
                for (int i = kv.Value.Max; i < of.Count; i++) of[i].Type = Filler;
            }

            // min (best-effort): promote eligible filler nodes until min is met.
            foreach (KeyValuePair<string, Count> kv in _counts)
            {
                int need = kv.Value.Min - NodesOfType(pool, kv.Key).Count;
                if (need <= 0) continue;
                List<MapNode> eligible = new List<MapNode>();
                foreach (MapNode n in NodesOfType(pool, Filler))
                    if (FloorAllowed(kv.Key, n.Row)) eligible.Add(n);
                Shuffle(eligible, rng);
                for (int i = 0; i < need && i < eligible.Count; i++) eligible[i].Type = kv.Key;
            }
        }

        static List<MapNode> NodesOfType(List<MapNode> pool, string type)
        {
            List<MapNode> r = new List<MapNode>();
            foreach (MapNode n in pool) if (n.Type == type) r.Add(n);
            return r;
        }

        // True when `floor` is inside the type's floor rule (or the type has no rule).
        bool FloorAllowed(string type, int floor)
        {
            Rule rule;
            if (!_rules.TryGetValue(type, out rule)) return true;
            return floor >= rule.Min && floor <= rule.Max;
        }

        static void Shuffle(List<MapNode> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                MapNode t = list[i]; list[i] = list[j]; list[j] = t;
            }
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
