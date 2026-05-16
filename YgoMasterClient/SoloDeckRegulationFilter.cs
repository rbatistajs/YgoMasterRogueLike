// Filter the Solo "Choose Deck" list down to decks whose RegulationId
// matches the active Solo regulation that the server publishes at
// `Solo.deck_info.regulation` (mirrored from Act_Room — stored as a JSON
// string, hence the int.TryParse).
//
// Non-obvious: `m_TemplateList` carries cell-template-type ids, NOT indices
// into `m_Decks`. We therefore filter `m_Decks` itself and re-emit the
// template list with the new size (all zeros — the only template id seen
// in practice; a future build using non-zero ids would need a smarter
// copy-and-trim).

using System;
using System.Collections.Generic;
using IL2CPP;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class SoloDeckRegulationFilter
    {
        static IL2Field fieldSelectMode;
        static IL2Field fieldDecks;
        static IL2Field fieldTemplateList;
        static IL2Class deckRefClass;
        static IL2Field fieldDeckRefRegID;
        static int selectModeSolo;          // resolved from the IL2 enum to avoid baking in an index
        static bool enabled;

        delegate void Del_UpdateTemplateList(IntPtr thisPtr);
        static Hook<Del_UpdateTemplateList> hookUpdateTemplateList;

        static SoloDeckRegulationFilter()
        {
            try
            {
                IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class vcClass = assembly.GetClass("DeckSelectViewController2", "YgomGame");
                fieldSelectMode = vcClass.GetField("m_SelectMode");
                fieldDecks = vcClass.GetField("m_Decks");
                fieldTemplateList = vcClass.GetField("m_TemplateList");

                deckRefClass = vcClass.GetNestedType("DeckReference");
                fieldDeckRefRegID = deckRefClass != null ? deckRefClass.GetField("regID") : null;

                IL2Class selectModeEnum = vcClass.GetNestedType("SelectMode");
                IL2Field soloField = selectModeEnum != null ? selectModeEnum.GetField("Solo") : null;
                if (soloField != null)
                {
                    selectModeSolo = soloField.GetValue().GetValueRef<int>();
                }

                if (deckRefClass == null || fieldDeckRefRegID == null) return;

                hookUpdateTemplateList = new Hook<Del_UpdateTemplateList>(
                    UpdateTemplateList,
                    vcClass.GetMethod("UpdateTemplateList"));
                enabled = true;
            }
            catch (Exception ex)
            {
                Utils.LogWarning("SoloDeckRegulationFilter init failed: " + ex);
            }
        }

        static void UpdateTemplateList(IntPtr thisPtr)
        {
            hookUpdateTemplateList.Original(thisPtr);
            if (!enabled) return;
            try
            {
                Filter(thisPtr);
            }
            catch (Exception ex)
            {
                Utils.LogWarning("SoloDeckRegulationFilter filter EX: " + ex);
            }
        }

        static void Filter(IntPtr thisPtr)
        {
            IL2Object selectModeObj = fieldSelectMode.GetValue(thisPtr);
            if (selectModeObj == null || selectModeObj.GetValueRef<int>() != selectModeSolo) return;

            int activeRegulation = GetActiveSoloRegulation();
            if (activeRegulation <= 0) return;

            IL2Object decksObj = fieldDecks.GetValue(thisPtr);
            IL2Object templateObj = fieldTemplateList.GetValue(thisPtr);
            if (decksObj == null || templateObj == null) return;

            IL2ListExplicit decks = new IL2ListExplicit(decksObj.ptr, deckRefClass);
            int deckCount = decks.Count;
            List<IntPtr> kept = new List<IntPtr>(deckCount);
            int dropped = 0;
            for (int i = 0; i < deckCount; i++)
            {
                IntPtr p = decks[i];
                if (p == IntPtr.Zero) continue;
                int reg = fieldDeckRefRegID.GetValue(p).GetValueRef<int>();
                if (reg == activeRegulation) kept.Add(p);
                else dropped++;
            }
            if (dropped == 0) return;

            decks.Clear();
            foreach (IntPtr p in kept) decks.Add(p);

            IL2List<int> templateList = new IL2List<int>(templateObj.ptr);
            templateList.Clear();
            for (int i = 0; i < kept.Count; i++)
            {
                int v = 0;
                templateList.Add(new IntPtr(&v));
            }
        }

        static int GetActiveSoloRegulation()
        {
            string s = YgomSystem.Utility.ClientWork.GetStringByJsonPath("Solo.deck_info.regulation", "");
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }
    }
}
