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
        // Radio overload: Open(int idx, string title, IReadOnlyList<string> entrys,
        //                       Action<int> callback, Action onCancel,
        //                       Dictionary<string,object> args)
        static IL2Method methodOpenRadio;

        static ActionSheetViewController()
        {
            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class classInfo = assembly.GetClass("ActionSheetViewController", "YgomGame.Menu");
            methodOpen = classInfo.GetMethod("Open", x => x.GetParameters().Length == 3);
            // Filter: 6 params, first Int32, second String, third IReadOnlyList<String>.
            methodOpenRadio = classInfo.GetMethod("Open", m =>
            {
                var p = m.GetParameters();
                if (p.Length != 6) return false;
                if (p[0].Type.Name != "System.Int32") return false;
                if (p[1].Type.Name != "System.String") return false;
                string t2 = p[2].Type.Name;
                // The IReadOnlyList<String> variant (not <ValueTuple<...>>) is the simple radio overload.
                return t2.Contains("IReadOnlyList") && t2.Contains("String") && !t2.Contains("ValueTuple");
            });
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

        // Radio variant: renders options as radio buttons; `selectedIndex` gets the
        // green checkmark. Same UI used by "Life Points" and other native PvP dialogs.
        // Returns true if the overload exists and was invoked; false otherwise.
        public static bool TryOpenRadio(string title, string[] entries, int selectedIndex, Action<IntPtr, int> callback)
        {
            if (methodOpenRadio == null) return false;
            IL2Class stringClass = typeof(string).GetClass();
            IL2ListExplicit entriesList = new IL2ListExplicit(IntPtr.Zero, stringClass, true);
            foreach (string str in entries)
            {
                entriesList.Add(new IL2String(str).ptr);
            }
            IntPtr entriesReadOnlyList = entriesList.MethodAsReadOnly();
            int idx = selectedIndex;
            methodOpenRadio.Invoke(new IntPtr[]
            {
                new IntPtr(&idx),                                                // int idx
                new IL2String(title).ptr,                                        // string title
                entriesReadOnlyList,                                             // IReadOnlyList<string>
                UnityEngine.Events._UnityAction.CreateAction<int>(callback),     // Action<int>
                IntPtr.Zero,                                                     // Action onCancel
                IntPtr.Zero                                                      // Dictionary<string,object>
            });
            return true;
        }
    }
}
