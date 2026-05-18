using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Port of build_grid_gate_procedural.py `generate_hourglass`.
    // Trunk → fan → branch descents → optional side chains. The main
    // path keeps `is_main_path=true`; boss is the deepest main-path tail.
    static class HourglassGenerator
    {
        public static void Generate(GenerationContext ctx,
            out List<LayoutNode> nodes, out LayoutNode root, out LayoutNode boss)
        {
            string sizeKey = ctx.Str("size", "medium");
            string branchKey = ctx.Str("branching", "normal");
            LayoutPresets.Size sz;
            if (!LayoutPresets.SizePresets.TryGetValue(sizeKey, out sz))
                sz = LayoutPresets.SizePresets["medium"];
            LayoutPresets.Branching br;
            if (!LayoutPresets.BranchingPresets.TryGetValue(branchKey, out br))
                br = LayoutPresets.BranchingPresets["normal"];

            int w = sz.GridWidth, h = sz.GridHeight;
            int cx = w / 2;
            int trunkLength = sz.TrunkLength;
            int fanCount = sz.FanCount;
            int branchLength = sz.BranchLength;
            int narrowingLength = sz.NarrowingLength;
            int fanXSpacing = ctx.Int("fan_x_spacing", 2);

            Dictionary<long, LayoutNode> occ = new Dictionary<long, LayoutNode>();
            nodes = new List<LayoutNode>();

            // Trunk
            root = LayoutGenerator.Place(occ, nodes, cx, 0, null, w, h);
            LayoutNode prev = root;
            for (int y = 1; y < trunkLength; y++)
            {
                LayoutNode n = LayoutGenerator.Place(occ, nodes, cx, y, prev, w, h);
                if (n != null) prev = n;
            }
            LayoutNode trunkEnd = prev;

            // Fan-out — main branch in the center column, sides flanking.
            LayoutNode mainRoot = LayoutGenerator.Place(occ, nodes, cx, trunkLength, trunkEnd, w, h);
            mainRoot.IsMainPath = true;
            List<LayoutNode> branchRoots = new List<LayoutNode> { mainRoot };

            int half = (fanCount - 1 + 1) / 2;
            List<int> sideOffsets = new List<int>();
            for (int i = 0; i < half; i++)
            {
                sideOffsets.Add(-(i + 1) * fanXSpacing);
                sideOffsets.Add( (i + 1) * fanXSpacing);
            }
            if (sideOffsets.Count > fanCount - 1)
                sideOffsets.RemoveRange(fanCount - 1, sideOffsets.Count - (fanCount - 1));
            foreach (int offset in sideOffsets)
            {
                LayoutNode sr = LayoutGenerator.Place(
                    occ, nodes, cx + offset, trunkLength - 1, trunkEnd, w, h);
                if (sr != null) branchRoots.Add(sr);
            }

            // Each branch descends
            List<LayoutNode> branchTails = new List<LayoutNode>();
            foreach (LayoutNode br0 in branchRoots)
            {
                int depth = br0.IsMainPath ? (branchLength + narrowingLength) : branchLength;
                LayoutNode p = br0;
                for (int step = 1; step < depth; step++)
                {
                    LayoutNode n = LayoutGenerator.Place(
                        occ, nodes, br0.X, br0.Y + step, p, w, h);
                    if (n == null) break;
                    if (br0.IsMainPath) n.IsMainPath = true;
                    p = n;
                }
                branchTails.Add(p);
            }

            // Side chains — small dead-ends sprouting horizontally from
            // non-root cells of each branch.
            foreach (LayoutNode br0 in branchRoots)
            {
                LayoutNode cur = br0;
                while (cur != null)
                {
                    // Continue down the V child (same column) of cur.
                    LayoutNode vChild = null;
                    foreach (LayoutNode c in cur.Children)
                    {
                        if (c.X == cur.X) { vChild = c; break; }
                    }
                    if (cur != br0 && ctx.Rng.NextDouble() < br.SideBranchChance)
                    {
                        int dirX = ctx.Rng.Next(2) == 0 ? -1 : 1;
                        int sx = cur.X + dirX * 2;
                        int sy = cur.Y;
                        LayoutNode sr = LayoutGenerator.Place(occ, nodes, sx, sy, cur, w, h);
                        if (sr != null)
                        {
                            LayoutNode sp = sr;
                            for (int ss = 1; ss < br.SideBranchLength; ss++)
                            {
                                LayoutNode n = LayoutGenerator.Place(
                                    occ, nodes, sx, sy + ss, sp, w, h);
                                if (n == null) break;
                                sp = n;
                            }
                            sp.IsLeafTerminal = true;
                        }
                    }
                    cur = vChild;
                }
            }

            // Boss = deepest tail on the main path.
            boss = null;
            foreach (LayoutNode t in branchTails)
            {
                if (t.IsMainPath) { boss = t; break; }
            }
            if (boss == null) boss = branchTails[0];
            boss.ChapterType = "boss";
            foreach (LayoutNode tail in branchTails)
            {
                if (tail != boss && !tail.IsLeafTerminal) tail.IsLeafTerminal = true;
            }
        }
    }
}
