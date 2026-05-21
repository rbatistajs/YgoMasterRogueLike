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
    }
}
