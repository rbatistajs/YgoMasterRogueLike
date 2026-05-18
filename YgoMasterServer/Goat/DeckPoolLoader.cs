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
            };
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
        // paths are anchored to the install root (parent of `dataDirectory`,
        // so `decks/normal/3` resolves to `<install>/decks/normal/3`).
        static string ResolvePath(string dataDirectory, string deckPool)
        {
            if (Path.IsPathRooted(deckPool)) return deckPool;
            string installRoot = Path.GetDirectoryName(dataDirectory) ?? dataDirectory;
            return Path.Combine(installRoot, deckPool);
        }
    }
}
