using System;
using System.Collections.Generic;
using System.Linq;

namespace YgoMaster.Modifiers
{
    // Port de scripts/_modifiers.py — encoder de modifier dict
    // alto-nível -> (cmds, random_specs, life, hnum) prontos pro
    // engine. NÃO resolve markers random (isso é responsabilidade
    // do RuntimeRandomResolver, em tempo de duelo).
    //
    // Camadas (low → high priority), mescladas via MergeModifiers:
    //   1. Gate-level    generic_params.modifier_defaults[<chapter_type>]
    //   2. Special deck  deck JSON's top-level `modifiers` field
    //   3. Chapter       manual_cells[i].modifiers
    //
    // Per-field merge:
    //   fieldSpell  → override
    //   monsters    → merge by index (null = defer)
    //   spellTraps  → merge by index
    //   hand        → merge by index
    //   extraLife   → sum
    //   extraHand   → sum
    //   graveyard   → silently dropped (engine não trata grave/banish)
    //
    // cmds shape: lista de tuplas de 7 ints, uma por colocação.
    //   [cmd_type=0, player, position, index, cid, prm, df]
    //   cid >= 0 → real cid, usa direto
    //   cid <  0 → marker, RuntimeRandomResolver troca por cid real
    static class ModifierApplier
    {
        // ---------- CardPos (in-game confirmados) ----------
        public const int POS_M1 = 0, POS_M2 = 1, POS_M3 = 2, POS_M4 = 3, POS_M5 = 4;
        public const int POS_EXTRA_M1 = 5, POS_EXTRA_M2 = 6;
        public const int POS_S1 = 7, POS_S2 = 8, POS_S3 = 9, POS_S4 = 10, POS_S5 = 11;
        public const int POS_FIELD = 12;
        public const int POS_HAND = 13;
        public const int POS_EXTRA_DECK = 14;
        public const int POS_DECK = 15;
        public const int POS_GRAVE = 16;
        public const int POS_BANISH = 17;
        public const int POS_SELECT = 18;

        static readonly int[] MonsterSlots   = { POS_M1, POS_M2, POS_M3, POS_M4, POS_M5 };
        static readonly int[] SpellTrapSlots = { POS_S1, POS_S2, POS_S3, POS_S4, POS_S5 };

        // (prm, df) por orientação de monster. prm = face state (0=down,
        // 1=up); df = defense flag (0=atk, 1=def).
        static readonly Dictionary<string, int[]> MonsterPos = new Dictionary<string, int[]>
        {
            { "atk",      new[] { 1, 0 } },
            { "def",      new[] { 1, 1 } },
            { "atk_fd",   new[] { 0, 0 } },
            { "def_fd",   new[] { 0, 1 } },
            { "set",      new[] { 0, 1 } },
            { "facedown", new[] { 0, 1 } },
        };

        // Spell/Trap: só 2 estados úteis.
        static readonly Dictionary<string, int[]> StPos = new Dictionary<string, int[]>
        {
            { "set",     new[] { 0, 0 } },
            { "face_up", new[] { 1, 0 } },
            { "active",  new[] { 1, 0 } },
        };

        static readonly Dictionary<string, int> DefaultHandSize = new Dictionary<string, int>
        {
            { "Normal", 5 },
            { "Rush",   4 },
        };
        const int DefaultLife = 8000;

        // ---------- merge ----------
        static void MergePlayer(Dictionary<string, object> dst, Dictionary<string, object> src)
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
                    List<object> arr;
                    object existing;
                    if (dst.TryGetValue(k, out existing))
                    {
                        arr = existing as List<object> ?? new List<object>();
                    }
                    else
                    {
                        arr = new List<object>();
                    }
                    while (arr.Count < incoming.Count) arr.Add(null);
                    for (int i = 0; i < incoming.Count; i++)
                    {
                        if (incoming[i] != null) arr[i] = incoming[i];
                    }
                    dst[k] = arr;
                }
                else if (k == "extraLife" || k == "extraHand")
                {
                    int cur = 0;
                    object existing;
                    if (dst.TryGetValue(k, out existing) && existing != null)
                    {
                        try { cur = Convert.ToInt32(existing); } catch { }
                    }
                    int add = 0;
                    try { add = Convert.ToInt32(kv.Value); } catch { }
                    dst[k] = cur + add;
                }
                else if (k == "graveyard")
                {
                    // silently drop — engine não trata grave placements.
                }
                else
                {
                    dst[k] = kv.Value;
                }
            }
        }

        // Merge layers in ordem (low → high priority). Retorna dict fresco
        // com `p1` e `p2`.
        public static Dictionary<string, object> MergeModifiers(
            params Dictionary<string, object>[] layers)
        {
            Dictionary<string, object> p1 = new Dictionary<string, object>();
            Dictionary<string, object> p2 = new Dictionary<string, object>();
            if (layers != null)
            {
                foreach (Dictionary<string, object> layer in layers)
                {
                    if (layer == null) continue;
                    Dictionary<string, object> src1 = Utils.GetValue<Dictionary<string, object>>(layer, "p1");
                    Dictionary<string, object> src2 = Utils.GetValue<Dictionary<string, object>>(layer, "p2");
                    if (src1 != null) MergePlayer(p1, src1);
                    if (src2 != null) MergePlayer(p2, src2);
                }
            }
            return new Dictionary<string, object> { { "p1", p1 }, { "p2", p2 } };
        }

        // ---------- encoder ----------
        class MarkerCounter
        {
            public int N;
            public int Next() { N++; return -N; }
        }

        // Strip chaves só-GUI (`pos` vive no tupla cmds em vez do spec)
        // e aplica defaults: source='deck', deck_owner='own'.
        static Dictionary<string, object> NormalizeRandomSpec(Dictionary<string, object> spec)
        {
            Dictionary<string, object> outSpec = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in spec)
            {
                if (kv.Key == "pos") continue;
                outSpec[kv.Key] = kv.Value;
            }
            if (outSpec.ContainsKey("random"))
            {
                if (!outSpec.ContainsKey("source")) outSpec["source"] = "deck";
                if (string.Equals(outSpec["source"] as string, "deck", StringComparison.Ordinal)
                    && !outSpec.ContainsKey("deck_owner"))
                {
                    outSpec["deck_owner"] = "own";
                }
            }
            return outSpec;
        }

        // Retorna o cid a embutir no tupla cmds.
        //   pinned: int positivo
        //   random: marker negativo (registra spec em `specs`)
        //   vazio:  null (caller pula)
        static int? ResolveCid(Dictionary<string, object> spec, MarkerCounter counter,
                                Dictionary<string, object> specs)
        {
            if (spec == null || spec.Count == 0) return null;
            bool hasCid = spec.ContainsKey("cid");
            bool hasRandom = spec.ContainsKey("random");
            if (hasCid && !hasRandom)
            {
                try { return Convert.ToInt32(spec["cid"]); } catch { return null; }
            }
            if (hasRandom)
            {
                int marker = counter.Next();
                specs[(-marker).ToString()] = NormalizeRandomSpec(spec);
                return marker;
            }
            return null;
        }

        // ---------- emit ----------
        static List<object> EmitCard(int playerIdx, int pos, int index, int cid,
                                      int prm = 1, int df = 0)
        {
            return new List<object> { 0, playerIdx, pos, index, cid, prm, df };
        }

        // Uma command por card a colocar. Cada entrada é um tupla de 7
        // ints consumida pelo `DuelStarter.InitEngine` (1 chamada de
        // DLL_DuelComCheatCard por entry). Specs random viram cids
        // negativos; pinned passam como cid positivo.
        static List<object> BuildCmdsAndSpecsForPlayer(int playerIdx,
            Dictionary<string, object> cfg, MarkerCounter counter,
            Dictionary<string, object> specs)
        {
            List<object> outCmds = new List<object>();
            if (cfg == null || cfg.Count == 0) return outCmds;

            // Field spell: sempre face-up active
            Dictionary<string, object> fs = Utils.GetValue<Dictionary<string, object>>(cfg, "fieldSpell");
            if (fs != null)
            {
                int? cid = ResolveCid(fs, counter, specs);
                if (cid.HasValue)
                {
                    outCmds.Add(EmitCard(playerIdx, POS_FIELD, 0, cid.Value, prm: 1, df: 0));
                }
            }

            // Monsters: 1 card por slot; prm/df do MonsterPos (default atk)
            List<object> monsters = Utils.GetValue<List<object>>(cfg, "monsters");
            if (monsters != null)
            {
                for (int slotIdx = 0; slotIdx < monsters.Count && slotIdx < MonsterSlots.Length; slotIdx++)
                {
                    Dictionary<string, object> m = monsters[slotIdx] as Dictionary<string, object>;
                    if (m == null) continue;
                    int? cid = ResolveCid(m, counter, specs);
                    if (!cid.HasValue) continue;
                    int[] pose;
                    string poseKey = Utils.GetValue<string>(m, "pos") ?? "atk";
                    if (!MonsterPos.TryGetValue(poseKey, out pose)) pose = new[] { 1, 0 };
                    outCmds.Add(EmitCard(playerIdx, MonsterSlots[slotIdx], 0, cid.Value, pose[0], pose[1]));
                }
            }

            // Spell/Traps: 1 card por slot; default set (face-down)
            List<object> spellTraps = Utils.GetValue<List<object>>(cfg, "spellTraps");
            if (spellTraps != null)
            {
                for (int slotIdx = 0; slotIdx < spellTraps.Count && slotIdx < SpellTrapSlots.Length; slotIdx++)
                {
                    Dictionary<string, object> st = spellTraps[slotIdx] as Dictionary<string, object>;
                    if (st == null) continue;
                    int? cid = ResolveCid(st, counter, specs);
                    if (!cid.HasValue) continue;
                    int[] pose;
                    string poseKey = Utils.GetValue<string>(st, "pos") ?? "set";
                    if (!StPos.TryGetValue(poseKey, out pose)) pose = new[] { 0, 0 };
                    outCmds.Add(EmitCard(playerIdx, SpellTrapSlots[slotIdx], 0, cid.Value, pose[0], pose[1]));
                }
            }

            // Hand: index incrementa por card (por player) começando em 0
            List<object> hand = Utils.GetValue<List<object>>(cfg, "hand");
            if (hand != null)
            {
                int handIdx = 0;
                foreach (object o in hand)
                {
                    Dictionary<string, object> h = o as Dictionary<string, object>;
                    if (h == null) continue;
                    int? cid = ResolveCid(h, counter, specs);
                    if (!cid.HasValue) continue;
                    outCmds.Add(EmitCard(playerIdx, POS_HAND, handIdx, cid.Value, 0, 0));
                    handIdx++;
                }
            }
            return outCmds;
        }

        // ---------- entrypoint ----------
        // In-place: adiciona `cmds` / `life` / `hnum` / `random_specs`
        // (quando houver). `resolved` é o dict merged de MergeModifiers.
        public static void ApplyModifiers(Dictionary<string, object> duelDict,
            Dictionary<string, object> resolved, string duelType)
        {
            int baseHand;
            if (!DefaultHandSize.TryGetValue(duelType ?? "", out baseHand)) baseHand = 5;
            int baseLife = DefaultLife;

            Dictionary<string, object> p1 =
                Utils.GetValue<Dictionary<string, object>>(resolved, "p1") ?? new Dictionary<string, object>();
            Dictionary<string, object> p2 =
                Utils.GetValue<Dictionary<string, object>>(resolved, "p2") ?? new Dictionary<string, object>();

            MarkerCounter counter = new MarkerCounter();
            Dictionary<string, object> specs = new Dictionary<string, object>();
            List<object> cmdsP1 = BuildCmdsAndSpecsForPlayer(0, p1, counter, specs);
            List<object> cmdsP2 = BuildCmdsAndSpecsForPlayer(1, p2, counter, specs);

            int extraLife1 = GetIntOr(p1, "extraLife", 0);
            int extraLife2 = GetIntOr(p2, "extraLife", 0);
            int extraHand1 = GetIntOr(p1, "extraHand", 0);
            int extraHand2 = GetIntOr(p2, "extraHand", 0);

            // Se nada relevante foi configurado, sai sem mexer no dict.
            if (cmdsP1.Count == 0 && cmdsP2.Count == 0
                && extraLife1 == 0 && extraLife2 == 0
                && extraHand1 == 0 && extraHand2 == 0)
            {
                return;
            }

            duelDict["life"] = new List<object>
            {
                Math.Max(1, baseLife + extraLife1),
                Math.Max(1, baseLife + extraLife2),
            };
            duelDict["hnum"] = new List<object>
            {
                Math.Max(1, baseHand + extraHand1),
                Math.Max(1, baseHand + extraHand2),
            };
            List<object> combined = new List<object>(cmdsP1.Count + cmdsP2.Count);
            combined.AddRange(cmdsP1);
            combined.AddRange(cmdsP2);
            duelDict["cmds"] = combined;
            if (specs.Count > 0)
            {
                duelDict["random_specs"] = specs;
            }
        }

        // Merge → encode → apply, numa chamada só. Retorna o resolved dict
        // (útil pra logging/preview).
        public static Dictionary<string, object> ApplyFullPipeline(
            Dictionary<string, object> duelDict, string duelType,
            params Dictionary<string, object>[] layers)
        {
            Dictionary<string, object> merged = MergeModifiers(layers);
            ApplyModifiers(duelDict, merged, duelType);
            return merged;
        }

        // ---------- helpers ----------
        static int GetIntOr(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v); } catch { return fallback; }
        }
    }
}
