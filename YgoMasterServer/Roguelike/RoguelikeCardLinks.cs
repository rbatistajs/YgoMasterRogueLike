using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Pure-C# parser of CardData/MD/CARD_Link.bytes — the game's "related cards" graph. Avoids
    // duel.dll's DLL_CardGetLinkCards, which corrupts the card tables when called in volume.
    //
    // Format: a flat array of little-endian int32, each packing two 16-bit cids as
    // (relatedCid << 16) | sourceCid, sorted by sourceCid. Built once into a bidirectional
    // cid -> related-cids map (cached; restart the server to reload).
    static class RoguelikeCardLinks
    {
        static Dictionary<int, HashSet<int>> _graph;
        static readonly object _lock = new object();

        static Dictionary<int, HashSet<int>> Graph(string dataDirectory)
        {
            if (_graph != null) return _graph;
            lock (_lock) { if (_graph == null) _graph = Build(dataDirectory); }
            return _graph;
        }

        static Dictionary<int, HashSet<int>> Build(string dataDirectory)
        {
            Dictionary<int, HashSet<int>> g = new Dictionary<int, HashSet<int>>();
            string path = Path.Combine(dataDirectory, "CardData", "MD", "CARD_Link.bytes");
            if (!File.Exists(path))
            {
                Console.WriteLine("[Roguelike] CARD_Link.bytes not found — 'link' source disabled");
                return g;
            }
            try
            {
                byte[] d = File.ReadAllBytes(path);
                for (int i = 0; i + 3 < d.Length; i += 4)
                {
                    int e = d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24);
                    int src = e & 0xFFFF;
                    int rel = (e >> 16) & 0xFFFF;
                    if (src == 0 || rel == 0 || src == rel) continue;
                    Add(g, src, rel);
                    Add(g, rel, src); // bidirectional — "related" is symmetric for our purposes
                }
                Console.WriteLine("[Roguelike] CARD_Link: " + g.Count + " cards with related");
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] CARD_Link parse EX: " + ex.Message); }
            return g;
        }

        static void Add(Dictionary<int, HashSet<int>> g, int a, int b)
        {
            HashSet<int> set;
            if (!g.TryGetValue(a, out set)) { set = new HashSet<int>(); g[a] = set; }
            set.Add(b);
        }

        // Union of the related cids of every cid in `sourceCids`.
        public static HashSet<int> RelatedOf(string dataDirectory, IEnumerable<int> sourceCids)
        {
            Dictionary<int, HashSet<int>> g = Graph(dataDirectory);
            HashSet<int> result = new HashSet<int>();
            if (sourceCids == null) return result;
            foreach (int cid in sourceCids)
            {
                HashSet<int> rel;
                if (g.TryGetValue(cid, out rel)) result.UnionWith(rel);
            }
            return result;
        }
    }
}
