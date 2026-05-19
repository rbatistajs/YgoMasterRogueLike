using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace YgoMasterSettings.Dialogs
{
    // 1 linha de slot pro modifier dialog. Port do _SlotRow Python.
    //
    // Modes mutuamente-exclusivos por slot kind (mirror do _KIND_MODES):
    //   Field     → "—", "Pin card", "Random"                            (random = field_spell)
    //   Monster   → "—", "Pin card", "Random", "Random main", "Random extra"
    //   SpellTrap → "—", "Pin card", "Random", "Random spell", "Random trap"
    //   Any       → "—", "Pin card", "Random (any)", "Random monster",
    //              "Random main monster", "Random extra monster",
    //              "Random spell", "Random trap", "Random field spell"
    //              (usado pra hand)
    //
    // Subtype options swap por kind resolvido (mirror do _SUBTYPE_OPTIONS):
    //   monster       → normal/effect/ritual/fusion/synchro/xyz/link
    //   main_monster  → normal/effect/ritual
    //   extra_monster → fusion/synchro/xyz/link
    //   spell         → normal/continuous/equip/field/quickplay/ritual
    //   trap          → normal/continuous/counter
    //
    // `_sync_enabled` desabilita widgets que não fazem sentido pro mode:
    //   "—"        → tudo disabled
    //   "Pin card" → só cid_entry enabled (subtype/source/filters disabled)
    //   "Random …" → cid disabled, subtype/source/pos/filters enabled
    class ModifierSlotRow
    {
        public Panel Container { get; private set; }

        public enum SlotKind { Field, Monster, SpellTrap, Any }
        readonly SlotKind _kind;
        readonly bool _showPos;
        readonly bool _showFilters;

        ComboBox _cmbMode;
        TextBox  _txtCid;
        ComboBox _cmbSubtype;
        ComboBox _cmbSource;
        ComboBox _cmbPos;
        TextBox  _txtMinAtk, _txtMaxAtk, _txtMinDef, _txtMaxDef, _txtMinLvl, _txtMaxLvl;

        // ----- presets -----
        static readonly string[] MonsterPos = { "atk", "def", "atk_fd", "def_fd" };
        static readonly string[] StPos = { "set", "face_up" };

        // Source display label ↔ (source, deck_owner) pair
        static readonly string[] SourceDisplay = { "own deck", "op deck", "any" };

        // Modes por kind
        static readonly Dictionary<SlotKind, string[]> ModesPerKind = new Dictionary<SlotKind, string[]>
        {
            { SlotKind.Field,     new[] { "—", "Pin card", "Random" } },
            { SlotKind.Monster,   new[] { "—", "Pin card", "Random", "Random main", "Random extra" } },
            { SlotKind.SpellTrap, new[] { "—", "Pin card", "Random", "Random spell", "Random trap" } },
            { SlotKind.Any,       new[] { "—", "Pin card", "Random (any)",
                                          "Random monster", "Random main monster", "Random extra monster",
                                          "Random spell", "Random trap", "Random field spell" } },
        };

        // "Random" without suffix → resolved random kind per slot kind
        static readonly Dictionary<SlotKind, string> KindDefaultRandom = new Dictionary<SlotKind, string>
        {
            { SlotKind.Field,     "field_spell" },
            { SlotKind.Monster,   "monster" },
            { SlotKind.SpellTrap, "spell_or_trap" },
            { SlotKind.Any,       "any" },
        };

        // Mode label → random spec value
        static readonly Dictionary<string, string> ModeToRandom = new Dictionary<string, string>
        {
            { "Random (any)",          "any" },
            { "Random monster",        "monster" },
            { "Random main",           "main_monster" },
            { "Random extra",          "extra_monster" },
            { "Random main monster",   "main_monster" },
            { "Random extra monster",  "extra_monster" },
            { "Random spell",          "spell" },
            { "Random trap",           "trap" },
            { "Random field spell",    "field_spell" },
        };

        // Subtype options por resolved random kind
        static readonly Dictionary<string, string[]> SubtypeOptions = new Dictionary<string, string[]>
        {
            { "monster",       new[] { "—", "normal", "effect", "ritual", "fusion", "synchro", "xyz", "link" } },
            { "main_monster",  new[] { "—", "normal", "effect", "ritual" } },
            { "extra_monster", new[] { "—", "fusion", "synchro", "xyz", "link" } },
            { "spell",         new[] { "—", "normal", "continuous", "equip", "field", "quickplay", "ritual" } },
            { "trap",          new[] { "—", "normal", "continuous", "counter" } },
            { "field_spell",   new[] { "—" } },
            { "spell_or_trap", new[] { "—" } },
            { "any",           new[] { "—" } },
        };

        public ModifierSlotRow(string label, SlotKind kind)
        {
            _kind = kind;
            _showPos     = kind == SlotKind.Monster || kind == SlotKind.SpellTrap;
            _showFilters = kind == SlotKind.Monster || kind == SlotKind.Any;
            Container = BuildRow(label);
            SyncEnabled();
        }

        Panel BuildRow(string label)
        {
            FlowLayoutPanel row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false, AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            // Adicionar 10+ controls com AutoSize-on dispara N relayouts.
            // SuspendLayout colapsa em 1 só no Resume final.
            row.SuspendLayout();

            row.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Width = 36,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 6, 0),
            });

            _cmbMode = Combo(ModesPerKind[_kind], 180);
            _cmbMode.SelectedIndexChanged += (s, e) => SyncEnabled();
            row.Controls.Add(_cmbMode);

            _txtCid = Txt(70, "cid");
            row.Controls.Add(_txtCid);

            _cmbSubtype = Combo(new[] { "—" }, 110);
            row.Controls.Add(_cmbSubtype);

            _cmbSource = Combo(SourceDisplay, 90);
            _cmbSource.SelectedItem = "own deck";
            row.Controls.Add(_cmbSource);

            if (_showPos)
            {
                string[] posOpts = _kind == SlotKind.SpellTrap ? StPos : MonsterPos;
                _cmbPos = Combo(posOpts, 70);
                row.Controls.Add(_cmbPos);
            }

            if (_showFilters)
            {
                _txtMinAtk = Txt(58, "≥ATK");
                _txtMaxAtk = Txt(58, "≤ATK");
                _txtMinDef = Txt(58, "≥DEF");
                _txtMaxDef = Txt(58, "≤DEF");
                _txtMinLvl = Txt(40, "≥L");
                _txtMaxLvl = Txt(40, "≤L");
                row.Controls.Add(_txtMinAtk);
                row.Controls.Add(_txtMaxAtk);
                row.Controls.Add(_txtMinDef);
                row.Controls.Add(_txtMaxDef);
                row.Controls.Add(_txtMinLvl);
                row.Controls.Add(_txtMaxLvl);
            }

            row.ResumeLayout(performLayout: false);
            return row;
        }

        // Enable/disable widgets baseado no mode atual.
        // "—"        → tudo disabled
        // "Pin card" → só cid enabled
        // "Random …" → cid disabled, resto enabled, subtype swap
        void SyncEnabled()
        {
            string mode = _cmbMode.SelectedItem as string ?? "—";
            bool isPin = mode == "Pin card";
            bool isRandom = mode.StartsWith("Random");
            bool isEmpty = mode == "—";

            _txtCid.Enabled = isPin;
            _cmbSubtype.Enabled = isRandom;
            _cmbSource.Enabled = isRandom;
            if (_cmbPos != null) _cmbPos.Enabled = isRandom || isPin;
            if (_showFilters)
            {
                _txtMinAtk.Enabled = isRandom;
                _txtMaxAtk.Enabled = isRandom;
                _txtMinDef.Enabled = isRandom;
                _txtMaxDef.Enabled = isRandom;
                _txtMinLvl.Enabled = isRandom;
                _txtMaxLvl.Enabled = isRandom;
            }

            if (isEmpty)
            {
                _cmbSubtype.Items.Clear();
                _cmbSubtype.Items.Add("—");
                _cmbSubtype.SelectedIndex = 0;
                return;
            }

            // Resolve random kind pra atualizar subtype options
            if (isRandom)
            {
                string resolved = ResolveRandomKind(mode);
                string[] opts;
                if (!SubtypeOptions.TryGetValue(resolved, out opts))
                    opts = new[] { "—" };
                string prev = _cmbSubtype.SelectedItem as string;
                _cmbSubtype.Items.Clear();
                foreach (string s in opts) _cmbSubtype.Items.Add(s);
                int idx = !string.IsNullOrEmpty(prev) ? _cmbSubtype.Items.IndexOf(prev) : 0;
                _cmbSubtype.SelectedIndex = idx >= 0 ? idx : 0;
            }
        }

        string ResolveRandomKind(string mode)
        {
            if (mode == "Random")
            {
                string def;
                return KindDefaultRandom.TryGetValue(_kind, out def) ? def : "any";
            }
            string r;
            return ModeToRandom.TryGetValue(mode, out r) ? r : "any";
        }

        ComboBox Combo(string[] items, int width)
        {
            ComboBox c = new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 2, 6, 2) };
            foreach (string s in items) c.Items.Add(s);
            if (items.Length > 0) c.SelectedIndex = 0;
            return c;
        }

        TextBox Txt(int width, string placeholder)
        {
            TextBox t = new TextBox { Width = width, Margin = new Padding(0, 2, 6, 2) };
            ApplyPlaceholder(t, placeholder);
            return t;
        }

        static void ApplyPlaceholder(TextBox t, string ph)
        {
            Color phColor   = SystemColors.GrayText;
            Color realColor = SystemColors.WindowText;
            t.Text = ph; t.ForeColor = phColor;
            t.GotFocus += (s, e) =>
            {
                if (t.Text == ph) { t.Text = ""; t.ForeColor = realColor; }
            };
            t.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(t.Text)) { t.Text = ph; t.ForeColor = phColor; }
                else t.ForeColor = realColor;
            };
            t.Tag = ph;
        }
        static string ReadIfNotPlaceholder(TextBox t)
        {
            string ph = t.Tag as string;
            if (ph != null && t.Text == ph && t.ForeColor == SystemColors.GrayText)
                return "";
            return t.Text;
        }
        static void ResetTextToPlaceholder(TextBox t)
        {
            string ph = t.Tag as string;
            if (ph != null) { t.Text = ph; t.ForeColor = SystemColors.GrayText; }
            else t.Text = "";
        }
        static void SetRealText(TextBox t, string val)
        {
            t.Text = val; t.ForeColor = SystemColors.WindowText;
        }

        // ----- I/O -----
        // Resolve mode label a partir dum spec dict.
        // Spec → display mode:
        //   {cid: N} (no random)               → "Pin card"
        //   {random: "field_spell"} no Field   → "Random" (uses default)
        //   {random: "any"} no Any             → "Random (any)"
        //   {random: "monster"} no Monster     → "Random"
        //   {random: "main_monster"}           → "Random main" / "Random main monster"
        //   {random: "spell"} no SpellTrap     → "Random spell"
        //   etc.
        string ModeFromSpec(Dictionary<string, object> spec)
        {
            if (spec == null) return "—";
            bool hasCid = spec.ContainsKey("cid");
            bool hasRandom = spec.ContainsKey("random");
            if (hasCid && !hasRandom) return "Pin card";
            if (!hasRandom) return "—";
            string r = spec["random"] as string ?? "";
            // Pra cada mode disponível, ver qual mapeia pro mesmo random
            string[] modes = ModesPerKind[_kind];
            foreach (string m in modes)
            {
                if (m == "—" || m == "Pin card") continue;
                if (m == "Random")
                {
                    string def = KindDefaultRandom[_kind];
                    if (def == r) return "Random";
                }
                else
                {
                    string mapped;
                    if (ModeToRandom.TryGetValue(m, out mapped) && mapped == r) return m;
                }
            }
            return "—";
        }

        public void LoadFrom(Dictionary<string, object> spec)
        {
            Clear();
            if (spec == null) return;
            string mode = ModeFromSpec(spec);
            SetCombo(_cmbMode, mode);
            SyncEnabled();   // popula subtype options apropriadas

            int cid;
            if (TryGetInt(spec, "cid", out cid) && cid > 0)
                SetRealText(_txtCid, cid.ToString(CultureInfo.InvariantCulture));

            string sub = GetStr(spec, "subtype");
            if (!string.IsNullOrEmpty(sub) && _cmbSubtype.Items.Contains(sub))
                _cmbSubtype.SelectedItem = sub;

            string src = GetStr(spec, "source") ?? "deck";
            string owner = GetStr(spec, "deck_owner") ?? "own";
            string srcLabel = src == "any" ? "any"
                            : owner == "rival" ? "op deck"
                            : "own deck";
            SetCombo(_cmbSource, srcLabel);

            if (_cmbPos != null)
            {
                string pos = GetStr(spec, "pos");
                if (!string.IsNullOrEmpty(pos)) SetCombo(_cmbPos, pos);
            }
            if (_showFilters)
            {
                int n;
                if (TryGetInt(spec, "minAtk",   out n)) SetRealText(_txtMinAtk, n.ToString(CultureInfo.InvariantCulture));
                if (TryGetInt(spec, "maxAtk",   out n)) SetRealText(_txtMaxAtk, n.ToString(CultureInfo.InvariantCulture));
                if (TryGetInt(spec, "minDef",   out n)) SetRealText(_txtMinDef, n.ToString(CultureInfo.InvariantCulture));
                if (TryGetInt(spec, "maxDef",   out n)) SetRealText(_txtMaxDef, n.ToString(CultureInfo.InvariantCulture));
                if (TryGetInt(spec, "minLevel", out n)) SetRealText(_txtMinLvl, n.ToString(CultureInfo.InvariantCulture));
                if (TryGetInt(spec, "maxLevel", out n)) SetRealText(_txtMaxLvl, n.ToString(CultureInfo.InvariantCulture));
            }
        }

        public Dictionary<string, object> Save()
        {
            string mode = _cmbMode.SelectedItem as string ?? "—";
            if (mode == "—") return null;

            Dictionary<string, object> spec = new Dictionary<string, object>();

            if (mode == "Pin card")
            {
                string cidTxt = ReadIfNotPlaceholder(_txtCid).Trim();
                int cid;
                if (int.TryParse(cidTxt, out cid) && cid > 0) spec["cid"] = cid;
                else return null;   // pin sem cid = inválido
                // Pos é válido pra Pin card (monster/ST)
                if (_cmbPos != null && _cmbPos.SelectedItem is string p && !string.IsNullOrEmpty(p))
                    spec["pos"] = p;
                return spec;
            }

            // Random …
            spec["random"] = ResolveRandomKind(mode);
            string sub = _cmbSubtype.SelectedItem as string ?? "—";
            if (sub != "—") spec["subtype"] = sub;

            // source — não escreve defaults (mantém JSON limpo)
            string srcLabel = _cmbSource.SelectedItem as string ?? "own deck";
            if (srcLabel == "any") spec["source"] = "any";
            else if (srcLabel == "op deck") { spec["source"] = "deck"; spec["deck_owner"] = "rival"; }
            // "own deck" = default — omite source/deck_owner do JSON

            if (_cmbPos != null && _cmbPos.SelectedItem is string pos && !string.IsNullOrEmpty(pos))
                spec["pos"] = pos;
            if (_showFilters)
            {
                AddIntIfSet(spec, "minAtk",   _txtMinAtk);
                AddIntIfSet(spec, "maxAtk",   _txtMaxAtk);
                AddIntIfSet(spec, "minDef",   _txtMinDef);
                AddIntIfSet(spec, "maxDef",   _txtMaxDef);
                AddIntIfSet(spec, "minLevel", _txtMinLvl);
                AddIntIfSet(spec, "maxLevel", _txtMaxLvl);
            }
            return spec;
        }

        public void Clear()
        {
            _cmbMode.SelectedIndex = 0;
            ResetTextToPlaceholder(_txtCid);
            // _cmbSubtype reset pelo SyncEnabled()
            if (_cmbSource != null) _cmbSource.SelectedItem = "own deck";
            if (_cmbPos != null) _cmbPos.SelectedIndex = 0;
            if (_showFilters)
            {
                ResetTextToPlaceholder(_txtMinAtk);
                ResetTextToPlaceholder(_txtMaxAtk);
                ResetTextToPlaceholder(_txtMinDef);
                ResetTextToPlaceholder(_txtMaxDef);
                ResetTextToPlaceholder(_txtMinLvl);
                ResetTextToPlaceholder(_txtMaxLvl);
            }
            SyncEnabled();
        }

        // ----- helpers -----
        static void AddIntIfSet(Dictionary<string, object> spec, string key, TextBox t)
        {
            string s = ReadIfNotPlaceholder(t).Trim();
            if (string.IsNullOrEmpty(s)) return;
            int v;
            if (int.TryParse(s, out v)) spec[key] = v;
        }
        static void SetCombo(ComboBox c, string value)
        {
            int idx = c.Items.IndexOf(value);
            if (idx >= 0) c.SelectedIndex = idx;
        }
        static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            return (d.TryGetValue(key, out v) && v != null) ? v.ToString() : null;
        }
        static bool TryGetInt(Dictionary<string, object> d, string key, out int v)
        {
            v = 0;
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return false;
            try { v = Convert.ToInt32(o); return true; } catch { return false; }
        }
    }
}
