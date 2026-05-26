using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Optional registry of named action trees (DataLE/Roguelike/Actions.json).
    // Encounters can reference an entry by name instead of inlining the tree:
    //   "action": "treasureChest"   -> resolved to Actions.json["treasureChest"]
    //   "action": { ... }           -> inline (unchanged)
    //   "action": null              -> no action
    //   "action" absent             -> caller may apply a type default
    //
    // String refs are resolved at load time (fail-fast on unknown names or cycles).
    // Resolved trees are mutated in-place: the same library entry referenced from N
    // encounters is shared (no deep clone) since the engine only reads it.
    static class RoguelikeActions
    {
        static Dictionary<string, Dictionary<string, object>> _lib;

        public static Dictionary<string, Dictionary<string, object>> Load(string dataDirectory)
        {
            if (_lib != null) return _lib;
            _lib = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            string p = Path.Combine(dataDirectory, "Roguelike", "Actions.json");
            if (!File.Exists(p)) return _lib; // optional file
            Dictionary<string, object> doc = null;
            try { doc = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>; }
            catch (Exception ex) { Console.WriteLine("[Roguelike] Actions.json parse EX: " + ex.Message); return _lib; }
            if (doc == null) return _lib;
            foreach (KeyValuePair<string, object> kv in doc)
            {
                Dictionary<string, object> tree = kv.Value as Dictionary<string, object>;
                if (tree == null) { Console.WriteLine("[Roguelike] Actions.json entry '" + kv.Key + "' must be object, skipped"); continue; }
                _lib[kv.Key] = tree;
            }
            return _lib;
        }

        // Resolve a single action ref. `raw` is whatever sits at `"action"` / `"next"`
        // (object | string | null). Returns the resolved tree or null. Throws on unknown
        // names or cycles. Idempotent: re-resolving an already-resolved tree is a no-op.
        public static Dictionary<string, object> Resolve(object raw, Dictionary<string, Dictionary<string, object>> lib, HashSet<string> visiting)
        {
            if (raw == null) return null;
            if (raw is string)
            {
                string name = (string)raw;
                if (string.IsNullOrEmpty(name)) return null;
                Dictionary<string, object> tree;
                if (lib == null || !lib.TryGetValue(name, out tree))
                    throw new Exception("action ref '" + name + "' not found in Actions.json");
                if (visiting.Contains(name))
                    throw new Exception("action cycle detected via '" + name + "'");
                visiting.Add(name);
                try { ResolveTreeInPlace(tree, lib, visiting); }
                finally { visiting.Remove(name); }
                return tree;
            }
            Dictionary<string, object> node = raw as Dictionary<string, object>;
            if (node == null) throw new Exception("action must be string ref, object, or null (got " + raw.GetType().Name + ")");
            ResolveTreeInPlace(node, lib, visiting);
            return node;
        }

        // Walk an action tree and resolve every `next` (the universal continuation key) when it
        // is a string. In-place mutation of `node`. Applies to:
        //   - `options[i].next` (the action that follows when option i is chosen)
        //   - `openpack.next`   (the action that follows after the pack is finalized)
        //   - `message.next`    (the action that follows after the OK is acknowledged)
        static void ResolveTreeInPlace(Dictionary<string, object> node, Dictionary<string, Dictionary<string, object>> lib, HashSet<string> visiting)
        {
            if (node == null) return;
            string type = Utils.GetValue<string>(node, "type", "");
            if (type == "options")
            {
                List<object> opts = Utils.GetValue<List<object>>(node, "options");
                if (opts != null)
                {
                    foreach (object o in opts)
                    {
                        Dictionary<string, object> od = o as Dictionary<string, object>;
                        if (od == null) continue;
                        object sub;
                        if (od.TryGetValue("next", out sub))
                            od["next"] = Resolve(sub, lib, visiting);
                    }
                }
            }
            else
            {
                // openpack / message / unknown kinds: all may carry `next` as a continuation.
                object nxt;
                if (node.TryGetValue("next", out nxt))
                    node["next"] = Resolve(nxt, lib, visiting);
            }
        }
    }
}
