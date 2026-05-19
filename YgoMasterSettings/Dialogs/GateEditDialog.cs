using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using YgoMaster;

namespace YgoMasterSettings.Dialogs
{
    // Form modal pra Add/Edit de uma entry no GridGates.json.
    //
    // Organização interna em TabControl:
    //   - "Gate":          ID, Duel type, Format, Runtime, Cosmetic mode,
    //                      Name, Blurb
    //   - "Format Params": campos específicos do formato selecionado
    //                      (re-renderiza ao trocar de format na tab Gate)
    //   - "Generic":       difficulty curve, levels, counts, seed
    //
    // Result fica em `Result` quando o user clica Save. null quando Cancel.
    //
    // Schema do form é hardcoded em GridGateSchema (mirror do builder
    // Python — atualizar lá quando adicionar formato novo no C#).
    class GateEditDialog : Form
    {
        public Dictionary<string, object> Result { get; private set; }

        readonly bool _isEdit;
        readonly Dictionary<string, object> _initial;

        // Controls da tab Gate
        TextBox _txtGateId, _txtName, _txtBlurb;
        ComboBox _cmbDuelType, _cmbFormat, _cmbCosmeticMode;
        CheckBox _chkRuntime;

        // Tab Format Params — re-renderiza quando format muda
        TabPage _pageFormat;
        Panel _formatParamsPanel;
        readonly Dictionary<string, Control> _formatInputs = new Dictionary<string, Control>();
        readonly Dictionary<string, Control> _genericInputs = new Dictionary<string, Control>();

        // Tab Generic — sub-dialogs state
        Button _btnEditBands;
        Label _lblBandsCount;
        List<object> _curveCustom = new List<object>();

        // Tab Modifiers — só pros chapter types que duelam.
        // UI inline: button group [boss][elite][duel] no topo seleciona
        // qual chapter type tá editando; embaixo, ModifierEditor completo
        // (P1/P2 tabs + sections). Troca de type salva o atual e carrega
        // o novo, sem fechar/reabrir dialog.
        static readonly string[] ModifierChapterTypes = { "boss", "elite", "duel" };
        // Estado in-memory dos modifier dicts (chapter_type → modifier).
        Dictionary<string, Dictionary<string, object>> _modifierDefaults =
            new Dictionary<string, Dictionary<string, object>>();
        // Chapter type atualmente em edição (boss/elite/duel).
        string _currentModType = "boss";
        // Botões do button group — refletem qual está selecionado + status.
        readonly Dictionary<string, Button> _modTypeButtons = new Dictionary<string, Button>();
        // O editor inline (1 instância, reusada quando troca de type).
        ModifierEditor _inlineModEditor;

        // Tab Rewards — inline. 4 drop chances + 13 category weights.
        NumericUpDown _bossDropChance, _eliteDropChance, _rewardDropChance, _duelDropChance;
        static readonly string[] RewardCategoryNames = {
            "CONSUME", "AVATAR", "ICON", "PROFILE_TAG", "ICON_FRAME",
            "PROTECTOR", "DECK_CASE", "FIELD", "FIELD_OBJ", "AVATAR_HOME",
            "STRUCTURE", "WALLPAPER", "PACK_TICKET",
        };
        readonly Dictionary<string, NumericUpDown> _rewardWeightInputs =
            new Dictionary<string, NumericUpDown>();

        // special_deck_chance inline (chapter type → "always"/"never"/0..1).
        // Só os types que duelam — treasure/reward/lock são passive (npc_id=0).
        static readonly string[] SpecialChapterTypes = { "boss", "elite", "duel" };
        readonly Dictionary<string, ComboBox> _specialChanceInputs =
            new Dictionary<string, ComboBox>();

        public GateEditDialog(Dictionary<string, object> initial = null)
        {
            _isEdit = initial != null;
            _initial = initial ?? new Dictionary<string, object>();

            Text = _isEdit ? ("Edit Gate " + Utils.GetValue<int>(_initial, "gate_id")) : "Add Gate";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 580);
            MinimumSize = new Size(640, 500);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            // Força Font do sistema pra herdar consistente nos child
            // controls — sem isso, em alguns ambientes WinForms herda
            // Microsoft Sans Serif 8.25 que parece menor que o resto.
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            LoadInitial();
        }

        // ----- UI -----
        void BuildUi()
        {
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildGateTab());
            _pageFormat = BuildFormatTab();
            tabs.TabPages.Add(_pageFormat);
            tabs.TabPages.Add(BuildGenericTab());
            tabs.TabPages.Add(BuildModifiersTab());
            tabs.TabPages.Add(BuildRewardsTab());
            tabs.TabPages.Add(BuildSpecialDecksTab());

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8, 8, 8, 8),
            };
            Button btnSave = new Button
            {
                Text = _isEdit ? "Save" : "Add",
                Width = 90, Height = 28, DialogResult = DialogResult.None,
            };
            btnSave.Click += OnSave;
            Button btnCancel = new Button
            {
                Text = "Cancel",
                Width = 90, Height = 28, DialogResult = DialogResult.Cancel,
            };
            buttons.Controls.Add(btnSave);
            buttons.Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(tabs);
            Controls.Add(buttons);
        }

        TabPage BuildGateTab()
        {
            TabPage page = new TabPage("Gate") { Padding = new Padding(12) };
            TableLayoutPanel t = NewForm();

            _txtGateId = new TextBox { Width = 160 };
            if (_isEdit) _txtGateId.ReadOnly = true;
            AddRow(t, "Gate ID", _txtGateId, "Any free int (1-9 reservados pela Konami)");

            _cmbDuelType = NewCombo(GridGateSchema.DuelTypes);
            _cmbDuelType.Width = 220;
            AddRow(t, "Duel type", _cmbDuelType);

            // Format combo — popula com labels visíveis, valor é o key
            _cmbFormat = new ComboBox { Width = 460, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (GridGateSchema.FormatMeta f in GridGateSchema.Formats)
                _cmbFormat.Items.Add(new ComboItem(f.Key, f.Label));
            _cmbFormat.SelectedIndexChanged += (s, e) => RenderFormatParams();
            AddRow(t, "Format", _cmbFormat);

            _cmbCosmeticMode = NewCombo(GridGateSchema.CosmeticModes);
            _cmbCosmeticMode.Width = 220;
            AddRow(t, "Cosmetic mode", _cmbCosmeticMode,
                "vanilla = baseline fixo; random = sorteia mat/sleeve/icon/etc por duelo");

            _chkRuntime = new CheckBox
            {
                Text = "Runtime gate (server regenera o layout per-player a cada regen)",
                AutoSize = true,
            };
            AddRow(t, "", _chkRuntime);

            _txtName = new TextBox { Width = 520 };
            AddRow(t, "Name", _txtName);

            _txtBlurb = new TextBox { Width = 520 };
            AddRow(t, "Blurb", _txtBlurb);

            page.Controls.Add(t);
            return page;
        }

        TabPage BuildFormatTab()
        {
            TabPage page = new TabPage("Format Params") { Padding = new Padding(12) };
            _formatParamsPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            page.Controls.Add(_formatParamsPanel);
            return page;
        }

        TabPage BuildGenericTab()
        {
            TabPage page = new TabPage("Generic") { Padding = new Padding(12) };
            Panel panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            TableLayoutPanel t = NewForm();
            foreach (GridGateSchema.Field f in GridGateSchema.GenericFields)
            {
                Control input = MakeFieldInput(f);
                _genericInputs[f.Key] = input;
                // difficulty_curve ganha um botão "Edit bands…" no
                // campo (terceira coluna) — só relevante em "custom".
                if (f.Key == "difficulty_curve")
                {
                    _lblBandsCount = new Label { AutoSize = true,
                        ForeColor = Theme.FgMuted, Margin = new Padding(0, 6, 12, 4) };
                    _btnEditBands = new Button { Text = "Edit bands…", Width = 110, Height = 24,
                        Margin = new Padding(0, 2, 0, 2) };
                    _btnEditBands.Click += OnEditBands;
                    // Compose row manualmente pra incluir [combo] [count] [btn]
                    int row = t.RowCount;
                    t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    t.Controls.Add(new Label { Text = f.Label, AutoSize = true,
                        Margin = new Padding(0, 6, 12, 4) }, 0, row);
                    input.Margin = new Padding(0, 4, 8, 4);
                    t.Controls.Add(input, 1, row);
                    FlowLayoutPanel extras = new FlowLayoutPanel
                    {
                        FlowDirection = FlowDirection.LeftToRight,
                        AutoSize = true, Margin = new Padding(0),
                    };
                    extras.Controls.Add(_btnEditBands);
                    extras.Controls.Add(_lblBandsCount);
                    t.Controls.Add(extras, 2, row);
                    t.RowCount = row + 1;
                    if (input is ComboBox cb)
                        cb.SelectedIndexChanged += (s, e) => RefreshBandsButtonState();
                    continue;
                }
                AddRow(t, f.Label, input);
            }
            panel.Controls.Add(t);
            page.Controls.Add(panel);
            return page;
        }

        // ----- Modifiers tab (inline editor + button group) -----
        TabPage BuildModifiersTab()
        {
            TabPage page = new TabPage("Modifiers") { Padding = new Padding(8) };
            Label intro = new Label
            {
                Text = "Modifier defaults per chapter type. Selecione boss/elite/duel " +
                       "pra editar — troca preserva os dados. Aplicado a TODOS os duels " +
                       "daquele tipo. Treasure/reward/lock são passive (não duelam).",
                AutoSize = false, Dock = DockStyle.Top, Height = 36,
                ForeColor = Theme.FgMuted,
                Padding = new Padding(4, 4, 4, 4),
            };

            // Button group [boss][elite][duel] no topo
            FlowLayoutPanel typeBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(4),
            };
            foreach (string ct in ModifierChapterTypes)
            {
                Button btn = new Button
                {
                    Text = ct, Width = 110, Height = 30,
                    Margin = new Padding(2, 2, 4, 2),
                    FlatStyle = FlatStyle.Flat,
                };
                string captured = ct;
                btn.Click += (s, e) => SwitchModType(captured);
                _modTypeButtons[ct] = btn;
                typeBar.Controls.Add(btn);
            }

            // Editor inline (1 instância só — reusada via Load/Save quando troca de tipo)
            _inlineModEditor = new ModifierEditor { Dock = DockStyle.Fill };

            page.Controls.Add(_inlineModEditor);
            page.Controls.Add(typeBar);
            page.Controls.Add(intro);
            return page;
        }

        // Salva o spec atual no _modifierDefaults, troca o button group
        // visual, carrega o spec do novo type no editor.
        void SwitchModType(string newType)
        {
            if (_inlineModEditor == null) return;
            // 1) Salva o que tá visível no editor pro tipo atual
            FlushCurrentModEditor();
            // 2) Atualiza estado
            _currentModType = newType;
            // 3) Carrega novo
            Dictionary<string, object> m;
            _modifierDefaults.TryGetValue(newType, out m);
            _inlineModEditor.LoadFrom(m);
            // 4) Estilo dos botões reflete seleção
            RefreshModTypeButtons();
        }

        // Lê o estado do editor inline e atualiza _modifierDefaults pro
        // chapter type que estava em edição. Chamado em SwitchModType e
        // no OnSave do dialog principal.
        void FlushCurrentModEditor()
        {
            if (_inlineModEditor == null) return;
            Dictionary<string, object> saved = _inlineModEditor.Save();
            if (saved == null || saved.Count == 0)
                _modifierDefaults.Remove(_currentModType);
            else
                _modifierDefaults[_currentModType] = saved;
        }

        // Realça o botão do tipo selecionado + marca os outros com ●
        // se tiverem dados configurados.
        void RefreshModTypeButtons()
        {
            foreach (KeyValuePair<string, Button> kv in _modTypeButtons)
            {
                bool isSelected = kv.Key == _currentModType;
                bool hasData = _modifierDefaults.ContainsKey(kv.Key);
                Button b = kv.Value;
                string suffix = hasData ? " ●" : "";
                b.Text = kv.Key + suffix;
                if (isSelected)
                {
                    b.BackColor = SystemColors.Highlight;
                    b.ForeColor = SystemColors.HighlightText;
                    b.Font = new Font(Font, FontStyle.Bold);
                }
                else
                {
                    b.BackColor = SystemColors.Control;
                    b.ForeColor = hasData ? Theme.FgSuccess : SystemColors.ControlText;
                    b.Font = Font;
                }
            }
        }

        // ----- Rewards tab (inline) -----
        TabPage BuildRewardsTab()
        {
            TabPage page = new TabPage("Rewards") { Padding = new Padding(12) };
            Label intro = new Label
            {
                Text = "Drop chances por chapter type (0..1) + pesos relativos das " +
                       "categorias de item. Boss=1.0 sempre dropa; categoria com peso 0 não cai.",
                AutoSize = false, Dock = DockStyle.Top, Height = 46,
                ForeColor = Theme.FgMuted,
            };

            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            // Drop chances no topo
            GroupBox gbDrop = new GroupBox
            {
                Text = "Drop chances (0..1)",
                Dock = DockStyle.Top, Height = 140,
                Padding = new Padding(10, 16, 10, 10),
            };
            TableLayoutPanel tDrop = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 8, AutoSize = true,
            };
            for (int i = 0; i < 8; i++) tDrop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _bossDropChance   = MakeFloat01(); AddDropPair(tDrop, "Boss",   _bossDropChance,   0);
            _eliteDropChance  = MakeFloat01(); AddDropPair(tDrop, "Elite",  _eliteDropChance,  2);
            _rewardDropChance = MakeFloat01(); AddDropPair(tDrop, "Reward", _rewardDropChance, 4);
            _duelDropChance   = MakeFloat01(); AddDropPair(tDrop, "Duel",   _duelDropChance,   6);
            gbDrop.Controls.Add(tDrop);

            // Category weights
            GroupBox gbCats = new GroupBox
            {
                Text = "Category weights (0 = excluído; >0 = peso relativo)",
                Dock = DockStyle.Top, Height = 220,
                Padding = new Padding(10, 16, 10, 10),
            };
            TableLayoutPanel tCats = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true,
            };
            for (int i = 0; i < 4; i++) tCats.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int rrow = 0, rcol = 0;
            foreach (string cat in RewardCategoryNames)
            {
                Label lbl = new Label { Text = cat, AutoSize = true,
                    Margin = new Padding(0, 6, 8, 4) };
                NumericUpDown n = MakeFloat01();
                tCats.Controls.Add(lbl, rcol, rrow);
                tCats.Controls.Add(n,   rcol + 1, rrow);
                _rewardWeightInputs[cat] = n;
                rcol += 2;
                if (rcol >= 4) { rcol = 0; rrow++; }
            }
            gbCats.Controls.Add(tCats);

            body.Controls.Add(gbCats);  // bottom-most → primeiro
            body.Controls.Add(gbDrop);

            page.Controls.Add(body);
            page.Controls.Add(intro);
            return page;
        }

        // Pra reduzir verbosidade: pair de [label] + [input] em 2 colunas
        // adjacentes do TableLayoutPanel.
        void AddDropPair(TableLayoutPanel t, string label, NumericUpDown input, int col)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true,
                Margin = new Padding(0, 6, 8, 4), Font = new Font(Font, FontStyle.Bold) }, col, 0);
            input.Margin = new Padding(0, 4, 16, 4);
            t.Controls.Add(input, col + 1, 0);
        }

        NumericUpDown MakeFloat01()
        {
            return new NumericUpDown
            {
                Width = 80,
                Minimum = 0M, Maximum = 1.0M,
                DecimalPlaces = 2, Increment = 0.05M,
            };
        }

        // ----- Special decks tab -----
        // special_deck_chance é nested no generic_params. UI é simples:
        // 1 combo por chapter type com presets ("never"/"always"/0.25/...).
        TabPage BuildSpecialDecksTab()
        {
            TabPage page = new TabPage("Special Decks") { Padding = new Padding(12) };
            Label intro = new Label
            {
                Text = "Chance de cada chapter type usar deck de decks/<type>/special_<level>/ " +
                       "(decks com modifiers embedded). 'never' nunca usa; 'always' sempre; " +
                       "float (0.25, 0.5…) = chance probabilística.",
                AutoSize = false, Dock = DockStyle.Top, Height = 60,
                ForeColor = Theme.FgMuted,
            };
            TableLayoutPanel t = NewForm();
            string[] presets = { "never", "0.10", "0.25", "0.50", "0.75", "always" };
            foreach (string ct in SpecialChapterTypes)
            {
                ComboBox cb = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
                foreach (string p in presets) cb.Items.Add(p);
                cb.Text = "never";
                _specialChanceInputs[ct] = cb;
                AddRow(t, ct, cb);
            }
            Panel wrap = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            wrap.Controls.Add(t);
            page.Controls.Add(wrap);
            page.Controls.Add(intro);
            return page;
        }

        void RefreshBandsButtonState()
        {
            ComboBox cb = _genericInputs["difficulty_curve"] as ComboBox;
            string mode = cb != null ? cb.SelectedItem as string ?? "" : "";
            bool customMode = mode == "custom";
            bool basicMode  = mode == "basic";

            // Edit bands… só visível em custom mode (Python: grid_remove)
            _btnEditBands.Visible = customMode;
            _lblBandsCount.Visible = customMode;
            if (customMode)
                _lblBandsCount.Text = "(" + _curveCustom.Count + " band(s))";

            // duel_level só relevante em basic mode (Python: grid/grid_remove
            // do row inteiro inclusive a Label).
            Control duelLevel;
            if (_genericInputs.TryGetValue("duel_level", out duelLevel))
            {
                duelLevel.Visible = basicMode;
                // Esconde a Label do row também — vamos buscar pela posição
                // no TableLayoutPanel (mesma row da NumericUpDown).
                TableLayoutPanel parentTable = duelLevel.Parent as TableLayoutPanel;
                if (parentTable != null)
                {
                    TableLayoutPanelCellPosition p = parentTable.GetCellPosition(duelLevel);
                    foreach (Control c in parentTable.Controls)
                    {
                        TableLayoutPanelCellPosition cp = parentTable.GetCellPosition(c);
                        if (cp.Row == p.Row && c != duelLevel) c.Visible = basicMode;
                    }
                }
            }
        }

        void OnEditBands(object sender, EventArgs e)
        {
            using (CurveBandsDialog dlg = new CurveBandsDialog(_curveCustom))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                _curveCustom = dlg.Result;
                RefreshBandsButtonState();
            }
        }

        // (Inline edit — sem sub-dialogs pra Modifiers/Rewards.)

        // Re-renderiza os fields do formato atualmente selecionado.
        void RenderFormatParams()
        {
            _formatParamsPanel.Controls.Clear();
            _formatInputs.Clear();
            ComboItem sel = _cmbFormat.SelectedItem as ComboItem;
            if (sel == null) return;
            GridGateSchema.FormatMeta fmt = GridGateSchema.FindFormat(sel.Key);

            TableLayoutPanel t = NewForm();
            foreach (GridGateSchema.Field f in fmt.Fields)
            {
                Control input = MakeFieldInput(f);
                _formatInputs[f.Key] = input;
                // Preset agnóstico do schema: se vir choices, usa.
                AddRow(t, f.Label, input);
            }
            _formatParamsPanel.Controls.Add(t);

            // Re-aplica os valores iniciais (caso seja edit ou troca de format).
            Dictionary<string, object> fmtParams =
                Utils.GetValue<Dictionary<string, object>>(_initial, "format_params");
            ApplyValues(_formatInputs, fmt.Fields, fmtParams ?? fmt.Defaults);
        }

        // ----- input factory -----
        Control MakeFieldInput(GridGateSchema.Field f)
        {
            switch (f.Kind)
            {
                case GridGateSchema.FieldKind.Preset:
                    ComboBox cmb = NewCombo(f.Choices);
                    return cmb;
                case GridGateSchema.FieldKind.Int:
                    NumericUpDown n = new NumericUpDown
                    {
                        Width = 100,
                        Minimum = (decimal)f.Min, Maximum = (decimal)f.Max,
                        DecimalPlaces = 0, Increment = 1,
                    };
                    return n;
                case GridGateSchema.FieldKind.IntOptional:
                    // Pra int opcional usamos TextBox (vazio = null).
                    return new TextBox { Width = 100 };
                case GridGateSchema.FieldKind.Float:
                    NumericUpDown nf = new NumericUpDown
                    {
                        Width = 100,
                        Minimum = (decimal)f.Min, Maximum = (decimal)f.Max,
                        DecimalPlaces = 2, Increment = 0.05M,
                    };
                    return nf;
                default:
                    return new TextBox { Width = 200 };
            }
        }

        ComboBox NewCombo(string[] items)
        {
            ComboBox c = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string s in items) c.Items.Add(s);
            return c;
        }

        TableLayoutPanel NewForm()
        {
            return new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0),
                ColumnStyles = {
                    new ColumnStyle(SizeType.AutoSize),
                    new ColumnStyle(SizeType.AutoSize),
                    new ColumnStyle(SizeType.AutoSize),
                },
            };
        }

        void AddRow(TableLayoutPanel t, string label, Control input, string hint = null)
        {
            int row = t.RowCount;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label lbl = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 12, 4) };
            t.Controls.Add(lbl, 0, row);
            input.Margin = new Padding(0, 4, 0, 4);
            t.Controls.Add(input, 1, row);
            if (hint != null)
            {
                Label h = new Label { Text = hint, AutoSize = true,
                    ForeColor = Theme.FgMuted, Margin = new Padding(12, 6, 0, 4) };
                t.Controls.Add(h, 2, row);
            }
            t.RowCount = row + 1;
        }

        // ----- load / save -----
        void LoadInitial()
        {
            _txtGateId.Text = _isEdit
                ? Utils.GetValue<int>(_initial, "gate_id").ToString(CultureInfo.InvariantCulture)
                : "";
            SetCombo(_cmbDuelType, Utils.GetValue<string>(_initial, "duel_type") ?? "Normal");
            string fmt = Utils.GetValue<string>(_initial, "format") ?? "hourglass";
            SetComboItem(_cmbFormat, fmt);
            SetCombo(_cmbCosmeticMode, Utils.GetValue<string>(_initial, "cosmetic_mode") ?? "vanilla");
            _chkRuntime.Checked = Utils.GetValue<bool>(_initial, "runtime");
            _txtName.Text = Utils.GetValue<string>(_initial, "name") ?? "";
            _txtBlurb.Text = Utils.GetValue<string>(_initial, "blurb") ?? "";

            RenderFormatParams();

            // Generic params
            Dictionary<string, object> gp =
                Utils.GetValue<Dictionary<string, object>>(_initial, "generic_params")
                ?? GridGateSchema.GenericDefaults;
            ApplyValues(_genericInputs, GridGateSchema.GenericFields, gp);

            // Custom curve bands (vivem fora do schema simples)
            object cc;
            if (gp != null && gp.TryGetValue("difficulty_curve_custom", out cc) && cc is List<object>)
                _curveCustom = new List<object>((List<object>)cc);
            RefreshBandsButtonState();

            // Modifier defaults — guarda o dict cru por chapter type
            // em _modifierDefaults. Edição é inline via _inlineModEditor
            // + button group; default visível é "boss".
            object md;
            if (gp != null && gp.TryGetValue("modifier_defaults", out md) && md is Dictionary<string, object>)
            {
                Dictionary<string, object> mds = (Dictionary<string, object>)md;
                foreach (string ct in ModifierChapterTypes)
                {
                    Dictionary<string, object> m = Utils.GetValue<Dictionary<string, object>>(mds, ct);
                    if (m != null && m.Count > 0) _modifierDefaults[ct] = m;
                }
            }
            // Carrega o tipo selecionado por default no editor inline +
            // atualiza visual dos botões (selected + ● em quem tem dados).
            _currentModType = "boss";
            Dictionary<string, object> initialMod;
            _modifierDefaults.TryGetValue(_currentModType, out initialMod);
            if (_inlineModEditor != null) _inlineModEditor.LoadFrom(initialMod);
            RefreshModTypeButtons();

            // Special deck chance
            object sdc;
            if (gp != null && gp.TryGetValue("special_deck_chance", out sdc) && sdc is Dictionary<string, object>)
            {
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)sdc)
                {
                    ComboBox cb;
                    if (_specialChanceInputs.TryGetValue(kv.Key, out cb) && kv.Value != null)
                        cb.Text = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                }
            }

            // Rewards block (top-level) — popula nos inputs inline
            object rw;
            if (_initial.TryGetValue("rewards", out rw) && rw is Dictionary<string, object>)
            {
                Dictionary<string, object> rb = (Dictionary<string, object>)rw;
                _bossDropChance.Value   = ClampDec01(GetDoubleOrDefault(rb, "boss_drop_chance", 0));
                _eliteDropChance.Value  = ClampDec01(GetDoubleOrDefault(rb, "elite_drop_chance", 0));
                _rewardDropChance.Value = ClampDec01(GetDoubleOrDefault(rb, "reward_drop_chance", 0));
                _duelDropChance.Value   = ClampDec01(GetDoubleOrDefault(rb, "duel_drop_chance", 0));
                Dictionary<string, object> weights =
                    Utils.GetValue<Dictionary<string, object>>(rb, "category_weights");
                if (weights != null)
                {
                    foreach (string cat in RewardCategoryNames)
                    {
                        NumericUpDown n;
                        if (!_rewardWeightInputs.TryGetValue(cat, out n)) continue;
                        if (weights.TryGetValue(cat, out object v) && v != null)
                            n.Value = ClampDec01(Convert.ToDouble(v));
                    }
                }
            }
        }

        static decimal ClampDec01(double v)
        {
            if (v < 0) return 0M;
            if (v > 1) return 1M;
            return (decimal)v;
        }
        static int GetIntOrDefault(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }
        static double GetDoubleOrDefault(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v); } catch { return fallback; }
        }

        void OnSave(object sender, EventArgs e)
        {
            int gid;
            if (!int.TryParse(_txtGateId.Text.Trim(), out gid))
            {
                MessageBox.Show("Gate ID precisa ser número inteiro.",
                    "Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Dictionary<string, object> formatParams = ReadValues(_formatInputs,
                GridGateSchema.FindFormat(((ComboItem)_cmbFormat.SelectedItem).Key).Fields);
            Dictionary<string, object> genericParams = ReadValues(_genericInputs,
                GridGateSchema.GenericFields);
            // Custom curve bands (preserva mesmo se mode != custom — o
            // user pode ter editado bandas e depois mudado de mode; não
            // queremos perder o trabalho dele).
            genericParams["difficulty_curve_custom"] = _curveCustom ?? new List<object>();
            // Modifier defaults — flush primeiro pra capturar edits que
            // ainda tão visíveis no editor inline mas não foram salvos
            // pro dict (user que editou e clicou Save sem trocar de type).
            FlushCurrentModEditor();
            Dictionary<string, object> modDefs = new Dictionary<string, object>();
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in _modifierDefaults)
                if (kv.Value != null && kv.Value.Count > 0) modDefs[kv.Key] = kv.Value;
            genericParams["modifier_defaults"] = modDefs;
            // Special deck chance — só inclui quem foi setado != "never".
            Dictionary<string, object> sdc = new Dictionary<string, object>();
            foreach (KeyValuePair<string, ComboBox> kv in _specialChanceInputs)
            {
                string val = (kv.Value.Text ?? "").Trim();
                if (string.IsNullOrEmpty(val) || val == "never") continue;
                if (val == "always") { sdc[kv.Key] = "always"; continue; }
                double f;
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)
                    && f > 0) sdc[kv.Key] = f;
            }
            genericParams["special_deck_chance"] = sdc;

            Result = new Dictionary<string, object>
            {
                { "gate_id",      gid },
                { "duel_type",    _cmbDuelType.SelectedItem as string ?? "Normal" },
                { "format",       ((ComboItem)_cmbFormat.SelectedItem).Key },
                { "cosmetic_mode", _cmbCosmeticMode.SelectedItem as string ?? "vanilla" },
                { "runtime",      _chkRuntime.Checked },
                { "name",         _txtName.Text.Trim() },
                { "blurb",        _txtBlurb.Text.Trim() },
                { "format_params", formatParams },
                { "generic_params", genericParams },
            };
            // Rewards block (top-level, fora de generic_params) — lê
            // direto dos inputs inline. Só inclui se algo foi setado >0.
            double boss   = (double)_bossDropChance.Value;
            double elite  = (double)_eliteDropChance.Value;
            double reward = (double)_rewardDropChance.Value;
            double duel   = (double)_duelDropChance.Value;
            Dictionary<string, object> weights = new Dictionary<string, object>();
            foreach (KeyValuePair<string, NumericUpDown> kv in _rewardWeightInputs)
            {
                if (kv.Value.Value > 0) weights[kv.Key] = (double)kv.Value.Value;
            }
            bool anyChance = boss > 0 || elite > 0 || reward > 0 || duel > 0;
            if (anyChance || weights.Count > 0)
            {
                Dictionary<string, object> rb = new Dictionary<string, object>
                {
                    { "boss_drop_chance",   boss },
                    { "elite_drop_chance",  elite },
                    { "reward_drop_chance", reward },
                    { "duel_drop_chance",   duel },
                };
                if (weights.Count > 0) rb["category_weights"] = weights;
                Result["rewards"] = rb;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ----- helpers -----
        static void ApplyValues(Dictionary<string, Control> inputs,
                                 List<GridGateSchema.Field> fields,
                                 Dictionary<string, object> src)
        {
            foreach (GridGateSchema.Field f in fields)
            {
                Control c;
                if (!inputs.TryGetValue(f.Key, out c)) continue;
                object v;
                if (!src.TryGetValue(f.Key, out v)) continue;
                if (c is NumericUpDown && v != null)
                {
                    try { ((NumericUpDown)c).Value = Convert.ToDecimal(v, CultureInfo.InvariantCulture); }
                    catch { }
                }
                else if (c is ComboBox && v is string)
                {
                    SetCombo((ComboBox)c, (string)v);
                }
                else if (c is TextBox)
                {
                    ((TextBox)c).Text = v == null ? ""
                        : Convert.ToString(v, CultureInfo.InvariantCulture);
                }
            }
        }

        static Dictionary<string, object> ReadValues(Dictionary<string, Control> inputs,
                                                     List<GridGateSchema.Field> fields)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            foreach (GridGateSchema.Field f in fields)
            {
                Control c;
                if (!inputs.TryGetValue(f.Key, out c)) continue;
                switch (f.Kind)
                {
                    case GridGateSchema.FieldKind.Int:
                        result[f.Key] = (int)((NumericUpDown)c).Value;
                        break;
                    case GridGateSchema.FieldKind.Float:
                        result[f.Key] = (double)((NumericUpDown)c).Value;
                        break;
                    case GridGateSchema.FieldKind.IntOptional:
                        string s = ((TextBox)c).Text.Trim();
                        int parsed;
                        if (string.IsNullOrEmpty(s)) result[f.Key] = null;
                        else if (int.TryParse(s, out parsed)) result[f.Key] = parsed;
                        else result[f.Key] = null;
                        break;
                    case GridGateSchema.FieldKind.Preset:
                        result[f.Key] = (c as ComboBox)?.SelectedItem as string ?? "";
                        break;
                }
            }
            return result;
        }

        static void SetCombo(ComboBox c, string value)
        {
            int idx = c.Items.IndexOf(value);
            if (idx < 0 && c.Items.Count > 0) idx = 0;
            if (idx >= 0) c.SelectedIndex = idx;
        }
        static void SetComboItem(ComboBox c, string key)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                ComboItem it = c.Items[i] as ComboItem;
                if (it != null && it.Key == key) { c.SelectedIndex = i; return; }
            }
            if (c.Items.Count > 0) c.SelectedIndex = 0;
        }

        // ComboBox usa esse wrapper pra mostrar label e guardar key.
        class ComboItem
        {
            public string Key, Label;
            public ComboItem(string key, string label) { Key = key; Label = label; }
            public override string ToString() { return Label; }
        }
    }
}
