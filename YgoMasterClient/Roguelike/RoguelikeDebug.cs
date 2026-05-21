using System;
using System.IO;
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
