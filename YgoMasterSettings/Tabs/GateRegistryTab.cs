using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using YgoMaster;
using YgoMaster.Builder;
using YgoMasterSettings.Dialogs;
using YgoMasterSettings.Util;

namespace YgoMasterSettings.Tabs
{
    // Fase 1 — port da GateRegistryTab do game_settings.py.
    //
    // Layout estilo CRUD: ações per-item ficam em botões na própria
    // linha (Edit / Edit Layout / Regen / Delete). Ações globais
    // (Add Gate / Migrate Decks) no topo.
    //
    // Chama os modules C# in-process (sem subprocess pro YgoMaster.exe).
    class GateRegistryTab : UserControl
    {
        DataGridView _grid;
        TextBox _output;

        // Index da coluna "Actions" (botão que abre context menu).
        const int COL_ACTIONS = 6;

        public GateRegistryTab()
        {
            Dock = DockStyle.Fill;
            BuildUi();
            Refresh_();
        }

        void BuildUi()
        {
            // ----- Top: ações globais -----
            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4),
            };
            actions.Controls.Add(MakeButton("+ Add Gate", OnAdd, 110));

            // ----- Center: grid CRUD -----
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowTemplate = { Height = 32 },
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                AutoGenerateColumns = false,
            };
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "ID",     Width = 60,  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "Format", Width = 100 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "Duel",   Width = 80,  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "Mode",   Width = 80,  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "Name",   Width = 280, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
                { HeaderText = "Last bake hash", Width = 120,
                  DefaultCellStyle = { Font = Theme.FontMono, ForeColor = Theme.FgMuted } });
            // Coluna "Actions" — botão "⋯" abre menu contextual com
            // ações aplicáveis pra essa linha (Edit / Layout / Regen /
            // Delete). Opções não-aplicáveis pra runtime simplesmente
            // não aparecem.
            _grid.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = "Actions", Text = "⋯",
                UseColumnTextForButtonValue = true, Width = 70,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
            });
            _grid.CellClick     += OnGridCellClick;
            _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) OnEdit(EntryAt(e.RowIndex)); };

            // ----- Bottom: log -----
            _output = new TextBox
            {
                Dock = DockStyle.Bottom, Height = 140,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                ReadOnly = true, Font = Theme.FontMono,
                BackColor = Theme.BgLog, ForeColor = Theme.FgMuted,
            };

            Controls.Add(_grid);
            Controls.Add(_output);
            Controls.Add(actions);
        }

        Button MakeButton(string text, EventHandler onClick, int width)
        {
            Button b = new Button
            {
                Text = text,
                Width = width,
                Height = 28,
                Margin = new Padding(2),
            };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        // ----- data -----
        readonly List<Dictionary<string, object>> _entries = new List<Dictionary<string, object>>();

        void Refresh_()
        {
            _entries.Clear();
            _grid.Rows.Clear();
            string gridPath = Path.Combine(Program.DataDir, "GridGates.json");
            if (!File.Exists(gridPath))
            {
                _grid.Rows.Add("—", "—", "—", "—", "(GridGates.json não encontrado)", "", "");
                return;
            }
            foreach (Dictionary<string, object> e in GridGateBaker.ReadAllEntries(gridPath))
            {
                _entries.Add(e);
                int gid       = Utils.GetValue<int>(e, "gate_id");
                string format = Utils.GetValue<string>(e, "format") ?? "";
                string duel   = Utils.GetValue<string>(e, "duel_type") ?? "";
                bool runtime  = Utils.GetValue<bool>(e, "runtime");
                bool manual   = Utils.GetValue<bool>(e, "manual");
                string mode   = runtime ? "runtime" : (manual ? "manual" : "baked");
                string name   = Utils.GetValue<string>(e, "name") ?? "";
                string hash   = Utils.GetValue<string>(e, "last_bake_hash") ?? "";
                if (hash.Length > 8) hash = hash.Substring(0, 8);
                _grid.Rows.Add(gid, format, duel, mode, name, hash, "⋯");
            }
        }

        Dictionary<string, object> EntryAt(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _entries.Count) return null;
            return _entries[rowIndex];
        }

        void OnGridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != COL_ACTIONS) return;
            Dictionary<string, object> entry = EntryAt(e.RowIndex);
            if (entry == null) return;
            ShowActionsMenu(entry, e.RowIndex, e.ColumnIndex);
        }

        // Abre o context menu ancorado na cell de actions. Mostra só os
        // itens aplicáveis pro tipo de gate (Layout/Regen escondidos pra
        // runtime — não fazem sentido).
        void ShowActionsMenu(Dictionary<string, object> entry, int rowIndex, int colIndex)
        {
            bool runtime = Utils.GetValue<bool>(entry, "runtime");
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Edit",        null, (s, _) => OnEdit(entry));
            if (!runtime)
            {
                menu.Items.Add("Edit Layout", null, (s, _) => OnEditLayout(entry));
                menu.Items.Add("Regenerate",  null, (s, _) => OnRegen(entry));
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem del = new ToolStripMenuItem("Delete",
                null, (s, _) => OnDelete(entry));
            del.ForeColor = Theme.FgDanger;
            menu.Items.Add(del);

            // Posiciona logo abaixo do botão "⋯"
            Rectangle r = _grid.GetCellDisplayRectangle(colIndex, rowIndex, false);
            menu.Show(_grid, new Point(r.Left, r.Bottom));
        }

        // ----- log -----
        void Log(string line)
        {
            _output.AppendText(line + Environment.NewLine);
        }

        // ----- actions -----
        void OnAdd(object sender, EventArgs e)
        {
            using (GateEditDialog dlg = new GateEditDialog(null))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK || dlg.Result == null) return;
                UpsertAndBake(dlg.Result);
            }
        }

        void OnEdit(Dictionary<string, object> entry)
        {
            using (GateEditDialog dlg = new GateEditDialog(entry))
            {
                if (dlg.ShowDialog(FindForm()) != DialogResult.OK || dlg.Result == null) return;
                UpsertAndBake(dlg.Result);
            }
        }

        // Salva entry no GridGates.json (upsert por gate_id, preserva
        // campos não-form) e, se for non-runtime, dispara Bake. Runtime
        // só faz SyncIdsBlock (pra pegar name/blurb novos).
        async void UpsertAndBake(Dictionary<string, object> entry)
        {
            int gid = Utils.GetValue<int>(entry, "gate_id");
            bool runtime = Utils.GetValue<bool>(entry, "runtime");
            Log("[upsert] gate " + gid + " (" + (runtime ? "runtime" : "baked") + ") ...");
            try
            {
                await Task.Run(() => UpsertEntry(entry));
                if (runtime)
                {
                    await Task.Run(() => GridGateBaker.SyncIdsBlock(Program.DataDir));
                    Log("  → upsert + IDS sincronizado (runtime gate, sem bake).");
                }
                else
                {
                    EnsureItemIdLoaded();
                    GridGateBaker.Summary s = await Task.Run(() =>
                    {
                        List<GridGateBaker.Summary> rs = GridGateBaker.BakeMany(
                            Program.DataDir, new List<Dictionary<string, object>> { ReloadEntry(gid) });
                        return rs.Count > 0 ? rs[0] : null;
                    });
                    if (s != null)
                        Log("  → bake: " + s.ChaptersCount + " chapters, " + s.DuelFilesWritten +
                            " SoloDuels, boss=" + s.BossChapterId + ", seed=" + s.Seed);
                }
                Refresh_();
            }
            catch (Exception ex) { Log("[upsert FAIL] " + ex.Message); }
        }

        // Insert or replace por gate_id em GridGates.json. Preserva
        // campos fora do form (manual_cells, chapter_overrides,
        // last_bake_hash) quando o gate já existe.
        //
        // Write atomic + backup automático (via JsonFileWriter) — previne
        // perda de outros gates se algo der errado a meio do save.
        void UpsertEntry(Dictionary<string, object> entry)
        {
            int gid = Utils.GetValue<int>(entry, "gate_id");
            string gridPath = Path.Combine(Program.DataDir, "GridGates.json");
            Dictionary<string, object> doc = File.Exists(gridPath)
                ? (MiniJSON.Json.DeserializeStripped(File.ReadAllText(gridPath)) as Dictionary<string, object>)
                : new Dictionary<string, object>();
            if (doc == null) doc = new Dictionary<string, object>();
            List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
            if (gates == null) { gates = new List<object>(); doc["gates"] = gates; }

            int existingCount = gates.Count;
            int existingIdx = -1;
            for (int i = 0; i < gates.Count; i++)
            {
                Dictionary<string, object> en = gates[i] as Dictionary<string, object>;
                if (en != null && Utils.GetValue<int>(en, "gate_id") == gid)
                { existingIdx = i; break; }
            }
            if (existingIdx < 0)
            {
                // Nova: defaults pros campos auxiliares.
                if (!entry.ContainsKey("manual"))         entry["manual"] = false;
                if (!entry.ContainsKey("manual_cells"))   entry["manual_cells"] = new List<object>();
                if (!entry.ContainsKey("manual_boss_pos")) entry["manual_boss_pos"] = null;
                if (!entry.ContainsKey("chapter_overrides")) entry["chapter_overrides"] = new Dictionary<string, object>();
                gates.Add(entry);
            }
            else
            {
                // Merge: form fields overwrite, resto preservado.
                Dictionary<string, object> old = (Dictionary<string, object>)gates[existingIdx];
                foreach (KeyValuePair<string, object> kv in entry) old[kv.Key] = kv.Value;
                // last_bake_hash vai sair obsoleto automaticamente (hash da entry muda).
            }
            // Sort por gate_id pra manter o arquivo legível.
            gates.Sort((a, b) =>
                Utils.GetValue<int>((Dictionary<string, object>)a, "gate_id")
                .CompareTo(Utils.GetValue<int>((Dictionary<string, object>)b, "gate_id")));

            // Safety: validar que não estamos PERDENDO entries por bug
            // de merge. Expected = existingCount + (0 se update, 1 se add).
            int expected = existingCount + (existingIdx < 0 ? 1 : 0);
            if (gates.Count < expected)
            {
                throw new InvalidOperationException(
                    "Abort: gate count caiu de " + existingCount + " pra " + gates.Count +
                    " (esperado " + expected + "). Save bloqueado pra preservar dados.");
            }

            // Backup + atomic write
            JsonFileWriter.SaveAtomic(gridPath, MiniJSON.Json.Serialize(doc), "GridGates");
        }

        // Relê a entry recém-salva (BakeMany precisa da versão com
        // campos auxiliares mesclados, não a versão crua do dialog).
        Dictionary<string, object> ReloadEntry(int gid)
        {
            string gridPath = Path.Combine(Program.DataDir, "GridGates.json");
            foreach (Dictionary<string, object> e in GridGateBaker.ReadAllEntries(gridPath))
                if (Utils.GetValue<int>(e, "gate_id") == gid) return e;
            throw new InvalidOperationException("gate " + gid + " sumiu do registry após upsert");
        }

        void OnEditLayout(Dictionary<string, object> entry)
        {
            int gid = Utils.GetValue<int>(entry, "gate_id");
            // O dialog mexe direto no GridGates.json + dispara bake; só
            // refrescamos a tabela depois.
            using (LayoutEditorDialog dlg = new LayoutEditorDialog(entry))
            {
                dlg.ShowDialog(FindForm());
                if (dlg.Saved)
                {
                    Log("[layout-edit] gate " + gid + " salvo + bakeado.");
                    Refresh_();
                }
            }
        }

        async void OnRegen(Dictionary<string, object> entry)
        {
            int gid = Utils.GetValue<int>(entry, "gate_id");
            Log("[regen] gate " + gid + " ...");
            try
            {
                EnsureItemIdLoaded();
                GridGateBaker.Summary s = await Task.Run(() =>
                {
                    List<GridGateBaker.Summary> results =
                        GridGateBaker.BakeMany(Program.DataDir, new List<Dictionary<string, object>> { entry });
                    return results.Count > 0 ? results[0] : null;
                });
                if (s != null)
                {
                    Log("  → " + s.ChaptersCount + " chapters, " + s.DuelFilesWritten +
                        " SoloDuels, boss=" + s.BossChapterId + " (" + (s.BossDeck ?? "?") + "), seed=" + s.Seed);
                }
                Refresh_();
            }
            catch (Exception ex) { Log("[regen FAIL] " + ex.Message); }
        }

        async void OnDelete(Dictionary<string, object> entry)
        {
            int gid = Utils.GetValue<int>(entry, "gate_id");
            string name = Utils.GetValue<string>(entry, "name") ?? "";
            DialogResult confirm = MessageBox.Show(
                "Deletar gate " + gid + (string.IsNullOrEmpty(name) ? "" : " (\"" + name + "\")") +
                "?\nIsso remove registry + Solo.json + SoloDuels.",
                "Confirma delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            Log("[delete] gate " + gid + " ...");
            try
            {
                await Task.Run(() =>
                {
                    // 1) Remove da registry
                    string gridPath = Path.Combine(Program.DataDir, "GridGates.json");
                    Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                        File.ReadAllText(gridPath)) as Dictionary<string, object>;
                    if (doc != null)
                    {
                        List<object> gates = Utils.GetValue<List<object>>(doc, "gates");
                        if (gates != null)
                        {
                            for (int i = gates.Count - 1; i >= 0; i--)
                            {
                                Dictionary<string, object> en = gates[i] as Dictionary<string, object>;
                                if (en != null && Utils.GetValue<int>(en, "gate_id") == gid)
                                    gates.RemoveAt(i);
                            }
                            // Backup + atomic write — recuperável em
                            // _bkp/GridGates.*.bak.json se delete errado.
                            JsonFileWriter.SaveAtomic(gridPath, MiniJSON.Json.Serialize(doc), "GridGates");
                        }
                    }
                    // 2) Solo.json + SoloDuels
                    SoloJsonPatcher.DeleteGate(Program.DataDir, gid);
                    // 3) Sincroniza IDS_SOLO sem essa gate
                    GridGateBaker.SyncIdsBlock(Program.DataDir);
                });
                Log("  → deletado (registry + Solo.json + SoloDuels + IDS sincronizado).");
                Refresh_();
            }
            catch (Exception ex) { Log("[delete FAIL] " + ex.Message); }
        }

        // ----- lazy ItemID load -----
        static bool _itemIdLoaded;
        static void EnsureItemIdLoaded()
        {
            if (_itemIdLoaded) return;
            ItemID.Load(Program.DataDir);
            _itemIdLoaded = true;
        }
    }
}
