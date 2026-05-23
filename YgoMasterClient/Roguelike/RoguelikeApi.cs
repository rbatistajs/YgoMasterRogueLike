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

        public static void StartRun(int ascension = 0)
        {
            Call("Roguelike.start_run", new Dictionary<string, object> { { "ascension", ascension } });
        }
        public static void AbandonRun() { Call("Roguelike.abandon_run"); }

        // ----- acts / ascension -----
        public static int Act() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.act"); }
        public static int Acts() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.acts"); }
        public static int Ascension() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.ascension"); }
        public static int MaxAscension() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.maxAscension"); }
        public static bool Won() { return YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Roguelike.won"); }
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

        // One offered deck, in the game's deck-list-item shape (so a native DeckReference can
        // be built from it).
        public class DeckOffer
        {
            public string Name = "Deck";
            public string Description = "";
            public List<int> PickCards = new List<int>(); // the 3 cover cards (pick_cards.ids)
            public List<int> PickDecos = new List<int>(); // their treatment/foil (pick_cards.r)
            public List<int> Main = new List<int>();      // main-deck card ids (for the viewer)
            public List<int> Extra = new List<int>();     // extra-deck card ids (for the viewer)
            public int Box, Sleeve, Field, Object;        // deck-box accessory (case/protector/...)
            public int DeckId;                            // synthetic id (deck_id) for the cell
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
                        Description = d.ContainsKey("description") ? Convert.ToString(d["description"]) : "",
                        DeckId = d.ContainsKey("deck_id") ? Convert.ToInt32(d["deck_id"]) : 0,
                    };
                    // accessory + pick_cards arrive in the game's nested deck-list-item shape.
                    Dictionary<string, object> acc = d.ContainsKey("accessory") ? d["accessory"] as Dictionary<string, object> : null;
                    if (acc != null)
                    {
                        offer.Box = AccInt(acc, "box");
                        offer.Sleeve = AccInt(acc, "sleeve");
                        offer.Field = AccInt(acc, "field");
                        offer.Object = AccInt(acc, "object");
                    }
                    Dictionary<string, object> pc = d.ContainsKey("pick_cards") ? d["pick_cards"] as Dictionary<string, object> : null;
                    ReadIndexed(pc, "ids", offer.PickCards);
                    ReadIndexed(pc, "r", offer.PickDecos);
                    ReadList(d, "main", offer.Main);
                    ReadList(d, "extra", offer.Extra);
                    result.Add(offer);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] GetDeckOffers EX: " + ex); }
            return result;
        }

        static int AccInt(Dictionary<string, object> d, string k)
        {
            try { return d.ContainsKey(k) ? Convert.ToInt32(d[k]) : 0; } catch { return 0; }
        }

        static void ReadList(Dictionary<string, object> d, string key, List<int> into)
        {
            List<object> arr = d.ContainsKey(key) ? d[key] as List<object> : null;
            if (arr == null) return;
            foreach (object v in arr) { try { into.Add(Convert.ToInt32(v)); } catch { } }
        }

        // Read an indexed sub-dict ({"1":a,"2":b,"3":c}) in 1..3 order into a list.
        static void ReadIndexed(Dictionary<string, object> parent, string key, List<int> into)
        {
            Dictionary<string, object> m = parent != null && parent.ContainsKey(key) ? parent[key] as Dictionary<string, object> : null;
            if (m == null) return;
            for (int k = 1; k <= 3; k++)
            {
                string kk = k.ToString();
                if (m.ContainsKey(kk)) { try { into.Add(Convert.ToInt32(m[kk])); } catch { } }
            }
        }

        // ----- map (M3) -----
        public class MapNode
        {
            public int Id, Row, Col;
            public string Type = "duel";
            public List<int> Next = new List<int>();
        }

        // Current node id (-1 = entry, before row 0).
        public static int Position()
        {
            return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.position");
        }

        // Ids of nodes already walked (includes the current position).
        public static HashSet<int> Visited()
        {
            HashSet<int> set = new HashSet<int>();
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.visited");
                if (string.IsNullOrEmpty(json)) return set;
                List<object> list = MiniJSON.Json.Deserialize(json) as List<object>;
                if (list == null) return set;
                foreach (object v in list) { try { set.Add(Convert.ToInt32(v)); } catch { } }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] Visited EX: " + ex); }
            return set;
        }

        public static List<MapNode> GetMapNodes()
        {
            List<MapNode> result = new List<MapNode>();
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.map");
                if (string.IsNullOrEmpty(json)) return result;
                Dictionary<string, object> map = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
                List<object> nodes = map != null && map.ContainsKey("nodes") ? map["nodes"] as List<object> : null;
                if (nodes == null) return result;
                foreach (object o in nodes)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    MapNode n = new MapNode
                    {
                        Id = d.ContainsKey("id") ? Convert.ToInt32(d["id"]) : -1,
                        Type = d.ContainsKey("type") ? Convert.ToString(d["type"]) : "duel",
                        Row = d.ContainsKey("row") ? Convert.ToInt32(d["row"]) : 0,
                        Col = d.ContainsKey("col") ? Convert.ToInt32(d["col"]) : 0,
                    };
                    List<object> next = d.ContainsKey("next") ? d["next"] as List<object> : null;
                    if (next != null) foreach (object v in next) { try { n.Next.Add(Convert.ToInt32(v)); } catch { } }
                    result.Add(n);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] GetMapNodes EX: " + ex); }
            return result;
        }

        public static void Move(int nodeId)
        {
            Call("Roguelike.move", new Dictionary<string, object> { { "nodeId", nodeId } });
        }

        // ----- combat (M4) -----

        // Combat node whose duel the server queued in this move's response (-1 = none). When
        // set, $.Duel holds the duel settings ready for Solo/SoloStartProduction.
        public static int PendingDuelNode()
        {
            return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.pendingDuelNode");
        }

        // Report a finished combat duel: win/loss + the player's remaining LP. Server carries the LP
        // into run HP (win, + heal) or ends the run (loss).
        public static void SendDuelResult(bool win, int playerLp)
        {
            Call("Roguelike.duel_result", new Dictionary<string, object> { { "win", win }, { "playerLp", playerLp } });
        }

        // Re-fetch the pending combat duel (same seed) so the client can relaunch an unfinished one.
        public static void ResumeDuel() { Call("Roguelike.resume_duel"); }

        // Current run HP / cap (for the map HP indicator).
        public static int Hp() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.hp"); }
        public static int MaxHp() { return YgomSystem.Utility.ClientWork.GetByJsonPath<int>("Roguelike.maxHp"); }
    }
}
