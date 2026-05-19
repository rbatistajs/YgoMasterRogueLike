// Map CardIcon.Ritual_R (7) -> CardIcon.Ritual (6) at icon-resolve time.
// The Rush mod added Ritual_R as a distinct icon value but the client has
// no sprite registered for it, so cards using it render iconless. Falling
// back to the vanilla Ritual sprite is the visually-correct choice.

using System;
using IL2CPP;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class RushRitualIcon
    {
        delegate int Del_DLL_CardGetIcon(int cardId);
        static Hook<Del_DLL_CardGetIcon> hook;
        static bool enabled;

        static RushRitualIcon()
        {
            try
            {
                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class contentClass = assembly.GetClass("Content", "YgomGame.Card");
                IL2Method method = contentClass.GetMethod("DLL_CardGetIcon");
                if (method == null) return;
                hook = new Hook<Del_DLL_CardGetIcon>(GetIcon, method);
                enabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushRitualIcon] init EX: " + ex);
            }
        }

        static int GetIcon(int cardId)
        {
            int icon = hook.Original(cardId);
            if (enabled && icon == (int)CardIcon.Ritual_R)
                return (int)CardIcon.Ritual;
            return icon;
        }
    }
}
