using System;
using System.Collections.Generic;

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
    }
}
