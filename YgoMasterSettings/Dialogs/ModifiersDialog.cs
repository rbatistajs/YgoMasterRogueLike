using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using YgoMaster;

namespace YgoMasterSettings.Dialogs
{
    // Wrapper Form pro ModifierEditor (UserControl). Usado pelo
    // LayoutEditorDialog quando o user clica "Edit modifiers…" no
    // side form de uma cell — abre dialog modal pra editar modifiers
    // per-cell.
    //
    // Pra modifiers gate-level, o GateEditDialog usa o ModifierEditor
    // INLINE (sem este wrapper) com um button group pra trocar entre
    // boss/elite/duel sem fechar/reabrir dialog.
    class ModifiersDialog : Form
    {
        public Dictionary<string, object> Result { get; private set; }

        ModifierEditor _editor;

        public ModifiersDialog(string contextLabel, Dictionary<string, object> initial)
        {
            Text = "Modifiers — " + contextLabel;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 720);
            MinimumSize = new Size(840, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            if (initial != null) _editor.LoadFrom(initial);
        }

        void BuildUi()
        {
            Label intro = new Label
            {
                Text = "Modifiers per-player. Slot vazio = sem opinião " +
                       "(defere pro layer mais baixo).",
                Dock = DockStyle.Top, Height = 22,
                Padding = new Padding(12, 4, 0, 0),
                ForeColor = Theme.FgMuted,
            };

            _editor = new ModifierEditor { Dock = DockStyle.Fill };

            FlowLayoutPanel bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
            };
            Button btnSave = new Button { Text = "Save", Width = 100, Height = 28,
                BackColor = SystemColors.Highlight, ForeColor = SystemColors.HighlightText,
                FlatStyle = FlatStyle.Flat };
            btnSave.Click += OnSave;
            Button btnCancel = new Button { Text = "Cancel", Width = 100, Height = 28,
                DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(btnSave);
            bottom.Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            FlowLayoutPanel bottomLeft = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 44,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8),
            };
            Button btnClear = new Button { Text = "Clear both", Width = 110, Height = 28 };
            btnClear.Click += (s, e) => _editor.ClearAll();
            bottomLeft.Controls.Add(btnClear);

            Controls.Add(_editor);
            Controls.Add(bottom);
            Controls.Add(bottomLeft);
            Controls.Add(intro);
        }

        void OnSave(object sender, EventArgs e)
        {
            Result = _editor.Save() ?? new Dictionary<string, object>();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
