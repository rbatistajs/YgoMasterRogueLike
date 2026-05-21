using System;
using System.Collections.Generic;
using IL2CPP;

namespace YgoMasterClient
{
    // Server-owned Roguelike run state. Reads come from $.Roguelike in ClientWork (the
    // server piggybacks the state into the home response); writes issue a custom act via
    // Request.Entry — the same call the game uses to talk to the server.
    static unsafe class RoguelikeApi
    {
        static IL2Method methodEntry;

        static RoguelikeApi()
        {
            try
            {
                IL2Class requestClass = Assembler.GetAssembly("Assembly-CSharp")
                    .GetClass("Request", "YgomSystem.Network");
                methodEntry = requestClass.GetMethod("Entry", m => m.GetParameters().Length == 3);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Roguelike] api init EX: " + ex);
            }
        }

        // True when the server reports an active run (populated on home load).
        public static bool IsRunActive()
        {
            return YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Roguelike.active");
        }

        // Issue a server act on demand: Request.Entry(act, params, timeout). The response
        // lands in ClientWork ($.Roguelike) and fires the RequestStructure.Complete hook.
        public static void Call(string act, Dictionary<string, object> args = null)
        {
            if (methodEntry == null) return;
            if (args == null) args = new Dictionary<string, object>();
            IntPtr argsPtr = YgomMiniJSON.Json.Deserialize(MiniJSON.Json.Serialize(args));
            float timeout = 30f;
            methodEntry.Invoke(new IntPtr[] { new IL2String(act).ptr, argsPtr, new IntPtr(&timeout) });
        }

        public static void StartRun() { Call("Roguelike.start_run"); }
        public static void AbandonRun() { Call("Roguelike.abandon_run"); }
        public static void ChooseDeck(int index)
        {
            Call("Roguelike.choose_deck", new Dictionary<string, object> { { "index", index } });
        }

        // True when the player has finalized a deck for the active run.
        public static bool IsDeckChosen()
        {
            return YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Roguelike.deckChosen");
        }

        // Names of the (up to 3) decks offered for the pending run.
        public static string[] GetDeckOfferNames()
        {
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.deckOffers");
                if (string.IsNullOrEmpty(json)) return new string[0];
                List<object> list = MiniJSON.Json.Deserialize(json) as List<object>;
                if (list == null) return new string[0];
                List<string> names = new List<string>();
                foreach (object o in list)
                {
                    Dictionary<string, object> item = o as Dictionary<string, object>;
                    names.Add(item != null && item.ContainsKey("name") ? Convert.ToString(item["name"]) : "Deck");
                }
                return names.ToArray();
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] offer names EX: " + ex); return new string[0]; }
        }

        // One offered deck (name + boss card + description + card-id lists).
        public class DeckOffer
        {
            public string Name = "Deck";
            public int BossCard;
            public string Description = "";
            public List<int> Main = new List<int>();
            public List<int> Extra = new List<int>();
            public List<int> Side = new List<int>();
        }

        // Full detail of the (up to 3) pending offers, read from $.Roguelike.deckOffers.
        public static List<DeckOffer> GetDeckOffers()
        {
            List<DeckOffer> result = new List<DeckOffer>();
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.deckOffers");
                if (string.IsNullOrEmpty(json)) return result;
                List<object> list = MiniJSON.Json.Deserialize(json) as List<object>;
                if (list == null) return result;
                foreach (object o in list)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    DeckOffer offer = new DeckOffer
                    {
                        Name = d.ContainsKey("name") ? Convert.ToString(d["name"]) : "Deck",
                        BossCard = d.ContainsKey("bossCard") ? Convert.ToInt32(d["bossCard"]) : 0,
                        Description = d.ContainsKey("description") ? Convert.ToString(d["description"]) : "",
                    };
                    ReadIds(d, "main", offer.Main);
                    ReadIds(d, "extra", offer.Extra);
                    ReadIds(d, "side", offer.Side);
                    result.Add(offer);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] GetDeckOffers EX: " + ex); }
            return result;
        }

        static void ReadIds(Dictionary<string, object> d, string key, List<int> into)
        {
            List<object> arr = d.ContainsKey(key) ? d[key] as List<object> : null;
            if (arr == null) return;
            foreach (object v in arr) { try { into.Add(Convert.ToInt32(v)); } catch { } }
        }
    }
}
