using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YgoMaster;

namespace YgoMasterClient
{
    // Valida que cada card no CardData tem imagem de ilustração disponível,
    // seja no bundle interno do client (via AssetHelper.FileExists), seja
    // no fallback de disco (DataLE/ClientData/Card/Images/Illust/tcg/).
    //
    // Gera 2 arquivos em DataLE/ClientDataDump/:
    //   - MissingCardImages.txt  : 1 cid por linha (ordem ascendente)
    //   - MissingCardImages.csv  : cid,name pra facilitar análise
    //
    // Pra rodar precisa que `AssetHelper.Init()` tenha rodado antes
    // (depende do ResourceManager IL2CPP populated). Chamado uma vez no
    // boot via Program.DllMain após AssetHelper.Init().
    //
    // Convenção de path do bundle: "Card/Images/Illust/tcg/<cid>"
    // (sem extensão — o resource manager resolve a extensão correta).
    static class CardImageValidator
    {
        public static void Run()
        {
            try
            {
                RunInternal();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CardImageValidator] FAILED: " + ex);
            }
        }

        static void RunInternal()
        {
            Console.WriteLine("[CardImageValidator] Starting…");
            Dictionary<int, YdkHelper.GameCardInfo> allCards =
                YdkHelper.LoadCardDataFromGame(Program.DataDir);
            if (allCards == null || allCards.Count == 0)
            {
                Console.WriteLine("[CardImageValidator] no CardData loaded — abort");
                return;
            }

            List<int> missing = new List<int>();
            // Caminho físico fallback — se AssetHelper.FileExists falhar
            // ainda checamos o disco direto (cobre cards que tão no
            // fork mas não no bundle vanilla).
            string illustDir = Path.Combine(Program.DataDir,
                "ClientData", "Card", "Images", "Illust", "tcg");

            foreach (int cid in allCards.Keys.OrderBy(c => c))
            {
                if (cid <= 0) continue;
                string assetPath = "Card/Images/Illust/tcg/" + cid;
                bool inBundle = false;
                try { inBundle = AssetHelper.FileExists(assetPath); }
                catch { /* AssetHelper não pronto — cai pro disco */ }
                if (inBundle) continue;
                // Disco como fallback (png ou jpg)
                if (File.Exists(Path.Combine(illustDir, cid + ".png"))) continue;
                if (File.Exists(Path.Combine(illustDir, cid + ".jpg"))) continue;
                missing.Add(cid);
            }

            string outDir = Program.ClientDataDumpDir;
            try { Directory.CreateDirectory(outDir); } catch { }
            string txtPath = Path.Combine(outDir, "MissingCardImages.txt");
            string csvPath = Path.Combine(outDir, "MissingCardImages.csv");

            // .txt: 1 cid por linha
            File.WriteAllLines(txtPath, missing.ConvertAll(c => c.ToString()));
            // .csv: cid,name (UTF-8 sem BOM pra Excel não bagunçar)
            using (StreamWriter sw = new StreamWriter(csvPath, false,
                new System.Text.UTF8Encoding(false)))
            {
                sw.WriteLine("cid,name");
                foreach (int cid in missing)
                {
                    string name = allCards.ContainsKey(cid) ? (allCards[cid].Name ?? "") : "";
                    // Escape CSV — só wrap em quotes se tiver vírgula/aspa
                    if (name.IndexOf(',') >= 0 || name.IndexOf('"') >= 0)
                        name = "\"" + name.Replace("\"", "\"\"") + "\"";
                    sw.WriteLine(cid + "," + name);
                }
            }

            Console.WriteLine("[CardImageValidator] checked " + allCards.Count +
                " cards, " + missing.Count + " missing image. Output:");
            Console.WriteLine("  " + txtPath);
            Console.WriteLine("  " + csvPath);
        }
    }
}
