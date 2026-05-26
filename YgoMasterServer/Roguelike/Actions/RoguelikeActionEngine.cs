using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Server-authoritative walker over an action tree. v1 understands two node types:
    //   options  : { type, text, options:[ { label, action } ] }  -> branch (await a choice)
    //   message  : { type, text }                                 -> terminal (await OK)
    //   openpack : { type, packs, pick, pulls:[...], next, ... }  -> generate cards, stage in PendingPack
    // The cursor (run.PendingAction) is the node currently presented; ActionToken bumps on
    // each new prompt so the client renders it once. Project() builds the thin wire payload.
    static class RoguelikeActionEngine
    {
        // Begin an action: set the root as the cursor, then settle on the first UI node.
        public static void Start(RoguelikeRun run, Dictionary<string, object> action,
            string dataDirectory = null, Dictionary<string, object> regulation = null)
        {
            string actType = action != null ? Utils.GetValue<string>(action, "type", "") : "(null)";
            Console.WriteLine("[Roguelike] engine.Start: type=" + actType + " dataDir=" + (dataDirectory != null) + " reg=" + (regulation != null));
            SetPending(run, action);
            Step(run, dataDirectory, regulation);
        }

        // Resolve the current prompt with the player's choice, then settle on the next UI node.
        public static void Respond(RoguelikeRun run, int choice,
            string dataDirectory = null, Dictionary<string, object> regulation = null)
        {
            Dictionary<string, object> cur = run.PendingAction;
            if (cur == null) return;
            string type = Utils.GetValue<string>(cur, "type", "");
            if (type == "options")
            {
                List<object> opts = Utils.GetValue<List<object>>(cur, "options");
                Dictionary<string, object> chosen = (opts != null && choice >= 0 && choice < opts.Count)
                    ? opts[choice] as Dictionary<string, object> : null;
                SetPending(run, chosen != null ? Utils.GetValue<Dictionary<string, object>>(chosen, "next") : null);
            }
            else if (type == "message")
            {
                SetPending(run, Utils.GetValue<Dictionary<string, object>>(cur, "next"));
            }
            else
            {
                SetPending(run, null); // unknown -> done
            }
            Step(run, dataDirectory, regulation);
        }

        // Advance through non-UI nodes; stop on a UI node (options/message/openpack) or when finished.
        static void Step(RoguelikeRun run,
            string dataDirectory = null, Dictionary<string, object> regulation = null)
        {
            while (run.PendingAction != null)
            {
                Dictionary<string, object> node = run.PendingAction;
                string type = Utils.GetValue<string>(node, "type", "");
                Console.WriteLine("[Roguelike] engine.Step: type=" + type);
                if (type == "options" || type == "message") return; // needs UI; Project() will emit it
                else if (type == "openpack")
                {
                    // ActionToken was already bumped by SetPending; use it as-is for deterministic RNG.
                    int packs = Utils.GetValue<int>(node, "packs", 1);
                    int pick = Utils.GetValue<int>(node, "pick", 0);
                    List<object> pulls = Utils.GetValue<List<object>>(node, "pulls");
                    Dictionary<string, object> next = Utils.GetValue<Dictionary<string, object>>(node, "next");
                    Console.WriteLine("[Roguelike] openpack enter: packs=" + packs + " pick=" + pick + " pulls=" + (pulls != null ? pulls.Count : 0));

                    // pity: action.pity = false disables; merge global+asc+action
                    object pityRaw;
                    bool pityEnabled = true;
                    Dictionary<string, object> actionPity = null;
                    if (node.TryGetValue("pity", out pityRaw))
                    {
                        if (pityRaw is bool && !(bool)pityRaw) pityEnabled = false;
                        else actionPity = pityRaw as Dictionary<string, object>;
                    }
                    Dictionary<int, RoguelikeCardPool.PityConfig> pityCfg =
                        pityEnabled ? MergePity(RoguelikeCardPool.Pity(dataDirectory, run.Ascension), actionPity) : null;

                    if (run.Pity == null) run.Pity = new Dictionary<string, int>();

                    Random rng = new Random(unchecked((int)(run.Seed ^ ((long)run.ActionToken * 2654435761L)))); // Knuth multiplicative hash
                    HashSet<int> anyPool = RoguelikeCardPool.AnyPool(dataDirectory, regulation, run.Ascension);
                    Console.WriteLine("[Roguelike] openpack anyPool size=" + (anyPool != null ? anyPool.Count : -1));

                    List<Dictionary<string, object>> allCards = new List<Dictionary<string, object>>();
                    for (int packIdx = 0; packIdx < packs; packIdx++)
                    {
                        HashSet<int> usedPack = new HashSet<int>();
                        List<RoguelikeCardPool.DrawResult> packDraws = new List<RoguelikeCardPool.DrawResult>();
                        if (pulls != null)
                            foreach (object pullObj in pulls)
                            {
                                Dictionary<string, object> pull = pullObj as Dictionary<string, object>;
                                if (pull == null) continue;
                                double chance = Utils.GetValue<double>(pull, "chance", 1.0);
                                if (chance < 1.0 && rng.NextDouble() >= chance) continue;
                                int count = Utils.GetValue<int>(pull, "count", 0);
                                Dictionary<string, object> pool = Utils.GetValue<Dictionary<string, object>>(pull, "pool");
                                string source = pool != null ? Utils.GetValue<string>(pool, "source", "any") : "any";
                                // v1: openpack only supports source=any. Other sources log and fall back to any.
                                if (source != "any")
                                {
                                    Console.WriteLine("[Roguelike] openpack pool.source '" + source + "' not supported in v1; falling back to 'any'");
                                }
                                bool weighted = true;
                                HashSet<int> universe = anyPool;

                                // rarityRates: action override + pity bonus, applied on top of layered (global+asc) rates
                                Dictionary<int, double> rrEffective = MergeRarityRatesWithPity(
                                    RoguelikeCardPool.LayeredRarityRates(dataDirectory, run.Ascension),
                                    pool != null ? Utils.GetValue<Dictionary<string, object>>(pool, "rarityRates") : null,
                                    pityCfg, run.Pity);

                                List<RoguelikeCardPool.DrawResult> drawn = RoguelikeCardPool.DrawN(
                                    dataDirectory, universe, pool, count, rng, run.Ascension,
                                    usedPack, rrEffective, weighted);
                                Console.WriteLine("[Roguelike] DrawN: requested=" + count + " got=" + drawn.Count + " (universe=" + universe.Count + ")");
                                packDraws.AddRange(drawn);
                            }
                        foreach (RoguelikeCardPool.DrawResult d in packDraws)
                        {
                            allCards.Add(new Dictionary<string, object>
                            {
                                { "cid", d.Cid }, { "rarity", d.Rarity },
                                { "new", d.IsNew }, { "premium", d.PremiumType },
                                { "packIdx", packIdx }
                            });
                        }
                        // pity tick after the pack (counters accumulate across packs within this openpack node)
                        if (pityCfg != null) UpdatePity(run.Pity, packDraws, pityCfg);
                    }

                    int size = allCards.Count;
                    Console.WriteLine("[Roguelike] openpack staged: packs=" + packs + " size=" + size + " pick=" + pick + " token=" + run.ActionToken);
                    if (size == 0)
                    {
                        // No cards drawn (empty universe / over-filtered pool / weights all zero).
                        // Don't stage an empty pack — advance to `next` (or terminate) instead.
                        Console.WriteLine("[Roguelike] openpack: 0 cards drawn; skipping stage and advancing");
                        SetPending(run, next);
                        continue; // re-enter the while loop on `next` (or fall through if null)
                    }
                    string mode = pick > 0 ? "pick" : "keep";
                    Dictionary<string, object> labels = BuildOpenPackLabels(node, pick, size);

                    run.PendingPack = new Dictionary<string, object>
                    {
                        { "token", run.ActionToken },
                        { "mode", mode }, { "pick", pick }, { "size", size },
                        { "cards", allCards.ConvertAll(c => (object)c) },
                        { "labels", labels },
                        { "next", next }
                    };
                    run.PendingAction = null;  // openpack takes its place; done for now
                    return;
                }
                else
                {
                    // v2: apply a state-mutating leaf here, then SetPending(next) or null.
                    SetPending(run, null); // v1: unknown leaf -> end
                }
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

        // Merge: global+asc -> action (per-field within rarity). null `action` = global+asc only.
        static Dictionary<int, RoguelikeCardPool.PityConfig> MergePity(
            Dictionary<int, RoguelikeCardPool.PityConfig> globalCfg,
            Dictionary<string, object> action)
        {
            Dictionary<int, RoguelikeCardPool.PityConfig> merged =
                new Dictionary<int, RoguelikeCardPool.PityConfig>(globalCfg);
            if (action == null) return merged;
            foreach (KeyValuePair<string, object> kv in action)
            {
                int r = RoguelikeCardPool.RarityKey(kv.Key);
                if (r <= 0) continue;
                Dictionary<string, object> entry = kv.Value as Dictionary<string, object>;
                if (entry == null) continue;
                RoguelikeCardPool.PityConfig pc;
                if (!merged.TryGetValue(r, out pc)) pc = new RoguelikeCardPool.PityConfig
                    { Increment = 0, Max = 0, ResetOn = new HashSet<int> { r } };
                object v;
                if (entry.TryGetValue("increment", out v)) { try { pc.Increment = Convert.ToDouble(v); } catch { } }
                if (entry.TryGetValue("max", out v))       { try { pc.Max = Convert.ToDouble(v); } catch { } }
                List<object> rs = Utils.GetValue<List<object>>(entry, "reset_on");
                if (rs != null)
                {
                    HashSet<int> set = new HashSet<int>();
                    foreach (object o in rs) { int rr = RoguelikeCardPool.RarityKey(Convert.ToString(o)); if (rr > 0) set.Add(rr); }
                    if (set.Count > 0) pc.ResetOn = set;
                }
                merged[r] = pc;
            }
            return merged;
        }

        // Effective rarityRates: layered (global+asc) -> action override (per-key) -> + pity bonus.
        static Dictionary<int, double> MergeRarityRatesWithPity(
            Dictionary<int, double> layered,
            Dictionary<string, object> actionOverride,
            Dictionary<int, RoguelikeCardPool.PityConfig> pityCfg,
            Dictionary<string, int> pity)
        {
            Dictionary<int, double> rates = layered != null
                ? new Dictionary<int, double>(layered)
                : new Dictionary<int, double>();
            if (actionOverride != null)
                foreach (KeyValuePair<string, object> kv in actionOverride)
                {
                    int rk = RoguelikeCardPool.RarityKey(kv.Key);
                    if (rk <= 0) continue;
                    double w; try { w = Convert.ToDouble(kv.Value); } catch { continue; }
                    rates[rk] = w;
                }
            if (pityCfg != null && pity != null)
                foreach (KeyValuePair<int, RoguelikeCardPool.PityConfig> kv in pityCfg)
                {
                    int rk = kv.Key;
                    int counter; pity.TryGetValue(RarityToKey(rk), out counter);
                    double bonus = Math.Min(counter * kv.Value.Increment, kv.Value.Max);
                    double cur; rates.TryGetValue(rk, out cur);
                    rates[rk] = cur + bonus;
                }
            return rates;
        }

        // Update counters after a pack: increment if no card has a rarity in reset_on; otherwise zero.
        static void UpdatePity(Dictionary<string, int> pity, List<RoguelikeCardPool.DrawResult> packCards,
                               Dictionary<int, RoguelikeCardPool.PityConfig> pityCfg)
        {
            HashSet<int> raritiesInPack = new HashSet<int>();
            foreach (RoguelikeCardPool.DrawResult d in packCards) raritiesInPack.Add(d.Rarity);
            foreach (KeyValuePair<int, RoguelikeCardPool.PityConfig> kv in pityCfg)
            {
                string key = RarityToKey(kv.Key);
                bool hit = false;
                foreach (int rr in kv.Value.ResetOn) if (raritiesInPack.Contains(rr)) { hit = true; break; }
                int cur; pity.TryGetValue(key, out cur);
                pity[key] = hit ? 0 : cur + 1;
            }
        }

        static string RarityToKey(int r)
        {
            switch (r) { case 1: return "N"; case 2: return "R"; case 3: return "SR"; case 4: return "UR"; }
            return "?";
        }

        // Resolve labels with passthrough; interpolate {0}=pick, {1}=size in title_pick only.
        // Returns ONLY the keys the action specified — client falls back to RoguelikeLabels defaults.
        static Dictionary<string, object> BuildOpenPackLabels(Dictionary<string, object> node, int pick, int size)
        {
            if (node == null) return new Dictionary<string, object>();
            string titleKeep = Utils.GetValue<string>(node, "title_keep", null);
            string titlePick = Utils.GetValue<string>(node, "title_pick", null);
            string confirm   = Utils.GetValue<string>(node, "confirm_label", null);
            Dictionary<string, object> r = new Dictionary<string, object>();
            if (titleKeep != null) r["title_keep"] = titleKeep;
            if (titlePick != null) r["title_pick"] = string.Format(titlePick, pick, size);
            if (confirm   != null) r["confirm"]    = confirm;
            return r;
        }
    }
}
