using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using YgoMaster;

namespace YgoMasterSettings.Dialogs
{
    // Editor do bloco `rewards` da entry GridGates. Tem:
    //   - 4 drop chances (boss/elite/reward/duel) — float 0..1
    //   - category_weights: 10+ categorias de item, pesos relativos
    //
    // Schema mirror do RewardConfig.cs:
    //   "rewards": {
    //     "boss_drop_chance": 1.0, ...
    //     "category_weights": { "AVATAR": 1.0, ... }
    //   }
    class RewardsDialog : Form
    {
        public Dictionary<string, object> Result { get; private set; }

        // ItemID.Category names que mapeiam pra reward — mirror do
        // CategoryToRewardId em RewardPicker.cs.
        static readonly string[] CategoryNames = {
            "CONSUME", "AVATAR", "ICON", "PROFILE_TAG", "ICON_FRAME",
            "PROTECTOR", "DECK_CASE", "FIELD", "FIELD_OBJ", "AVATAR_HOME",
            "STRUCTURE", "WALLPAPER", "PACK_TICKET",
        };

        NumericUpDown _bossChance, _eliteChance, _rewardChance, _duelChance;
        Dictionary<string, NumericUpDown> _catWeights = new Dictionary<string, NumericUpDown>();

        public RewardsDialog(Dictionary<string, object> initial)
        {
            Text = "Edit Rewards";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 560);
            MinimumSize = new Size(560, 460);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            LoadInitial(initial ?? new Dictionary<string, object>());
        }

        void BuildUi()
        {
            Panel body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };

            // ----- Drop chances -----
            GroupBox gbDrop = new GroupBox
            {
                Text = "Drop chances por chapter type (0..1)",
                Dock = DockStyle.Top, Height = 130,
                Padding = new Padding(10, 16, 10, 10),
            };
            TableLayoutPanel tDrop = NewForm();
            _bossChance   = MakeFloatInput();
            _eliteChance  = MakeFloatInput();
            _rewardChance = MakeFloatInput();
            _duelChance   = MakeFloatInput();
            AddDropRow(tDrop, "Boss",   _bossChance,   "chance do boss chapter dropar 1 item");
            AddDropRow(tDrop, "Elite",  _eliteChance,  "idem pra elite chapters");
            AddDropRow(tDrop, "Reward", _rewardChance, "reward/treasure chapters");
            AddDropRow(tDrop, "Duel",   _duelChance,   "duels normais (geralmente 0)");
            gbDrop.Controls.Add(tDrop);

            // ----- Category weights -----
            GroupBox gbCats = new GroupBox
            {
                Text = "Category weights (0 = não pode dropar; >0 = peso relativo)",
                Dock = DockStyle.Top, Height = 360,
                Padding = new Padding(10, 16, 10, 10),
            };
            TableLayoutPanel tCats = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true,
            };
            for (int i = 0; i < 4; i++)
                tCats.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0, col = 0;
            foreach (string cat in CategoryNames)
            {
                Label lbl = new Label { Text = cat, AutoSize = true,
                    Margin = new Padding(0, 6, 8, 4) };
                NumericUpDown n = MakeFloatInput();
                tCats.Controls.Add(lbl, col,     row);
                tCats.Controls.Add(n,   col + 1, row);
                _catWeights[cat] = n;
                col += 2;
                if (col >= 4) { col = 0; row++; }
            }
            gbCats.Controls.Add(tCats);

            body.Controls.Add(gbCats);   // bottom-most → primeiro pra Dock=Top empilhar correto
            body.Controls.Add(gbDrop);

            FlowLayoutPanel bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
            };
            Button btnSave = new Button { Text = "Save", Width = 90, Height = 28 };
            btnSave.Click += OnSave;
            Button btnCancel = new Button { Text = "Cancel", Width = 90, Height = 28,
                DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(body);
            Controls.Add(bottom);
        }

        NumericUpDown MakeFloatInput()
        {
            return new NumericUpDown
            {
                Width = 80,
                Minimum = 0M, Maximum = 1.0M,
                DecimalPlaces = 2, Increment = 0.05M,
            };
        }

        TableLayoutPanel NewForm()
        {
            TableLayoutPanel t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3, AutoSize = true,
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        void AddDropRow(TableLayoutPanel t, string label, NumericUpDown input, string hint)
        {
            int row = t.RowCount;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true,
                Margin = new Padding(0, 6, 12, 4), Width = 70 }, 0, row);
            input.Margin = new Padding(0, 4, 12, 4);
            t.Controls.Add(input, 1, row);
            t.Controls.Add(new Label { Text = hint, AutoSize = true,
                ForeColor = Theme.FgMuted, Margin = new Padding(0, 6, 0, 4) }, 2, row);
            t.RowCount = row + 1;
        }

        void LoadInitial(Dictionary<string, object> initial)
        {
            _bossChance.Value   = ToDec(initial, "boss_drop_chance",   0M);
            _eliteChance.Value  = ToDec(initial, "elite_drop_chance",  0M);
            _rewardChance.Value = ToDec(initial, "reward_drop_chance", 0M);
            _duelChance.Value   = ToDec(initial, "duel_drop_chance",   0M);

            Dictionary<string, object> weights =
                Utils.GetValue<Dictionary<string, object>>(initial, "category_weights");
            if (weights == null) return;
            foreach (string cat in CategoryNames)
            {
                if (weights.TryGetValue(cat, out object v))
                    _catWeights[cat].Value = ToDecVal(v, 0M);
            }
        }

        static decimal ToDec(Dictionary<string, object> d, string key, decimal fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            return ToDecVal(v, fallback);
        }
        static decimal ToDecVal(object v, decimal fallback)
        {
            try
            {
                decimal dec = Convert.ToDecimal(v);
                if (dec < 0) return 0M;
                if (dec > 1) return 1M;
                return dec;
            }
            catch { return fallback; }
        }

        void OnSave(object sender, EventArgs e)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["boss_drop_chance"]   = (double)_bossChance.Value;
            r["elite_drop_chance"]  = (double)_eliteChance.Value;
            r["reward_drop_chance"] = (double)_rewardChance.Value;
            r["duel_drop_chance"]   = (double)_duelChance.Value;
            Dictionary<string, object> weights = new Dictionary<string, object>();
            foreach (KeyValuePair<string, NumericUpDown> kv in _catWeights)
            {
                if (kv.Value.Value > 0) weights[kv.Key] = (double)kv.Value.Value;
            }
            if (weights.Count > 0) r["category_weights"] = weights;
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
