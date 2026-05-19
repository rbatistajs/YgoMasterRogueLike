using System.Drawing;
using System.Windows.Forms;
using YgoMasterSettings.Tabs;

namespace YgoMasterSettings
{
    // Shell principal — TabControl com 1 tab por feature da game_settings.py
    // legada. Tabs marcadas como `PlaceholderTab` por enquanto serão
    // portadas em fases.
    class MainWindow : Form
    {
        public MainWindow()
        {
            Text = "YgoMaster Settings — Goat";
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            // Mesmo padrão dos dialogs — Font do sistema + DPI scaling.
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;

            TabControl tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(12, 6),
            };

            // Tabs implementadas. Novas features são adicionadas aqui
            // conforme planejado — sem placeholders pra evitar slots
            // vazios que confundem o user.
            tabs.TabPages.Add(MakeTab("Grid Gates", new GateRegistryTab()));
            tabs.TabPages.Add(MakeTab("Card List", new CardListTab()));
            tabs.TabPages.Add(MakeTab("Shop", new ShopTab()));

            Controls.Add(tabs);
            Theme.Apply(this);
        }

        // Wrap qualquer UserControl numa TabPage.
        static TabPage MakeTab(string title, UserControl body)
        {
            TabPage page = new TabPage(title) { Padding = new Padding(8) };
            body.Dock = DockStyle.Fill;
            page.Controls.Add(body);
            return page;
        }
    }
}
