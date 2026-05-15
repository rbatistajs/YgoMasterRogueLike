using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace YgoMaster
{
    partial class DuelSimulator
    {
        public string DataDir { get; private set; }

        public DuelSimulator(string dataDir)
        {
            DataDir = dataDir;
        }

        RunEffect DoRunEffect = (int id, int param1, int param2, int param3) =>
        {
            DuelViewType viewType = (DuelViewType)id;
            switch (viewType)
            {
                case DuelViewType.DuelEnd:
                    //Console.WriteLine("DoRun " + viewType + " resultType:" + (DuelResultType)param1 + " finishType:" + (DuelFinishType)param2 + " p3:" + param3);
                    break;
                default:
                    // This crashes when the duel ends
                    //Console.WriteLine("DoRun " + viewType + " p1:" + param1 + " p2:" + param2 + " p3:" + param3 + " | " +
                        //DLL_DuelGetLP(0) + " " + DLL_DuelGetCardNum(0, 15) + " " + DLL_DuelGetLP(1) + " " + DLL_DuelGetCardNum(1, 15));
                    break;
            }
            return 0;
        };

        IsBusyEffect DoIsBusyEffect = (int id) =>
        {
            DuelViewType viewType = (DuelViewType)id;
            //Console.WriteLine("DoIsBusyEffect " + viewType);
            return 0;
        };

        void SetDeck(int player, DeckInfo deck)
        {
            int[] main = deck.MainDeckCards.GetIds().ToArray();
            int[] extra = deck.ExtraDeckCards.GetIds().ToArray();
            int[] side = deck.SideDeckCards.GetIds().ToArray();
            DLL_DuelSysSetDeck2(player, main, main.Length, extra, extra.Length, side, side.Length);
        }

        uint GetCpuParam(int val, DuelCpuParam param = DuelCpuParam.None)
        {
            val = Math.Min(100, Math.Max(-100, val));
            if (val < 0)
            {
                param |= DuelCpuParam.Def;
                val = -val;
            }
            return (uint)(val | (int)param);
        }

        public int RunCpuVsCpu(string deckFile1, string deckFile2, uint seed, bool goFirst, int iterationsBeforeIdle, Process parentProcess = null, string duelType = "Normal", string saveReplayDir = null)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            long duelBeginEpoch = Utils.GetEpochTime();
            Console.WriteLine("[Sim] DuelType=" + duelType +
                " deck1=" + Path.GetFileName(deckFile1) +
                " deck2=" + Path.GetFileName(deckFile2) +
                " seed=" + seed + " goFirst=" + goFirst);
            DeckInfo deck1 = new DeckInfo();
            deck1.File = deckFile1;
            deck1.Load();

            DeckInfo deck2 = new DeckInfo();
            deck2.File = deckFile2;
            deck2.Load();

            // Replay capture: the AddRecord callback accumulates bytes into a MemoryStream.
            // duel.dll invokes the callback on every duel action (same mechanism as --pvp).
            MemoryStream replayRec = new MemoryStream();
            AddRecord replayCb = (ptr, size) =>
            {
                byte[] tmp = new byte[size];
                Marshal.Copy(ptr, tmp, 0, size);
                lock (replayRec)
                {
                    replayRec.Write(tmp, 0, size);
                }
            };

            int num = DLL_SetWorkMemory(IntPtr.Zero);
            IntPtr engineWork = Marshal.AllocHGlobal(num);
            Debug.Assert(DLL_SetWorkMemory(engineWork) == 0);

            DLL_SetEffectDelegate(DoRunEffect, DoIsBusyEffect);
            DLL_DuelSysClearWork();
            DLL_DuelSetMyPlayerNum(0);
            DLL_DuelSetRandomSeed(seed);
            SetDeck(0, deck1);
            SetDeck(1, deck2);
            DLL_DuelSetPlayerType(0, (int)DuelPlayerType.CPU);
            DLL_DuelSetPlayerType(1, (int)DuelPlayerType.CPU);
            DLL_DuelSetCpuParam(0, GetCpuParam(100));
            DLL_DuelSetCpuParam(1, GetCpuParam(100));
            DLL_DuelSetFirstPlayer(goFirst ? 0 : 1);
            DLL_DuelSetDuelLimitedType((uint)DuelLimitedType.None);
            DLL_SetAddRecordDelegate(replayCb);
            if (duelType.Equals("Rush", StringComparison.OrdinalIgnoreCase))
            {
                DLL_DuelSysInitCustom((int)DllDuelType.Rush, false, 8000, 8000, 4, 4, false);
            }
            else
            {
                DLL_DuelSysInitCustom((int)DllDuelType.Normal, false, 8000, 8000, 5, 5, false);
            }
            Stopwatch turnTimer = new Stopwatch();
            turnTimer.Start();
            uint lastTurn = 0;
            iterationsBeforeIdle = Math.Max(1, iterationsBeforeIdle);
            while (true)
            {
                bool complete = false;
                for (int i = 0; i < iterationsBeforeIdle; i++)
                {
                    if (DLL_DuelSysAct() > 0)
                    {
                        complete = true;
                        break;
                    }
                }
                if (complete)
                {
                    break;
                }
                if (parentProcess != null && parentProcess.HasExited)
                {
                    break;
                }
                uint turn = DLL_DuelGetTurnNum();
                if (turn != lastTurn)
                {
                    lastTurn = turn;
                    turnTimer.Restart();
                }
                if (turnTimer.Elapsed > TimeSpan.FromMinutes(3))
                {
                    // No turn change in 3 minutes is probably enough...
                    Console.WriteLine("[Sim] STUCK turn=" + DLL_DuelGetTurnNum() + " -- forcing Draw");
                    return (int)DuelResultType.Draw;
                }
                System.Threading.Thread.Sleep(1);
            }
            sw.Stop();
            //Console.WriteLine("finish: " + (DuelFinishType)DLL_DuelGetDuelFinish() + " turn: " + DLL_DuelGetTurnNum());
            /*for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.AppendAllText("tempfile.txt", "finish: " + (DuelFinishType)DLL_DuelGetDuelFinish() + " turn: " + DLL_DuelGetTurnNum() + 
                        " deck1: '" + deck1.Name + "' deck2: '" + deck2.Name + "' duration:" + sw.Elapsed + "\n");
                    break;
                }
                catch
                {
                }
                System.Threading.Thread.Sleep(10);
            }*/
            int finalResult = DLL_DuelGetDuelResult();
            int finalFinish = DLL_DuelGetDuelFinish();
            byte[] replayBytes;
            lock (replayRec)
            {
                replayBytes = replayRec.ToArray();
            }
            Console.WriteLine("[Sim] DuelEnded result=" + (DuelResultType)finalResult +
                " turns=" + DLL_DuelGetTurnNum() +
                " duration=" + sw.Elapsed.TotalSeconds.ToString("F1") + "s" +
                " replayBytes=" + replayBytes.Length);

            // Save the replay only when there is a winner (not Draw) and a path was requested.
            DuelResultType resultType = (DuelResultType)finalResult;
            if (!string.IsNullOrEmpty(saveReplayDir) && (resultType == DuelResultType.Win || resultType == DuelResultType.Lose) && replayBytes.Length > 0)
            {
                try
                {
                    SaveReplayFile(saveReplayDir, deck1, deck2, seed, goFirst, resultType, finalFinish, duelBeginEpoch, replayBytes, duelType);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Sim] ReplaySave FAILED: " + ex.Message);
                }
            }
            return finalResult;
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "deck";
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        void SaveReplayFile(string saveReplayDir, DeckInfo deck1, DeckInfo deck2, uint seed, bool goFirst,
            DuelResultType resultType, int finalFinish, long duelBeginEpoch, byte[] replayBytes, string duelType)
        {
            Directory.CreateDirectory(saveReplayDir);
            DeckInfo winner = resultType == DuelResultType.Win ? deck1 : deck2;
            DeckInfo loser = resultType == DuelResultType.Win ? deck2 : deck1;
            string winnerName = !string.IsNullOrEmpty(winner.Name) ? winner.Name : Path.GetFileNameWithoutExtension(winner.File);
            string loserName = !string.IsNullOrEmpty(loser.Name) ? loser.Name : Path.GetFileNameWithoutExtension(loser.File);

            // Packaging: base64(MessagePack({b: zlib(replayBytes), f: [finish, finish]}))
            // Same format DuelStarter.GetReplayDataString produces client-side.
            byte[] zlibBytes = Utils.ZLibCompress(replayBytes);
            Dictionary<string, object> packDict = new Dictionary<string, object>();
            packDict["b"] = zlibBytes;
            packDict["f"] = new int[] { finalFinish, finalFinish };
            byte[] msgpackBytes = MessagePack.Pack(packDict);
            string replaym = Convert.ToBase64String(msgpackBytes);

            // Build minimal DuelSettings so the Replay UI can list it.
            DuelSettings ds = new DuelSettings();
            ds.DuelBeginTime = duelBeginEpoch;
            ds.DuelEndTime = Utils.GetEpochTime();
            ds.RandSeed = seed;
            ds.FirstPlayer = goFirst ? 0 : 1;
            ds.res = (int)resultType;
            ds.finish = (int)finalFinish;
            ds.turn = (int)DLL_DuelGetTurnNum();
            ds.replaym = replaym;
            ds.Deck[0].CopyFrom(deck1);
            ds.Deck[1].CopyFrom(deck2);
            ds.name[0] = !string.IsNullOrEmpty(deck1.Name) ? deck1.Name : Path.GetFileNameWithoutExtension(deck1.File);
            ds.name[1] = !string.IsNullOrEmpty(deck2.Name) ? deck2.Name : Path.GetFileNameWithoutExtension(deck2.File);
            ds.chapter = 0;
            ds.GameMode = (int)GameMode.Audience; // mais permissivo p/ UI listar

            string filename = "cpucontest_" + seed + "_" + SanitizeName(winnerName) + "_W_vs_" + SanitizeName(loserName) + ".json";
            string filepath = Path.Combine(saveReplayDir, filename);
            File.WriteAllText(filepath, MiniJSON.Json.Serialize(ds.ToDictionary()));
            FileInfo fi = new FileInfo(filepath);
            Console.WriteLine("[Sim] ReplaySaved " + filename + " (" + (fi.Length / 1024) + "KB)");
        }

        public void Init()
        {
            if (string.IsNullOrEmpty(DataDir) || !Directory.Exists(DataDir))
            {
                return;
            }
            InitContent();

            //Console.WriteLine("Version: " + DLL_GetRevision());

            DeckInfo deck1 = new DeckInfo();
            deck1.File = "241244.ydk";
            deck1.Load();

            DeckInfo deck2 = new DeckInfo();
            deck2.File = "241310.ydk";
            deck2.Load();

            const int myPlayerNum = 0;
            uint seed = 0;
            Random rand = new Random();
            seed = (uint)rand.Next();

            int num = DLL_SetWorkMemory(IntPtr.Zero);
            IntPtr engineWork = Marshal.AllocHGlobal(num);
            Debug.Assert(DLL_SetWorkMemory(engineWork) == 0);

            DLL_SetEffectDelegate(DoRunEffect, DoIsBusyEffect);
            DLL_DuelSysClearWork();
            DLL_DuelSetMyPlayerNum(myPlayerNum);
            DLL_DuelSetRandomSeed(seed);
            SetDeck(0, deck1);
            SetDeck(1, deck2);
            DLL_DuelSetPlayerType(0, (int)DuelPlayerType.CPU);//Human);
            DLL_DuelSetPlayerType(1, (int)DuelPlayerType.CPU);//Human);
            DLL_DuelSetCpuParam(0, GetCpuParam(100));
            DLL_DuelSetCpuParam(1, GetCpuParam(100));
            DLL_DuelSetFirstPlayer(0);
            DLL_DuelSetDuelLimitedType((uint)DuelLimitedType.None);
            DLL_DuelSysInitCustom((int)DllDuelType.Normal, false, 8000, 8000, 5, 5, false);
            //DLL_DuelSysInitRush(); <--- does this just auto push Rush,4000,4000?
            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (true)
            {
                //System.Threading.Thread.Sleep(1000);
                if (DLL_DuelSysAct() > 0)
                {
                    Console.WriteLine("!!!");
                    break;
                }
                System.Threading.Thread.Sleep(1);
            }
            
            Console.WriteLine(sw.Elapsed);
        }

        enum DllDuelType
        {
            Normal = 0,
            Speed = 1,
            Rush = 2
        }
    }
}
