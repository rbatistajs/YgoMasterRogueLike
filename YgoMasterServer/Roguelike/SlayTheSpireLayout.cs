using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Slay-the-Spire style: carve N paths bottom->top on a floors x width grid. Each step goes
    // straight or one column diagonally, refusing the move that would make an X with an existing
    // edge (the classic no-crossing rule). Nodes exist only where a path visits; merges give
    // multi-parent nodes for free. Top floor is a single boss the last body row funnels into.
    class SlayTheSpireLayout : RoguelikeMapLayout
    {
        public override RoguelikeMap Build(int seed, Dictionary<string, object> settings)
        {
            int floors = Math.Max(2, RoguelikeSettings.Floors(settings));
            int width = Math.Max(2, RoguelikeSettings.Width(settings));
            int pathCount = Math.Max(2, RoguelikeSettings.Paths(settings));
            RoguelikeTypePolicy policy = RoguelikeTypePolicy.FromSettings(settings, floors);
            Random rng = new Random(seed);

            RoguelikeMap map = new RoguelikeMap { Rows = floors };
            int bodyTop = floors - 2; // last non-boss row
            MapNode[,] grid = new MapNode[floors, width];
            int nextId = 0;

            MapNode boss = new MapNode { Id = nextId++, Row = floors - 1, Col = width / 2, Type = "boss" };
            grid[floors - 1, width / 2] = boss;

            for (int p = 0; p < pathCount; p++)
            {
                int col = rng.Next(width);
                EnsureNode(grid, ref nextId, 0, col, policy, rng);
                for (int r = 0; r < bodyTop; r++)
                {
                    int nc = NextCol(grid, r, col, width, rng);
                    MapNode from = grid[r, col];
                    MapNode to = EnsureNode(grid, ref nextId, r + 1, nc, policy, rng);
                    if (!from.Next.Contains(to.Id)) from.Next.Add(to.Id);
                    col = nc;
                }
            }

            for (int c = 0; c < width; c++)
            {
                MapNode n = grid[bodyTop, c];
                if (n != null && !n.Next.Contains(boss.Id)) n.Next.Add(boss.Id);
            }

            for (int r = 0; r < floors; r++)
                for (int c = 0; c < width; c++)
                    if (grid[r, c] != null) map.Nodes.Add(grid[r, c]);
            policy.EnforceCounts(map.Nodes, rng); // bound per-type counts after the full set exists
            return map;
        }

        static MapNode EnsureNode(MapNode[,] grid, ref int nextId, int r, int c,
            RoguelikeTypePolicy policy, Random rng)
        {
            if (grid[r, c] != null) return grid[r, c];
            MapNode n = new MapNode { Id = nextId++, Row = r, Col = c, Type = policy.PickType(r, rng) };
            grid[r, c] = n;
            return n;
        }

        // Pick the next column from {col-1, col, col+1}, dropping any diagonal that would cross an
        // edge already carved by an earlier path.
        static int NextCol(MapNode[,] grid, int r, int col, int width, Random rng)
        {
            List<int> cand = new List<int>();
            for (int d = -1; d <= 1; d++)
            {
                int c = col + d;
                if (c < 0 || c >= width) continue;
                if (d != 0 && WouldCross(grid, r, col, c)) continue;
                cand.Add(c);
            }
            if (cand.Count == 0) cand.Add(col); // straight up is always in range
            return cand[rng.Next(cand.Count)];
        }

        // Edge (r,col)->(r+1,target) crosses the mirror edge (r,target)->(r+1,col).
        static bool WouldCross(MapNode[,] grid, int r, int col, int target)
        {
            MapNode neighbor = grid[r, target];
            MapNode opposite = grid[r + 1, col];
            if (neighbor == null || opposite == null) return false;
            return neighbor.Next.Contains(opposite.Id);
        }
    }
}
