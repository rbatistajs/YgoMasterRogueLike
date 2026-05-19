// Rush Legend rule: deck may contain at most 1 Legend card per Type
// (1 Legend Monster + 1 Legend Spell + 1 Legend Trap). The Legend cid
// list is pushed by the server in Act_UserHome under $.Master.CardLegend.
//
// Invoked from DeckEditorUtils.GetAddableType (the official add-validation
// hook). Framework allows only one hook per method, so we expose a static
// helper that the existing GetAddableType handler calls.

using System;
using System.Collections.Generic;
using IL2CPP;
using YgoMaster;
using YgomGame.Deck;

namespace YgoMasterClient
{
    static unsafe class RushLegendLimit
    {
        // Canonical Legend cids from server (Master.CardLegend). Variants
        // (alt-art etc.) are resolved on the fly via DLL_CardIsThisSameCard
        // and cached in _legendVariantCache.
        static HashSet<int> _legends;
        static Dictionary<int, bool> _legendVariantCache = new Dictionary<int, bool>();
        static IL2Method _methodDLLCardGetKind;
        static IL2Method _methodDLLCardIsThisSameCard;
        static IL2Field _fieldMainList;
        static IL2Field _fieldExtraList;
        static IL2Class _cardBaseDataClass;
        static bool _initialized;

        static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class contentClass = assembly.GetClass("Content", "YgomGame.Card");
                IL2Class deckViewClass = assembly.GetClass("DeckView", "YgomGame.Deck");
                _methodDLLCardGetKind = contentClass.GetMethod("DLL_CardGetKind");
                _methodDLLCardIsThisSameCard = contentClass.GetMethod("DLL_CardIsThisSameCard");
                _fieldMainList = deckViewClass.GetField("mainCardDataList");
                _fieldExtraList = deckViewClass.GetField("extraCardDataList");
                _cardBaseDataClass = assembly.GetClass("CardBaseData", "YgomGame.Deck");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushLegendLimit] init EX: " + ex);
            }
        }

        static HashSet<int> GetLegendSet()
        {
            if (_legends != null) return _legends;
            HashSet<int> set = new HashSet<int>();
            try
            {
                string json = YgomSystem.Utility.ClientWork.SerializePath("$.Master.CardLegend");
                if (!string.IsNullOrEmpty(json))
                {
                    List<object> arr = MiniJSON.Json.Deserialize(json) as List<object>;
                    if (arr != null)
                    {
                        foreach (object o in arr)
                        {
                            try { set.Add(Convert.ToInt32(o)); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushLegendLimit] load EX: " + ex);
            }
            _legends = set;
            return _legends;
        }

        enum LegendType { Monster, Spell, Trap }

        static LegendType TypeOf(int cardId)
        {
            int kind = _methodDLLCardGetKind.Invoke(IntPtr.Zero,
                new IntPtr[] { new IntPtr(&cardId) }).GetValueRef<int>();
            if (kind == (int)CardKind.Magic) return LegendType.Spell;
            if (kind == (int)CardKind.Trap)  return LegendType.Trap;
            return LegendType.Monster;
        }

        public static bool IsLegendVariantPublic(int cardId)
        {
            EnsureInitialized();
            return IsLegendVariant(cardId);
        }

        // True when `cardId` is itself a canonical Legend OR shares CARD_Same
        // with a canonical Legend. Result is cached per cid since the answer
        // is static for the session.
        static bool IsLegendVariant(int cardId)
        {
            if (_legendVariantCache.TryGetValue(cardId, out bool cached)) return cached;
            HashSet<int> legends = GetLegendSet();
            bool isLegend = legends.Contains(cardId);
            if (!isLegend && _methodDLLCardIsThisSameCard != null)
            {
                int cidLocal = cardId;
                foreach (int legendCid in legends)
                {
                    int legendLocal = legendCid;
                    int sameResult = _methodDLLCardIsThisSameCard.Invoke(IntPtr.Zero,
                        new IntPtr[] { new IntPtr(&cidLocal), new IntPtr(&legendLocal) })
                        .GetValueRef<int>();
                    if (sameResult != 0) { isLegend = true; break; }
                }
            }
            _legendVariantCache[cardId] = isLegend;
            return isLegend;
        }

        // Returns true if adding this card would violate "1 Legend per Type".
        // Returns false if the card is not a Legend variant (no constraint).
        // Bypassed entirely when ClientSettings.DisableRushLegendLimit is true.
        public static bool WouldExceedLegendLimit(IntPtr deckView, int cardId)
        {
            if (ClientSettings.DisableRushLegendLimit) return false;
            EnsureInitialized();
            if (_methodDLLCardGetKind == null) return false;
            if (GetLegendSet().Count == 0) return false;
            if (!IsLegendVariant(cardId)) return false;

            LegendType targetType = TypeOf(cardId);
            return ListContainsLegendOfType(_fieldMainList, deckView, targetType)
                || ListContainsLegendOfType(_fieldExtraList, deckView, targetType);
        }

        static bool ListContainsLegendOfType(IL2Field listField, IntPtr deckView, LegendType target)
        {
            if (listField == null) return false;
            IL2Object listObj = listField.GetValue(deckView);
            if (listObj == null) return false;
            IL2ListExplicit list = new IL2ListExplicit(listObj.ptr, _cardBaseDataClass);
            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                CardBaseData cd = list.GetRef<CardBaseData>(i);
                if (!IsLegendVariant(cd.CardID)) continue;
                if (TypeOf(cd.CardID) == target) return true;
            }
            return false;
        }
    }
}
