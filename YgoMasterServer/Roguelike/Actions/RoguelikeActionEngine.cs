using System.Collections.Generic;

namespace YgoMaster
{
    // Server-authoritative walker over an action tree. v1 understands two node types:
    //   options : { type, text, options:[ { label, action } ] }  -> branch (await a choice)
    //   message : { type, text }                                 -> terminal (await OK)
    // The cursor (run.PendingAction) is the node currently presented; ActionToken bumps on
    // each new prompt so the client renders it once. Project() builds the thin wire payload.
    static class RoguelikeActionEngine
    {
        // Begin an action: set the root as the cursor, then settle on the first UI node.
        public static void Start(RoguelikeRun run, Dictionary<string, object> action)
        {
            SetPending(run, action);
            Step(run);
        }

        // Resolve the current prompt with the player's choice, then settle on the next UI node.
        public static void Respond(RoguelikeRun run, int choice)
        {
            Dictionary<string, object> cur = run.PendingAction;
            if (cur == null) return;
            string type = Utils.GetValue<string>(cur, "type", "");
            if (type == "options")
            {
                List<object> opts = Utils.GetValue<List<object>>(cur, "options");
                Dictionary<string, object> chosen = (opts != null && choice >= 0 && choice < opts.Count)
                    ? opts[choice] as Dictionary<string, object> : null;
                SetPending(run, chosen != null ? Utils.GetValue<Dictionary<string, object>>(chosen, "action") : null);
            }
            else
            {
                SetPending(run, null); // message OK (or unknown) -> done
            }
            Step(run);
        }

        // Advance through non-UI nodes; stop on a UI node (options/message) or when finished.
        static void Step(RoguelikeRun run)
        {
            while (run.PendingAction != null)
            {
                string type = Utils.GetValue<string>(run.PendingAction, "type", "");
                if (type == "options" || type == "message") return; // needs UI; Project() will emit it
                // v2: apply a state-mutating leaf here, then SetPending(next) or null.
                SetPending(run, null); // v1: unknown leaf -> end
            }
        }

        static void SetPending(RoguelikeRun run, Dictionary<string, object> node)
        {
            run.PendingAction = node;
            if (node != null) run.ActionToken++;
        }

        // Thin prompt for the wire ($.Roguelike.action), or null when nothing is pending.
        public static Dictionary<string, object> Project(RoguelikeRun run)
        {
            Dictionary<string, object> cur = run.PendingAction;
            if (cur == null) return null;
            string type = Utils.GetValue<string>(cur, "type", "");
            Dictionary<string, object> p = new Dictionary<string, object>
            {
                { "token", run.ActionToken },
                { "type", type },
                // title = header, message = body. `text` is a legacy alias for title.
                { "title", Utils.GetValue<string>(cur, "title", Utils.GetValue<string>(cur, "text", "")) },
                { "message", Utils.GetValue<string>(cur, "message", "") },
            };
            if (type == "options")
            {
                List<object> labels = new List<object>();
                List<object> opts = Utils.GetValue<List<object>>(cur, "options");
                if (opts != null)
                    foreach (object o in opts)
                    {
                        Dictionary<string, object> od = o as Dictionary<string, object>;
                        labels.Add(od != null ? Utils.GetValue<string>(od, "label", "") : "");
                    }
                p["options"] = labels;
            }
            return p;
        }
    }
}
