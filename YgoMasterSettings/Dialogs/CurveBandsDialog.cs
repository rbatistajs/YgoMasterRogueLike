using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace YgoMasterSettings.Dialogs
{
    // Editor de bandas de difficulty curve customizado. Usado pelo
    // GateEditDialog quando `difficulty_curve == "custom"`.
    //
    // Cada banda = [y_min, y_max, {level_str: weight, ...}]. UI mostra
    // como DataGridView com 9 colunas: y_min, y_max, l0..l6. Add/Remove
    // banda via botões.
    //
    // Result é a list serializável que vai pra `difficulty_curve_custom`
    // do generic_params da entry.
    class CurveBandsDialog : Form
    {
        public List<object> Result { get; private set; }

        DataGridView _grid;

        public CurveBandsDialog(List<object> initial)
        {
            Text = "Edit Difficulty Bands";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 420);
            MinimumSize = new Size(620, 320);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            LoadInitial(initial);
        }

        void BuildUi()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                AutoGenerateColumns = false,
            };
            _grid.Columns.Add(NewIntCol("y_min", "Y min", 60));
            _grid.Columns.Add(NewIntCol("y_max", "Y max", 60));
            for (int lvl = 0; lvl <= 6; lvl++)
                _grid.Columns.Add(NewFloatCol("l" + lvl, "L" + lvl, 70));

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6),
            };
            Button btnAdd = new Button { Text = "+ Add band", Width = 100, Height = 26 };
            // Sugere y_min/y_max baseado na última banda (Python:
            // next_min = last_max + 1).
            btnAdd.Click += (s, e) =>
            {
                int yMin = 0, yMax = 3;
                if (_grid.Rows.Count > 0)
                {
                    int lastMax = ToInt(_grid.Rows[_grid.Rows.Count - 1].Cells[1].Value);
                    yMin = lastMax + 1;
                    yMax = yMin + 3;
                }
                _grid.Rows.Add(yMin, yMax, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0);
            };
            Button btnRem = new Button { Text = "- Remove",   Width = 100, Height = 26 };
            btnRem.Click += (s, e) =>
            {
                if (_grid.CurrentRow != null && !_grid.CurrentRow.IsNewRow)
                    _grid.Rows.Remove(_grid.CurrentRow);
            };
            actions.Controls.Add(btnAdd);
            actions.Controls.Add(btnRem);

            // Separator + preset loader (Python: "Seed from preset").
            actions.Controls.Add(new Label { Text = "│", AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(8, 6, 8, 0) });
            actions.Controls.Add(new Label { Text = "Seed from preset:", AutoSize = true,
                Margin = new Padding(0, 6, 6, 0) });
            ComboBox cmbPreset = new ComboBox { Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 6, 2) };
            foreach (string p in new[] { "easy", "default", "brutal" }) cmbPreset.Items.Add(p);
            cmbPreset.SelectedIndex = 1;
            actions.Controls.Add(cmbPreset);
            Button btnLoad = new Button { Text = "Load", Width = 70, Height = 26 };
            btnLoad.Click += (s, e) => LoadPreset(cmbPreset.SelectedItem as string);
            actions.Controls.Add(btnLoad);

            FlowLayoutPanel bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
            };
            Button btnSave = new Button { Text = "Save",   Width = 90, Height = 28 };
            btnSave.Click += OnSave;
            Button btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28,
                DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(actions);
        }

        DataGridViewColumn NewIntCol(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = width,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter },
            };
        }
        DataGridViewColumn NewFloatCol(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = header, Width = width,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter,
                                     Format = "0.##" },
            };
        }

        void LoadInitial(List<object> initial)
        {
            if (initial == null) return;
            foreach (object o in initial)
            {
                List<object> band = o as List<object>;
                if (band == null || band.Count < 3) continue;
                int yMin = ToInt(band[0]);
                int yMax = ToInt(band[1]);
                Dictionary<string, object> w = band[2] as Dictionary<string, object>;
                double[] weights = new double[7];
                if (w != null)
                {
                    foreach (KeyValuePair<string, object> kv in w)
                    {
                        int lvl;
                        if (int.TryParse(kv.Key, out lvl) && lvl >= 0 && lvl <= 6)
                            weights[lvl] = ToDouble(kv.Value);
                    }
                }
                _grid.Rows.Add(yMin, yMax,
                    weights[0], weights[1], weights[2], weights[3],
                    weights[4], weights[5], weights[6]);
            }
        }

        // Preencher a grid com as bandas de um preset (Mirror do
        // LayoutPresets.DifficultyPresets em YgoMasterServer/Goat/Layout.
        // Hardcoded aqui pra evitar dependência cruzada com modules
        // backend só pra um helper de seed).
        void LoadPreset(string name)
        {
            _grid.Rows.Clear();
            if (string.IsNullOrEmpty(name)) return;
            switch (name)
            {
                case "easy":
                    AddRow(0,  4,  new[] { 0.7, 0.3, 0,   0,   0,   0,   0   });
                    AddRow(5,  9,  new[] { 0,   0.4, 0.4, 0.2, 0,   0,   0   });
                    AddRow(10, 99, new[] { 0,   0,   0,   0.4, 0.4, 0.2, 0   });
                    break;
                case "brutal":
                    AddRow(0, 2,  new[] { 0,   0.5, 0.5, 0,   0,   0,   0   });
                    AddRow(3, 6,  new[] { 0,   0,   0.3, 0.4, 0.3, 0,   0   });
                    AddRow(7, 99, new[] { 0,   0,   0,   0,   0.3, 0.4, 0.3 });
                    break;
                case "default":
                default:
                    AddRow(0,  3,  new[] { 0.6, 0.4, 0,   0,   0,   0,   0   });
                    AddRow(4,  7,  new[] { 0,   0.3, 0.4, 0.3, 0,   0,   0   });
                    AddRow(8,  11, new[] { 0,   0,   0,   0.3, 0.4, 0.3, 0   });
                    AddRow(12, 99, new[] { 0,   0,   0,   0,   0,   0.3, 0.7 });
                    break;
            }
        }

        void AddRow(int yMin, int yMax, double[] weights)
        {
            _grid.Rows.Add(yMin, yMax,
                weights[0], weights[1], weights[2], weights[3],
                weights[4], weights[5], weights[6]);
        }

        void OnSave(object sender, EventArgs e)
        {
            List<object> bands = new List<object>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;
                int yMin = ToInt(row.Cells[0].Value);
                int yMax = ToInt(row.Cells[1].Value);
                Dictionary<string, object> weights = new Dictionary<string, object>();
                bool any = false;
                for (int lvl = 0; lvl <= 6; lvl++)
                {
                    double w = ToDouble(row.Cells[2 + lvl].Value);
                    if (w > 0) { weights[lvl.ToString()] = w; any = true; }
                }
                if (!any) continue;   // skip banda totalmente zerada
                bands.Add(new List<object> { yMin, yMax, weights });
            }
            if (bands.Count == 0)
            {
                DialogResult dr = MessageBox.Show(
                    "Nenhuma banda com peso > 0 — salvar como custom mode vazio?\n" +
                    "(o gerador vai cair pro default curve quando rodar)",
                    "Custom curve vazio", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }
            Result = bands;
            DialogResult = DialogResult.OK;
            Close();
        }

        static int ToInt(object v)
        {
            if (v == null) return 0;
            int i;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
        }
        static double ToDouble(object v)
        {
            if (v == null) return 0;
            double d;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }
    }
}
