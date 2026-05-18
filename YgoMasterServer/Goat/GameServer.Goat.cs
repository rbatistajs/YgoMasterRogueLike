using System;
using System.Collections.Generic;

namespace YgoMaster
{
    // Goat: boot hook called once from GameServer.State.LoadSettings()
    // immediately after LoadSoloDuels(). Wires up the runtime resolver and
    // processes Goat-specific CLI args (kept off the upstream switch so we
    // can add diagnostic commands without touching GameServer.State.cs).
    partial class GameServer
    {
        void InitGoat()
        {
            RuntimeRandomResolver.Init(dataDirectory, Regulation);
            RuntimeGateGenerator.Init(dataDirectory);
            ResponseDebugDumper.DataDirectory = dataDirectory;
            ResponseDebugDumper.Enabled = true;   // flip off when done debugging
            HandleGoatArgs();
        }

        // Goat: bridge for Act_SoloInfo. Wraps SoloData with per-player
        // runtime-generated chapters (clone-on-write; vanilla flow is
        // pass-through). Returns the dict the response should embed.
        internal Dictionary<string, object> WrapSoloDataForRuntimeGates(
            Dictionary<string, object> soloData, Player player)
        {
            return RuntimeGateGenerator.WrapSoloData(
                soloData, player, GetPlayerDirectory(player));
        }

        // Goat-only CLI args, dispatched after the resolver is initialized
        // (so DLL_CardGet* calls inside diagnostic helpers are safe).
        void HandleGoatArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--card-info":
                        if (i + 1 < args.Length)
                        {
                            int cid;
                            if (int.TryParse(args[++i], out cid))
                            {
                                RuntimeRandomResolver.DumpCardInfo(cid);
                            }
                            else
                            {
                                Console.WriteLine("[--card-info] expected integer cid, got '" + args[i] + "'");
                            }
                        }
                        else
                        {
                            Console.WriteLine("[--card-info] missing cid argument");
                        }
                        break;

                    case "--resolve-chapter":
                        // Usage: --resolve-chapter <chapterId>
                        if (i + 1 < args.Length)
                        {
                            int chapterId;
                            if (int.TryParse(args[++i], out chapterId))
                            {
                                DuelSettings ds;
                                if (SoloDuels.TryGetValue(chapterId, out ds))
                                {
                                    RuntimeRandomResolver.ResolveChapter(ds);
                                }
                                else
                                {
                                    Console.WriteLine("[--resolve-chapter] chapter " + chapterId + " not found in SoloDuels");
                                }
                            }
                            else
                            {
                                Console.WriteLine("[--resolve-chapter] expected integer chapterId");
                            }
                        }
                        break;

                    case "--match-test":
                        // Usage: --match-test <cid> <random> [subtype] [minAtk=N] [maxAtk=N] ...
                        if (i + 2 < args.Length)
                        {
                            int cid;
                            if (!int.TryParse(args[i + 1], out cid))
                            {
                                Console.WriteLine("[--match-test] expected integer cid, got '" + args[i + 1] + "'");
                                i += 2;
                                break;
                            }
                            string randomKind = args[i + 2];
                            string subtype = null;
                            var numericFilters = new Dictionary<string, int>();
                            int consumed = 2;
                            for (int j = i + 3; j < args.Length && !args[j].StartsWith("--"); j++)
                            {
                                string a = args[j];
                                int eq = a.IndexOf('=');
                                if (eq > 0 && int.TryParse(a.Substring(eq + 1), out int v))
                                {
                                    numericFilters[a.Substring(0, eq)] = v;
                                }
                                else if (subtype == null)
                                {
                                    subtype = a;
                                }
                                consumed++;
                            }
                            RuntimeRandomResolver.TestMatch(cid, randomKind, subtype, numericFilters);
                            i += consumed;
                        }
                        else
                        {
                            Console.WriteLine("[--match-test] usage: --match-test <cid> <random> [subtype] [minAtk=N] [maxAtk=N]");
                        }
                        break;
                }
            }
        }
    }
}
