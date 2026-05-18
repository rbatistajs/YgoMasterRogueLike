using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YgoMaster.Builder
{
    // Goat: hash estável de uma entry de GridGates.json. Usado pra
    // detectar drift ("essa entry mudou desde o último bake?") sem ter
    // que diff-ar a entry inteira.
    //
    // Estabilidade: serializa o dict em formato canônico (chaves
    // ordenadas, números com cultura invariante) e gera SHA1 em hex.
    // Mesmo input → mesmo hash (independente da ordem que MiniJSON
    // serializa as chaves).
    //
    // Antes de hashear, remove chaves voláteis que mudam mesmo sem
    // edição do user — ex: `last_bake_hash` (circular!) e
    // `last_summary` (timestamp). Runtime-only fields também ficam de
    // fora — bake não depende deles.
    static class EntryHasher
    {
        static readonly HashSet<string> VolatileKeys = new HashSet<string>
        {
            "last_bake_hash",
            "last_summary",
            // Legacy fields from when Python pre-computed templates +
            // layout pool. Server now generates everything on-demand;
            // mantidos no ignore-list só pra entries velhas não dispararem
            // rebake só porque esses campos estão lá.
            "runtime_templates",
            "runtime_chapters",
            "runtime_chapter_meta",
            "runtime_boss_chapter_id",
            "runtime_locks",
            "runtime_layout_pool",
        };

        public static string Compute(Dictionary<string, object> entry)
        {
            StringBuilder sb = new StringBuilder();
            WriteCanonical(sb, entry, topLevel: true);
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        static void WriteCanonical(StringBuilder sb, object value, bool topLevel)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (IsNumber(value))
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is Dictionary<string, object> dict)
            {
                sb.Append('{');
                List<string> keys = new List<string>(dict.Keys);
                keys.Sort(StringComparer.Ordinal);
                bool first = true;
                foreach (string k in keys)
                {
                    if (topLevel && VolatileKeys.Contains(k)) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, k);
                    sb.Append(':');
                    WriteCanonical(sb, dict[k], topLevel: false);
                }
                sb.Append('}');
                return;
            }
            if (value is List<object> list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteCanonical(sb, list[i], topLevel: false);
                }
                sb.Append(']');
                return;
            }
            // Fallback: usa ToString invariante.
            sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        static bool IsNumber(object v)
        {
            return v is int || v is long || v is short || v is byte
                || v is float || v is double || v is decimal;
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
