using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Slay-the-Spire style: floors bottom->top, 2..width nodes per floor (floor 0 = width
    // entries, top = single boss), each node links to 1-2 nearest next-floor nodes (no
    // crossing), every upper node guaranteed an incoming edge. Multi-parent is natural.
    class SlayTheSpireLayout : RoguelikeMapLayout
    {
        public override RoguelikeMap Build(int seed, Dictionary<string, object> settings)
        {
            int floors = Math.Max(2, RoguelikeSettings.Floors(settings));
            int width = Math.Max(2, RoguelikeSettings.Width(settings));
            List<KeyValuePair<string, double>> weights = NormalizeWeights(RoguelikeSettings.TypeWeights(settings));
            Random rng = new Random(seed);

            RoguelikeMap map = new RoguelikeMap { Rows = floors };
            int nextId = 0;
            List<List<MapNode>> rows = new List<List<MapNode>>();

            for (int r = 0; r < floors; r++)
            {
                List<MapNode> row = new List<MapNode>();
                int count = r == floors - 1 ? 1 : (r == 0 ? width : 2 + rng.Next(width - 1));
                List<int> cols = SpreadColumns(count, width);
                for (int i = 0; i < count; i++)
                {
                    MapNode n = new MapNode { Id = nextId++, Row = r, Col = cols[i] };
                    n.Type = r == 0 ? "duel" : (r == floors - 1 ? "boss" : PickType(weights, rng));
                    row.Add(n);
                    map.Nodes.Add(n);
                }
                rows.Add(row);
            }

            for (int r = 0; r < floors - 1; r++)
            {
                List<MapNode> cur = rows[r];
                List<MapNode> nxt = rows[r + 1];
                foreach (MapNode n in cur)
                {
                    MapNode nearest = NearestByCol(nxt, n.Col);
                    if (nearest != null) n.Next.Add(nearest.Id);
                    if (nxt.Count > 1 && rng.Next(100) < 45)
                    {
                        MapNode second = NearestByCol(nxt, n.Col, nearest != null ? nearest.Id : -1);
                        if (second != null && !n.Next.Contains(second.Id)) n.Next.Add(second.Id);
                    }
                }
                foreach (MapNode up in nxt)
                {
                    if (!HasIncoming(cur, up.Id))
                    {
                        MapNode src = NearestByCol(cur, up.Col);
                        if (src != null) src.Next.Add(up.Id);
                    }
                }
            }
            return map;
        }

        static List<int> SpreadColumns(int count, int width)
        {
            List<int> cols = new List<int>();
            if (count >= width) { for (int i = 0; i < count; i++) cols.Add(i % width); return cols; }
            double step = (double)width / (count + 1);
            for (int i = 0; i < count; i++)
            {
                int c = (int)Math.Round(step * (i + 1)) - 1;
                cols.Add(c < 0 ? 0 : (c >= width ? width - 1 : c));
            }
            return cols;
        }

        static MapNode NearestByCol(List<MapNode> nodes, int col, int exclude = -1)
        {
            MapNode best = null; int bestD = int.MaxValue;
            foreach (MapNode n in nodes)
            {
                if (n.Id == exclude) continue;
                int d = Math.Abs(n.Col - col);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        static bool HasIncoming(List<MapNode> from, int id)
        {
            foreach (MapNode n in from) if (n.Next.Contains(id)) return true;
            return false;
        }

        static List<KeyValuePair<string, double>> NormalizeWeights(Dictionary<string, object> w)
        {
            List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            double total = 0;
            foreach (KeyValuePair<string, object> kv in w)
            {
                double v; try { v = Convert.ToDouble(kv.Value); } catch { v = 0; }
                if (v > 0) { list.Add(new KeyValuePair<string, double>(kv.Key, v)); total += v; }
            }
            if (total <= 0) list.Add(new KeyValuePair<string, double>("duel", 1.0));
            return list;
        }

        static string PickType(List<KeyValuePair<string, double>> weights, Random rng)
        {
            double total = 0; foreach (KeyValuePair<string, double> kv in weights) total += kv.Value;
            double roll = rng.NextDouble() * total;
            foreach (KeyValuePair<string, double> kv in weights) { roll -= kv.Value; if (roll <= 0) return kv.Key; }
            return weights[0].Key;
        }
    }
}
