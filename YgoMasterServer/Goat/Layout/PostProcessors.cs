using System.Collections.Generic;
using System;

namespace YgoMaster.Layout
{
    // Port of the shared post-processing helpers from
    // build_grid_gate_procedural.py — assign_types, setup_progression,
    // assign_levels (+ assign_levels_by_curve), and the small tree
    // helpers (path-to-boss, descendants, ancestor check).
    //
    // Each helper mirrors the Python contract; manual cells are skipped
    // wherever the Python version does so edits in the layout editor
    // stick across regens.
    static class PostProcessors
    {
        // ---------- tree helpers ----------
        public static HashSet<LayoutNode> ComputePathToBoss(LayoutNode boss)
        {
            HashSet<LayoutNode> path = new HashSet<LayoutNode>();
            for (LayoutNode cur = boss; cur != null; cur = cur.Parent) path.Add(cur);
            return path;
        }

        public static List<LayoutNode> DescendantsOf(LayoutNode n)
        {
            List<LayoutNode> out_ = new List<LayoutNode>();
            Stack<LayoutNode> stack = new Stack<LayoutNode>(n.Children);
            while (stack.Count > 0)
            {
                LayoutNode x = stack.Pop();
                out_.Add(x);
                foreach (LayoutNode c in x.Children) stack.Push(c);
            }
            return out_;
        }

        // ---------- types ----------
        // Default untyped cells to `duel`, then promote N treasures + M
        // rewards from the duel pool (treasures first — rarer, bigger
        // payout — so they don't get squeezed out when the pool is tight).
        public static void AssignTypes(List<LayoutNode> nodes,
                                       int rewardCount, int treasureCount, Random rng)
        {
            foreach (LayoutNode n in nodes)
            {
                if (n.IsManualCell || !string.IsNullOrEmpty(n.ChapterType)) continue;
                n.ChapterType = "duel";
            }
            List<LayoutNode> pool = new List<LayoutNode>();
            foreach (LayoutNode n in nodes)
            {
                if (n.ChapterType == "duel" && !n.IsManualCell) pool.Add(n);
            }
            Shuffle(pool, rng);
            int t = Math.Max(0, treasureCount);
            int r = Math.Max(0, rewardCount);
            int idx = 0;
            for (int i = 0; i < t && idx < pool.Count; i++, idx++)
                pool[idx].ChapterType = "treasure";
            for (int i = 0; i < r && idx < pool.Count; i++, idx++)
                pool[idx].ChapterType = "reward";
        }

        // ---------- progression: locks + elites + keys ----------
        public static void SetupProgression(List<LayoutNode> nodes,
            int eliteCount, int lockCount, Random rng,
            HashSet<LayoutNode> pathToBoss,
            out Dictionary<LayoutNode, LayoutNode> locksMap)
        {
            // 1. Place locks at off-path branching nodes, largest subtree first.
            List<LayoutNode> lockCandidates = new List<LayoutNode>();
            foreach (LayoutNode n in nodes)
            {
                if (!pathToBoss.Contains(n)
                    && !n.IsManualCell
                    && n.ChapterType != "boss"
                    && n.Children.Count > 0)
                {
                    lockCandidates.Add(n);
                }
            }
            // Sort by (-descendants_count, random tiebreak).
            Dictionary<LayoutNode, int> descCache = new Dictionary<LayoutNode, int>();
            Dictionary<LayoutNode, double> tiebreak = new Dictionary<LayoutNode, double>();
            foreach (LayoutNode n in lockCandidates)
            {
                descCache[n] = DescendantsOf(n).Count;
                tiebreak[n] = rng.NextDouble();
            }
            lockCandidates.Sort((a, b) =>
            {
                int c = descCache[b].CompareTo(descCache[a]);
                if (c != 0) return c;
                return tiebreak[a].CompareTo(tiebreak[b]);
            });

            HashSet<LayoutNode> lockedNodes = new HashSet<LayoutNode>();
            List<LayoutNode> locksPending = new List<LayoutNode>();
            foreach (LayoutNode cand in lockCandidates)
            {
                if (locksPending.Count >= lockCount) break;
                List<LayoutNode> descs = DescendantsOf(cand);
                bool conflict = lockedNodes.Contains(cand);
                if (!conflict)
                {
                    foreach (LayoutNode d in descs)
                    {
                        if (lockedNodes.Contains(d)) { conflict = true; break; }
                    }
                }
                if (conflict) continue;
                cand.ChapterType = "lock";
                locksPending.Add(cand);
                lockedNodes.Add(cand);
                foreach (LayoutNode d in descs) lockedNodes.Add(d);
                // Guarantee a prize inside the locked area.
                bool hasPrize = false;
                foreach (LayoutNode d in descs)
                {
                    if (d.ChapterType == "treasure" || d.ChapterType == "reward")
                    { hasPrize = true; break; }
                }
                if (!hasPrize)
                {
                    List<LayoutNode> leaves = new List<LayoutNode>();
                    foreach (LayoutNode d in descs)
                    {
                        if (d.Children.Count == 0 && d.ChapterType != "boss") leaves.Add(d);
                    }
                    if (leaves.Count > 0)
                    {
                        leaves[rng.Next(leaves.Count)].ChapterType = "treasure";
                    }
                }
            }

            // 2. Place elites outside locked subtrees (so they can be keys).
            List<LayoutNode> offPath = new List<LayoutNode>();
            foreach (LayoutNode n in nodes)
            {
                if (Eligible(n, lockedNodes) && !pathToBoss.Contains(n)) offPath.Add(n);
            }
            List<LayoutNode> elitePool;
            if (offPath.Count >= eliteCount)
            {
                elitePool = offPath;
            }
            else
            {
                elitePool = new List<LayoutNode>();
                foreach (LayoutNode n in nodes)
                {
                    if (Eligible(n, lockedNodes)) elitePool.Add(n);
                }
            }
            // Sort: parents (has children) first, then random tiebreak.
            Dictionary<LayoutNode, double> elitTie = new Dictionary<LayoutNode, double>();
            foreach (LayoutNode n in elitePool) elitTie[n] = rng.NextDouble();
            elitePool.Sort((a, b) =>
            {
                int aHas = a.Children.Count > 0 ? 0 : 1;
                int bHas = b.Children.Count > 0 ? 0 : 1;
                int c = aHas.CompareTo(bHas);
                if (c != 0) return c;
                return elitTie[a].CompareTo(elitTie[b]);
            });

            List<LayoutNode> placedElites = new List<LayoutNode>();
            foreach (LayoutNode n in elitePool)
            {
                if (placedElites.Count >= eliteCount) break;
                n.ChapterType = "elite";
                // Hide a treasure under one of the elite's children, if any
                // child is still a free-form cell.
                foreach (LayoutNode child in n.Children)
                {
                    if (child.ChapterType != "boss"
                        && child.ChapterType != "elite"
                        && child.ChapterType != "lock"
                        && !lockedNodes.Contains(child)
                        && !child.IsManualCell)
                    {
                        child.ChapterType = "treasure";
                        break;
                    }
                }
                placedElites.Add(n);
            }

            // 3. Assign a key elite to each lock (outside its subtree).
            locksMap = new Dictionary<LayoutNode, LayoutNode>();
            foreach (LayoutNode lk in locksPending)
            {
                List<LayoutNode> descs = DescendantsOf(lk);
                HashSet<LayoutNode> descSet = new HashSet<LayoutNode>(descs);
                List<LayoutNode> options = new List<LayoutNode>();
                foreach (LayoutNode e in placedElites)
                {
                    if (e != lk && !descSet.Contains(e)) options.Add(e);
                }
                if (options.Count == 0)
                {
                    lk.ChapterType = "duel";       // orphan lock — demote
                    continue;
                }
                locksMap[lk] = options[rng.Next(options.Count)];
            }
        }

        static bool Eligible(LayoutNode n, HashSet<LayoutNode> lockedNodes)
        {
            return n.ChapterType != "boss"
                && n.ChapterType != "lock"
                && !n.IsManualCell
                && !lockedNodes.Contains(n);
        }

        // ---------- levels ----------
        public static void AssignLevels(List<LayoutNode> nodes,
                                        int duelLevel, int eliteLevel, int bossLevel)
        {
            foreach (LayoutNode n in nodes)
            {
                if (n.IsManualCell) continue;
                if (n.ChapterType == "boss")        n.Level = bossLevel;
                else if (n.ChapterType == "elite")  n.Level = eliteLevel;
                else                                n.Level = duelLevel;
            }
        }

        public static void AssignLevelsByCurve(List<LayoutNode> nodes,
            List<LayoutPresets.CurveBand> curve, Random rng,
            int eliteLevel, int bossLevel)
        {
            foreach (LayoutNode n in nodes)
            {
                if (n.IsManualCell) continue;
                if (n.ChapterType == "boss")       n.Level = bossLevel;
                else if (n.ChapterType == "elite") n.Level = eliteLevel;
                else                               n.Level = PickLevelForRow(n.Y, curve, rng);
            }
        }

        static int PickLevelForRow(int y, List<LayoutPresets.CurveBand> curve, Random rng)
        {
            foreach (LayoutPresets.CurveBand band in curve)
            {
                if (band.YMin <= y && y <= band.YMax)
                {
                    double total = 0;
                    foreach (KeyValuePair<int, double> kv in band.Weights) total += kv.Value;
                    if (total <= 0) continue;
                    double r = rng.NextDouble() * total;
                    double acc = 0;
                    foreach (KeyValuePair<int, double> kv in band.Weights)
                    {
                        acc += kv.Value;
                        if (r <= acc) return kv.Key;
                    }
                }
            }
            return 3;
        }

        // ---------- misc ----------
        static void Shuffle<T>(List<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }
}
