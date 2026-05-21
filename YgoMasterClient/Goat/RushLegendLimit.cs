// Rush Legend rule: deck may contain at most 1 Legend card per Type
// (1 Legend Monster + 1 Legend Spell + 1 Legend Trap). Legend status is read
// straight from the native duel.dll via DuelDll.CardIsLegend (the
// flag lives in CARD_Prop), so no client-side list/cache is needed and
// variants are covered automatically (they carry the bit too).
//
// Invoked from DeckEditorUtils.GetAddableType (the official add-validation
// hook). Framework allows only one hook per method, so we expose a static
// helper that the existing GetAddableType handler calls.

using System;
using IL2CPP;
using YgoMaster;
using YgomGame.Deck;

namespace YgoMasterClient
{
    static unsafe class RushLegendLimit
    {
        static IL2Method _methodDLLCardGetKind;
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
                _fieldMainList = deckViewClass.GetField("mainCardDataList");
                _fieldExtraList = deckViewClass.GetField("extraCardDataList");
                _cardBaseDataClass = assembly.GetClass("CardBaseData", "YgomGame.Deck");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushLegendLimit] init EX: " + ex);
            }
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

        // Returns true if adding this card would violate "1 Legend per Type".
        // Returns false if the card is not a Legend (no constraint).
        // Bypassed entirely when ClientSettings.DisableRushLegendLimit is true.
        public static bool WouldExceedLegendLimit(IntPtr deckView, int cardId)
        {
            if (ClientSettings.DisableRushLegendLimit) return false;
            EnsureInitialized();
            if (_methodDLLCardGetKind == null) return false;
            if (!DuelDll.CardIsLegend(cardId)) return false;

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
                if (!DuelDll.CardIsLegend(cd.CardID)) continue;
                if (TypeOf(cd.CardID) == target) return true;
            }
            return false;
        }
    }
}
