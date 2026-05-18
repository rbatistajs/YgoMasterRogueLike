using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YgoMaster
{
    // Goat: at solo duel start, scans `DuelSettings.cmds` for negative `cid`
    // markers and substitutes a real random cid for each one, honoring the
    // spec in `DuelSettings.random_specs[abs(cid)]`.
    //
    // Spec keys (all optional):
    //   random      "any" | "monster" | "main_monster" | "extra_monster"
    //               | "spell" | "trap" | "field_spell" | "spell_or_trap"
    //   subtype     monster: "normal"|"effect"|"ritual"|"fusion"|"synchro"|"xyz"|"link"
    //               spell:   "normal"|"continuous"|"equip"|"field"|"quickplay"|"ritual"
    //               trap:    "normal"|"continuous"|"counter"
    //   minAtk/maxAtk/minDef/maxDef/minLevel/maxLevel  — int filters (monsters only)
    //   source      "deck" (default) | "any"
    //   deck_owner  "own" (default) | "rival" | "p1" | "p2"
    //
    // For `source="any"` the pool is the regulation's allowed list: every
    // cid in CardList.json minus what `Regulation.json[<regId>].available.a0`
    // bans. The duel's `regulation_name` (Goat Format / Rush Duel / …) maps
    // to a regulation id via `DeckInfo.RegulationIdsByName`, with fallback
    // to `DefaultRegulationId` (Normal). Each format already bans the cards
    // that don't belong, so the pool is naturally scoped.
    //
    // Mutates the cloned `DuelSettings` that `CreateSoloDuelSettingsInstance`
    // already produces — never touches the global `SoloDuels[chapterId]`.
    static class RuntimeRandomResolver
    {
        // ----- categorization -----
        enum CardCategory { Other, MainDeckMonster, ExtraDeckMonster, Spell, Trap, Token }

        // CardKind → CardCategory. One entry per value of Enums/Card.cs
        // `CardKind`. Unlisted kinds default to `Other` (rejected from typed
        // slots) so the resolver stays safe when new kinds appear.
        static readonly Dictionary<CardKind, CardCategory> kindCategory =
            new Dictionary<CardKind, CardCategory>
        {
            { CardKind.Normal,        CardCategory.MainDeckMonster  },
            { CardKind.Effect,        CardCategory.MainDeckMonster  },
            { CardKind.Fusion,        CardCategory.ExtraDeckMonster },
            { CardKind.FusionFx,      CardCategory.ExtraDeckMonster },
            { CardKind.Ritual,        CardCategory.MainDeckMonster  },
            { CardKind.RitualFx,      CardCategory.MainDeckMonster  },
            { CardKind.Toon,          CardCategory.MainDeckMonster  },
            { CardKind.Spirit,        CardCategory.MainDeckMonster  },
            { CardKind.Union,         CardCategory.MainDeckMonster  },
            { CardKind.Dual,          CardCategory.MainDeckMonster  },   // Gemini
            { CardKind.Token,         CardCategory.Token            },
            { CardKind.God,           CardCategory.Other            },   // Egyptian Gods
            { CardKind.Dummy,         CardCategory.Other            },
            { CardKind.Magic,         CardCategory.Spell            },
            { CardKind.Trap,          CardCategory.Trap             },
            { CardKind.Tuner,         CardCategory.MainDeckMonster  },
            { CardKind.TunerFx,       CardCategory.MainDeckMonster  },
            { CardKind.Sync,          CardCategory.ExtraDeckMonster },
            { CardKind.SyncFx,        CardCategory.ExtraDeckMonster },
            { CardKind.SyncTuner,     CardCategory.ExtraDeckMonster },
            { CardKind.Dtuner,        CardCategory.MainDeckMonster  },   // dark tuner = main
            { CardKind.Dsync,         CardCategory.ExtraDeckMonster },   // dark synchro = extra
            { CardKind.Xyz,           CardCategory.ExtraDeckMonster },
            { CardKind.XyzFx,         CardCategory.ExtraDeckMonster },
            { CardKind.Flip,          CardCategory.MainDeckMonster  },
            { CardKind.Pend,          CardCategory.Other            },   // no pendulum support
            { CardKind.PendFx,        CardCategory.Other            },
            { CardKind.SpEffect,      CardCategory.MainDeckMonster  },
            { CardKind.SpToon,        CardCategory.MainDeckMonster  },
            { CardKind.SpSpirit,      CardCategory.MainDeckMonster  },
            { CardKind.SpTuner,       CardCategory.MainDeckMonster  },
            { CardKind.SpDtuner,      CardCategory.MainDeckMonster  },
            { CardKind.FlipTuner,     CardCategory.MainDeckMonster  },
            { CardKind.PendTuner,     CardCategory.Other            },
            { CardKind.XyzPend,       CardCategory.Other            },
            { CardKind.PendFlip,      CardCategory.Other            },
            { CardKind.SyncPend,      CardCategory.Other            },
            { CardKind.UnionTuner,    CardCategory.MainDeckMonster  },
            { CardKind.RitualSpirit,  CardCategory.MainDeckMonster  },
            { CardKind.FusionTuner,   CardCategory.ExtraDeckMonster },
            { CardKind.SpPend,        CardCategory.Other            },
            { CardKind.FusionPend,    CardCategory.Other            },
            { CardKind.Link,          CardCategory.ExtraDeckMonster },
            { CardKind.LinkFx,        CardCategory.ExtraDeckMonster },
            { CardKind.PendNTuner,    CardCategory.Other            },
            { CardKind.PendSpirit,    CardCategory.Other            },
            { CardKind.Maximum,       CardCategory.MainDeckMonster  },   // Rush
            { CardKind.RirualTunerFX, CardCategory.MainDeckMonster  },
            { CardKind.FusionTunerFX, CardCategory.ExtraDeckMonster },
            { CardKind.TokenTuner,    CardCategory.MainDeckMonster  },
            { CardKind.R_Fusion,      CardCategory.ExtraDeckMonster },   // Rush fusion
            { CardKind.R_FusionFX,    CardCategory.ExtraDeckMonster },
            { CardKind.RitualPend,    CardCategory.Other            },
            { CardKind.RitualFlip,    CardCategory.MainDeckMonster  },
        };

        // Monster subtype label → allowed kinds.
        static readonly Dictionary<string, HashSet<CardKind>> monsterSubtypeKinds =
            new Dictionary<string, HashSet<CardKind>>
        {
            { "normal",  new HashSet<CardKind> { CardKind.Normal }},
            { "effect",  new HashSet<CardKind> {
                CardKind.Effect,    CardKind.Toon,       CardKind.Spirit,
                CardKind.Union,     CardKind.Dual,       CardKind.Tuner,
                CardKind.TunerFx,   CardKind.Dtuner,     CardKind.Flip,
                CardKind.SpEffect,  CardKind.SpToon,     CardKind.SpSpirit,
                CardKind.SpTuner,   CardKind.SpDtuner,   CardKind.FlipTuner,
                CardKind.UnionTuner,CardKind.TokenTuner,
            }},
            { "ritual",  new HashSet<CardKind> {
                CardKind.Ritual,        CardKind.RitualFx,  CardKind.RitualSpirit,
                CardKind.RirualTunerFX, CardKind.RitualFlip,
            }},
            { "fusion",  new HashSet<CardKind> {
                CardKind.Fusion,        CardKind.FusionFx,  CardKind.FusionTuner,
                CardKind.FusionTunerFX, CardKind.R_Fusion,  CardKind.R_FusionFX,
            }},
            { "synchro", new HashSet<CardKind> {
                CardKind.Sync, CardKind.SyncFx, CardKind.SyncTuner, CardKind.Dsync,
            }},
            { "xyz",     new HashSet<CardKind> { CardKind.Xyz,  CardKind.XyzFx  }},
            { "link",    new HashSet<CardKind> { CardKind.Link, CardKind.LinkFx }},
        };

        // Spell/trap subtype label → CardIcon enum value (from Enums/Card.cs).
        static readonly Dictionary<string, int> spellTrapSubtypeIcons = new Dictionary<string, int>
        {
            { "normal",     0 }, { "counter",    1 }, { "field",   2 },
            { "equip",      3 }, { "continuous", 4 }, { "quickplay", 5 },
            { "ritual",     6 },
        };

        // ----- state -----
        // Per-regulation allowed cid pool, built from CardList.json minus
        // the regulation's a0 (forbidden) list. Keyed by regulation id.
        static readonly Dictionary<int, HashSet<int>> allowedByRegulation =
            new Dictionary<int, HashSet<int>>();
        static readonly object initLock = new object();
        static readonly Random rng = new Random();
        static bool initialized;
        static bool dllReady;   // false → DLL_CardGet* would AV; skip filtering

        // ----- public API -----
        public static void Init(string dataDirectory, Dictionary<string, object> regulation)
        {
            lock (initLock)
            {
                if (initialized) return;
                try
                {
                    LoadCardData(dataDirectory);
                    BuildAnyPools(dataDirectory, regulation);
                    initialized = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[RuntimeRandomResolver] Init failed: " + ex);
                }
            }
        }

        // Diagnostic: dump every DLL prop we read for a given cid plus our
        // own categorization. Mirrors what `Matches` queries, so output
        // tracks the exact same data the resolver would see.
        public static void DumpCardInfo(int cid)
        {
            Console.WriteLine("[card " + cid + "]");
            if (!dllReady)
            {
                Console.WriteLine("  DLL not ready — cannot query props");
                return;
            }
            int kindInt = DuelDllProps.DLL_CardGetKind(cid);
            int atk     = DuelDllProps.DLL_CardGetAtk(cid);
            int def     = DuelDllProps.DLL_CardGetDef(cid);
            int lvl     = DuelDllProps.DLL_CardGetLevel(cid);
            int attr    = DuelDllProps.DLL_CardGetAttr(cid);
            int icon    = DuelDllProps.DLL_CardGetIcon(cid);
            CardKind kind = (CardKind)kindInt;
            CardCategory cat = CategorizeKind(kindInt);

            Console.WriteLine("  loaded:   " + IsCardLoaded(cid));
            Console.WriteLine("  kind:     " + kind + " (" + kindInt + ")");
            Console.WriteLine("  category: " + cat);
            Console.WriteLine("  atk/def:  " + atk + "/" + def);
            Console.WriteLine("  level:    " + lvl);
            Console.WriteLine("  attr:     " + attr);
            Console.WriteLine("  icon:     " + icon + IconLabel(icon));
            Console.WriteLine("  subtypes: " + MatchingMonsterSubtypes(kind));
        }

        // Diagnostic: runs the actual Matches filter against a cid + given
        // spec fields (random kind + optional subtype + optional key=value
        // pairs like minAtk=1000). Lets you confirm whether the resolver
        // would accept/reject a specific card under a given spec.
        public static void TestMatch(int cid, string randomKind, string subtype,
                                      Dictionary<string, int> numericFilters)
        {
            var spec = new Dictionary<string, object> { { "random", randomKind } };
            if (!string.IsNullOrEmpty(subtype)) spec["subtype"] = subtype;
            if (numericFilters != null)
            {
                foreach (KeyValuePair<string, int> kv in numericFilters)
                {
                    spec[kv.Key] = kv.Value;
                }
            }
            bool pass = Matches(cid, spec);
            Console.WriteLine("[match-test cid=" + cid + " " + DescribeSpec(spec)
                + "] → " + (pass ? "ACCEPT" : "REJECT"));
        }

        // Diagnostic: replays OnSoloDuelStart on a chapter JSON in isolation
        // and prints what the resolver picks (or drops) for each marker.
        // Useful for confirming what would happen at duel start without
        // having to actually click into the chapter in-game.
        public static void ResolveChapter(DuelSettings ds)
        {
            Console.WriteLine("[resolve-chapter " + ds.chapter
                + " regulation=" + (ds.regulation_name ?? "(none)") + "]");
            if (ds.random_specs == null || ds.random_specs.Count == 0)
            {
                Console.WriteLine("  no random_specs → nothing to resolve");
                return;
            }
            // Clone the cmds (same as OnSoloDuelStart does) so we don't
            // mutate the loaded DuelSettings.
            for (int p = 0; p < ds.cmds.Length; p++)
            {
                if (ds.cmds[p] != null) ds.cmds[p] = new List<int>(ds.cmds[p]);
            }
            var usedByPlayer = new Dictionary<int, HashSet<int>>
            {
                { 0, new HashSet<int>() },
                { 1, new HashSet<int>() },
            };
            for (int p = 0; p < ds.cmds.Length; p++)
            {
                List<int> cmds = ds.cmds[p];
                if (cmds == null) continue;
                const int TUPLE = 7;
                for (int i = 0; i + TUPLE - 1 < cmds.Count; i += TUPLE)
                {
                    int cmd = cmds[i], cmdPlayer = cmds[i + 1];
                    int pos = cmds[i + 2], cid = cmds[i + 4];
                    if (cmd != 0 || cid >= 0) continue;
                    string key = (-cid).ToString();
                    object specObj;
                    if (!ds.random_specs.TryGetValue(key, out specObj))
                    {
                        Console.WriteLine("  marker " + cid + " (p" + cmdPlayer
                            + " pos" + pos + ") → MISSING spec");
                        continue;
                    }
                    var spec = specObj as Dictionary<string, object>;
                    int picked = PickRandom(spec, ds, cmdPlayer, usedByPlayer[cmdPlayer]);
                    Console.WriteLine("  marker " + cid + " (p" + cmdPlayer + " pos" + pos
                        + ") spec=" + DescribeSpec(spec)
                        + " → " + (picked > 0 ? "cid=" + picked : "EMPTY POOL"));
                }
            }
        }

        static string DescribeSpec(Dictionary<string, object> spec)
        {
            var parts = new List<string>();
            foreach (var kv in spec) parts.Add(kv.Key + "=" + kv.Value);
            return "{" + string.Join(", ", parts) + "}";
        }

        static string IconLabel(int icon)
        {
            foreach (KeyValuePair<string, int> kv in spellTrapSubtypeIcons)
            {
                if (kv.Value == icon) return " (" + kv.Key + ")";
            }
            return "";
        }

        static string MatchingMonsterSubtypes(CardKind kind)
        {
            List<string> matches = new List<string>();
            foreach (KeyValuePair<string, HashSet<CardKind>> kv in monsterSubtypeKinds)
            {
                if (kv.Value.Contains(kind)) matches.Add(kv.Key);
            }
            return matches.Count == 0 ? "(none)" : string.Join(", ", matches);
        }

        public static void OnSoloDuelStart(DuelSettings ds, Player player)
        {
            if (!initialized || ds == null) return;
            if (ds.random_specs == null || ds.random_specs.Count == 0) return;

            // Deep-clone cmds inner lists before mutating — the array
            // itself was shallow-copied by DuelSettings.CopyFrom.
            if (ds.cmds != null)
            {
                for (int p = 0; p < ds.cmds.Length; p++)
                {
                    if (ds.cmds[p] != null) ds.cmds[p] = new List<int>(ds.cmds[p]);
                }
            }
            try { ResolveAll(ds); }
            catch (Exception ex) { Console.WriteLine("[RuntimeRandomResolver] resolve failed: " + ex); }
        }

        // ----- init helpers -----
        static void LoadCardData(string dataDirectory)
        {
            string cardDataDir = Path.Combine(dataDirectory, "CardData");
            int loaded = DuelDllProps.LoadAllCardData(cardDataDir);
            dllReady = loaded > 0;
            if (dllReady)
            {
                Console.WriteLine("[RuntimeRandomResolver] card data loaded ("
                    + loaded + " bytes) — DLL ready");
            }
            else
            {
                Console.WriteLine("[RuntimeRandomResolver] WARN: card data not loaded — "
                    + "spec filters disabled (random picks won't be validated)");
            }
        }

        // For each regulation in the config, allowed = (CardList.json universe)
        // minus the regulation's `available.a0` forbidden list. Builds one
        // pool per regulation id.
        static void BuildAnyPools(string dataDirectory, Dictionary<string, object> regulation)
        {
            HashSet<int> universe = LoadCardListCids(dataDirectory);
            if (universe.Count == 0)
            {
                Console.WriteLine("[RuntimeRandomResolver] WARN: empty CardList — any-source disabled");
                return;
            }
            if (regulation == null)
            {
                Console.WriteLine("[RuntimeRandomResolver] WARN: no Regulation data — any-source disabled");
                return;
            }

            foreach (KeyValuePair<string, object> entry in regulation)
            {
                int regId;
                if (!int.TryParse(entry.Key, out regId)) continue;
                HashSet<int> forbidden = ExtractForbidden(entry.Value);
                HashSet<int> allowed = new HashSet<int>(universe);
                allowed.ExceptWith(forbidden);
                allowedByRegulation[regId] = allowed;
            }

            Console.WriteLine("[RuntimeRandomResolver] any-pool: universe="
                + universe.Count + ", " + allowedByRegulation.Count + " regulations");
            foreach (KeyValuePair<int, HashSet<int>> kv in allowedByRegulation)
            {
                string name;
                DeckInfo.RegulationNamesById.TryGetValue(kv.Key, out name);
                Console.WriteLine("  [" + kv.Key + " " + (name ?? "?") + "] "
                    + kv.Value.Count + " allowed cids");
            }
        }

        static HashSet<int> LoadCardListCids(string dataDirectory)
        {
            HashSet<int> cids = new HashSet<int>();
            string path = Path.Combine(dataDirectory, "CardList.json");
            if (!File.Exists(path)) return cids;
            Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (doc == null) return cids;
            foreach (string key in doc.Keys)
            {
                int cid;
                if (int.TryParse(key, out cid)) cids.Add(cid);
            }
            return cids;
        }

        // Pulls the a0 (forbidden) list from a regulation entry. Empty set
        // if the entry is malformed or has no a0.
        static HashSet<int> ExtractForbidden(object regulationEntry)
        {
            HashSet<int> forbidden = new HashSet<int>();
            Dictionary<string, object> entry = regulationEntry as Dictionary<string, object>;
            if (entry == null) return forbidden;
            Dictionary<string, object> available = Utils.GetValue<Dictionary<string, object>>(entry, "available");
            if (available == null) return forbidden;
            object a0Obj;
            if (!available.TryGetValue("a0", out a0Obj)) return forbidden;
            List<object> a0 = a0Obj as List<object>;
            if (a0 == null) return forbidden;
            foreach (object o in a0)
            {
                try { forbidden.Add(Convert.ToInt32(o)); } catch { }
            }
            return forbidden;
        }

        // ----- resolution -----
        static void ResolveAll(DuelSettings ds)
        {
            // Track picks per player so the same cid isn't drawn twice into
            // adjacent slots (e.g. two random monsters in Z1/Z2).
            Dictionary<int, HashSet<int>> usedByPlayer = new Dictionary<int, HashSet<int>>
            {
                { 0, new HashSet<int>() },
                { 1, new HashSet<int>() },
            };

            int dropped = 0;
            for (int p = 0; p < ds.cmds.Length; p++)
            {
                List<int> cmds = ds.cmds[p];
                if (cmds == null) continue;
                List<int> rebuilt = ResolveCmdList(cmds, ds, usedByPlayer);
                if (rebuilt.Count == 0 && cmds.Count > 0) dropped++;
                ds.cmds[p] = rebuilt;
            }

            // Compact outer array — strip empty inner lists so the engine
            // doesn't receive `[]` entries.
            ds.SetCmds(ds.cmds.Where(c => c != null && c.Count > 0).ToArray());

            if (dropped > 0)
            {
                Console.WriteLine("[RuntimeRandomResolver] dropped " + dropped
                    + " cmd(s) with unresolvable random markers");
            }
        }

        // Rebuild a cmd list, resolving random markers and dropping any whose
        // pool comes up empty (leaving a negative cid would no-op on the
        // client; safer to omit the cmd entirely).
        static List<int> ResolveCmdList(List<int> cmds, DuelSettings ds,
                                         Dictionary<int, HashSet<int>> usedByPlayer)
        {
            const int TUPLE = 7;
            List<int> rebuilt = new List<int>(cmds.Count);
            for (int i = 0; i + TUPLE - 1 < cmds.Count; i += TUPLE)
            {
                int cmd       = cmds[i];
                int cmdPlayer = cmds[i + 1];
                int cid       = cmds[i + 4];

                int finalCid = cid;
                if (cmd == 0 && cid < 0)   // CheatCard with random marker
                {
                    finalCid = ResolveRandomMarker(cid, ds, cmdPlayer, usedByPlayer[cmdPlayer]);
                    if (finalCid <= 0) continue;   // drop unresolvable cmd
                }

                for (int j = 0; j < TUPLE; j++)
                {
                    rebuilt.Add(j == 4 ? finalCid : cmds[i + j]);
                }
            }
            return rebuilt;
        }

        // Returns a real cid for the marker, or 0 if the spec is missing /
        // pool is empty.
        static int ResolveRandomMarker(int cid, DuelSettings ds, int cmdPlayer,
                                        HashSet<int> used)
        {
            string key = (-cid).ToString();
            object specObj;
            if (!ds.random_specs.TryGetValue(key, out specObj)) return 0;
            Dictionary<string, object> spec = specObj as Dictionary<string, object>;
            if (spec == null) return 0;
            return PickRandom(spec, ds, cmdPlayer, used);
        }

        static int PickRandom(Dictionary<string, object> spec, DuelSettings ds,
                              int cmdPlayer, HashSet<int> used)
        {
            IEnumerable<int> pool = ResolvePool(spec, ds, cmdPlayer);
            if (pool == null) return 0;

            List<int> candidates = new List<int>();
            foreach (int cid in pool)
            {
                if (used.Contains(cid)) continue;
                if (!Matches(cid, spec)) continue;
                candidates.Add(cid);
            }
            if (candidates.Count == 0) return 0;
            int pick = candidates[rng.Next(candidates.Count)];
            used.Add(pick);
            return pick;
        }

        // Returns the cid pool the spec draws from, or null when the source
        // doesn't resolve (missing deck, missing any-bucket).
        static IEnumerable<int> ResolvePool(Dictionary<string, object> spec,
                                             DuelSettings ds, int cmdPlayer)
        {
            string source = Utils.GetValue<string>(spec, "source");
            if (string.IsNullOrEmpty(source)) source = "deck";

            if (source == "deck")
            {
                string owner = Utils.GetValue<string>(spec, "deck_owner");
                if (string.IsNullOrEmpty(owner)) owner = "own";
                int ownerIdx = ResolveOwnerIdx(owner, cmdPlayer);
                if (ds.Deck == null || ownerIdx < 0 || ownerIdx >= ds.Deck.Length
                        || ds.Deck[ownerIdx] == null) return null;
                return ds.Deck[ownerIdx]
                    .GetAllCards(main: true, extra: true, side: false)
                    .Distinct();
            }

            // "any" → regulation's allowed pool. Map name → id with
            // fallback to default regulation when unknown / missing.
            return ResolveAnyPool(ds.regulation_name);
        }

        static IEnumerable<int> ResolveAnyPool(string regulationName)
        {
            int regId;
            if (string.IsNullOrEmpty(regulationName)
                || !DeckInfo.RegulationIdsByName.TryGetValue(regulationName, out regId))
            {
                regId = DeckInfo.DefaultRegulationId;
            }
            HashSet<int> pool;
            return allowedByRegulation.TryGetValue(regId, out pool) ? pool : null;
        }

        static int ResolveOwnerIdx(string owner, int cmdPlayer)
        {
            switch (owner)
            {
                case "p1":    return 0;
                case "p2":    return 1;
                case "rival": return 1 - cmdPlayer;
                default:      return cmdPlayer;   // "own" + fallback
            }
        }

        // ----- card filter -----
        static bool Matches(int cid, Dictionary<string, object> spec)
        {
            // No DLL → can't query props; trust the encoder and accept.
            if (!dllReady) return true;
            // Props-missing cards (return 0 for every query) would render as
            // a broken 0/0 monster — always reject.
            if (!IsCardLoaded(cid)) return false;

            CardKind kind = (CardKind)DuelDllProps.DLL_CardGetKind(cid);
            int icon = DuelDllProps.DLL_CardGetIcon(cid);
            CardCategory cat = CategorizeKind((int)kind);

            if (!PassesRandomKind(spec, cat, icon)) return false;
            if (!PassesSubtype(spec, cat, kind, icon)) return false;
            if (cat == CardCategory.MainDeckMonster || cat == CardCategory.ExtraDeckMonster)
            {
                if (!PassesNumericFilters(spec, cid)) return false;
            }
            return true;
        }

        // `spec.random` — broad category filter. Empty / "any" / "true" = no
        // filter; otherwise the category must match.
        static bool PassesRandomKind(Dictionary<string, object> spec, CardCategory cat, int icon)
        {
            string rkind = Utils.GetValue<string>(spec, "random");
            if (string.IsNullOrEmpty(rkind) || rkind == "any" || rkind == "true") return true;

            bool isMainMonster  = cat == CardCategory.MainDeckMonster;
            bool isExtraMonster = cat == CardCategory.ExtraDeckMonster;
            bool isMonster      = isMainMonster || isExtraMonster;
            bool isSpell        = cat == CardCategory.Spell;
            bool isTrap         = cat == CardCategory.Trap;
            bool isFieldSpell   = isSpell && icon == spellTrapSubtypeIcons["field"];

            switch (rkind)
            {
                case "monster":       return isMonster;
                case "main_monster":  return isMainMonster;
                case "extra_monster": return isExtraMonster;
                case "spell":         return isSpell;
                case "trap":          return isTrap;
                case "field_spell":   return isFieldSpell;
                case "spell_or_trap": return isSpell || isTrap;
                default:              return true;   // unknown rkind = no filter
            }
        }

        // `spec.subtype` — fine-grained: kind-group for monsters, icon for
        // spells/traps. Unknown subtype rejects the card.
        static bool PassesSubtype(Dictionary<string, object> spec, CardCategory cat,
                                   CardKind kind, int icon)
        {
            string subtype = Utils.GetValue<string>(spec, "subtype");
            if (string.IsNullOrEmpty(subtype)) return true;
            subtype = subtype.ToLowerInvariant();

            if (cat == CardCategory.MainDeckMonster || cat == CardCategory.ExtraDeckMonster)
            {
                HashSet<CardKind> allowed;
                return monsterSubtypeKinds.TryGetValue(subtype, out allowed)
                    && allowed.Contains(kind);
            }
            if (cat == CardCategory.Spell || cat == CardCategory.Trap)
            {
                int wantIcon;
                return spellTrapSubtypeIcons.TryGetValue(subtype, out wantIcon)
                    && icon == wantIcon;
            }
            return false;   // subtype set but card isn't monster/spell/trap
        }

        static bool PassesNumericFilters(Dictionary<string, object> spec, int cid)
        {
            int atk = DuelDllProps.DLL_CardGetAtk(cid);
            int def = DuelDllProps.DLL_CardGetDef(cid);
            int lvl = DuelDllProps.DLL_CardGetLevel(cid);
            int v;
            if (TryGetInt(spec, "minAtk",   out v) && atk < v) return false;
            if (TryGetInt(spec, "maxAtk",   out v) && atk > v) return false;
            if (TryGetInt(spec, "minDef",   out v) && def < v) return false;
            if (TryGetInt(spec, "maxDef",   out v) && def > v) return false;
            if (TryGetInt(spec, "minLevel", out v) && lvl < v) return false;
            if (TryGetInt(spec, "maxLevel", out v) && lvl > v) return false;
            return true;
        }

        // ----- low-level helpers -----
        static CardCategory CategorizeKind(int kind)
        {
            CardCategory c;
            return kindCategory.TryGetValue((CardKind)kind, out c) ? c : CardCategory.Other;
        }

        static bool IsCardLoaded(int cid)
        {
            return !(DuelDllProps.DLL_CardGetKind(cid)  == 0
                  && DuelDllProps.DLL_CardGetAtk(cid)   == 0
                  && DuelDllProps.DLL_CardGetDef(cid)   == 0
                  && DuelDllProps.DLL_CardGetLevel(cid) == 0);
        }

        static bool TryGetInt(Dictionary<string, object> spec, string key, out int value)
        {
            value = 0;
            object o;
            if (!spec.TryGetValue(key, out o) || o == null) return false;
            try { value = Convert.ToInt32(o); return true; }
            catch { return false; }
        }
    }
}
