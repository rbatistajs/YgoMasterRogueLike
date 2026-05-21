using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    class RoguelikeRun
    {
        public int Version = 1;
        public bool Active;
        public string GameType = "base_deck";
        public long Seed;
        public string CreatedAt;
        public bool DeckChosen;
        public List<object> DeckOffers;            // each item: {name, bossCard, file}
        public Dictionary<string, object> Deck;    // {name, bossCard, deck:{Main,Extra,Side}} or null

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "version", Version }, { "active", Active }, { "gameType", GameType },
                { "seed", Seed }, { "createdAt", CreatedAt },
                { "deckChosen", DeckChosen },
                { "deckOffers", DeckOffers ?? new List<object>() },
                { "deck", Deck },
            };
        }

        public static RoguelikeRun FromDictionary(Dictionary<string, object> d)
        {
            if (d == null) return new RoguelikeRun { Active = false };
            return new RoguelikeRun
            {
                Version   = Utils.GetValue<int>(d, "version", 1),
                Active    = Utils.GetValue<bool>(d, "active", false),
                GameType  = Utils.GetValue<string>(d, "gameType", "base_deck"),
                Seed      = Utils.GetValue<long>(d, "seed", 0),
                CreatedAt = Utils.GetValue<string>(d, "createdAt", null),
                DeckChosen = Utils.GetValue<bool>(d, "deckChosen", false),
                DeckOffers = Utils.GetValue<List<object>>(d, "deckOffers"),
                Deck       = Utils.GetValue<Dictionary<string, object>>(d, "deck"),
            };
        }

        public static string PathFor(string playerDir) => Path.Combine(playerDir, "roguelike.json");

        public static RoguelikeRun Load(string playerDir)
        {
            string p = PathFor(playerDir);
            if (!File.Exists(p)) return new RoguelikeRun { Active = false };
            try
            {
                var d = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>;
                return FromDictionary(d);
            }
            catch { return new RoguelikeRun { Active = false }; }
        }

        public void Save(string playerDir)
        {
            File.WriteAllText(PathFor(playerDir), MiniJSON.Json.Format(MiniJSON.Json.Serialize(ToDictionary())));
        }

        public static void Delete(string playerDir)
        {
            string p = PathFor(playerDir);
            if (File.Exists(p)) File.Delete(p);
        }
    }
}
