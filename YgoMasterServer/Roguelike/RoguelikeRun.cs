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
        public List<object> DeckOffers;            // rolled starter-deck file paths (rel); expanded on the wire
        public Dictionary<string, object> Deck;    // {name, bossCard, deck:{Main,Extra,Side}} or null
        public Dictionary<string, object> Cards;   // run-owned cards (Player.Cards shape: {cid:{st,r,n,...,tn}}); deck pick + rewards
        public Dictionary<string, object> Map;     // RoguelikeMap.ToDictionary() or null
        public int Position = -1;                   // current node id (-1 = entry, before row 0)
        public List<object> Visited;               // ids already walked
        public int Currency;                        // run currency, credited on duel win
        public int PendingDuelNode = -1;            // combat node whose duel is in progress (-1 = none)
        public string PendingEncounterId = "";      // encounter chosen for the in-progress duel (reward lookup)
        public int Lp;                              // current run LP (= player's starting LP each duel)
        public int MaxLp;                           // LP cap (snapshot of playerMaxLp at run start)
        public int Act;                             // current act (0-based)
        public int Ascension;                       // ascension tier this run is played at
        public bool Won;                            // set when the final act's boss falls

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "version", Version }, { "active", Active }, { "gameType", GameType },
                { "seed", Seed }, { "createdAt", CreatedAt },
                { "deckChosen", DeckChosen },
                { "deckOffers", DeckOffers ?? new List<object>() },
                { "deck", Deck },
                { "Cards", Cards ?? new Dictionary<string, object>() },
                { "map", Map },
                { "position", Position },
                { "visited", Visited ?? new List<object>() },
                { "currency", Currency },
                { "pendingDuelNode", PendingDuelNode },
                { "pendingEncounterId", PendingEncounterId ?? "" },
                { "lp", Lp },
                { "maxLp", MaxLp },
                { "act", Act },
                { "ascension", Ascension },
                { "won", Won },
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
                Cards      = Utils.GetValue<Dictionary<string, object>>(d, "Cards"),
                Map        = Utils.GetValue<Dictionary<string, object>>(d, "map"),
                Position   = Utils.GetValue<int>(d, "position", -1),
                Visited    = Utils.GetValue<List<object>>(d, "visited"),
                Currency   = Utils.GetValue<int>(d, "currency", 0),
                PendingDuelNode = Utils.GetValue<int>(d, "pendingDuelNode", -1),
                PendingEncounterId = Utils.GetValue<string>(d, "pendingEncounterId", ""),
                Lp         = Utils.GetValue<int>(d, "lp", Utils.GetValue<int>(d, "hp", 0)),    // hp = legacy key
                MaxLp      = Utils.GetValue<int>(d, "maxLp", Utils.GetValue<int>(d, "maxHp", 0)),
                Act        = Utils.GetValue<int>(d, "act", 0),
                Ascension  = Utils.GetValue<int>(d, "ascension", 0),
                Won        = Utils.GetValue<bool>(d, "won", false),
            };
        }

        // Add `count` normal copies of a card to the run-owned collection (Player.Cards shape). Used
        // on deck pick (deck multiplicity) and by the card-reward flow. Preserves any styled counts.
        public void AddCard(int cardId, int count = 1)
        {
            if (count <= 0) return;
            if (Cards == null) Cards = new Dictionary<string, object>();
            string key = cardId.ToString();
            Dictionary<string, object> e = Utils.GetValue<Dictionary<string, object>>(Cards, key) ?? new Dictionary<string, object>();
            // NoDismantle (p_n) so the deck editor disables the craft/dismantle button for run cards
            // natively — same as how the player's own deck cards are added.
            int n     = Utils.GetValue<int>(e, "n");
            int p1n   = Utils.GetValue<int>(e, "p1n");
            int p2n   = Utils.GetValue<int>(e, "p2n");
            int p_n   = Utils.GetValue<int>(e, "p_n") + count;
            int p_p1n = Utils.GetValue<int>(e, "p_p1n");
            int p_p2n = Utils.GetValue<int>(e, "p_p2n");
            Cards[key] = new Dictionary<string, object>
            {
                { "st", Utils.GetEpochTime() },
                { "r", 1 },
                { "n", n }, { "p1n", p1n }, { "p2n", p2n },
                { "p_n", p_n }, { "p_p1n", p_p1n }, { "p_p2n", p_p2n },
                { "tn", n + p1n + p2n + p_n + p_p1n + p_p2n },
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
