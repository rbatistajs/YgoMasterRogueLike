using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YgoMaster
{
    // Goat: loads player-format deck JSONs from a directory and converts
    // them into the SoloDuel deck shape so they can be embedded directly
    // into a generated DuelSettings.
    //
    // Player format (e.g. `decks/normal/3/Burn.json`):
    //   { "name": "...", "m": { "ids": [...], "r": [...] }, "e": {...}, "s": {...} }
    //
    // SoloDuel format (what DuelSettings.FromDictionary expects):
    //   { "Main": { "CardIds": [...], "Rare": [...] }, "Extra": {...}, "Side": {...} }
    static class DeckPoolLoader
    {
        public class LoadedDeck
        {
            public string Name;
            public Dictionary<string, object> SoloDuelDeck;   // ready to drop into DuelSettings Deck[i]
            // Goat: cardId da "carta principal" do deck — usada como
            // imagem do chapter (`card_image`) e futuramente como reward
            // do boss / cover card. Lê do field `boss_card` do JSON; se
            // ausente, usa o primeiro cid do main deck.
            public int BossCard;
        }

        // Carrega TODOS os pools por level (decks/<duelType>/0..6/) usado
        // pelo bake (per-chapter level-based deck selection). Retorna
        // dict {level: [decks]} — levels vazios omitidos.
        public static Dictionary<int, List<LoadedDeck>> LoadByLevels(
            string dataDirectory, string duelType)
        {
            string sub = string.Equals(duelType, "Rush", StringComparison.OrdinalIgnoreCase)
                ? "decks/rush" : "decks/normal";
            Dictionary<int, List<LoadedDeck>> result = new Dictionary<int, List<LoadedDeck>>();
            for (int level = 0; level <= 6; level++)
            {
                List<LoadedDeck> decks = LoadAll(dataDirectory, sub + "/" + level);
                if (decks.Count > 0) result[level] = decks;
            }
            return result;
        }

        // Pega um pool com fallback pra levels adjacentes quando o
        // pedido está vazio. Procura level exato primeiro, depois +/-1,
        // +/-2... até level 0..6.
        public static List<LoadedDeck> PoolForLevel(
            Dictionary<int, List<LoadedDeck>> byLevel, int level)
        {
            if (byLevel == null || byLevel.Count == 0) return null;
            for (int delta = 0; delta <= 6; delta++)
            {
                int lo = level - delta, hi = level + delta;
                List<LoadedDeck> pool;
                if (lo >= 0 && byLevel.TryGetValue(lo, out pool) && pool.Count > 0) return pool;
                if (delta > 0 && hi <= 6 && byLevel.TryGetValue(hi, out pool) && pool.Count > 0) return pool;
            }
            return null;
        }

        // Reads every `.json` deck from the directory (resolved relative
        // to the data root if not absolute). Returns empty list if the
        // directory doesn't exist.
        public static List<LoadedDeck> LoadAll(string dataDirectory, string deckPool)
        {
            List<LoadedDeck> decks = new List<LoadedDeck>();
            if (string.IsNullOrEmpty(deckPool)) return decks;

            string dir = ResolvePath(dataDirectory, deckPool);
            if (!Directory.Exists(dir))
            {
                Console.WriteLine("[DeckPoolLoader] deck pool not found: " + dir);
                return decks;
            }

            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    LoadedDeck d = LoadOne(file);
                    if (d != null) decks.Add(d);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[DeckPoolLoader] skipping " + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            return decks;
        }

        public static LoadedDeck LoadOne(string path)
        {
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (doc == null) return null;

            Dictionary<string, object> deck = new Dictionary<string, object>
            {
                { "Main",  ConvertSection(doc, "m") },
                { "Extra", ConvertSection(doc, "e") },
                { "Side",  ConvertSection(doc, "s") },
            };
            return new LoadedDeck
            {
                Name = Utils.GetValue<string>(doc, "name", Path.GetFileNameWithoutExtension(path)),
                SoloDuelDeck = deck,
                BossCard = ResolveBossCard(doc),
            };
        }

        // Lê `boss_card` da raiz do deck JSON. Fallback: primeiro cid do
        // main deck (m.ids[0]) — quase sempre é a carta-vedete do deck
        // (deck construído manualmente costuma listar a "headline" no
        // topo do main).
        static int ResolveBossCard(Dictionary<string, object> doc)
        {
            int explicit_ = Utils.GetValue<int>(doc, "boss_card");
            if (explicit_ > 0) return explicit_;

            Dictionary<string, object> main = Utils.GetValue<Dictionary<string, object>>(doc, "m");
            List<object> ids = main != null ? Utils.GetValue<List<object>>(main, "ids") : null;
            if (ids == null || ids.Count == 0) return 0;
            try { return Convert.ToInt32(ids[0]); } catch { return 0; }
        }

        // Translates the player-deck `{ids: [...], r: [...]}` shape into
        // the SoloDuel `{CardIds: [...], Rare: [...]}` shape. Missing
        // section → empty.
        static Dictionary<string, object> ConvertSection(Dictionary<string, object> player, string key)
        {
            Dictionary<string, object> section = Utils.GetValue<Dictionary<string, object>>(player, key);
            List<object> ids = section != null ? Utils.GetValue<List<object>>(section, "ids") : null;
            List<object> rare = section != null ? Utils.GetValue<List<object>>(section, "r") : null;
            return new Dictionary<string, object>
            {
                { "CardIds", ids  ?? new List<object>() },
                { "Rare",    rare ?? new List<object>() },
            };
        }

        // Resolves the deck pool path. Absolute paths used as-is; relative
        // paths are anchored to `dataDirectory`, so `decks/normal/3`
        // resolves to `<install>/DataLE/decks/normal/3`.
        static string ResolvePath(string dataDirectory, string deckPool)
        {
            if (Path.IsPathRooted(deckPool)) return deckPool;
            return Path.Combine(dataDirectory, deckPool);
        }
    }
}
