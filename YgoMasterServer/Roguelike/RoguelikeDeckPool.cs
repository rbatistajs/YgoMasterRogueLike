using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Roguelike-owned starter-deck loader. Reads player-format deck JSONs from
    // DataLE/Roguelike/StartingDecks. Display name = file name; boss card = `boss_card`
    // field or the first main-deck id. (Inspired by Goat's DeckPoolLoader; not reused.)
    static class RoguelikeDeckPool
    {
        public class StarterDeck
        {
            public string Name;                       // file name (no extension)
            public int BossCard;
            public string Description;                // optional `description` field
            public Dictionary<string, object> Json;   // raw player-format deck dict
        }

        // All .json files under <dataDirectory>/Roguelike/StartingDecks (full paths).
        public static List<string> ListFiles(string dataDirectory)
        {
            string dir = Path.Combine(dataDirectory, "Roguelike", "StartingDecks");
            if (!Directory.Exists(dir)) return new List<string>();
            return new List<string>(Directory.GetFiles(dir, "*.json"));
        }

        // Enemy decks under <dataDirectory>/Roguelike/Opponents (json or ydk). Full paths.
        public static List<string> ListOpponentFiles(string dataDirectory)
        {
            string dir = Path.Combine(dataDirectory, "Roguelike", "Opponents");
            if (!Directory.Exists(dir)) return new List<string>();
            List<string> files = new List<string>(Directory.GetFiles(dir, "*.json"));
            files.AddRange(Directory.GetFiles(dir, "*.ydk"));
            return files;
        }

        public static StarterDeck LoadOne(string fullPath)
        {
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(fullPath)) as Dictionary<string, object>;
            if (doc == null) return null;
            return new StarterDeck
            {
                Name = Path.GetFileNameWithoutExtension(fullPath),
                BossCard = ResolveBossCard(doc),
                Description = Utils.GetValue<string>(doc, "description", ""),
                Json = doc,
            };
        }

        static int ResolveBossCard(Dictionary<string, object> doc)
        {
            int explicitId = Utils.GetValue<int>(doc, "boss_card");
            if (explicitId > 0) return explicitId;
            Dictionary<string, object> main = Utils.GetValue<Dictionary<string, object>>(doc, "m");
            List<object> ids = main != null ? Utils.GetValue<List<object>>(main, "ids") : null;
            if (ids == null || ids.Count == 0) return 0;
            try { return Convert.ToInt32(ids[0]); } catch { return 0; }
        }
    }
}
