using System.Drawing;
using System.Windows.Forms;

namespace YgoMasterSettings
{
    // Tema "padrão Windows" — usa SystemColors pra herdar o look nativo
    // (light, com accent do tema do usuário). Mantém só uns helpers de
    // fonte/cor pra destacar pontos específicos (headers, logs, status).
    static class Theme
    {
        // Cores: deixa o WinForms usar SystemColors por default; só
        // expõe constantes pros pontos onde queremos cor explícita
        // (status / log / accents).
        public static readonly Color BgLog       = Color.FromArgb(0xfa, 0xfa, 0xfa);
        public static readonly Color FgMuted     = Color.FromArgb(0x80, 0x80, 0x80);
        public static readonly Color FgAccent    = Color.FromArgb(0x00, 0x78, 0xd4);   // Windows accent blue
        public static readonly Color FgDanger    = Color.FromArgb(0xc4, 0x2b, 0x1c);
        public static readonly Color FgSuccess   = Color.FromArgb(0x10, 0x7c, 0x10);

        public static readonly Font  FontUi      = SystemFonts.MessageBoxFont;
        public static readonly Font  FontUiBold  = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        public static readonly Font  FontMono    = new Font("Consolas", 9F);
        public static readonly Font  FontHeading = new Font(SystemFonts.MessageBoxFont.FontFamily,
                                                            11F, FontStyle.Bold);

        // No-op: o WinForms já usa o tema nativo (SystemColors). Mantemos
        // a função pra que MainWindow continue tendo um único ponto de
        // wiring de tema se quisermos customizar depois.
        public static void Apply(Control root)
        {
            // intencionalmente vazio — light/native theme
        }
    }
}
