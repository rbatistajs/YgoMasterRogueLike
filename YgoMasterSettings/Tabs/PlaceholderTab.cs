using System.Drawing;
using System.Windows.Forms;

namespace YgoMasterSettings.Tabs
{
    // Tab vazia com mensagem — usado pra slots ainda não-portados das
    // tabs do game_settings.py legado. Cada uma vai ser substituída por
    // uma UserControl real conforme a migração avança.
    class PlaceholderTab : UserControl
    {
        public PlaceholderTab(string message)
        {
            Label lbl = new Label
            {
                Text = message,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = Theme.FontHeading,
                ForeColor = Theme.FgMuted,
            };
            Controls.Add(lbl);
        }
    }
}
