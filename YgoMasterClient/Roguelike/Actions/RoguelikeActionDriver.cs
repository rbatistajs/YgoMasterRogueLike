using System;

namespace YgoMasterClient
{
    // Renders the server's current action prompt ($.Roguelike.action) and sends the player's choice
    // back. Driven by RoguelikeFlow.OnNetworkComplete; token dedup keeps a prompt from re-opening.
    //
    // Prompts use CommonDialogViewController with allowCancel:false (natively non-cancelable — no
    // cancel button, no tap-outside): message/1-option -> confirmation, 2-option -> yes/no. 3+ options
    // fall back to the ActionSheet (interim; CommonDialog tops out at 2 tap-buttons).
    static class RoguelikeActionDriver
    {
        static int _shownToken = -1;
        static readonly Action _opt0 = () => RoguelikeApi.ActionRespond(0);
        static readonly Action<IntPtr, int> _onSelect = OnOptionSelected;

        public static void Pump()
        {
            try
            {
                RoguelikeApi.ActionPrompt p = RoguelikeApi.GetActionPrompt();
                if (p == null) { _shownToken = -1; return; }
                if (p.Token == _shownToken) return; // already shown
                _shownToken = p.Token;

                if (p.Type == "message")
                    YgomGame.Menu.CommonDialogViewController.OpenConfirmationDialog(
                        p.Title, p.Message, RoguelikeLabels.Get("common.ok", "OK"), _opt0, null, false);
                else // options -> N-option custom sheet (title header + message body), non-cancelable
                    YgomGame.Menu.ActionSheetViewController.OpenCustomSheet(p.Title, p.Message, p.Options ?? new string[0], _onSelect, true);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] action pump EX: " + ex); }
        }

        static void OnOptionSelected(IntPtr thisPtr, int index)
        {
            if (index >= 0) RoguelikeApi.ActionRespond(index);
        }
    }
}
