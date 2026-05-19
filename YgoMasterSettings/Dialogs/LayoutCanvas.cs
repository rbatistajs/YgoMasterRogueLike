using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YgoMasterSettings.Dialogs
{
    // PictureBox custom que desenha o grid de cells + edges. Mouse
    // events expostos via eventos C#; o LayoutEditorDialog faz o
    // wiring (add/select/parent/delete).
    //
    // Mesmas convenções visuais do editor Python:
    //   - Cell colorida por type, label "letra do type" + "L<level>"
    //   - Border ciano = root (parent_pos null)
    //   - Border amarelo = boss (boss_pos match)
    //   - Border branco grosso = selected
    //   - Edge verde no caminho boss→root, branco senão
    class LayoutCanvas : Panel
    {
        public const int CELL_SIZE = 56;
        public const int CELL_GAP  = 8;
        public const int GRID_PAD  = 16;

        public static readonly Dictionary<string, Color> TypeColors = new Dictionary<string, Color>
        {
            { "duel",     Color.FromArgb(0x4a, 0x7b, 0xd5) },
            { "elite",    Color.FromArgb(0xe0, 0x90, 0x40) },
            { "boss",     Color.FromArgb(0xd5, 0x4a, 0x4a) },
            { "reward",   Color.FromArgb(0x52, 0xc7, 0x77) },
            { "treasure", Color.FromArgb(0xd5, 0xc8, 0x4a) },
            { "lock",     Color.FromArgb(0x9a, 0x4a, 0xd5) },
        };

        // ----- state (dirige a renderização) -----
        public List<Dictionary<string, object>> Cells = new List<Dictionary<string, object>>();
        public string BossPos;         // "x,y" string OR null
        public int GridW = 20, GridH = 20;
        public Point? Selected;        // null = nada selecionado

        // ----- events que o dialog assina -----
        public event Action<int, int, MouseButtons, Keys> CellClicked;

        public LayoutCanvas()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(0x18, 0x18, 0x18);
            ResizeRedraw = true;
        }

        public void RefreshAll()
        {
            // Atualiza scroll region baseado no tamanho do grid
            AutoScrollMinSize = new Size(
                GRID_PAD * 2 + GridW * (CELL_SIZE + CELL_GAP),
                GRID_PAD * 2 + GridH * (CELL_SIZE + CELL_GAP));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

            // Faint grid backdrop
            using (Brush bgFill = new SolidBrush(Color.FromArgb(0x22, 0x22, 0x22)))
            using (Pen bgPen = new Pen(Color.FromArgb(0x2c, 0x2c, 0x2c)))
            {
                for (int gy = 0; gy < GridH; gy++)
                {
                    for (int gx = 0; gx < GridW; gx++)
                    {
                        Point p = CellTopLeft(gx, gy);
                        g.FillRectangle(bgFill, p.X, p.Y, CELL_SIZE, CELL_SIZE);
                        g.DrawRectangle(bgPen, p.X, p.Y, CELL_SIZE, CELL_SIZE);
                    }
                }
            }

            // Edges (under cells)
            HashSet<string> pathSet = ComputePathToBoss();
            Dictionary<string, Dictionary<string, object>> byPos = CellsByPos();
            using (Pen edgeOn  = new Pen(Color.FromArgb(0x6f, 0xff, 0x6f), 4f))
            using (Pen edgeOff = new Pen(Color.White, 4f))
            {
                foreach (Dictionary<string, object> c in Cells)
                {
                    string pp = GetStr(c, "parent_pos");
                    if (string.IsNullOrEmpty(pp)) continue;
                    Point pp2;
                    if (!TryParsePos(pp, out pp2)) continue;
                    if (!byPos.ContainsKey(pp)) continue;
                    int cx = GetInt(c, "grid_x"), cy = GetInt(c, "grid_y");
                    Point a = CellTopLeft(pp2.X, pp2.Y);
                    Point b = CellTopLeft(cx, cy);
                    string thisPos = cx + "," + cy;
                    bool on = pathSet.Contains(thisPos) && pathSet.Contains(pp);
                    g.DrawLine(on ? edgeOn : edgeOff,
                        a.X + CELL_SIZE / 2f, a.Y + CELL_SIZE / 2f,
                        b.X + CELL_SIZE / 2f, b.Y + CELL_SIZE / 2f);
                }
            }

            // Cells
            using (Font fontGlyph = new Font("Segoe UI", 14f, FontStyle.Bold))
            using (Font fontLvl   = new Font("Segoe UI", 8f))
            using (Brush textFill = new SolidBrush(Color.White))
            using (StringFormat fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (Dictionary<string, object> c in Cells)
                {
                    int gx = GetInt(c, "grid_x"), gy = GetInt(c, "grid_y");
                    string type = GetStr(c, "type"); if (string.IsNullOrEmpty(type)) type = "duel";
                    Point p = CellTopLeft(gx, gy);

                    Color color;
                    if (!TypeColors.TryGetValue(type, out color)) color = Color.FromArgb(0x66, 0x66, 0x66);
                    using (Brush b = new SolidBrush(color))
                        g.FillRectangle(b, p.X, p.Y, CELL_SIZE, CELL_SIZE);

                    // Outline + selection
                    bool sel = Selected.HasValue && Selected.Value.X == gx && Selected.Value.Y == gy;
                    using (Pen outline = new Pen(sel ? Color.White : Color.FromArgb(0x88, 0x88, 0x88), sel ? 3f : 1f))
                        g.DrawRectangle(outline, p.X, p.Y, CELL_SIZE, CELL_SIZE);

                    // Root (no parent) → cyan outer band
                    if (string.IsNullOrEmpty(GetStr(c, "parent_pos")))
                    {
                        using (Pen cyan = new Pen(Color.FromArgb(0x40, 0xc8, 0xff), 2f))
                            g.DrawRectangle(cyan, p.X - 4, p.Y - 4, CELL_SIZE + 8, CELL_SIZE + 8);
                    }
                    // Boss → yellow inner band
                    if (BossPos == (gx + "," + gy))
                    {
                        using (Pen yellow = new Pen(Color.FromArgb(0xff, 0xe0, 0x40), 3f))
                            g.DrawRectangle(yellow, p.X - 2, p.Y - 2, CELL_SIZE + 4, CELL_SIZE + 4);
                    }

                    // Labels
                    string letter = type.Substring(0, 1).ToUpperInvariant();
                    int lvl = GetInt(c, "level");
                    g.DrawString(letter, fontGlyph, textFill,
                        new RectangleF(p.X, p.Y - 8, CELL_SIZE, CELL_SIZE), fmt);
                    g.DrawString("L" + lvl, fontLvl, textFill,
                        new RectangleF(p.X, p.Y + 12, CELL_SIZE, CELL_SIZE), fmt);
                }
            }
        }

        // ----- mouse -----
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Point pt = GridFromPixel(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);
            if (pt.X < 0 || pt.X >= GridW || pt.Y < 0 || pt.Y >= GridH) return;
            CellClicked?.Invoke(pt.X, pt.Y, e.Button, ModifierKeys);
        }

        // ----- helpers -----
        Point CellTopLeft(int gx, int gy)
        {
            return new Point(
                GRID_PAD + gx * (CELL_SIZE + CELL_GAP),
                GRID_PAD + gy * (CELL_SIZE + CELL_GAP));
        }

        Point GridFromPixel(int px, int py)
        {
            int gx = (px - GRID_PAD) / (CELL_SIZE + CELL_GAP);
            int gy = (py - GRID_PAD) / (CELL_SIZE + CELL_GAP);
            // Hit-test: garante que o pixel cai DENTRO da cell (não no gap)
            Point top = CellTopLeft(gx, gy);
            if (px < top.X || px > top.X + CELL_SIZE
                || py < top.Y || py > top.Y + CELL_SIZE)
                return new Point(-1, -1);
            return new Point(gx, gy);
        }

        Dictionary<string, Dictionary<string, object>> CellsByPos()
        {
            Dictionary<string, Dictionary<string, object>> idx =
                new Dictionary<string, Dictionary<string, object>>();
            foreach (Dictionary<string, object> c in Cells)
                idx[GetInt(c, "grid_x") + "," + GetInt(c, "grid_y")] = c;
            return idx;
        }

        // BFS do boss até root pra colorir edges no caminho.
        HashSet<string> ComputePathToBoss()
        {
            HashSet<string> path = new HashSet<string>();
            if (string.IsNullOrEmpty(BossPos)) return path;
            Dictionary<string, Dictionary<string, object>> idx = CellsByPos();
            Dictionary<string, object> cur;
            if (!idx.TryGetValue(BossPos, out cur)) return path;
            string curPos = BossPos;
            int safety = 200;
            while (cur != null && safety-- > 0)
            {
                path.Add(curPos);
                string pp = GetStr(cur, "parent_pos");
                if (string.IsNullOrEmpty(pp) || !idx.ContainsKey(pp)) break;
                cur = idx[pp];
                curPos = pp;
            }
            return path;
        }

        public static bool TryParsePos(string s, out Point p)
        {
            p = Point.Empty;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(',');
            if (parts.Length != 2) return false;
            int x, y;
            if (!int.TryParse(parts[0].Trim(), out x)) return false;
            if (!int.TryParse(parts[1].Trim(), out y)) return false;
            p = new Point(x, y);
            return true;
        }

        static int GetInt(Dictionary<string, object> d, string key)
        {
            object v; if (!d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }
        static string GetStr(Dictionary<string, object> d, string key)
        {
            object v; if (!d.TryGetValue(key, out v) || v == null) return null;
            return v as string;
        }
    }
}
