using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YgoMaster
{
    // Goat: dumps the request.Response dict of selected endpoints to disk
    // right before it's serialized to the client. Useful for debugging
    // runtime-gate flows (Solo.info / Solo.gate_entry / Solo.detail /
    // Solo.start / Duel.begin) — you can diff what the server sends per
    // endpoint and confirm injected runtime chapters look right.
    //
    // Disabled by default (set Enabled=true to start dumping). Dumps to
    // `<dataDirectory>/_goat_debug/<actName>_<seq>.json`.
    static class ResponseDebugDumper
    {
        public static bool Enabled = false;
        public static string DataDirectory;

        // Endpoints we care about. `null` = capture ALL acts (helpful when
        // an unknown endpoint is misbehaving and we don't know which one
        // to scope to).
        static HashSet<string> WatchedActs = null;

        static int seq;
        static readonly object writeLock = new object();

        public static void MaybeDump(string actName, Dictionary<string, object> response)
        {
            if (!Enabled || string.IsNullOrEmpty(actName)) return;
            if (WatchedActs != null && !WatchedActs.Contains(actName)) return;
            if (string.IsNullOrEmpty(DataDirectory)) return;

            try
            {
                lock (writeLock)
                {
                    int n = ++seq;
                    string dir = Path.Combine(DataDirectory, "_goat_debug");
                    Directory.CreateDirectory(dir);
                    string fileName = string.Format("{0:D4}_{1}.json",
                        n, actName.Replace('.', '_'));
                    string path = Path.Combine(dir, fileName);
                    File.WriteAllText(path, PrettyPrint(response));
                    Console.WriteLine("[ResponseDebugDumper] " + actName
                        + " → " + Path.GetFileName(path));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ResponseDebugDumper] dump failed for "
                    + actName + ": " + ex.Message);
            }
        }

        // MiniJSON.Serialize is single-line. This adds line breaks + indent
        // so the dumps are diff-friendly.
        static string PrettyPrint(Dictionary<string, object> dict)
        {
            string raw = MiniJSON.Json.Serialize(dict);
            StringBuilder sb = new StringBuilder(raw.Length * 2);
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"' && (i == 0 || raw[i - 1] != '\\')) inString = !inString;

                if (!inString)
                {
                    if (c == '{' || c == '[')
                    {
                        sb.Append(c);
                        sb.Append('\n');
                        indent++;
                        sb.Append(' ', indent * 2);
                        continue;
                    }
                    if (c == '}' || c == ']')
                    {
                        sb.Append('\n');
                        indent--;
                        sb.Append(' ', indent * 2);
                        sb.Append(c);
                        continue;
                    }
                    if (c == ',')
                    {
                        sb.Append(c);
                        sb.Append('\n');
                        sb.Append(' ', indent * 2);
                        continue;
                    }
                    if (c == ':')
                    {
                        sb.Append(c);
                        sb.Append(' ');
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
