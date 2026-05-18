using System;
using System.IO;
using System.Runtime.InteropServices;

namespace YgoMaster
{
    // Goat: pinvoke wrappers around `duel.dll`'s card-property queries.
    // Used by RuntimeRandomResolver to filter pools by atk/def/kind/icon/
    // level without parsing CARD_Prop.bytes by hand.
    //
    // Mirrors `Sim/DuelSimulator.DllContent.cs` (which the simulator uses
    // for game-state queries) but isolated here so solo-duel flows can
    // use card props without dragging in the simulator.
    //
    // The native DLL needs SIX init calls before any DLL_CardGet* is
    // safe — DLL_SetInternalID is critical (without it CardGet*
    // dereferences a null table → AV). `LoadAllCardData` handles all of
    // them and copies the bytes into unmanaged memory (via AllocHGlobal,
    // matching the pattern in Pvp.cs) so the DLL's pointers stay valid
    // for the lifetime of the process. Passing a managed `byte[]` here
    // works briefly but the GC may move/collect it after the call
    // returns, leaving the DLL with a dangling pointer — which silently
    // corrupts every subsequent CardGet* query.
    static class DuelDllProps
    {
        // Same relative path used by Pvp.cs / DuelSimulator.DllEngine.cs.
        const string DLL = "../masterduel_Data/Plugins/x86_64/duel.dll";

        // ----- card-property queries -----
        [DllImport(DLL)] public static extern int DLL_CardGetAtk(int cardId);
        [DllImport(DLL)] public static extern int DLL_CardGetDef(int cardId);
        [DllImport(DLL)] public static extern int DLL_CardGetLevel(int cardId);
        [DllImport(DLL)] public static extern int DLL_CardGetAttr(int cardId);
        [DllImport(DLL)] public static extern int DLL_CardGetKind(int cardId);
        [DllImport(DLL)] public static extern int DLL_CardGetIcon(int cardId);

        // ----- setters (must be called once at boot before any CardGet*) -----
        // IntPtr versions: the DLL keeps the pointer, so we hand it
        // unmanaged memory that we never free.
        [DllImport(DLL)] static extern void DLL_SetInternalID(IntPtr data);
        [DllImport(DLL)] static extern int  DLL_SetCardProperty(IntPtr data, int size);
        [DllImport(DLL)] static extern void DLL_SetCardSame(IntPtr data, int size);
        [DllImport(DLL)] static extern void DLL_SetCardGenre(IntPtr data);
        [DllImport(DLL)] static extern void DLL_SetCardNamed(IntPtr data);
        [DllImport(DLL)] static extern void DLL_SetCardLink(IntPtr data, int size);

        // Calls every setter the DLL needs before card queries are safe.
        // Returns total bytes loaded (0 on failure).
        public static int LoadAllCardData(string cardDataDir)
        {
            try
            {
                int total = 0;
                total += LoadAndSet(cardDataDir, "#",  "CARD_IntID.bytes", (p, n) => DLL_SetInternalID(p));
                total += LoadAndSet(cardDataDir, "#",  "CARD_Prop.bytes",  (p, n) => DLL_SetCardProperty(p, n));
                total += LoadAndSet(cardDataDir, "MD", "CARD_Same.bytes",  (p, n) => DLL_SetCardSame(p, n));
                total += LoadAndSet(cardDataDir, "#",  "CARD_Genre.bytes", (p, n) => DLL_SetCardGenre(p));
                total += LoadAndSet(cardDataDir, "#",  "CARD_Named.bytes", (p, n) => DLL_SetCardNamed(p));
                total += LoadAndSet(cardDataDir, "MD", "CARD_Link.bytes",  (p, n) => DLL_SetCardLink(p, n));
                return total;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DuelDllProps] LoadAllCardData failed: " + ex);
                return 0;
            }
        }

        // Copies the file bytes into unmanaged memory and hands the DLL
        // the pointer. The buffer is intentionally never freed — the DLL
        // keeps it alive for the rest of the process.
        static int LoadAndSet(string cardDataDir, string subDir, string fileName,
                               Action<IntPtr, int> setter)
        {
            string path = Path.Combine(cardDataDir, subDir, fileName);
            if (!File.Exists(path))
            {
                Console.WriteLine("[DuelDllProps] missing required file: " + path);
                return 0;
            }
            byte[] data = File.ReadAllBytes(path);
            IntPtr ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            setter(ptr, data.Length);
            return data.Length;
        }
    }
}
