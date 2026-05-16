using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Pluggable opponent-selection strategies used by CpuContest. The strategy
    // owns the pairing logic and (optionally) the duel total -- CpuContest is
    // just a runner. Each subclass declares its own config keys prefixed with
    // the strategy name and is selected by the "matchmaking" key in
    // ContestSettings.json.
    abstract class MatchmakingStrategy
    {
        public abstract string Name { get; }

        // Read strategy-specific config keys from ContestSettings.json. No-op
        // for strategies that have no tunables.
        public virtual void LoadConfig(Dictionary<string, object> settings) { }

        // One-line summary appended to the startup log. Override to expose the
        // loaded config values.
        public virtual string ConfigSummary() { return string.Empty; }

        // Total duels for the contest. Default uses the upstream formula
        // (matches Random/Elo behavior). Override (e.g. RoundRobin) when the
        // strategy decides the schedule itself.
        public virtual int ComputeTotalDuels(int deckCount, int duelsPerDeckSetting)
        {
            return ((deckCount + (deckCount % 2)) / 2) * duelsPerDeckSetting;
        }

        // Called once after decks are loaded, before any pairing call.
        public virtual void Initialize(DeckStatsRegistry allDecks, Random rand) { }

        // Resume-from-disk hook. Called before Initialize so subclasses can
        // hydrate persistent state (e.g. RoundRobin's pair history). Default no-op.
        public virtual void LoadState(string stateDir) { }

        // Called by CpuContest after each duel finishes. Default no-op.
        // RoundRobin uses it to tick per-pair counters and write them back to disk
        // so a restarted contest doesn't replay pairs that already met the quota.
        public virtual void OnDuelComplete(DeckInfo primary, DeckInfo opponent) { }

        // Returns true with a (primary, opponent) pair, or false when the
        // contest schedule is exhausted (RoundRobin uses this; Random/Elo
        // never return false because their pool is refilled on demand).
        public abstract bool TryPickNextPair(
            DeckStatsRegistry allDecks,
            Random rand,
            out DeckInfo primary,
            out DeckInfo opponent,
            out string logLine);

        public static MatchmakingStrategy Resolve(string name)
        {
            if (string.Equals(name, EloMatchmaking.StrategyName, StringComparison.OrdinalIgnoreCase))
                return new EloMatchmaking();
            if (string.Equals(name, RoundRobinMatchmaking.StrategyName, StringComparison.OrdinalIgnoreCase))
                return new RoundRobinMatchmaking();
            return new RandomMatchmaking();
        }
    }

    // Read-only views handed to strategies so they can stay decoupled from
    // CpuContest's private DeckStats class. Implemented by CpuContest.
    interface DeckStatsView
    {
        DeckInfo Deck { get; }
        double Rating { get; }
        int NumDuels { get; }
    }

    interface DeckStatsRegistry
    {
        int Count { get; }
        IEnumerable<DeckStatsView> All { get; }
        IEnumerable<DeckInfo> AllDecks { get; }
        DeckStatsView Get(DeckInfo deck);
    }

    // Shared base for the random-pool strategies (Random, Elo). Owns a
    // shuffled `pool` that is refilled lazily; subclasses pick primary/opponent
    // out of it.
    abstract class PoolMatchmaking : MatchmakingStrategy
    {
        protected readonly List<DeckInfo> pool = new List<DeckInfo>();

        protected void RefillIfEmpty(DeckStatsRegistry allDecks, Random rand, DeckInfo excludeFromRefill)
        {
            if (pool.Count > 0) return;
            foreach (DeckInfo d in allDecks.AllDecks)
            {
                if (d != excludeFromRefill) pool.Add(d);
            }
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                DeckInfo tmp = pool[j];
                pool[j] = pool[i];
                pool[i] = tmp;
            }
        }

        protected DeckInfo PopLast()
        {
            int last = pool.Count - 1;
            DeckInfo d = pool[last];
            pool.RemoveAt(last);
            return d;
        }
    }

    // Upstream's original behavior: shuffle round-robin pool, take last
    // entries. No config, no warmup, no rating awareness.
    sealed class RandomMatchmaking : PoolMatchmaking
    {
        public const string StrategyName = "Random";
        public override string Name { get { return StrategyName; } }

        public override bool TryPickNextPair(
            DeckStatsRegistry allDecks, Random rand,
            out DeckInfo primary, out DeckInfo opponent, out string logLine)
        {
            logLine = null;
            RefillIfEmpty(allDecks, rand, null);
            primary = PopLast();
            RefillIfEmpty(allDecks, rand, primary);
            opponent = PopLast();
            return true;
        }
    }

    // Picks the opponent with the closest rating. Falls back to random pairing
    // while any deck has fewer than EloWarmupDuelsPerDeck total duels, so
    // initial ratings settle before Elo-aware matchmaking kicks in. Once warm,
    // samples among the EloTopK closest candidates so pairings stay tight
    // without becoming strictly deterministic.
    sealed class EloMatchmaking : PoolMatchmaking
    {
        public const string StrategyName = "Elo";
        public override string Name { get { return StrategyName; } }

        public int WarmupDuelsPerDeck { get; private set; }
        public int TopK { get; private set; }

        bool loggedWarmup;
        bool loggedElo;

        public EloMatchmaking()
        {
            WarmupDuelsPerDeck = 10;
            TopK = 3;
        }

        public override void LoadConfig(Dictionary<string, object> settings)
        {
            WarmupDuelsPerDeck = Utils.GetValue(settings, "EloWarmupDuelsPerDeck", WarmupDuelsPerDeck);
            TopK = Math.Max(1, Utils.GetValue(settings, "EloTopK", TopK));
        }

        public override string ConfigSummary()
        {
            return "warmup=" + WarmupDuelsPerDeck + " topK=" + TopK;
        }

        public override bool TryPickNextPair(
            DeckStatsRegistry allDecks, Random rand,
            out DeckInfo primary, out DeckInfo opponent, out string logLine)
        {
            logLine = null;
            RefillIfEmpty(allDecks, rand, null);
            primary = PopLast();
            DeckStatsView primaryStats = allDecks.Get(primary);

            bool warmupComplete = true;
            int minDuels = int.MaxValue;
            foreach (DeckStatsView s in allDecks.All)
            {
                int n = s.NumDuels;
                if (n < minDuels) minDuels = n;
                if (n < WarmupDuelsPerDeck) warmupComplete = false;
            }

            RefillIfEmpty(allDecks, rand, primary);

            if (!warmupComplete)
            {
                opponent = PopLast();
                if (!loggedWarmup)
                {
                    loggedWarmup = true;
                    logLine = "[CpuContest] Matchmaking: Random (warmup, min " +
                        minDuels + "/" + WarmupDuelsPerDeck + " duels/deck)";
                }
                return true;
            }

            double myRating = primaryStats.Rating;
            List<DeckInfo> candidates = new List<DeckInfo>(pool);
            candidates.Sort((a, b) =>
            {
                double da = Math.Abs(allDecks.Get(a).Rating - myRating);
                double db = Math.Abs(allDecks.Get(b).Rating - myRating);
                return da.CompareTo(db);
            });
            int topK = Math.Min(TopK, candidates.Count);
            opponent = candidates[rand.Next(topK)];
            pool.Remove(opponent);

            if (!loggedElo)
            {
                loggedElo = true;
                double delta = Math.Abs(allDecks.Get(opponent).Rating - myRating);
                logLine = "[CpuContest] Matchmaking: Elo (delta=" + (int)delta +
                    ", " + primaryStats.Deck.Name + " r" + (int)myRating +
                    " vs " + opponent.Name + " r" + (int)allDecks.Get(opponent).Rating + ")";
            }
            return true;
        }
    }

    // Every deck duels every other deck exactly RoundRobinDuelsPerPair times.
    // Ignores ContestSettings.duelsPerDeck -- total is C(n,2) * duelsPerPair.
    // Pairs are shuffled once at Initialize so engines don't all play the same
    // pair in the same order. Resumes correctly across restarts by persisting
    // a per-pair counter to pair_history.json.
    sealed class RoundRobinMatchmaking : MatchmakingStrategy
    {
        public const string StrategyName = "RoundRobin";
        public const string PairHistoryFileName = "pair_history.json";
        public override string Name { get { return StrategyName; } }

        public int DuelsPerPair { get; private set; }

        readonly Queue<Pairing> queue = new Queue<Pairing>();
        // Number of duels already completed per canonical pair key.
        // Persisted to <stateDir>/pair_history.json across runs.
        readonly Dictionary<string, int> pairHistory = new Dictionary<string, int>();
        string pairHistoryPath;
        bool logged;

        struct Pairing { public DeckInfo A; public DeckInfo B; }

        public RoundRobinMatchmaking()
        {
            DuelsPerPair = 1;
        }

        public override void LoadConfig(Dictionary<string, object> settings)
        {
            DuelsPerPair = Math.Max(1, Utils.GetValue(settings, "RoundRobinDuelsPerPair", DuelsPerPair));
        }

        public override string ConfigSummary()
        {
            return "duelsPerPair=" + DuelsPerPair;
        }

        public override int ComputeTotalDuels(int deckCount, int duelsPerDeckSetting)
        {
            return deckCount * (deckCount - 1) / 2 * DuelsPerPair;
        }

        // Canonical pair key: sorted by name so (A,B) and (B,A) collide.
        static string PairKey(string a, string b)
        {
            return string.Compare(a, b, StringComparison.Ordinal) <= 0
                ? a + "||" + b
                : b + "||" + a;
        }

        public override void LoadState(string stateDir)
        {
            pairHistoryPath = Path.Combine(stateDir, PairHistoryFileName);
            pairHistory.Clear();
            if (!File.Exists(pairHistoryPath)) return;
            try
            {
                Dictionary<string, object> data = MiniJSON.Json.Deserialize(Utils.SafeReadAllText(pairHistoryPath)) as Dictionary<string, object>;
                if (data == null) return;
                foreach (KeyValuePair<string, object> entry in data)
                {
                    int n;
                    if (entry.Value is long) n = (int)(long)entry.Value;
                    else if (entry.Value is int) n = (int)entry.Value;
                    else if (!int.TryParse(entry.Value == null ? "0" : entry.Value.ToString(), out n)) n = 0;
                    if (n > 0) pairHistory[entry.Key] = n;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RoundRobin] LoadState failed: " + ex.Message);
            }
        }

        public override void Initialize(DeckStatsRegistry allDecks, Random rand)
        {
            queue.Clear();
            List<DeckInfo> decks = new List<DeckInfo>(allDecks.AllDecks);
            List<Pairing> pairs = new List<Pairing>();
            int skipped = 0;
            for (int i = 0; i < decks.Count; i++)
            {
                for (int j = i + 1; j < decks.Count; j++)
                {
                    int alreadyDone;
                    pairHistory.TryGetValue(PairKey(decks[i].Name, decks[j].Name), out alreadyDone);
                    int remaining = DuelsPerPair - alreadyDone;
                    if (remaining <= 0) { skipped++; continue; }
                    for (int k = 0; k < remaining; k++)
                    {
                        pairs.Add(new Pairing { A = decks[i], B = decks[j] });
                    }
                }
            }
            for (int i = pairs.Count - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                Pairing tmp = pairs[j];
                pairs[j] = pairs[i];
                pairs[i] = tmp;
            }
            foreach (Pairing p in pairs) queue.Enqueue(p);
            if (skipped > 0)
            {
                Console.WriteLine("[RoundRobin] resumed -- " + skipped + " pairs already complete, "
                    + queue.Count + " duels left in queue");
            }
        }

        public override void OnDuelComplete(DeckInfo primary, DeckInfo opponent)
        {
            if (primary == null || opponent == null) return;
            string key = PairKey(primary.Name, opponent.Name);
            lock (pairHistory)
            {
                int n;
                pairHistory.TryGetValue(key, out n);
                pairHistory[key] = n + 1;
                if (!string.IsNullOrEmpty(pairHistoryPath))
                {
                    try
                    {
                        Dictionary<string, object> data = new Dictionary<string, object>();
                        foreach (KeyValuePair<string, int> kv in pairHistory)
                        {
                            data[kv.Key] = kv.Value;
                        }
                        Utils.SafeWriteAllText(pairHistoryPath, MiniJSON.Json.Format(MiniJSON.Json.Serialize(data)));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[RoundRobin] save pair_history failed: " + ex.Message);
                    }
                }
            }
        }

        public override bool TryPickNextPair(
            DeckStatsRegistry allDecks, Random rand,
            out DeckInfo primary, out DeckInfo opponent, out string logLine)
        {
            logLine = null;
            lock (queue)
            {
                if (queue.Count == 0)
                {
                    primary = null;
                    opponent = null;
                    return false;
                }
                Pairing p = queue.Dequeue();
                primary = p.A;
                opponent = p.B;
            }
            if (!logged)
            {
                logged = true;
                logLine = "[CpuContest] Matchmaking: RoundRobin (duelsPerPair=" + DuelsPerPair + ")";
            }
            return true;
        }
    }
}
