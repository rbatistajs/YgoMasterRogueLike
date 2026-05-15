using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Globalization;
using System.IO;
using System.Diagnostics;

namespace YgoMasterClient
{
    class Program
    {
        public static int DllMain(string arg)
        {
            try
            {
                YgoMaster.Program.IsMonoRun = true;
                Console.WriteLine("(MonoRun)");
                return YgoMaster.Program.Main(new string[0]);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return 0;
        }
    }
}

namespace YgoMaster
{
    class Program
    {
        public static bool IsMonoRun;

        internal static int Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            if (IsMonoRun)
            {
                args = ProcessCommandLine.GetCommandLineArgs().Skip(2).ToArray();
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].ToLowerInvariant() == "--pvp" && i < args.Length - 1)
                {
                    Pvp pvp = new Pvp();
                    pvp.Run(Encoding.UTF8.GetString(Convert.FromBase64String(args[i + 1])));
                    return 0;
                }
                if (args[i].ToLowerInvariant() == "--cpucontest-sim" && i < args.Length - 5)
                {
                    try
                    {
                        string deckFile1 = args[i + 1];
                        string deckFile2 = args[i + 2];
                        uint seed;
                        bool goFirst;
                        int iterationsBeforeIdle;
                        if (!uint.TryParse(args[i + 3], out seed) || !bool.TryParse(args[i + 4], out goFirst) ||
                            !int.TryParse(args[i + 5], out iterationsBeforeIdle) || !File.Exists(deckFile1) || !File.Exists(deckFile2))
                        {
                            return -1;
                        }
                        Process parentProcess = null;
                        int pid;
                        if (i < args.Length - 6 && int.TryParse(args[i + 6], out pid))
                        {
                            parentProcess = Process.GetProcessById(pid);
                        }
                        // 8th positional arg (i+7): duelType -- "Normal" | "Rush", default "Normal"
                        string duelType = "Normal";
                        if (i < args.Length - 7)
                        {
                            string dt = args[i + 7];
                            if (string.Equals(dt, "Rush", StringComparison.OrdinalIgnoreCase))
                                duelType = "Rush";
                            else if (string.Equals(dt, "Normal", StringComparison.OrdinalIgnoreCase))
                                duelType = "Normal";
                        }
                        // 9th positional arg (i+8): saveReplayDir (optional, empty string = off)
                        string saveReplayDir = null;
                        if (i < args.Length - 8)
                        {
                            string srd = args[i + 8];
                            if (!string.IsNullOrEmpty(srd))
                                saveReplayDir = srd;
                        }
                        string dataDir = Utils.GetDataDirectory(true);
                        YdkHelper.LoadIdMap(dataDir);
                        DuelSimulator sim = new DuelSimulator(dataDir);
                        if (!sim.InitContent())
                        {
                            return -1;
                        }
                        return sim.RunCpuVsCpu(deckFile1, deckFile2, seed, goFirst, iterationsBeforeIdle, parentProcess, duelType, saveReplayDir);
                    }
                    catch
                    {
                        // NOTE: This doesn't catch duel.dll access violations... TODO: Add some native error handler
                        return -2;
                    }
                }
            }

            GameServer server = new GameServer();
            server.Start();
            Process.GetCurrentProcess().WaitForExit();
            return 0;
        }
    }
}
