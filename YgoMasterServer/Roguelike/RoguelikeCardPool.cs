using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Pure-C# card classifier for random modifier specs — no duel.dll. Parses CARD_Prop via
    // YdkHelper.GameCardInfo (cached once) and answers "does this cid match this random spec?"
    // (kind / subtype / atk-def-level filters). Avoids the server-side DLL card queries that
    // caused table corruption before.
    static class RoguelikeCardPool
    {
        static Dictionary<int, YdkHelper.GameCardInfo> _cards;
        static readonly object _lock = new object();

        static Dictionary<int, YdkHelper.GameCardInfo> Cards(string dataDirectory)
        {
            if (_cards != null) return _cards;
            lock (_lock)
            {
                if (_cards == null)
                {
                    try { _cards = YdkHelper.LoadCardDataFromGame(dataDirectory); }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[Roguelike] card data load failed (random filters disabled): " + ex.Message);
                        _cards = new Dictionary<int, YdkHelper.GameCardInfo>();
                    }
                }
            }
            return _cards;
        }

        // spell/trap subtype label -> CardIcon int (same bit field as GameCardInfo.Icon).
        static readonly Dictionary<string, int> SpellTrapIcon = new Dictionary<string, int>
        {
            { "normal", 0 }, { "counter", 1 }, { "field", 2 }, { "equip", 3 },
            { "continuous", 4 }, { "quickplay", 5 }, { "ritual", 6 },
        };

        // True if cid satisfies the spec's random kind / subtype / numeric filters.
        public static bool Matches(string dataDirectory, int cid, Dictionary<string, object> spec)
        {
            YdkHelper.GameCardInfo c;
            if (!Cards(dataDirectory).TryGetValue(cid, out c) || !c.Exist) return false;
            if (!PassesKind(Utils.GetValue<string>(spec, "random"), c)) return false;
            string subtype = Utils.GetValue<string>(spec, "subtype");
            if (!string.IsNullOrEmpty(subtype) && !PassesSubtype(subtype.ToLowerInvariant(), c)) return false;
            if (c.IsMonster && !PassesNumeric(spec, c)) return false;
            return true;
        }

        static bool PassesKind(string rkind, YdkHelper.GameCardInfo c)
        {
            if (string.IsNullOrEmpty(rkind) || rkind == "any" || rkind == "true") return true;
            bool isSpell = c.Frame == CardFrame.Magic;
            bool isTrap = c.Frame == CardFrame.Trap;
            switch (rkind)
            {
                case "monster":       return c.IsMonster;
                case "main_monster":  return c.IsMonster && c.IsMainDeck;
                case "extra_monster": return c.IsMonster && c.IsExtraDeck;
                case "spell":         return isSpell;
                case "trap":          return isTrap;
                case "field_spell":   return isSpell && (int)c.Icon == 2;
                case "spell_or_trap": return isSpell || isTrap;
                default:              return true; // unknown kind = no filter
            }
        }

        static bool PassesSubtype(string subtype, YdkHelper.GameCardInfo c)
        {
            if (c.IsMonster) return MonsterSubtype(subtype, c.Frame);
            if (c.Frame == CardFrame.Magic || c.Frame == CardFrame.Trap)
            {
                int icon;
                return SpellTrapIcon.TryGetValue(subtype, out icon) && (int)c.Icon == icon;
            }
            return false; // subtype set but card is neither monster nor spell/trap
        }

        // Coarse monster subtype by frame (DLL-free).
        static bool MonsterSubtype(string subtype, CardFrame frame)
        {
            switch (subtype)
            {
                case "normal":  return frame == CardFrame.Normal;
                case "effect":  return frame == CardFrame.Effect;
                case "ritual":  return frame == CardFrame.Ritual || frame == CardFrame.RitualPend;
                case "fusion":  return frame == CardFrame.Fusion || frame == CardFrame.FusionPend;
                case "synchro": return frame == CardFrame.Sync || frame == CardFrame.SyncPend;
                case "xyz":     return frame == CardFrame.Xyz || frame == CardFrame.XyzPend;
                case "link":    return frame == CardFrame.Link;
                default:        return false;
            }
        }

        static bool PassesNumeric(Dictionary<string, object> spec, YdkHelper.GameCardInfo c)
        {
            int v;
            if (TryInt(spec, "minAtk", out v) && c.Atk < v) return false;
            if (TryInt(spec, "maxAtk", out v) && c.Atk > v) return false;
            if (TryInt(spec, "minDef", out v) && c.Def < v) return false;
            if (TryInt(spec, "maxDef", out v) && c.Def > v) return false;
            if (TryInt(spec, "minLevel", out v) && c.Level < v) return false;
            if (TryInt(spec, "maxLevel", out v) && c.Level > v) return false;
            return true;
        }

        static bool TryInt(Dictionary<string, object> d, string key, out int value)
        {
            value = 0;
            object o;
            if (d == null || !d.TryGetValue(key, out o) || o == null) return false;
            try { value = Convert.ToInt32(o); return true; } catch { return false; }
        }

        // ----- "any" pool (DataLE/Roguelike/CardPool.json) -----
        // Configurable cid pool for `source:"any"` random picks (and, later, card rewards). Base =
        // CardList minus the configured/default regulation's banlist; then global and per-ascension
        // include/exclude. Cached per ascension; restart the server to apply edits.
        static Dictionary<string, object> _poolCfg;
        static bool _poolCfgLoaded;
        static readonly Dictionary<int, HashSet<int>> _anyByAsc = new Dictionary<int, HashSet<int>>();

        static Dictionary<string, object> PoolConfig(string dataDirectory)
        {
            if (_poolCfgLoaded) return _poolCfg;
            _poolCfgLoaded = true;
            string p = Path.Combine(dataDirectory, "Roguelike", "CardPool.json");
            if (File.Exists(p))
            {
                try { _poolCfg = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>; }
                catch (Exception ex) { Console.WriteLine("[Roguelike] CardPool.json parse EX: " + ex.Message); }
            }
            return _poolCfg;
        }

        public static HashSet<int> AnyPool(string dataDirectory, Dictionary<string, object> regulation, int ascension)
        {
            HashSet<int> cached;
            if (_anyByAsc.TryGetValue(ascension, out cached)) return cached;

            Dictionary<string, object> cfg = PoolConfig(dataDirectory);
            HashSet<int> pool = LoadCardListCids(dataDirectory);

            int regId = DeckInfo.DefaultRegulationId;
            string regName = cfg != null ? Utils.GetValue<string>(cfg, "regulation") : null;
            if (!string.IsNullOrEmpty(regName) && !DeckInfo.RegulationIdsByName.TryGetValue(regName, out regId))
            {
                Console.WriteLine("[Roguelike] CardPool regulation '" + regName + "' unknown — using default");
                regId = DeckInfo.DefaultRegulationId;
            }
            pool.ExceptWith(Banned(regulation, regId));

            if (cfg != null)
            {
                ApplyIncludeExclude(pool, Utils.GetValue<List<object>>(cfg, "include"), Utils.GetValue<List<object>>(cfg, "exclude"));
                Dictionary<string, object> asc = ItemAt(Utils.GetValue<List<object>>(cfg, "byAscension"), ascension);
                if (asc != null)
                    ApplyIncludeExclude(pool, Utils.GetValue<List<object>>(asc, "include"), Utils.GetValue<List<object>>(asc, "exclude"));
            }

            _anyByAsc[ascension] = pool;
            Console.WriteLine("[Roguelike] any-pool asc " + ascension + ": " + pool.Count + " cids");
            return pool;
        }

        static void ApplyIncludeExclude(HashSet<int> pool, List<object> include, List<object> exclude)
        {
            if (include != null) foreach (object o in include) { int c; if (TryCid(o, out c)) pool.Add(c); }
            if (exclude != null) foreach (object o in exclude) { int c; if (TryCid(o, out c)) pool.Remove(c); }
        }

        static HashSet<int> Banned(Dictionary<string, object> regulation, int regId)
        {
            HashSet<int> banned = new HashSet<int>();
            Dictionary<string, object> entry = regulation != null
                ? Utils.GetValue<Dictionary<string, object>>(regulation, regId.ToString()) : null;
            Dictionary<string, object> avail = entry != null
                ? Utils.GetValue<Dictionary<string, object>>(entry, "available") : null;
            List<object> a0 = avail != null ? Utils.GetValue<List<object>>(avail, "a0") : null;
            if (a0 != null) foreach (object o in a0) { int c; if (TryCid(o, out c)) banned.Add(c); }
            return banned;
        }

        static HashSet<int> LoadCardListCids(string dataDirectory)
        {
            HashSet<int> cids = new HashSet<int>();
            string path = Path.Combine(dataDirectory, "CardList.json");
            if (!File.Exists(path)) { Console.WriteLine("[Roguelike] CardList.json not found — 'any' pool empty"); return cids; }
            try
            {
                Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(File.ReadAllText(path)) as Dictionary<string, object>;
                if (doc != null) foreach (string k in doc.Keys) { int c; if (int.TryParse(k, out c)) cids.Add(c); }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] CardList.json parse EX: " + ex.Message); }
            return cids;
        }

        static Dictionary<string, object> ItemAt(List<object> list, int i)
        {
            return (list != null && i >= 0 && i < list.Count) ? list[i] as Dictionary<string, object> : null;
        }

        static bool TryCid(object o, out int cid) { cid = 0; try { cid = Convert.ToInt32(o); return true; } catch { return false; } }
    }
}
