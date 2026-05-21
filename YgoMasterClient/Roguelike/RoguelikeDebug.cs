using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace YgoMasterClient
{
    // Dev-only debug helper: writes dumps/logs to a _tmp folder for inspection while
    // building the Roguelike UI (GameObject hierarchies, state, etc.). Not for production.
    static class RoguelikeDebug
    {
        public static string Dir = @"D:\www\ygomaster-fork\YgoMaster\_tmp";

        public static void Write(string name, string content)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, name), content);
                Console.WriteLine("[RoguelikeDebug] wrote _tmp/" + name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RoguelikeDebug] write EX: " + ex);
            }
        }

        // Dump a GameObject's hierarchy + components to _tmp/<name>.
        public static void DumpGO(string name, IntPtr go)
        {
            if (go == IntPtr.Zero)
            {
                Console.WriteLine("[RoguelikeDebug] " + name + ": null GameObject");
                return;
            }
            Write(name, GameObject.Dump(go));
        }

        // Compact skeleton: indented GameObject names + their component type names, no
        // member values, capped at maxDepth. Small + readable (GameObject.Dump bloats to
        // MBs on big screens). Used to scout reusable UI prefabs.
        public static string DumpTree(IntPtr go, int maxDepth)
        {
            StringBuilder sb = new StringBuilder();
            if (go != IntPtr.Zero) DumpTreeInto(sb, GameObject.GetTransform(go), 0, maxDepth);
            return sb.ToString();
        }

        static void DumpTreeInto(StringBuilder sb, IntPtr transform, int depth, int maxDepth)
        {
            IntPtr obj = Component.GetGameObject(transform);
            sb.Append(' ', depth * 2).Append(UnityObject.GetName(obj));
            IntPtr[] comps = GameObject.GetComponents(obj);
            if (comps != null && comps.Length > 0)
            {
                sb.Append("  [");
                for (int i = 0; i < comps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    IntPtr cls = IL2CPP.Import.Object.il2cpp_object_get_class(comps[i]);
                    sb.Append(System.Runtime.InteropServices.Marshal.PtrToStringAnsi(
                        IL2CPP.Import.Class.il2cpp_class_get_name(cls)));
                }
                sb.Append(']');
            }
            sb.Append('\n');
            if (depth >= maxDepth) return;
            int n = Transform.GetChildCount(transform);
            for (int i = 0; i < n; i++)
                DumpTreeInto(sb, Transform.GetChild(transform, i), depth + 1, maxDepth);
        }

        // Append a timestamped line to _tmp/roguelike.log.
        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "roguelike.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\n");
            }
            catch { }
        }
    }
}
