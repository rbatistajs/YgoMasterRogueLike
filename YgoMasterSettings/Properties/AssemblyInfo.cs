using System.Reflection;
using System.Runtime.InteropServices;

// Metadata do .exe — entre outras coisas, ajuda heurísticas de detecção
// de processo (Discord, RTSS, OBS auto-add) a categorizar isso como
// "ferramenta de configuração", não "jogo". Não há garantia que o
// Discord respeite (a lista interna deles geralmente vence), mas pelo
// menos fica explícito no Properties da DLL.
[assembly: AssemblyTitle("YgoMaster Settings (Goat)")]
[assembly: AssemblyDescription("Configuration tool for the YgoMaster Goat mod — gate editor, deck migrator, cosmetics/rewards configuration. NOT a game.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Goat fork")]
[assembly: AssemblyProduct("YgoMaster Settings")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
