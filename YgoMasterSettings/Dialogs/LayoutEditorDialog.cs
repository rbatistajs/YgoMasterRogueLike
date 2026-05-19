using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using YgoMaster;
using YgoMaster.Builder;
using YgoMaster.Layout;

namespace YgoMasterSettings.Dialogs
{
    // Editor visual de manual_cells. Port do LayoutEditorDialog Python.
    //
    // Hierarquia:
    //   ┌─────────────────────────────────────────────┐
    //   │ Title bar — gate name + Regenerate Procedural│
    //   ├──────────────────────────┬──────────────────┤
    //   │ LayoutCanvas (cells)     │ Side form        │
    //   │                          │  Type/Level/Deck │
    //   │                          │  Is boss?        │
    //   │                          │  Delete          │
    //   │                          │ — OR —           │
    //   │                          │  Legend (when    │
    //   │                          │   nothing sel.)  │
    //   ├──────────────────────────┴──────────────────┤
    //   │             Cancel | Save (as Manual)        │
    //   └─────────────────────────────────────────────┘
    //
    // Cells são gravadas em entry.manual_cells; flag manual=true ativa
    // o ManualGenerator no bake. Save dispara upsert + bake automático.
    class LayoutEditorDialog : Form
    {
        public bool Saved { get; private set; }

        readonly Dictionary<string, object> _entry;
        List<Dictionary<string, object>> _cells;
        string _bossPos;
        int _gridW = 20, _gridH = 20;
        Point? _selected;
        // {level → [deck filenames]} carregado de DataLE/decks/<duelType>/<level>/.
        // Usado pelo deck combo no side form (filtra pelo level da cell).
        Dictionary<int, List<string>> _deckPools;

        LayoutCanvas _canvas;
        Panel _sidePanel;
        Label _statusLabel;
        Timer _statusTimer;

        public LayoutEditorDialog(Dictionary<string, object> entry)
        {
            _entry = entry;
            Text = "Layout Editor — Gate " + Utils.GetValue<int>(entry, "gate_id") +
                   " (" + (Utils.GetValue<string>(entry, "name") ?? "") + ")";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(1024, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            LoadCellsFromEntry();
            LoadDeckPools();
            BuildUi();
            EnsureSnapshot();   // se a entry ainda não tem cells, snapshota do procedural
        }

        // Lê DataLE/decks/<duelType>/{0..6}/*.json pra popular o deck
        // combo do side form. Filtrado por level na hora de renderizar.
        void LoadDeckPools()
        {
            _deckPools = new Dictionary<int, List<string>>();
            string duelType = (Utils.GetValue<string>(_entry, "duel_type") ?? "Normal").ToLowerInvariant();
            string root = Path.Combine(Program.DataDir, "decks", duelType);
            if (!Directory.Exists(root)) return;
            for (int lvl = 0; lvl <= 6; lvl++)
            {
                string dir = Path.Combine(root, lvl.ToString());
                if (!Directory.Exists(dir)) continue;
                List<string> files = new List<string>();
                foreach (string f in Directory.GetFiles(dir, "*.json"))
                    files.Add(Path.GetFileName(f));
                files.Sort();
                _deckPools[lvl] = files;
            }
        }

        void LoadCellsFromEntry()
        {
            _cells = new List<Dictionary<string, object>>();
            List<object> mc = Utils.GetValue<List<object>>(_entry, "manual_cells");
            if (mc != null)
            {
                foreach (object o in mc)
                {
                    Dictionary<string, object> c = o as Dictionary<string, object>;
                    if (c != null) _cells.Add(c);
                }
            }
            _bossPos = Utils.GetValue<string>(_entry, "manual_boss_pos");
            // Calcula grid_w/h cobrindo o maior cell + margem
            int maxX = 8, maxY = 8;
            foreach (Dictionary<string, object> c in _cells)
            {
                int x = Utils.GetValue<int>(c, "grid_x");
                int y = Utils.GetValue<int>(c, "grid_y");
                if (x + 4 > maxX) maxX = x + 4;
                if (y + 4 > maxY) maxY = y + 4;
            }
            _gridW = Math.Max(_gridW, maxX);
            _gridH = Math.Max(_gridH, maxY);
        }

        void BuildUi()
        {
            // ----- Top bar -----
            FlowLayoutPanel top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 38,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
            };
            Button btnRegen = new Button { Text = "↻ Regenerate Procedural",
                Width = 200, Height = 28, ForeColor = Color.FromArgb(0x77, 0x66, 0x33) };
            btnRegen.Click += OnRegenProcedural;
            top.Controls.Add(btnRegen);

            // ----- Bottom: status (esquerda) + Save/Cancel (direita) -----
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 44,
                Padding = new Padding(8) };

            _statusLabel = new Label
            {
                Dock = DockStyle.Left, AutoSize = false, Width = 600,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.FgMuted, Padding = new Padding(4, 0, 0, 0),
            };
            _statusTimer = new Timer { Interval = 3000 };
            _statusTimer.Tick += (s, e) => { _statusLabel.Text = ""; _statusTimer.Stop(); };

            FlowLayoutPanel rightButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 280,
                FlowDirection = FlowDirection.RightToLeft,
            };
            Button btnSave = new Button { Text = "Save (as Manual)",
                Width = 160, Height = 28, BackColor = SystemColors.Highlight,
                ForeColor = SystemColors.HighlightText, FlatStyle = FlatStyle.Flat };
            btnSave.Click += OnSave;
            Button btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28,
                DialogResult = DialogResult.Cancel };
            rightButtons.Controls.Add(btnSave);
            rightButtons.Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            bottom.Controls.Add(rightButtons);
            bottom.Controls.Add(_statusLabel);

            // ----- Center: canvas (esquerda, expande) + side panel (direita, fixo) -----
            // TableLayoutPanel evita o erro do SplitContainer
            // (SplitterDistance precisa de Width válido antes do Add).
            TableLayoutPanel center = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            };
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _canvas = new LayoutCanvas
            {
                Dock = DockStyle.Fill,
                Cells = _cells, BossPos = _bossPos,
                GridW = _gridW, GridH = _gridH,
            };
            _canvas.CellClicked += OnCellClicked;
            center.Controls.Add(_canvas, 0, 0);

            _sidePanel = new Panel
            {
                Dock = DockStyle.Fill, AutoScroll = true,
                Padding = new Padding(12),
                BorderStyle = BorderStyle.FixedSingle,
            };
            center.Controls.Add(_sidePanel, 1, 0);
            RenderSidePanel(null);

            Controls.Add(center);
            Controls.Add(bottom);
            Controls.Add(top);

            _canvas.RefreshAll();
        }

        // ----- snapshot inicial (se não tem cells, chama export-manual in-process) -----
        void EnsureSnapshot()
        {
            if (_cells.Count > 0) return;
            // Roda LayoutGenerator.GenerateNodes (mesma rotina que o CLI
            // export-manual usa) pra começar com algo razoável.
            try
            {
                List<Dictionary<string, object>> snap = GenerateSnapshot(_entry);
                if (snap != null && snap.Count > 0)
                {
                    _cells.AddRange(snap);
                    _canvas.Cells = _cells;
                    // Marca boss = última cell por grid_y descendente
                    Dictionary<string, object> boss = null;
                    foreach (Dictionary<string, object> c in snap)
                    {
                        if (Utils.GetValue<string>(c, "type") == "boss") { boss = c; break; }
                    }
                    if (boss != null)
                    {
                        _bossPos = Utils.GetValue<int>(boss, "grid_x") + "," +
                                   Utils.GetValue<int>(boss, "grid_y");
                        _canvas.BossPos = _bossPos;
                    }
                    RecalcGrid();
                    _canvas.RefreshAll();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha no snapshot procedural: " + ex.Message,
                    "Snapshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ----- mouse handler do canvas -----
        void OnCellClicked(int gx, int gy, MouseButtons btn, Keys modifiers)
        {
            Dictionary<string, object> hit = CellAt(gx, gy);

            if (btn == MouseButtons.Right)
            {
                if (hit == null) return;
                DeleteCellWithDescendants(hit);
                return;
            }

            if (btn != MouseButtons.Left) return;

            // Ctrl+Click cell = vira root (parent_pos = null, reverte chain anterior)
            if ((modifiers & Keys.Control) == Keys.Control && hit != null)
            {
                MakeRoot(hit);
                return;
            }
            // Shift+Click cell = vira parent da selecionada
            if ((modifiers & Keys.Shift) == Keys.Shift && hit != null && _selected.HasValue)
            {
                Dictionary<string, object> child = CellAt(_selected.Value.X, _selected.Value.Y);
                if (child != null && hit != child) SetParent(child, hit);
                return;
            }

            if (hit != null)
            {
                _selected = new Point(gx, gy);
                _canvas.Selected = _selected;
                RenderSidePanel(hit);
                _canvas.RefreshAll();
                return;
            }
            // Click vazio = add new (parent = selected se H/V adjacente)
            AddCell(gx, gy);
        }

        // ----- mutations -----
        Dictionary<string, object> CellAt(int gx, int gy)
        {
            foreach (Dictionary<string, object> c in _cells)
                if (Utils.GetValue<int>(c, "grid_x") == gx
                    && Utils.GetValue<int>(c, "grid_y") == gy) return c;
            return null;
        }

        void AddCell(int gx, int gy)
        {
            string parentPos = null;
            if (_selected.HasValue)
            {
                int sx = _selected.Value.X, sy = _selected.Value.Y;
                // H/V adjacent (same row OR same col, distance ≥ 1)
                if (sx == gx && sy != gy) parentPos = sx + "," + sy;
                else if (sy == gy && sx != gx) parentPos = sx + "," + sy;
            }
            Dictionary<string, object> cell = new Dictionary<string, object>
            {
                { "grid_x", gx }, { "grid_y", gy }, { "parent_pos", parentPos },
                { "type", "duel" }, { "level", 3 },
                { "deck_file", null }, { "gems_override", null },
                { "card_rewards", new List<object>() },
            };
            _cells.Add(cell);
            if (string.IsNullOrEmpty(_bossPos))
            {
                cell["type"] = "boss";
                _bossPos = gx + "," + gy;
                _canvas.BossPos = _bossPos;
            }
            _selected = new Point(gx, gy);
            _canvas.Selected = _selected;
            RecalcGrid();
            RenderSidePanel(cell);
            _canvas.RefreshAll();
        }

        void SetParent(Dictionary<string, object> child, Dictionary<string, object> parent)
        {
            int px = Utils.GetValue<int>(parent, "grid_x");
            int py = Utils.GetValue<int>(parent, "grid_y");
            child["parent_pos"] = px + "," + py;
            _canvas.RefreshAll();
            RenderSidePanel(child);
        }

        // Marca cell como root da árvore. Caminha do target até o root
        // atual, **reverte** cada link no caminho — o target vira root,
        // cada cell na chain pega a cell anterior como parent. Siblings
        // de cells na chain ficam intactos (off-path branches preservados).
        //
        // Sem essa reversão, marcar uma cell folha como root deixaria
        // a árvore quebrada (forest com 2 raízes).
        void MakeRoot(Dictionary<string, object> cell)
        {
            int gx = Utils.GetValue<int>(cell, "grid_x");
            int gy = Utils.GetValue<int>(cell, "grid_y");
            string startPos = gx + "," + gy;
            string parentOfTarget = Utils.GetValue<string>(cell, "parent_pos");
            if (string.IsNullOrEmpty(parentOfTarget))
            {
                FlashStatus("(" + gx + "," + gy + ") já é o player start.");
                return;
            }

            // Walk: monta chain do target até o root atual, em ordem.
            List<string> chain = new List<string> { startPos };
            string curPos = startPos;
            int guard = _cells.Count + 1;
            while (guard-- > 0)
            {
                Dictionary<string, object> curCell = CellAtPos(curPos);
                if (curCell == null) break;
                string pp = Utils.GetValue<string>(curCell, "parent_pos");
                if (string.IsNullOrEmpty(pp)) break;
                if (CellAtPos(pp) == null) break;
                chain.Add(pp);
                curPos = pp;
            }
            if (guard < 0)
            {
                FlashStatus("Cycle detected — aborting re-root.");
                return;
            }

            // Reverse: chain[0] (target) vira root; chain[i+1] aponta pra chain[i].
            Dictionary<string, object> rootCell = CellAtPos(chain[0]);
            if (rootCell != null) rootCell["parent_pos"] = null;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                Dictionary<string, object> child = CellAtPos(chain[i + 1]);
                if (child != null) child["parent_pos"] = chain[i];
            }
            FlashStatus("Player start movido pra (" + gx + "," + gy + ").");
            _canvas.RefreshAll();
            RenderSidePanel(cell);
        }

        // Mensagem transitória no rodapé (auto-clear em 3s). Mirror do
        // _flash_status do Python.
        void FlashStatus(string text)
        {
            _statusLabel.Text = text;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        Dictionary<string, object> CellAtPos(string pos)
        {
            if (string.IsNullOrEmpty(pos)) return null;
            string[] parts = pos.Split(',');
            if (parts.Length != 2) return null;
            int x, y;
            if (!int.TryParse(parts[0].Trim(), out x)) return null;
            if (!int.TryParse(parts[1].Trim(), out y)) return null;
            return CellAt(x, y);
        }

        // Deleta cell + todos seus descendentes (com confirm).
        void DeleteCellWithDescendants(Dictionary<string, object> cell)
        {
            HashSet<string> toRemove = new HashSet<string>();
            CollectDescendants(cell, toRemove);
            string cellPos = Utils.GetValue<int>(cell, "grid_x") + "," + Utils.GetValue<int>(cell, "grid_y");
            toRemove.Add(cellPos);
            DialogResult ok = MessageBox.Show(
                "Deletar cell " + cellPos + " e " + (toRemove.Count - 1) + " descendente(s)?",
                "Confirma delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ok != DialogResult.Yes) return;
            _cells.RemoveAll(c => toRemove.Contains(
                Utils.GetValue<int>(c, "grid_x") + "," + Utils.GetValue<int>(c, "grid_y")));
            if (_bossPos != null && toRemove.Contains(_bossPos))
            {
                _bossPos = null;
                _canvas.BossPos = null;
            }
            if (_selected.HasValue
                && toRemove.Contains(_selected.Value.X + "," + _selected.Value.Y))
            {
                _selected = null;
                _canvas.Selected = null;
            }
            RenderSidePanel(_selected.HasValue ? CellAt(_selected.Value.X, _selected.Value.Y) : null);
            _canvas.RefreshAll();
        }

        void CollectDescendants(Dictionary<string, object> root, HashSet<string> outSet)
        {
            string pos = Utils.GetValue<int>(root, "grid_x") + "," + Utils.GetValue<int>(root, "grid_y");
            foreach (Dictionary<string, object> c in _cells)
            {
                if (Utils.GetValue<string>(c, "parent_pos") == pos)
                {
                    string cp = Utils.GetValue<int>(c, "grid_x") + "," + Utils.GetValue<int>(c, "grid_y");
                    if (outSet.Add(cp)) CollectDescendants(c, outSet);
                }
            }
        }

        void RecalcGrid()
        {
            int maxX = _gridW, maxY = _gridH;
            foreach (Dictionary<string, object> c in _cells)
            {
                int x = Utils.GetValue<int>(c, "grid_x") + 4;
                int y = Utils.GetValue<int>(c, "grid_y") + 4;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            _canvas.GridW = _gridW = maxX;
            _canvas.GridH = _gridH = maxY;
        }

        // ----- side panel (port completo do _render_form Python) -----
        void RenderSidePanel(Dictionary<string, object> cell)
        {
            _sidePanel.Controls.Clear();
            if (cell == null) { RenderLegend(); return; }

            int gx = Utils.GetValue<int>(cell, "grid_x");
            int gy = Utils.GetValue<int>(cell, "grid_y");
            string pos = gx + "," + gy;

            TableLayoutPanel t = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Label header = new Label
            {
                Text = "Cell (" + gx + ", " + gy + ")",
                Font = new Font(Font, FontStyle.Bold), AutoSize = true,
                Margin = new Padding(0, 0, 0, 12),
            };
            t.Controls.Add(header, 0, 0); t.SetColumnSpan(header, 2);
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowCount = 1;

            // ----- Type -----
            ComboBox cmbType = NewSideCombo(
                new[] { "duel", "elite", "boss", "reward", "treasure", "lock" });
            cmbType.SelectedItem = Utils.GetValue<string>(cell, "type") ?? "duel";
            AddSideRow(t, "Type", cmbType);

            // ----- Level -----
            NumericUpDown numLevel = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 6,
                Value = Utils.GetValue<int>(cell, "level"),
                Margin = new Padding(0, 2, 0, 4) };
            AddSideRow(t, "Level (0=hardest)", numLevel);

            // ----- Deck file (combo filtrado por level + "(random from level)") -----
            ComboBox cmbDeck = new ComboBox { Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 0, 4) };
            string curDeck = Utils.GetValue<string>(cell, "deck_file");
            Action refreshDeckChoices = () =>
            {
                cmbDeck.Items.Clear();
                cmbDeck.Items.Add("(random from level)");
                int lvl = (int)numLevel.Value;
                List<string> pool;
                if (_deckPools != null && _deckPools.TryGetValue(lvl, out pool))
                    foreach (string f in pool) cmbDeck.Items.Add(f);
                if (!string.IsNullOrEmpty(curDeck) && cmbDeck.Items.Contains(curDeck))
                    cmbDeck.SelectedItem = curDeck;
                else cmbDeck.SelectedIndex = 0;
            };
            refreshDeckChoices();
            numLevel.ValueChanged += (s, e) =>
            {
                cell["level"] = (int)numLevel.Value;
                refreshDeckChoices();
                _canvas.RefreshAll();
            };
            AddSideRow(t, "Deck file", cmbDeck);
            AddSideHint(t, "(blank/random = pick from level pool at regen)");

            // ----- Parent cell (combo de H/V neighbours) -----
            ComboBox cmbParent = new ComboBox { Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 0, 4) };
            cmbParent.Items.Add("(root)");
            foreach (Dictionary<string, object> c in _cells)
            {
                int px = Utils.GetValue<int>(c, "grid_x");
                int py = Utils.GetValue<int>(c, "grid_y");
                if (px == gx && py == gy) continue;
                if (px == gx || py == gy) cmbParent.Items.Add(px + "," + py);
            }
            string curParent = Utils.GetValue<string>(cell, "parent_pos");
            if (!string.IsNullOrEmpty(curParent) && cmbParent.Items.Contains(curParent))
                cmbParent.SelectedItem = curParent;
            else cmbParent.SelectedIndex = 0;
            AddSideRow(t, "Parent cell", cmbParent);

            // ----- Is boss -----
            CheckBox chkBoss = new CheckBox
            {
                Text = "Mark this cell as the gate's boss (clear_chapter)",
                AutoSize = true,
                Checked = _bossPos == pos,
                Margin = new Padding(0, 8, 0, 4),
            };
            t.Controls.Add(chkBoss, 0, t.RowCount); t.SetColumnSpan(chkBoss, 2);
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;

            // ----- Gems override -----
            TextBox txtGems = new TextBox { Width = 100,
                Text = GetIntStringOrEmpty(cell, "gems_override"),
                Margin = new Padding(0, 2, 0, 4) };
            AddSideRow(t, "Gems override", txtGems);
            AddSideHint(t, "(blank = auto from level)");

            // ----- Card reward (1 id + qty pra V1) -----
            List<object> existingRewards = Utils.GetValue<List<object>>(cell, "card_rewards")
                ?? new List<object>();
            string cardIdInit = "", cardQtyInit = "1";
            if (existingRewards.Count > 0)
            {
                Dictionary<string, object> r = existingRewards[0] as Dictionary<string, object>;
                if (r != null)
                {
                    object rid; if (r.TryGetValue("id", out rid) && rid != null)
                        cardIdInit = rid.ToString();
                    object rqty; if (r.TryGetValue("qty", out rqty) && rqty != null)
                        cardQtyInit = rqty.ToString();
                }
            }
            FlowLayoutPanel rewardRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, AutoSize = true,
                Margin = new Padding(0, 2, 0, 4),
            };
            TextBox txtCardId = new TextBox { Width = 120, Text = cardIdInit };
            TextBox txtCardQty = new TextBox { Width = 40, Text = cardQtyInit };
            rewardRow.Controls.Add(txtCardId);
            rewardRow.Controls.Add(new Label { Text = " × ", AutoSize = true,
                Margin = new Padding(4, 6, 4, 4) });
            rewardRow.Controls.Add(txtCardQty);
            AddSideRow(t, "Card reward", rewardRow);
            AddSideHint(t, "(card id, blank = no manual reward; auto-grant for elite/boss usa boss_card do deck)");

            // ----- Force-special tristate (auto/yes/no) -----
            ComboBox cmbForce = NewSideCombo(new[] { "auto", "yes", "no" });
            bool? curForce = Utils.GetValue<bool>(cell, "force_special") ? (bool?)true : null;
            object fsObj;
            if (cell.TryGetValue("force_special", out fsObj))
            {
                if (fsObj is bool b) curForce = b;
                else if (fsObj == null) curForce = null;
            }
            cmbForce.SelectedItem = curForce.HasValue ? (curForce.Value ? "yes" : "no") : "auto";
            AddSideRow(t, "Special deck", cmbForce);
            AddSideHint(t, "(auto = chance da gate; yes = sempre special; no = sempre normal)");

            // ----- Chapter-level modifiers (botão abre ModifiersDialog) -----
            Button btnMod = new Button { Width = 200, Height = 26,
                Margin = new Padding(0, 4, 0, 4) };
            Dictionary<string, object> curMod =
                Utils.GetValue<Dictionary<string, object>>(cell, "modifiers");
            UpdateModButton(btnMod, curMod);
            btnMod.Click += (s, e) =>
            {
                using (ModifiersDialog dlg = new ModifiersDialog(
                    "cell (" + gx + "," + gy + ")", curMod))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    if (dlg.Result == null || dlg.Result.Count == 0)
                    {
                        cell["modifiers"] = null;
                        curMod = null;
                    }
                    else
                    {
                        cell["modifiers"] = dlg.Result;
                        curMod = dlg.Result;
                    }
                    UpdateModButton(btnMod, curMod);
                }
            };
            AddSideRow(t, "Chapter modifiers", btnMod);
            AddSideHint(t, "(per-cell — merged on top de gate-level + deck-embedded)");

            // ----- Apply button -----
            Button btnApply = new Button
            {
                Text = "Apply to cell", Width = 220, Height = 30,
                BackColor = SystemColors.Highlight,
                ForeColor = SystemColors.HighlightText, FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 12, 0, 4),
            };
            btnApply.Click += (s, e) => ApplyCellChanges(cell, gx, gy,
                cmbType, numLevel, cmbDeck, cmbParent, chkBoss,
                txtGems, txtCardId, txtCardQty, cmbForce);
            t.Controls.Add(btnApply, 0, t.RowCount); t.SetColumnSpan(btnApply, 2);
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;

            // ----- atalhos: Delete + MakeRoot -----
            FlowLayoutPanel shortcuts = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
            };
            Button btnMakeRoot = new Button { Text = "↤ Make player start", Width = 150, Height = 24 };
            btnMakeRoot.Click += (s, e) => MakeRoot(cell);
            Button btnDelete = new Button { Text = "✕ Delete (+ desc)", Width = 140, Height = 24,
                ForeColor = Theme.FgDanger };
            btnDelete.Click += (s, e) => DeleteCellWithDescendants(cell);
            shortcuts.Controls.Add(btnMakeRoot);
            shortcuts.Controls.Add(btnDelete);
            t.Controls.Add(shortcuts, 0, t.RowCount); t.SetColumnSpan(shortcuts, 2);
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize)); t.RowCount++;

            _sidePanel.Controls.Add(t);
        }

        void ApplyCellChanges(
            Dictionary<string, object> cell, int gx, int gy,
            ComboBox cmbType, NumericUpDown numLevel, ComboBox cmbDeck,
            ComboBox cmbParent, CheckBox chkBoss,
            TextBox txtGems, TextBox txtCardId, TextBox txtCardQty,
            ComboBox cmbForce)
        {
            cell["type"] = cmbType.SelectedItem as string ?? "duel";
            cell["level"] = (int)numLevel.Value;
            string dv = cmbDeck.SelectedItem as string ?? "";
            cell["deck_file"] = (dv == "" || dv == "(random from level)") ? null : (object)dv;
            string pv = cmbParent.SelectedItem as string ?? "(root)";
            cell["parent_pos"] = pv == "(root)" ? null : (object)pv;
            // Boss flag
            string pos = gx + "," + gy;
            if (chkBoss.Checked) { _bossPos = pos; cell["type"] = "boss"; }
            else if (_bossPos == pos) _bossPos = null;
            _canvas.BossPos = _bossPos;
            // Gems
            string gv = txtGems.Text.Trim();
            int gemsInt;
            cell["gems_override"] = (!string.IsNullOrEmpty(gv) && int.TryParse(gv, out gemsInt))
                ? (object)gemsInt : null;
            // Card reward
            string cidTxt = txtCardId.Text.Trim();
            int cidInt;
            if (!string.IsNullOrEmpty(cidTxt) && int.TryParse(cidTxt, out cidInt))
            {
                int qty;
                if (!int.TryParse(txtCardQty.Text.Trim(), out qty)) qty = 1;
                cell["card_rewards"] = new List<object>
                {
                    new Dictionary<string, object> { { "id", cidInt }, { "qty", qty } },
                };
            }
            else cell["card_rewards"] = new List<object>();
            // Force-special tristate
            string fs = cmbForce.SelectedItem as string ?? "auto";
            cell["force_special"] = fs == "yes" ? (object)true
                                   : fs == "no" ? (object)false : null;
            // modifiers já estão atualizados live pelo botão

            FlashStatus("Cell (" + gx + "," + gy + ") atualizado.");
            _canvas.RefreshAll();
        }

        ComboBox NewSideCombo(string[] items)
        {
            ComboBox c = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 0, 4) };
            foreach (string s in items) c.Items.Add(s);
            return c;
        }

        static string GetIntStringOrEmpty(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            return v.ToString();
        }

        void UpdateModButton(Button btn, Dictionary<string, object> mod)
        {
            bool hasMod = mod != null && mod.Count > 0;
            btn.Text = "Edit modifiers…" + (hasMod ? " ●" : "");
            btn.ForeColor = hasMod ? Theme.FgSuccess : SystemColors.ControlText;
        }

        static void AddSideRow(TableLayoutPanel t, string label, Control input)
        {
            int row = t.RowCount;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true,
                Margin = new Padding(0, 6, 12, 4) }, 0, row);
            t.Controls.Add(input, 1, row);
            t.RowCount = row + 1;
        }

        static void AddSideHint(TableLayoutPanel t, string text)
        {
            int row = t.RowCount;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label
            {
                Text = text, AutoSize = false, MaximumSize = new Size(260, 0),
                Width = 260, Height = 26, ForeColor = Theme.FgMuted,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8F),
                Margin = new Padding(0, 0, 0, 6),
            }, 1, row);
            t.RowCount = row + 1;
        }

        // ----- legend (quando nada selecionado) -----
        void RenderLegend()
        {
            Panel wrap = new Panel { Dock = DockStyle.Fill };

            Label hdr = new Label { Text = "No cell selected",
                Font = new Font(Font, FontStyle.Italic), ForeColor = Theme.FgMuted,
                AutoSize = true, Margin = new Padding(0, 0, 0, 10), Top = 0, Left = 0 };
            wrap.Controls.Add(hdr);

            int y = 30;
            y = AddLegendSection(wrap, "Chapter types", y);
            foreach (KeyValuePair<string, Color> kv in LayoutCanvas.TypeColors)
            {
                y = AddLegendChip(wrap, kv.Key, kv.Value, y);
            }
            y = AddLegendSection(wrap, "Borders", y + 8);
            y = AddBorderRow(wrap, "Boss",          Color.FromArgb(0xff, 0xe0, 0x40), y);
            y = AddBorderRow(wrap, "Player start",  Color.FromArgb(0x40, 0xc8, 0xff), y);
            y = AddBorderRow(wrap, "Selection",     Color.White,                       y);

            y = AddLegendSection(wrap, "Controls", y + 8);
            y = AddCtrl(wrap, "Click empty",  "add chapter (parent=selected H/V adj)", y);
            y = AddCtrl(wrap, "Click cell",   "select",                                y);
            y = AddCtrl(wrap, "Shift+Click",  "make clicked cell PARENT of selected", y);
            y = AddCtrl(wrap, "Ctrl+Click",   "mark as PLAYER START (root)",          y);
            y = AddCtrl(wrap, "Right-click",  "delete (+ descendants, with confirm)", y);

            _sidePanel.Controls.Add(wrap);
        }

        int AddLegendSection(Panel wrap, string title, int y)
        {
            Label lbl = new Label { Text = title, Font = new Font(Font, FontStyle.Bold),
                ForeColor = Theme.FgAccent, AutoSize = true, Top = y, Left = 0 };
            wrap.Controls.Add(lbl);
            return y + 22;
        }
        int AddLegendChip(Panel wrap, string name, Color color, int y)
        {
            Panel sw = new Panel { Width = 22, Height = 18, BackColor = color,
                Top = y, Left = 0 };
            Label l = new Label { Text = name, AutoSize = true, Top = y + 2, Left = 30 };
            wrap.Controls.Add(sw);
            wrap.Controls.Add(l);
            return y + 22;
        }
        int AddBorderRow(Panel wrap, string name, Color color, int y)
        {
            Panel outer = new Panel { Width = 22, Height = 18, BackColor = color,
                Top = y, Left = 0 };
            Panel inner = new Panel { Width = 16, Height = 12, Top = 3, Left = 3,
                BackColor = Color.FromArgb(0x4a, 0x7b, 0xd5) };
            outer.Controls.Add(inner);
            Label l = new Label { Text = name, AutoSize = true, Top = y + 2, Left = 30 };
            wrap.Controls.Add(outer);
            wrap.Controls.Add(l);
            return y + 22;
        }
        int AddCtrl(Panel wrap, string shortcut, string desc, int y)
        {
            Label sc = new Label { Text = shortcut, AutoSize = true,
                Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = Theme.FgAccent,
                Top = y, Left = 0, Width = 110 };
            Label dl = new Label { Text = desc, AutoSize = true, MaximumSize = new Size(220, 0),
                Top = y, Left = 120 };
            wrap.Controls.Add(sc);
            wrap.Controls.Add(dl);
            return y + 36;
        }

        // ----- regen procedural -----
        async void OnRegenProcedural(object sender, EventArgs e)
        {
            string fmt = Utils.GetValue<string>(_entry, "format") ?? "?";
            DialogResult ok = MessageBox.Show(
                "Descartar suas edits manuais e re-rolar o generator procedural " +
                "(" + fmt + ") com um seed NOVO?\n\nUse Edit > Generic > seed se quiser pinar.",
                "Regenerate Procedural", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;
            try
            {
                // Bumpa seed
                Random r = new Random();
                int newSeed = r.Next(1, int.MaxValue);
                Dictionary<string, object> gp =
                    Utils.GetValue<Dictionary<string, object>>(_entry, "generic_params");
                if (gp == null) { gp = new Dictionary<string, object>(); _entry["generic_params"] = gp; }
                gp["seed"] = newSeed;
                _entry["manual"] = false;
                _entry.Remove("manual_cells");
                _entry.Remove("manual_boss_pos");

                List<Dictionary<string, object>> snap = await Task.Run(() => GenerateSnapshot(_entry));
                _cells.Clear();
                if (snap != null) _cells.AddRange(snap);
                _canvas.Cells = _cells;
                // Boss
                Dictionary<string, object> boss = null;
                foreach (Dictionary<string, object> c in _cells)
                    if (Utils.GetValue<string>(c, "type") == "boss") { boss = c; break; }
                _bossPos = boss != null
                    ? Utils.GetValue<int>(boss, "grid_x") + "," + Utils.GetValue<int>(boss, "grid_y")
                    : null;
                _canvas.BossPos = _bossPos;
                _selected = null;
                _canvas.Selected = null;
                RecalcGrid();
                _canvas.RefreshAll();
                RenderSidePanel(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Falha: " + ex.Message, "Regenerate",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Roda o LayoutGenerator no formato da entry e converte os nodes
        // em cells (mesma shape que `manual_cells`). Cópia do que o CLI
        // export-manual faz, mas in-process.
        static List<Dictionary<string, object>> GenerateSnapshot(Dictionary<string, object> entry)
        {
            string format = Utils.GetValue<string>(entry, "format") ?? "hourglass";
            int gateId = Utils.GetValue<int>(entry, "gate_id");
            string duelType = Utils.GetValue<string>(entry, "duel_type") ?? "Normal";
            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(entry, "generic_params")
                ?? new Dictionary<string, object>();
            long seed = 0;
            object so;
            if (gp.TryGetValue("seed", out so) && so != null)
            { try { seed = Convert.ToInt64(so); } catch { } }
            if (seed == 0)
                seed = ((long)(gateId.GetHashCode() ^ duelType.GetHashCode() ^ format.GetHashCode())) & 0xFFFFFFFFL;
            Random rng = new Random((int)(seed & 0x7FFFFFFF));

            GenerationContext ctx = new GenerationContext
            {
                GateId = gateId, Format = format, Rng = rng,
                FormatParams = Utils.GetValue<Dictionary<string, object>>(entry, "format_params"),
                EliteCount    = GetIntOr(gp, "elite_count", 2),
                LockCount     = GetIntOr(gp, "lock_count", 0),
                RewardCount   = GetIntOr(gp, "reward_count", 3),
                TreasureCount = GetIntOr(gp, "treasure_count", 2),
                DuelLevel     = GetIntOr(gp, "duel_level", 3),
                EliteLevel    = GetIntOr(gp, "elite_level", 2),
                BossLevel     = GetIntOr(gp, "boss_level", 1),
                DifficultyMode = Utils.GetValue<string>(gp, "difficulty_curve") ?? "default",
            };
            LayoutGenerator.NodesResult nr = LayoutGenerator.GenerateNodes(ctx);
            if (nr == null) return null;
            List<Dictionary<string, object>> cells = new List<Dictionary<string, object>>();
            foreach (LayoutNode n in nr.Nodes)
            {
                cells.Add(new Dictionary<string, object>
                {
                    { "grid_x", n.X }, { "grid_y", n.Y },
                    { "parent_pos", n.Parent != null ? (n.Parent.X + "," + n.Parent.Y) : null },
                    { "type", n.ChapterType }, { "level", n.Level },
                    { "deck_file", null }, { "gems_override", null },
                    { "card_rewards", new List<object>() },
                });
            }
            return cells;
        }

        static int GetIntOr(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }

        // ----- save -----
        async void OnSave(object sender, EventArgs e)
        {
            if (_cells.Count == 0)
            {
                MessageBox.Show("Empty layout — adicione pelo menos 1 cell.",
                    "Empty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_bossPos))
            {
                MessageBox.Show("Marque 1 cell como boss (clear_chapter).",
                    "No boss", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            int gid = Utils.GetValue<int>(_entry, "gate_id");
            try
            {
                await Task.Run(() =>
                {
                    // Atualiza entry no GridGates.json com manual=true + cells.
                    string gridPath = Path.Combine(Program.DataDir, "GridGates.json");
                    Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                        File.ReadAllText(gridPath)) as Dictionary<string, object>;
                    List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
                    Dictionary<string, object> stored = null;
                    if (gates != null)
                    {
                        foreach (object o in gates)
                        {
                            Dictionary<string, object> g = o as Dictionary<string, object>;
                            if (g != null && Utils.GetValue<int>(g, "gate_id") == gid)
                            { stored = g; break; }
                        }
                    }
                    if (stored == null)
                        throw new InvalidOperationException("gate " + gid + " não está no registry");
                    stored["manual"] = true;
                    List<object> cellList = new List<object>();
                    foreach (Dictionary<string, object> c in _cells) cellList.Add(c);
                    stored["manual_cells"] = cellList;
                    stored["manual_boss_pos"] = _bossPos;
                    // Hash velho fica obsoleto — auto-bake detecta.
                    stored.Remove("last_bake_hash");
                    File.WriteAllText(gridPath, MiniJSON.Json.Serialize(doc));

                    // Bake imediato (reusa nosso pipeline padrão)
                    YgoMaster.ItemID.Load(Program.DataDir);
                    GridGateBaker.BakeMany(Program.DataDir,
                        new List<Dictionary<string, object>> { stored });
                });
                Saved = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save FAIL: " + ex.Message, "Save",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
