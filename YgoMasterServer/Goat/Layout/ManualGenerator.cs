using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Port of build_grid_gate_procedural.py `generate_manual`. Builds the
    // tree verbatim from a list of cell dicts — no randomness; same input
    // always produces the same layout (which is the point: gates flagged
    // `manual: true` are hand-authored in the editor).
    //
    // For manual runtime gates the layout is identical every regen; the
    // *decks* are still picked randomly per chapter though, so each
    // session feels different even with a fixed map.
    static class ManualGenerator
    {
        public static void Generate(GenerationContext ctx,
            out List<LayoutNode> nodes, out LayoutNode root, out LayoutNode boss)
        {
            nodes = new List<LayoutNode>();
            if (ctx.ManualCells == null || ctx.ManualCells.Count == 0)
            {
                root = new LayoutNode(0, 0, null)
                {
                    IsManualCell = true,
                    ChapterType  = "boss",
                    Level        = 6,
                };
                nodes.Add(root);
                boss = root;
                return;
            }

            // Pos → node for parent linking afterwards.
            Dictionary<long, LayoutNode> posToNode = new Dictionary<long, LayoutNode>();
            List<Dictionary<string, object>> cellDicts = new List<Dictionary<string, object>>();

            foreach (object o in ctx.ManualCells)
            {
                Dictionary<string, object> c = o as Dictionary<string, object>;
                if (c == null) continue;
                int cx = Utils.GetValue<int>(c, "grid_x");
                int cy = Utils.GetValue<int>(c, "grid_y");
                LayoutNode n = new LayoutNode(cx, cy, null)
                {
                    IsManualCell = true,
                };
                string ctype = Utils.GetValue<string>(c, "type");
                if (!string.IsNullOrEmpty(ctype)) n.ChapterType = ctype;
                object lvl;
                if (c.TryGetValue("level", out lvl) && lvl != null)
                {
                    try { n.Level = System.Convert.ToInt32(lvl); } catch { }
                }
                long key = ((long)cx << 32) | (uint)cy;
                posToNode[key] = n;
                nodes.Add(n);
                cellDicts.Add(c);
            }

            // Wire parents via `parent_pos` strings ("x,y").
            root = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                LayoutNode n = nodes[i];
                Dictionary<string, object> c = cellDicts[i];
                string pp = Utils.GetValue<string>(c, "parent_pos");
                bool linked = false;
                if (!string.IsNullOrEmpty(pp))
                {
                    string[] parts = pp.Split(',');
                    int px, py;
                    if (parts.Length == 2
                        && int.TryParse(parts[0].Trim(), out px)
                        && int.TryParse(parts[1].Trim(), out py))
                    {
                        long pkey = ((long)px << 32) | (uint)py;
                        LayoutNode parent;
                        if (posToNode.TryGetValue(pkey, out parent) && parent != n)
                        {
                            n.Parent = parent;
                            parent.Children.Add(n);
                            linked = true;
                        }
                    }
                }
                if (!linked && root == null) root = n;
            }
            if (root == null) root = nodes[0];

            // Boss anchor: explicit, or first cell typed boss, or deepest.
            boss = null;
            if (!string.IsNullOrEmpty(ctx.ManualBossPos))
            {
                string[] parts = ctx.ManualBossPos.Split(',');
                int bx, by;
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out bx)
                    && int.TryParse(parts[1].Trim(), out by))
                {
                    long bkey = ((long)bx << 32) | (uint)by;
                    posToNode.TryGetValue(bkey, out boss);
                }
            }
            if (boss == null)
            {
                foreach (LayoutNode n in nodes)
                {
                    if (n.ChapterType == "boss") { boss = n; break; }
                }
            }
            if (boss == null)
            {
                LayoutNode deepest = nodes[0];
                foreach (LayoutNode n in nodes)
                {
                    if (n.Y > deepest.Y || (n.Y == deepest.Y && n.X > deepest.X)) deepest = n;
                }
                boss = deepest;
            }
            boss.ChapterType = "boss";
        }
    }
}
