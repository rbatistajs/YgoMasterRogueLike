using System;
using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Port of build_grid_gate_procedural.py's `_precompute_runtime_layout`
    // — but server-side, run fresh per (player, regen) so every map is
    // unique to the player. Python no longer precomputes a pool; the
    // GridGates entry only carries the format + params and this code
    // does the rest.
    //
    // Pipeline (mirrors the Python flow):
    //   1. Pick the format generator and produce a node tree
    //   2. assign_types: promote duels into treasure/reward via counts
    //   3. setup_progression: place locks + elites, build locks_map
    //   4. assign_levels: flat (basic) or by-row-curve
    //   5. Allocate sequential chapter ids inside the gate's range
    //   6. Emit chapter dicts + per-chapter type/level metadata
    static class LayoutGenerator
    {
        // The boss keeps its position in the node list — chapter ids are
        // sequential and the boss's id ends up wherever it is in iteration
        // order, same as the Python builder.
        public class Result
        {
            public Dictionary<string, Dictionary<string, object>> Chapters;
            public Dictionary<string, Dictionary<string, object>> ChapterMeta;
            public int BossChapterId;
            // lock_chapter_id_str -> key_chapter_id_str (post-progression).
            public Dictionary<string, string> Locks;
        }

        // Saída do passo 1: nodes processadas (types, levels, locks)
        // mas SEM chapter ids nem dicts. Usado pelo `export-manual` que
        // precisa do tree cru pra serializar como `manual_cells`.
        public class NodesResult
        {
            public List<LayoutNode> Nodes;
            public LayoutNode Root;
            public LayoutNode Boss;
            public Dictionary<LayoutNode, LayoutNode> LocksMap;   // lock -> key elite
        }

        // Passo 1: roda o format generator + post-processors. Não toca
        // em chapter ids — fica pra quem chamar.
        public static NodesResult GenerateNodes(GenerationContext ctx)
        {
            List<LayoutNode> nodes;
            LayoutNode root, boss;
            switch (ctx.Format)
            {
                case "hourglass": HourglassGenerator.Generate(ctx, out nodes, out root, out boss); break;
                case "dungeon":   DungeonGenerator.Generate(ctx, out nodes, out root, out boss);   break;
                case "tower":     TowerGenerator.Generate(ctx, out nodes, out root, out boss);     break;
                case "manual":    ManualGenerator.Generate(ctx, out nodes, out root, out boss);    break;
                default: return null;
            }

            HashSet<LayoutNode> pathToBoss = PostProcessors.ComputePathToBoss(boss);
            PostProcessors.AssignTypes(nodes, ctx.RewardCount, ctx.TreasureCount, ctx.Rng);
            Dictionary<LayoutNode, LayoutNode> locksMap;
            PostProcessors.SetupProgression(nodes, ctx.EliteCount, ctx.LockCount,
                                            ctx.Rng, pathToBoss, out locksMap);

            if (ctx.DifficultyMode == "basic")
            {
                PostProcessors.AssignLevels(nodes, ctx.DuelLevel, ctx.EliteLevel, ctx.BossLevel);
            }
            else
            {
                List<LayoutPresets.CurveBand> curve;
                if (!LayoutPresets.DifficultyPresets.TryGetValue(ctx.DifficultyMode, out curve))
                {
                    curve = LayoutPresets.DifficultyPresets["default"];
                }
                PostProcessors.AssignLevelsByCurve(nodes, curve, ctx.Rng, ctx.EliteLevel, ctx.BossLevel);
            }

            return new NodesResult
            {
                Nodes = nodes, Root = root, Boss = boss, LocksMap = locksMap,
            };
        }

        // Passo 2: pipeline completo (passo 1 + chapter ids + dicts +
        // locks dict). Usado pelo bake / runtime / preview.
        public static Result Generate(GenerationContext ctx)
        {
            NodesResult nr = GenerateNodes(ctx);
            if (nr == null) return null;
            List<LayoutNode> nodes = nr.Nodes;
            LayoutNode boss = nr.Boss;
            Dictionary<LayoutNode, LayoutNode> locksMap = nr.LocksMap;

            // Sequential ids inside this gate's reserved range.
            int idBase = ctx.GateId * 10000 + 1;
            for (int i = 0; i < nodes.Count; i++) nodes[i].ChapterId = idBase + i;

            Result result = new Result
            {
                Chapters    = new Dictionary<string, Dictionary<string, object>>(),
                ChapterMeta = new Dictionary<string, Dictionary<string, object>>(),
                Locks       = new Dictionary<string, string>(),
                BossChapterId = boss.ChapterId,
            };

            foreach (LayoutNode n in nodes)
            {
                Dictionary<string, object> ch = new Dictionary<string, object>
                {
                    { "parent_chapter", n.Parent != null ? n.Parent.ChapterId : 0 },
                    { "grid_x",         n.X },
                    { "grid_y",         n.Y },
                    { "begin_sn",       "" },
                    { "anime",          0 },
                };
                bool isDuel = n.ChapterType == "duel" || n.ChapterType == "elite" || n.ChapterType == "boss";
                if (isDuel)
                {
                    ch["mydeck_set_id"] = n.ChapterId;
                    ch["set_id"]        = 0;
                    ch["unlock_id"]     = 0;
                    ch["npc_id"]        = 1;
                }
                else
                {
                    ch["mydeck_set_id"] = 0;
                    ch["set_id"]        = n.ChapterId;
                    ch["unlock_id"]     = 0;
                    ch["npc_id"]        = 0;
                }
                string key = n.ChapterId.ToString();
                result.Chapters[key] = ch;
                result.ChapterMeta[key] = new Dictionary<string, object>
                {
                    { "type",  n.ChapterType },
                    { "level", n.Level },
                };
            }

            // Lock progression: lock chapter + every descendant gets
            // unlock_id stamped; locks_map records the key chapter.
            foreach (KeyValuePair<LayoutNode, LayoutNode> kv in locksMap)
            {
                LayoutNode lockNode = kv.Key;
                LayoutNode keyNode = kv.Value;
                if (keyNode.ChapterId == 0) continue;
                string lkey = lockNode.ChapterId.ToString();
                Dictionary<string, object> lockChapter;
                if (result.Chapters.TryGetValue(lkey, out lockChapter))
                {
                    lockChapter["unlock_id"] = lockNode.ChapterId;
                }
                foreach (LayoutNode d in PostProcessors.DescendantsOf(lockNode))
                {
                    string dkey = d.ChapterId.ToString();
                    Dictionary<string, object> dch;
                    if (result.Chapters.TryGetValue(dkey, out dch))
                    {
                        dch["unlock_id"] = lockNode.ChapterId;
                    }
                }
                result.Locks[lkey] = keyNode.ChapterId.ToString();
            }
            return result;
        }

        // Helper used by every generator: place a node at (x,y) under
        // `parent` if free + in bounds, link it, push it onto the node
        // list. Returns null if collision or OOB.
        public static LayoutNode Place(
            Dictionary<long, LayoutNode> occupied,
            List<LayoutNode> nodes,
            int x, int y, LayoutNode parent,
            int boundsW, int boundsH)
        {
            if (x < 0 || x >= boundsW || y < 0 || y >= boundsH) return null;
            long key = ((long)x << 32) | (uint)y;
            if (occupied.ContainsKey(key)) return null;
            LayoutNode n = new LayoutNode(x, y, parent);
            if (parent != null) parent.Children.Add(n);
            nodes.Add(n);
            occupied[key] = n;
            return n;
        }
    }
}
