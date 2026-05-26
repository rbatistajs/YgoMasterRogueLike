using IL2CPP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using YgoMasterClient;

namespace YgomGame.Menu
{
    unsafe static class ActionSheetViewController
    {
        static IL2Method methodOpen;
        static IL2Method methodOpenCustom;   // OpenCustomSheet(...)
        static IL2Method entryCreateEntrys;  // EntryData.CreateEntrys(IReadOnlyList<string>, int) -> EntryData[]
        static IL2Field fieldOffCloseKey;    // k_ArgKeyOffCloseButton (the actual arg-dict key string)
        static IL2Class buttonStyleClass, entryWidgetClass, actionStyleWidgetClass; // for the OnCreate callback
        static readonly Action<IntPtr, int, IntPtr> onCreateBtnDetour = OnCreateButton; // hide cancel + title hook
        static bool hideCancelFlag;             // current sheet: hide the CANCEL button?
        static Action<IntPtr> onTitleAreaCb;    // current sheet: callback given the TitleArea GameObject
        static bool titleAreaFired;             // invoke onTitleAreaCb once per open

        static ActionSheetViewController()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("ActionSheetViewController", "YgomGame.Menu");
            methodOpen = classInfo.GetMethod("Open", x => x.GetParameters().Length == 3);
            fieldOffCloseKey = classInfo.GetField("k_ArgKeyOffCloseButton");
            methodOpenCustom = classInfo.GetMethod("OpenCustomSheet");
            IL2Class entryData = classInfo.GetNestedType("EntryData");
            entryCreateEntrys = entryData != null ? entryData.GetMethod("CreateEntrys", x => x.GetParameters().Length == 2) : null;
            buttonStyleClass = classInfo.GetNestedType("ButtonStyle");
            entryWidgetClass = classInfo.GetNestedType("EntryButtonWidget");
        }

        // Full N-option sheet via OpenCustomSheet. Builds EntryData[] from the strings (the game's own
        // EntryData.CreateEntrys), shows a `message` body, and passes OffCloseButton (kills tap-outside).
        // hideCancel hides the sheet's CancelButton (non-cancelable). onTitleArea (if set) is invoked
        // once after the sheet builds with the TitleArea GameObject, so callers can inject art there.
        public static void OpenCustomSheet(string title, string message, string[] entries, Action<IntPtr, int> callback, bool hideCancel = false, Action<IntPtr> onTitleArea = null)
        {
            hideCancelFlag = hideCancel;
            onTitleAreaCb = onTitleArea;
            titleAreaFired = false;
            IL2Class stringClass = typeof(string).GetClass();
            IL2ListExplicit entriesList = new IL2ListExplicit(IntPtr.Zero, stringClass, true);
            foreach (string str in entries) entriesList.Add(new IL2String(str).ptr);
            IntPtr entriesReadOnlyList = entriesList.MethodAsReadOnly();

            int destructive = 0;
            IL2Object entryArr = entryCreateEntrys.Invoke(new IntPtr[] { entriesReadOnlyList, new IntPtr(&destructive) });
            IntPtr entryArrPtr = entryArr != null ? entryArr.ptr : IntPtr.Zero;

            // Only kill tap-outside for modal sheets; cancelable ones keep close-on-tap-outside.
            IntPtr argsPtr = IntPtr.Zero;
            if (hideCancel)
            {
                string offKey = fieldOffCloseKey != null ? fieldOffCloseKey.GetValue().GetValueObj<string>() : "offCloseButton";
                argsPtr = YgomMiniJSON.Json.Deserialize(MiniJSON.Json.Serialize(
                    new Dictionary<string, object> { { offKey, true } }));
            }

            methodOpenCustom.Invoke(new IntPtr[]
            {
                new IL2String(title).ptr,
                entryArrPtr,
                IntPtr.Zero, // customButtonMap
                (hideCancel || onTitleArea != null) ? BuildOnCreateCallback() : IntPtr.Zero, // customOnCreateCallback
                IntPtr.Zero, // customOnUpdateButtonCallback
                UnityEngine.Events._UnityAction.CreateAction<int>(callback),
                IntPtr.Zero, // onCancel
                new IL2String(message).ptr,
                IntPtr.Zero, // embedObject
                argsPtr
            });
        }

        // Action<ButtonStyle, EntryButtonWidget> il2cpp delegate bound to OnCreateButton.
        static IntPtr BuildOnCreateCallback()
        {
            if (buttonStyleClass == null || entryWidgetClass == null) return IntPtr.Zero;
            if (actionStyleWidgetClass == null)
                actionStyleWidgetClass = typeof(Action<,>).GetClass().MakeGenericType(
                    new IntPtr[] { buttonStyleClass.IL2Typeof(), entryWidgetClass.IL2Typeof() });
            return UnityEngine.Events._UnityAction.CreateDelegate<IntPtr>(onCreateBtnDetour, IntPtr.Zero, actionStyleWidgetClass);
        }

        // Fires as each entry button is created: hide the CANCEL button (modal) and hand the TitleArea
        // GameObject to the caller once (so it can inject art/content there).
        static void OnCreateButton(IntPtr ctx, int style, IntPtr widget)
        {
            try
            {
                IntPtr asheet = UnityEngine.GameObject.Find("ActionSheet");
                if (asheet == IntPtr.Zero) return;
                if (hideCancelFlag)
                {
                    IntPtr cb = UnityEngine.GameObject.FindGameObjectByName(asheet, "CancelButton");
                    if (cb != IntPtr.Zero) UnityEngine.GameObject.SetActive(cb, false);
                }
                if (onTitleAreaCb != null && !titleAreaFired)
                {
                    IntPtr ta = UnityEngine.GameObject.FindGameObjectByName(asheet, "TitleArea");
                    if (ta != IntPtr.Zero) { titleAreaFired = true; onTitleAreaCb(ta); }
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] custom sheet cb EX: " + ex); }
        }

        public static void Open(string title, string[] entries, Action<IntPtr, int> callback)
        {
            IL2Class stringClass = typeof(string).GetClass();
            IL2ListExplicit entriesList = new IL2ListExplicit(IntPtr.Zero, stringClass, true);
            foreach (string str in entries)
            {
                entriesList.Add(new IL2String(str).ptr);
            }
            IntPtr entriesReadOnlyList = entriesList.MethodAsReadOnly();

            methodOpen.Invoke(new IntPtr[]
            {
                new IL2String(title).ptr,
                entriesReadOnlyList,
                UnityEngine.Events._UnityAction.CreateAction<int>(callback)
            });
        }
    }
}
