using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YgoMasterSettings
{
    // YgoMasterSettings.exe — substitui o `python scripts/game_settings.py`
    // por uma UI nativa WinForms. Migração em fases (ver TODO):
    //   Fase 0 (atual): esqueleto + main window com TabControl
    //   Fase 1+: portar cada tab/dialog do game_settings.py
    //
    // O exe é dropado no install dir pelo AfterTarget do csproj; o usuário
    // roda direto (Game Settings.bat redirecionará pra ele depois).
    static class Program
    {
        // Path do install (= pasta onde o exe está). Usado pra resolver
        // DataLE/*, ClientData/*, etc.
        public static string InstallRoot { get; private set; }
        public static string DataDir     { get; private set; }

        [STAThread]
        static void Main()
        {
            InstallRoot = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            DataDir = Path.Combine(InstallRoot, "DataLE");

            // DPI awareness — complementa o app.manifest. Tem que ser a
            // PRIMEIRA chamada do app (antes de EnableVisualStyles e de
            // qualquer criação de Form/Control). Em .NET 4.8 isso evita
            // que child windows DPI-unaware (como Modal dialogs) sejam
            // bitmap-stretched e fiquem visualmente menores que a main.
            try
            {
                System.Runtime.InteropServices.Marshal.PrelinkAll(typeof(NativeDpi));
                NativeDpi.SetProcessDpiAwarenessContext(NativeDpi.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch { /* SO antigo — manifest cobre */ }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }

        // Win10+ API pra setar DPI awareness em runtime (manifest faz a
        // mesma coisa mas redundância garante).
        static class NativeDpi
        {
            public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        }
    }
}
