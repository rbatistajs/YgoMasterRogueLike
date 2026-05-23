using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Roguelike duel modifiers: compile a high-level { player, enemy } board spec into engine `cmds`
    // (one DLL_DuelComCheatCard tuple per card) plus extraLp/extraHand deltas. Adapted from upstream
    // Goat ModifierApplier — phase 1 supports pinned cids only (no random markers); deltas ADD onto
    // the duel's already-set base LP/hand instead of resetting to a fixed base.
    //
    // cmd tuple: [0, playerIdx, position, index, cid, prm, df]   (player 0 = you, 1 = enemy)
    //   prm = face state (0 down / 1 up); df = defense flag (0 atk / 1 def)
    static class RoguelikeModifiers
    {
        // CardPos (confirmed in-game upstream).
        const int POS_FIELD = 12, POS_HAND = 13;
        static readonly int[] MonsterSlots   = { 0, 1, 2, 3, 4 };   // M1..M5
        static readonly int[] SpellTrapSlots = { 7, 8, 9, 10, 11 }; // S1..S5

        static readonly Dictionary<string, int[]> MonsterPos = new Dictionary<string, int[]>
        {
            { "atk",      new[] { 1, 0 } }, { "def",      new[] { 1, 1 } },
            { "atk_fd",   new[] { 0, 0 } }, { "def_fd",   new[] { 0, 1 } },
            { "set",      new[] { 0, 1 } }, { "facedown", new[] { 0, 1 } },
        };
        static readonly Dictionary<string, int[]> StPos = new Dictionary<string, int[]>
        {
            { "set", new[] { 0, 0 } }, { "face_up", new[] { 1, 0 } }, { "active", new[] { 1, 0 } },
        };

        static int BaseHand(string duelType) { return duelType == "Rush" ? 4 : 5; }

        // ----- merge (player/enemy keys; layers low -> high priority) -----
        static void MergeSide(Dictionary<string, object> dst, Dictionary<string, object> src)
        {
            if (src == null) return;
            foreach (KeyValuePair<string, object> kv in src)
            {
                if (kv.Value == null) continue;
                string k = kv.Key;
                if (k == "monsters" || k == "spellTraps" || k == "hand")
                {
                    List<object> incoming = kv.Value as List<object>;
                    if (incoming == null) continue;
                    object existing;
                    List<object> arr = dst.TryGetValue(k, out existing) ? existing as List<object> : null;
                    if (arr == null) arr = new List<object>();
                    while (arr.Count < incoming.Count) arr.Add(null);
                    for (int i = 0; i < incoming.Count; i++) if (incoming[i] != null) arr[i] = incoming[i];
                    dst[k] = arr;
                }
                else if (k == "extraLp" || k == "extraHand")
                {
                    dst[k] = GetInt(dst, k, 0) + ToInt(kv.Value, 0); // deltas sum across layers
                }
                else if (k == "graveyard")
                {
                    // engine has no grave/banish placement — silently dropped
                }
                else
                {
                    dst[k] = kv.Value; // fieldSpell etc. — override
                }
            }
        }

        public static Dictionary<string, object> Merge(IEnumerable<Dictionary<string, object>> layers)
        {
            Dictionary<string, object> player = new Dictionary<string, object>();
            Dictionary<string, object> enemy = new Dictionary<string, object>();
            if (layers != null)
            {
                foreach (Dictionary<string, object> layer in layers)
                {
                    if (layer == null) continue;
                    MergeSide(player, Utils.GetValue<Dictionary<string, object>>(layer, "player"));
                    MergeSide(enemy, Utils.GetValue<Dictionary<string, object>>(layer, "enemy"));
                }
            }
            return new Dictionary<string, object> { { "player", player }, { "enemy", enemy } };
        }

        // ----- card resolution (pinned cid or seeded random pick) -----
        // Resolves a card spec to a real cid: a pinned `cid`, or a `random` pick from a deck pool
        // filtered by RoguelikeCardPool and deduped per player. Seeded (Rng) + server-side, so a
        // resumed duel reproduces the same cards. A null/empty Resolver yields pinned cids only.
        public class Resolver
        {
            public Random Rng;
            public DeckInfo[] Decks;   // [0] = player, [1] = enemy
            public string DataDir;
            readonly HashSet<int>[] _used = { new HashSet<int>(), new HashSet<int>() };

            public int? Resolve(Dictionary<string, object> spec, int playerIdx)
            {
                if (spec == null) return null;
                object v;
                bool hasRandom = spec.ContainsKey("random");
                if (spec.TryGetValue("cid", out v) && !hasRandom)
                {
                    try { return Convert.ToInt32(v); } catch { return null; }
                }
                if (!hasRandom || Rng == null) return null;
                HashSet<int> pool = BuildPool(spec, playerIdx);
                if (pool == null) return null;
                HashSet<int> used = _used[playerIdx == 0 ? 0 : 1];
                List<int> cands = new List<int>();
                foreach (int cid in pool)
                    if (!used.Contains(cid) && RoguelikeCardPool.Matches(DataDir, cid, spec)) cands.Add(cid);
                if (cands.Count == 0) return null;
                int pick = cands[Rng.Next(cands.Count)];
                used.Add(pick);
                return pick;
            }

            HashSet<int> BuildPool(Dictionary<string, object> spec, int playerIdx)
            {
                string source = Utils.GetValue<string>(spec, "source");
                if (!string.IsNullOrEmpty(source) && source != "deck")
                {
                    Console.WriteLine("[Roguelike] modifier random source='" + source + "' not supported yet — use 'deck'");
                    return null;
                }
                string owner = Utils.GetValue<string>(spec, "deck_owner");
                int idx;
                switch (owner)
                {
                    case "p1": idx = 0; break;
                    case "p2": idx = 1; break;
                    case "rival": idx = 1 - playerIdx; break;
                    default: idx = playerIdx; break; // "own" + fallback
                }
                if (Decks == null || idx < 0 || idx >= Decks.Length || Decks[idx] == null) return null;
                return new HashSet<int>(Decks[idx].GetAllCards(true, true, false));
            }
        }

        // ----- encode -----
        static List<object> EmitCard(int playerIdx, int pos, int index, int cid, int prm, int df)
        {
            return new List<object> { 0, playerIdx, pos, index, cid, prm, df };
        }

        static void EmitSide(List<object> cmds, int playerIdx, Dictionary<string, object> cfg, Resolver r)
        {
            if (cfg == null) return;

            Dictionary<string, object> fs = Utils.GetValue<Dictionary<string, object>>(cfg, "fieldSpell");
            if (fs != null) { int? c = r.Resolve(fs, playerIdx); if (c.HasValue) cmds.Add(EmitCard(playerIdx, POS_FIELD, 0, c.Value, 1, 0)); }

            List<object> monsters = Utils.GetValue<List<object>>(cfg, "monsters");
            if (monsters != null)
                for (int s = 0; s < monsters.Count && s < MonsterSlots.Length; s++)
                {
                    Dictionary<string, object> m = monsters[s] as Dictionary<string, object>;
                    int? c = r.Resolve(m, playerIdx); if (!c.HasValue) continue;
                    int[] pose;
                    string key = Utils.GetValue<string>(m, "pos") ?? "atk";
                    if (!MonsterPos.TryGetValue(key, out pose)) pose = new[] { 1, 0 };
                    cmds.Add(EmitCard(playerIdx, MonsterSlots[s], 0, c.Value, pose[0], pose[1]));
                }

            List<object> sts = Utils.GetValue<List<object>>(cfg, "spellTraps");
            if (sts != null)
                for (int s = 0; s < sts.Count && s < SpellTrapSlots.Length; s++)
                {
                    Dictionary<string, object> st = sts[s] as Dictionary<string, object>;
                    int? c = r.Resolve(st, playerIdx); if (!c.HasValue) continue;
                    int[] pose;
                    string key = Utils.GetValue<string>(st, "pos") ?? "set";
                    if (!StPos.TryGetValue(key, out pose)) pose = new[] { 0, 0 };
                    cmds.Add(EmitCard(playerIdx, SpellTrapSlots[s], 0, c.Value, pose[0], pose[1]));
                }

            List<object> hand = Utils.GetValue<List<object>>(cfg, "hand");
            if (hand != null)
            {
                int hi = 0;
                foreach (object o in hand)
                {
                    int? c = r.Resolve(o as Dictionary<string, object>, playerIdx); if (!c.HasValue) continue;
                    cmds.Add(EmitCard(playerIdx, POS_HAND, hi, c.Value, 0, 0));
                    hi++;
                }
            }
        }

        // ----- entrypoint -----
        // Merge `layers` (each a { player, enemy } dict), encode pinned cards into duelDict["cmds"],
        // and add extraLp/extraHand deltas onto the dict's existing life/hnum base. No-op when nothing
        // is configured.
        public static void Apply(Dictionary<string, object> duelDict, string duelType,
            Resolver resolver, params Dictionary<string, object>[] layers)
        {
            if (resolver == null) resolver = new Resolver();
            Dictionary<string, object> merged = Merge(layers);
            Dictionary<string, object> player = Utils.GetValue<Dictionary<string, object>>(merged, "player");
            Dictionary<string, object> enemy = Utils.GetValue<Dictionary<string, object>>(merged, "enemy");

            List<object> cmds = new List<object>();
            EmitSide(cmds, 0, player, resolver);
            EmitSide(cmds, 1, enemy, resolver);

            int extraLpP = GetInt(player, "extraLp", 0),   extraLpE = GetInt(enemy, "extraLp", 0);
            int extraHandP = GetInt(player, "extraHand", 0), extraHandE = GetInt(enemy, "extraHand", 0);

            if (cmds.Count == 0 && extraLpP == 0 && extraLpE == 0 && extraHandP == 0 && extraHandE == 0)
                return;

            if (cmds.Count > 0) duelDict["cmds"] = cmds;

            if (extraLpP != 0 || extraLpE != 0)
            {
                duelDict["life"] = new List<object>
                {
                    Math.Max(1, LifeAt(duelDict, 0) + extraLpP),
                    Math.Max(1, LifeAt(duelDict, 1) + extraLpE),
                };
            }
            if (extraHandP != 0 || extraHandE != 0)
            {
                int bh = BaseHand(duelType);
                duelDict["hnum"] = new List<object>
                {
                    Math.Max(1, bh + extraHandP),
                    Math.Max(1, bh + extraHandE),
                };
            }
        }

        // ----- helpers -----
        static int LifeAt(Dictionary<string, object> duelDict, int idx)
        {
            List<object> life = Utils.GetValue<List<object>>(duelDict, "life");
            return (life != null && idx < life.Count) ? ToInt(life[idx], 0) : 0;
        }

        static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null) ? ToInt(v, fallback) : fallback;
        }

        static int ToInt(object v, int fallback) { try { return Convert.ToInt32(v); } catch { return fallback; } }
    }
}
