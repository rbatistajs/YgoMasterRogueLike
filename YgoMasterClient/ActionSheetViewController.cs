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
        static IL2Method methodOpenEmbed;
        static IL2Method methodOpenCustom;   // OpenCustomSheet(...)
        static IL2Method entryCreateEntrys;  // EntryData.CreateEntrys(IReadOnlyList<string>, int) -> EntryData[]
        static IL2Field fieldOffCloseKey;    // k_ArgKeyOffCloseButton (the actual arg-dict key string)
        static IL2Class buttonStyleClass, entryWidgetClass, actionStyleWidgetClass; // for the OnCreate callback
        static readonly Action<IntPtr, int, IntPtr> onCreateBtnDetour = OnCreateButton; // hides CancelButtonArea

        static ActionSheetViewController()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("ActionSheetViewController", "YgomGame.Menu");
            methodOpen = classInfo.GetMethod("Open", x => x.GetParameters().Length == 3);
            // OpenWithEmbedObject(string, GameObject, IReadOnlyList<string>, int destructiveLength, Action<int>, Action)
            methodOpenEmbed = classInfo.GetMethod("OpenWithEmbedObject",
                x => x.GetParameters().Length == 6 && x.GetParameters()[3].Name == "destructiveLength");
            fieldOffCloseKey = classInfo.GetField("k_ArgKeyOffCloseButton");
            methodOpenCustom = classInfo.GetMethod("OpenCustomSheet");
            IL2Class entryData = classInfo.GetNestedType("EntryData");
            entryCreateEntrys = entryData != null ? entryData.GetMethod("CreateEntrys", x => x.GetParameters().Length == 2) : null;
            buttonStyleClass = classInfo.GetNestedType("ButtonStyle");
            entryWidgetClass = classInfo.GetNestedType("EntryButtonWidget");
        }

        // Full N-option sheet via OpenCustomSheet. Builds EntryData[] from the strings (the game's own
        // EntryData.CreateEntrys), shows a `message` body, and passes OffCloseButton (kills tap-outside).
        // hideCancel wires customOnCreateCallback to hide the sheet's CancelButton (non-cancelable).
        public static void OpenCustomSheet(string title, string message, string[] entries, Action<IntPtr, int> callback, bool hideCancel = false)
        {
            IL2Class stringClass = typeof(string).GetClass();
            IL2ListExplicit entriesList = new IL2ListExplicit(IntPtr.Zero, stringClass, true);
            foreach (string str in entries) entriesList.Add(new IL2String(str).ptr);
            IntPtr entriesReadOnlyList = entriesList.MethodAsReadOnly();

            int destructive = 0;
            IL2Object entryArr = entryCreateEntrys.Invoke(new IntPtr[] { entriesReadOnlyList, new IntPtr(&destructive) });
            IntPtr entryArrPtr = entryArr != null ? entryArr.ptr : IntPtr.Zero;

            string offKey = fieldOffCloseKey != null ? fieldOffCloseKey.GetValue().GetValueObj<string>() : "offCloseButton";
            IntPtr argsPtr = YgomMiniJSON.Json.Deserialize(MiniJSON.Json.Serialize(
                new Dictionary<string, object> { { offKey, true } }));

            methodOpenCustom.Invoke(new IntPtr[]
            {
                new IL2String(title).ptr,
                entryArrPtr,
                IntPtr.Zero, // customButtonMap
                hideCancel ? BuildOnCreateCallback() : IntPtr.Zero, // customOnCreateCallback -> hide CANCEL
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

        // Fires as each entry button is created -> hide the sheet's CANCEL button (makes it modal).
        static void OnCreateButton(IntPtr ctx, int style, IntPtr widget)
        {
            try
            {
                IntPtr asheet = UnityEngine.GameObject.Find("ActionSheet");
                IntPtr cb = asheet != IntPtr.Zero ? UnityEngine.GameObject.FindGameObjectByName(asheet, "CancelButton") : IntPtr.Zero;
                if (cb != IntPtr.Zero) UnityEngine.GameObject.SetActive(cb, false);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] hide cancel EX: " + ex); }
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

        // Like Open, but embeds a caller-built GameObject in the sheet's EmbedObjectArea.
        public static void OpenWithEmbedObject(string title, IntPtr embedObject, string[] entries, Action<IntPtr, int> callback)
        {
            IL2Class stringClass = typeof(string).GetClass();
            IL2ListExplicit entriesList = new IL2ListExplicit(IntPtr.Zero, stringClass, true);
            foreach (string str in entries)
            {
                entriesList.Add(new IL2String(str).ptr);
            }
            IntPtr entriesReadOnlyList = entriesList.MethodAsReadOnly();

            int destructiveLength = 0;
            methodOpenEmbed.Invoke(new IntPtr[]
            {
                new IL2String(title).ptr,
                embedObject,
                entriesReadOnlyList,
                new IntPtr(&destructiveLength),
                UnityEngine.Events._UnityAction.CreateAction<int>(callback),
                IntPtr.Zero // onCancel
            });
        }
    }
}
