using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YgoMaster.Builder
{
    // Port de scripts/_solo_helpers — escreve nos arquivos texto do
    // jogo que precisam de patch quando uma gate é gerada:
    //
    //   - DataLE/ClientData/IDS/IDS_SOLO.txt — strings dos gates +
    //     chapters. Tem um bloco delimitado por BEGIN/END que é
    //     re-escrito por inteiro a cada gen.
    //   - DataLE/ClientData/SoloGateCards.txt — entry "gateId, cardId,
    //     uvY, uvY2" que define a carta de capa de cada gate. Uma
    //     linha por gate; substitui a existente.
    //
    // Operações são idempotentes (rodar de novo produz o mesmo
    // arquivo). Arquivo é criado se não existir.
    static class BakedFilePatcher
    {
        const string IdsSoloBlockBegin = "# === BEGIN goat-builder block ===";
        const string IdsSoloBlockEnd   = "# === END goat-builder block ===";

        public static string IdsSoloPath(string dataDirectory)
        {
            return Path.Combine(dataDirectory, "ClientData", "IDS", "IDS_SOLO.txt");
        }

        public static string SoloGateCardsPath(string dataDirectory)
        {
            return Path.Combine(dataDirectory, "ClientData", "SoloGateCards.txt");
        }

        // Substitui o bloco goat-builder em IDS_SOLO.txt por `newLines`.
        // Tudo fora do bloco BEGIN/END é preservado.
        //
        // Defensive parsing — versões antigas de scripts podem ter:
        //   * BEGINs/ENDs grudados com texto vizinho na mesma linha
        //   * Múltiplos BEGINs (sem END entre eles)
        //   * Bloco sem END terminal
        // Aqui usamos `Contains` em vez de comparação exata, e removemos
        // TODOS os blocos detectados — não só o último. Resultado: o
        // arquivo fica com no máximo UM bloco goat-builder após patch,
        // independente do estado de entrada.
        public static void PatchIdsSoloBlock(string dataDirectory, IList<string> newLines)
        {
            string path = IdsSoloPath(dataDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string existing = File.Exists(path) ? File.ReadAllText(path) : "";

            // Pré-processamento: se um marker estiver grudado com texto
            // na mesma linha, quebra em linhas separadas. Senão a regra
            // de match abaixo nunca limpa o legado.
            existing = existing
                .Replace(IdsSoloBlockBegin, "\n" + IdsSoloBlockBegin + "\n")
                .Replace(IdsSoloBlockEnd,   "\n" + IdsSoloBlockEnd   + "\n");

            List<string> outLines = new List<string>();
            bool inside = false;
            foreach (string raw in existing.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();
                if (trimmed == IdsSoloBlockBegin) { inside = true;  continue; }
                if (trimmed == IdsSoloBlockEnd)   { inside = false; continue; }
                if (!inside) outLines.Add(line);
            }
            // Remove blank trailing lines (vamos re-adicionar antes do bloco).
            while (outLines.Count > 0 && outLines[outLines.Count - 1] == "")
                outLines.RemoveAt(outLines.Count - 1);
            if (outLines.Count > 0) outLines.Add("");
            outLines.Add(IdsSoloBlockBegin);
            if (newLines != null) outLines.AddRange(newLines);
            outLines.Add(IdsSoloBlockEnd);

            File.WriteAllText(path, string.Join("\n", outLines) + "\n", new UTF8Encoding(false));
        }

        // Define a cover card pra um gate em SoloGateCards.txt. Remove
        // qualquer entry pré-existente do mesmo gateId e adiciona a nova.
        // Outras entries ficam intactas.
        //
        // uvY / uvYOther ajustam o crop vertical da arte (0=topo, 1=base).
        // 0.20/0.15 enquadra a maioria dos boss monsters mid-body.
        public static void SetGateCoverCard(string dataDirectory, int gateId, int cardId,
                                            double uvY = 0.20, double uvYOther = 0.15)
        {
            string path = SoloGateCardsPath(dataDirectory);
            if (!Directory.Exists(Path.GetDirectoryName(path))) return;

            List<string> outLines = new List<string>();
            if (File.Exists(path))
            {
                foreach (string raw in File.ReadAllText(path).Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) { outLines.Add(line); continue; }
                    int comma = trimmed.IndexOf(',');
                    string firstField = comma >= 0 ? trimmed.Substring(0, comma) : trimmed;
                    int firstId;
                    if (!int.TryParse(firstField, out firstId)) { outLines.Add(line); continue; }
                    if (firstId == gateId) continue;   // drop existing entry pro esse gate
                    outLines.Add(line);
                }
                // Tira blank trailing antes de re-add.
                while (outLines.Count > 0 && outLines[outLines.Count - 1] == "")
                    outLines.RemoveAt(outLines.Count - 1);
            }
            outLines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3}", gateId, cardId, uvY, uvYOther));
            File.WriteAllText(path, string.Join("\n", outLines) + "\n", new UTF8Encoding(false));
        }
    }
}
