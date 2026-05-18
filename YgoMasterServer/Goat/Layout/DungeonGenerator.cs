using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Carve BFS + jumps. Cada nó pega 1..max_children filhos; cada filho
    // pula 1..max_jump_distance tiles ortogonais (N/S/E/W). Tiles
    // intermediários do corredor ficam bloqueados pra outros corredores
    // não cruzarem — visual fica com cara de Slay the Spire / Hades em
    // vez do "tetris denso" que sai do random walk frontier.
    //
    // Boss = folha de maior profundidade (BFS depth) — garante viagem
    // longa até o final mesmo quando a árvore se ramifica cedo.
    //
    // Adaptado do algoritmo que o usuário trouxe, com 3 mudanças:
    //   - Usa ctx.Rng (seed reprodutível por player/regen)
    //   - Não gerencia "AssignRoles" — PostProcessors faz isso depois
    //     com o vocabulário do projeto (elite/lock/reward/treasure)
    //   - Sem matriz `int[,]` separada: colisão por Dictionary +
    //     HashSet de corredores
    static class DungeonGenerator
    {
        static readonly int[] DX = { 0,  0, 1, -1 };
        static readonly int[] DY = { 1, -1, 0,  0 };

        public static void Generate(GenerationContext ctx,
            out List<LayoutNode> nodes, out LayoutNode root, out LayoutNode boss)
        {
            int w           = ctx.Int("grid_width",        13);
            int h           = ctx.Int("grid_height",       13);
            int targetCount = ctx.Int("room_count",        25);
            int maxJump     = ctx.Int("max_jump_distance",  3);
            int minChildren = ctx.Int("min_children",       1);
            int maxChildren = ctx.Int("max_children",       3);
            if (maxJump < 1) maxJump = 1;
            if (minChildren < 1) minChildren = 1;
            if (maxChildren < minChildren) maxChildren = minChildren;

            Dictionary<long, LayoutNode> occupied = new Dictionary<long, LayoutNode>();
            // Tiles "carved" para corredores entre salas — bloqueiam novos
            // corredores e novas salas. Visualmente garante separação.
            HashSet<long> corridorTiles = new HashSet<long>();

            nodes = new List<LayoutNode>();
            int cx = w / 2;
            int cy = h / 2;
            root = LayoutGenerator.Place(occupied, nodes, cx, cy, null, w, h);
            if (root == null)
            {
                // grid degenerado — devolve nó solto.
                root = new LayoutNode(0, 0, null) { ChapterType = "boss" };
                nodes.Add(root);
                boss = root;
                return;
            }

            // BFS expansion + depth tracking pra escolher o boss depois.
            Dictionary<LayoutNode, int> depth = new Dictionary<LayoutNode, int> { { root, 0 } };
            Queue<LayoutNode> queue = new Queue<LayoutNode>();
            queue.Enqueue(root);

            int[] dirOrder = { 0, 1, 2, 3 };
            while (queue.Count > 0 && nodes.Count < targetCount)
            {
                LayoutNode parent = queue.Dequeue();
                int childrenAttempts = ctx.Rng.Next(minChildren, maxChildren + 1);
                ShuffleInts(dirOrder, ctx.Rng);

                for (int i = 0; i < childrenAttempts && i < dirOrder.Length; i++)
                {
                    if (nodes.Count >= targetCount) break;

                    int dx = DX[dirOrder[i]];
                    int dy = DY[dirOrder[i]];
                    int wantDist = ctx.Rng.Next(1, maxJump + 1);

                    // Raycast: anda step a step até bater em borda, sala
                    // existente ou corredor existente. Final = último
                    // tile livre alcançado.
                    int reachedX = parent.X;
                    int reachedY = parent.Y;
                    int reachedSteps = 0;
                    for (int step = 1; step <= wantDist; step++)
                    {
                        int nx = parent.X + dx * step;
                        int ny = parent.Y + dy * step;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) break;
                        long key = TileKey(nx, ny);
                        if (occupied.ContainsKey(key) || corridorTiles.Contains(key)) break;
                        reachedX = nx;
                        reachedY = ny;
                        reachedSteps = step;
                    }
                    if (reachedSteps == 0) continue;     // não andou nada

                    LayoutNode child = LayoutGenerator.Place(
                        occupied, nodes, reachedX, reachedY, parent, w, h);
                    if (child == null) continue;
                    depth[child] = depth[parent] + 1;
                    queue.Enqueue(child);

                    // Marca tiles intermediários (do passo 1 ao passo
                    // anterior ao final) como corredor — bloqueia
                    // qualquer outro raycast de cruzar aqui depois.
                    for (int step = 1; step < reachedSteps; step++)
                    {
                        corridorTiles.Add(TileKey(parent.X + dx * step,
                                                   parent.Y + dy * step));
                    }
                }
            }

            // Boss = folha de maior depth. Empate → primeiro encontrado
            // (BFS já dá ordem determinística por seed).
            boss = root;
            int bestDepth = -1;
            foreach (LayoutNode n in nodes)
            {
                if (n.Children.Count != 0) continue;
                if (n == root) continue;
                int d;
                if (!depth.TryGetValue(n, out d)) continue;
                if (d > bestDepth)
                {
                    bestDepth = d;
                    boss = n;
                }
            }
            boss.ChapterType = "boss";

            // Critical path Spawn→Boss recebe is_main_path (a coloração
            // verde do client lê isso pra desenhar a trilha do boss).
            for (LayoutNode cur = boss; cur != null; cur = cur.Parent)
                cur.IsMainPath = true;
            foreach (LayoutNode n in nodes)
            {
                if (n.Children.Count == 0 && n != boss) n.IsLeafTerminal = true;
            }
        }

        static long TileKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        static void ShuffleInts(int[] arr, System.Random rng)
        {
            for (int i = arr.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = arr[i]; arr[i] = arr[j]; arr[j] = t;
            }
        }
    }
}
