using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using YgoMaster;

namespace YgoMasterSettings.Dialogs
{
    // UserControl reusável que renderiza o editor completo de UM
    // modifier dict (P1/P2 tabs + sections fieldSpell/monsters/
    // spellTraps/hand/adjustments). Extraído do antigo PlayerPanel
    // nested no ModifiersDialog pra que possa ser:
    //   1. Embedded inline na tab Modifiers do GateEditDialog
    //   2. Reusado pelo ModifiersDialog (per-cell do LayoutEditor)
    //
    // API:
    //   LoadFrom(modifier_dict)  — popula UI com o dict ({p1, p2})
    //   Save() -> modifier_dict  — produz o dict atual (null se vazio)
    //   ClearAll()               — zera P1 e P2
    class ModifierEditor : UserControl
    {
        PlayerPanel _p1, _p2;
        TabPage _tabP2;
        Dictionary<string, object> _pendingP2;   // initial loaded mas P2 ainda não criada

        public ModifierEditor()
        {
            BuildUi();
        }

        void BuildUi()
        {
            SuspendLayout();
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            _p1 = BuildPlayerPanel();
            TabPage tp1 = new TabPage("P1 (Player)") { Padding = new Padding(4) };
            tp1.Controls.Add(_p1);
            _tabP2 = new TabPage("P2 (CPU)") { Padding = new Padding(4) };
            tabs.TabPages.Add(tp1);
            tabs.TabPages.Add(_tabP2);
            // Lazy-load: P2 só quando user clicar (reduz N controls iniciais).
            tabs.SelectedIndexChanged += (s, e) =>
            {
                if (tabs.SelectedTab == _tabP2 && _p2 == null) EnsureP2();
            };
            Controls.Add(tabs);
            ResumeLayout(performLayout: false);
        }

        PlayerPanel BuildPlayerPanel()
        {
            PlayerPanel p = new PlayerPanel { Dock = DockStyle.Fill };
            p.SuspendLayout();
            p.Build();
            p.ResumeLayout(performLayout: false);
            return p;
        }

        void EnsureP2()
        {
            if (_p2 != null) return;
            _p2 = BuildPlayerPanel();
            _tabP2.Controls.Add(_p2);
            if (_pendingP2 != null) { _p2.LoadFrom(_pendingP2); _pendingP2 = null; }
        }

        // ----- API pública -----
        public void LoadFrom(Dictionary<string, object> mod)
        {
            // Reset antes de carregar pro caso de switch entre chapter types
            // (não queremos vazamento do spec anterior).
            _p1.ClearAll();
            if (_p2 != null) _p2.ClearAll();
            _pendingP2 = null;
            if (mod == null) return;
            Dictionary<string, object> p1 = Utils.GetValue<Dictionary<string, object>>(mod, "p1");
            Dictionary<string, object> p2 = Utils.GetValue<Dictionary<string, object>>(mod, "p2");
            if (p1 != null) _p1.LoadFrom(p1);
            if (p2 != null)
            {
                if (_p2 != null) _p2.LoadFrom(p2);
                else _pendingP2 = p2;   // espera user clicar na tab P2
            }
        }

        public Dictionary<string, object> Save()
        {
            Dictionary<string, object> p1 = _p1.Save();
            Dictionary<string, object> p2 = _p2 != null ? _p2.Save() : _pendingP2;
            Dictionary<string, object> result = new Dictionary<string, object>();
            if (p1 != null) result["p1"] = p1;
            if (p2 != null) result["p2"] = p2;
            return result.Count > 0 ? result : null;
        }

        public void ClearAll()
        {
            _p1.ClearAll();
            if (_p2 != null) _p2.ClearAll();
            _pendingP2 = null;
        }

        // ===== inner PlayerPanel (movido pra cá; o ModifiersDialog
        //       agora delega tudo pra essa classe) =====
        class PlayerPanel : UserControl
        {
            ModifierSlotRow _field;
            readonly List<ModifierSlotRow> _monsters = new List<ModifierSlotRow>();
            readonly List<ModifierSlotRow> _spellTraps = new List<ModifierSlotRow>();
            readonly List<ModifierSlotRow> _hand = new List<ModifierSlotRow>();
            FlowLayoutPanel _handContainer;
            NumericUpDown _extraLife, _extraHand;

            public PlayerPanel()
            {
                AutoScroll = true;
                Padding = new Padding(6);
            }

            public void Build()
            {
                SuspendLayout();
                // Adicionar em ordem REVERSA porque Dock=Top empilha por
                // Z-order (último a entrar fica em cima). Resultado visual:
                // Field, Monsters, Spell/Traps, Hand, Adjustments.
                Controls.Add(BuildAdjustmentsSection());
                Controls.Add(BuildHandSection());
                Controls.Add(BuildSpellTrapsSection());
                Controls.Add(BuildMonstersSection());
                Controls.Add(BuildFieldSection());
                ResumeLayout(performLayout: true);
            }

            // Cada section é um CollapsibleSection: header clicável que
            // toggleia visibility do body. Margin reduzido (2px) pra não
            // dar gap exagerado entre sections.
            TableLayoutPanel NewRowsTable()
            {
                return new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 1,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                };
            }

            CollapsibleSection BuildFieldSection()
            {
                CollapsibleSection s = new CollapsibleSection("Field Spell");
                _field = new ModifierSlotRow("Field", ModifierSlotRow.SlotKind.Field);
                _field.Container.Dock = DockStyle.Top;
                s.Body.Controls.Add(_field.Container);
                return s;
            }

            CollapsibleSection BuildMonstersSection()
            {
                CollapsibleSection s = new CollapsibleSection("Monsters (Z1–Z5)");
                TableLayoutPanel t = NewRowsTable();
                for (int i = 1; i <= 5; i++)
                {
                    ModifierSlotRow r = new ModifierSlotRow("Z" + i, ModifierSlotRow.SlotKind.Monster);
                    _monsters.Add(r);
                    t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    t.Controls.Add(r.Container, 0, i - 1);
                    t.RowCount = i;
                }
                s.Body.Controls.Add(t);
                return s;
            }

            CollapsibleSection BuildSpellTrapsSection()
            {
                CollapsibleSection s = new CollapsibleSection("Spell/Traps (S1–S5)");
                TableLayoutPanel t = NewRowsTable();
                for (int i = 1; i <= 5; i++)
                {
                    ModifierSlotRow r = new ModifierSlotRow("S" + i, ModifierSlotRow.SlotKind.SpellTrap);
                    _spellTraps.Add(r);
                    t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    t.Controls.Add(r.Container, 0, i - 1);
                    t.RowCount = i;
                }
                s.Body.Controls.Add(t);
                return s;
            }

            CollapsibleSection BuildHandSection()
            {
                CollapsibleSection s = new CollapsibleSection("Hand");
                // Hand é dinâmico — usar TableLayoutPanel com botão na
                // row 0 e container de rows abaixo. AutoSize na coluna
                // pra acompanhar adições/remoções.
                TableLayoutPanel t = NewRowsTable();
                Button btnAdd = new Button { Text = "+ Add hand card", Width = 140, Height = 26,
                    Margin = new Padding(0, 4, 0, 4), AutoSize = false };
                btnAdd.Click += (s2, e) => AddHandSlot(null);
                _handContainer = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown,
                    AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false, Dock = DockStyle.Top };
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                t.Controls.Add(btnAdd, 0, 0);
                t.Controls.Add(_handContainer, 0, 1);
                t.RowCount = 2;
                s.Body.Controls.Add(t);
                return s;
            }

            void AddHandSlot(Dictionary<string, object> initial)
            {
                int idx = _hand.Count;
                ModifierSlotRow r = new ModifierSlotRow("H" + idx, ModifierSlotRow.SlotKind.Any);
                if (initial != null) r.LoadFrom(initial);
                _hand.Add(r);
                _handContainer.Controls.Add(r.Container);
            }

            CollapsibleSection BuildAdjustmentsSection()
            {
                CollapsibleSection s = new CollapsibleSection("Adjustments");
                TableLayoutPanel t = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    ColumnCount = 3,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                t.Controls.Add(new Label { Text = "Extra LP (signed):", AutoSize = true,
                    Margin = new Padding(0, 6, 8, 4) }, 0, 0);
                _extraLife = new NumericUpDown { Width = 100, Minimum = -7999, Maximum = 92000,
                    Increment = 500, Margin = new Padding(0, 4, 8, 4) };
                t.Controls.Add(_extraLife, 1, 0);
                t.Controls.Add(new Label { Text = "(ex: -3000 = 5000 LP; +1000 = 9000 LP)",
                    AutoSize = true, ForeColor = Theme.FgMuted,
                    Margin = new Padding(0, 6, 0, 4) }, 2, 0);

                t.Controls.Add(new Label { Text = "Extra hand (signed):", AutoSize = true,
                    Margin = new Padding(0, 6, 8, 4) }, 0, 1);
                _extraHand = new NumericUpDown { Width = 100, Minimum = -4, Maximum = 15,
                    Margin = new Padding(0, 4, 8, 4) };
                t.Controls.Add(_extraHand, 1, 1);
                t.Controls.Add(new Label { Text = "(ex: +2 = 7 cartas)",
                    AutoSize = true, ForeColor = Theme.FgMuted,
                    Margin = new Padding(0, 6, 0, 4) }, 2, 1);

                s.Body.Controls.Add(t);
                return s;
            }

            // ----- IO -----
            public void LoadFrom(Dictionary<string, object> p)
            {
                ClearAll();
                Dictionary<string, object> fs = Utils.GetValue<Dictionary<string, object>>(p, "fieldSpell");
                if (fs != null) _field.LoadFrom(fs);

                List<object> ms = Utils.GetValue<List<object>>(p, "monsters");
                if (ms != null)
                    for (int i = 0; i < _monsters.Count && i < ms.Count; i++)
                        _monsters[i].LoadFrom(ms[i] as Dictionary<string, object>);

                List<object> sts = Utils.GetValue<List<object>>(p, "spellTraps");
                if (sts != null)
                    for (int i = 0; i < _spellTraps.Count && i < sts.Count; i++)
                        _spellTraps[i].LoadFrom(sts[i] as Dictionary<string, object>);

                List<object> hd = Utils.GetValue<List<object>>(p, "hand");
                if (hd != null)
                    foreach (object o in hd) AddHandSlot(o as Dictionary<string, object>);

                int el;
                if (TryGetInt(p, "extraLife", out el)) _extraLife.Value = ClampLife(el);
                int eh;
                if (TryGetInt(p, "extraHand", out eh)) _extraHand.Value = ClampHand(eh);
            }

            decimal ClampLife(int v)
            {
                if (v < _extraLife.Minimum) return _extraLife.Minimum;
                if (v > _extraLife.Maximum) return _extraLife.Maximum;
                return v;
            }
            decimal ClampHand(int v)
            {
                if (v < _extraHand.Minimum) return _extraHand.Minimum;
                if (v > _extraHand.Maximum) return _extraHand.Maximum;
                return v;
            }

            public Dictionary<string, object> Save()
            {
                Dictionary<string, object> p = new Dictionary<string, object>();
                Dictionary<string, object> fs = _field.Save();
                if (fs != null) p["fieldSpell"] = fs;
                List<object> ms = SaveList(_monsters);
                if (ms != null) p["monsters"] = ms;
                List<object> sts = SaveList(_spellTraps);
                if (sts != null) p["spellTraps"] = sts;
                List<object> hd = SaveList(_hand);
                if (hd != null) p["hand"] = hd;
                int el = (int)_extraLife.Value;
                int eh = (int)_extraHand.Value;
                if (el != 0) p["extraLife"] = el;
                if (eh != 0) p["extraHand"] = eh;
                return p.Count > 0 ? p : null;
            }

            static List<object> SaveList(List<ModifierSlotRow> rows)
            {
                List<object> list = new List<object>();
                bool any = false;
                foreach (ModifierSlotRow r in rows)
                {
                    Dictionary<string, object> spec = r.Save();
                    if (spec == null) list.Add(null);
                    else { list.Add(spec); any = true; }
                }
                return any ? list : null;
            }

            public void ClearAll()
            {
                _field.Clear();
                foreach (ModifierSlotRow r in _monsters)   r.Clear();
                foreach (ModifierSlotRow r in _spellTraps) r.Clear();
                _hand.Clear();
                _handContainer.Controls.Clear();
                _extraLife.Value = 0;
                _extraHand.Value = 0;
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

    // Panel com header clicável que expande/colapsa o body. Substitui
    // GroupBox quando precisamos esconder sections grandes pra reduzir
    // scroll/clutter (ex: Modifiers tem 5 sections grandes empilhadas).
    //
    // Estrutura interna: header Label (Dock=Top, clicável) + body Panel
    // (Dock=Top, AutoSize). Toggle muda Body.Visible e atualiza chevron
    // (▼ expandido / ▶ colapsado). O CollapsibleSection é AutoSize
    // pra que o parent re-layout absorva a mudança de altura.
    class CollapsibleSection : Panel
    {
        readonly Label _header;
        readonly Panel _body;
        readonly string _title;
        bool _expanded = true;

        public CollapsibleSection(string title)
        {
            _title = title;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // Margin reduzido pra que sections fiquem próximas (era 6px no
            // GroupBox antigo + padding interno gerava gap de ~20px).
            Margin = new Padding(0, 0, 0, 2);
            Padding = new Padding(0);
            BorderStyle = BorderStyle.FixedSingle;

            _body = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = SystemColors.Window,
            };

            _header = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = SystemColors.ControlLight,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            };
            _header.Click += (s, e) => Toggle();
            UpdateHeaderText();

            // Order matters: body adicionado primeiro (Z-bottom), header
            // depois (Z-top fica em cima pq Dock=Top stack).
            Controls.Add(_body);
            Controls.Add(_header);
        }

        // Container onde callers adicionam seu conteúdo
        // (Field/Monsters/etc).
        public Panel Body { get { return _body; } }

        public bool Expanded
        {
            get { return _expanded; }
            set { SetExpanded(value); }
        }

        public void Toggle() { SetExpanded(!_expanded); }

        void SetExpanded(bool e)
        {
            if (_expanded == e) return;
            _expanded = e;
            _body.Visible = e;
            UpdateHeaderText();
            PerformLayout();
        }

        void UpdateHeaderText()
        {
            _header.Text = (_expanded ? "▼  " : "▶  ") + _title;
        }
    }
}
