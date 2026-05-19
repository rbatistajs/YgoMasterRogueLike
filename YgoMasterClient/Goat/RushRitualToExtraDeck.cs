// Route Rush ritual monsters (CardKind.R_Ritual / R_RitualFX) to the Extra
// Deck regardless of regulation. Rule is kind-based, not regulation-based.

using System;
using IL2CPP;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class RushRitualToExtraDeck
    {
        static IL2Method methodDLLCardGetKind;
        static bool enabled;

        delegate csbool Del_IsExtraDeckCard(IntPtr thisPtr, int mrk);
        static Hook<Del_IsExtraDeckCard> hookIsExtraDeckCard;

        static RushRitualToExtraDeck()
        {
            try
            {
                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class contentClass = assembly.GetClass("Content", "YgomGame.Card");
                methodDLLCardGetKind = contentClass.GetMethod("DLL_CardGetKind");
                IL2Method methodIsExtraDeckCard = contentClass.GetMethod("IsExtraDeckCard");
                if (methodDLLCardGetKind == null || methodIsExtraDeckCard == null) return;

                hookIsExtraDeckCard = new Hook<Del_IsExtraDeckCard>(
                    IsExtraDeckCard, methodIsExtraDeckCard);
                enabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushRitualToExtraDeck] init EX: " + ex);
            }
        }

        static csbool IsExtraDeckCard(IntPtr thisPtr, int mrk)
        {
            csbool original = hookIsExtraDeckCard.Original(thisPtr, mrk);
            if (!enabled) return original;

            // IntPtr.Zero (not null) — null would resolve to the IL2Object
            // overload and NRE on obj.ptr.
            int kind = methodDLLCardGetKind.Invoke(IntPtr.Zero,
                new IntPtr[] { new IntPtr(&mrk) }).GetValueRef<int>();
            if (kind == (int)CardKind.R_Ritual || kind == (int)CardKind.R_RitualFX)
                return (csbool)true;
            return original;
        }
    }
}
