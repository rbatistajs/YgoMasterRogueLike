using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Port of build_grid_gate_procedural.py `generate_tower`. Vertical
    // trunk with lateral branches at each floor. Boss at the trunk's
    // bottom.
    static class TowerGenerator
    {
        public static void Generate(GenerationContext ctx,
            out List<LayoutNode> nodes, out LayoutNode root, out LayoutNode boss)
        {
            int floors = ctx.Int("floor_count", 5);
            int branches = ctx.Int("branch_count_per_floor", 2);
            int floorDist = ctx.Int("floor_distance", 2);
            int branchLen = ctx.Int("branch_length", 3);

            int height = floors * floorDist + 2;
            int width = branches == 0 ? 3 : (2 + branches * 2 + 1);
            int boundsW = width * 2 + 1;
            int boundsH = height;
            int cx = boundsW / 2;

            Dictionary<long, LayoutNode> occ = new Dictionary<long, LayoutNode>();
            nodes = new List<LayoutNode>();
            root = LayoutGenerator.Place(occ, nodes, cx, 0, null, boundsW, boundsH);
            root.IsMainPath = true;
            LayoutNode prev = root;

            for (int f = 1; f < floors; f++)
            {
                for (int d = 1; d <= floorDist; d++)
                {
                    LayoutNode n = LayoutGenerator.Place(
                        occ, nodes, cx, prev.Y + 1, prev, boundsW, boundsH);
                    if (n == null) break;
                    n.IsMainPath = true;
                    prev = n;
                }
                for (int b = 0; b < branches; b++)
                {
                    int side = (b % 2 == 0) ? -1 : 1;
                    int mag = (b / 2 + 1) * 2;
                    int bx = cx + side * mag;
                    int by = prev.Y;
                    LayoutNode br = LayoutGenerator.Place(
                        occ, nodes, bx, by, prev, boundsW, boundsH);
                    if (br == null) continue;
                    LayoutNode sp = br;
                    for (int s = 1; s < branchLen; s++)
                    {
                        LayoutNode n = LayoutGenerator.Place(
                            occ, nodes, bx, by + s, sp, boundsW, boundsH);
                        if (n == null) break;
                        sp = n;
                    }
                    sp.IsLeafTerminal = true;
                }
            }
            boss = prev;
            boss.ChapterType = "boss";
        }
    }
}
