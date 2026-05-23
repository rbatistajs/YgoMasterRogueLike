using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YgoMaster
{
    // Dev helper (server CLI `--create-deck`) that builds weak NPC decks for the roguelike
    // Opponents folder. Card metadata (attribute, race, frame, genre, related cards) comes from
    // the duel.dll via DuelSimulator; rarity comes from CardList.json (CardRare). Filters combine
    // with AND; the rarity budget (default 30 N / 7 R / 2 SR / 1 UR) fills a 40-card main deck with
    // pure-random selection (up to 3 copies each), relaxing to neighbouring rarities if a pool is short.
    class RoguelikeDeckBuilder
    {
        readonly string _dataDir;
        readonly Dictionary<int, int> _cardRare;          // cardId -> CardRarity (1=N,2=R,3=SR,4=UR)
        readonly DuelSimulator _sim;
        readonly Dictionary<int, YdkHelper.GameCardInfo> _names; // cardId -> info (for names)
        readonly Random _rng = new Random();

        // Enum numbering matches the duel.dll / CARD_Prop data (NOT the standard MD "Content" enum),
        // derived empirically via --dump-cards (e.g. Blue-Eyes = LIGHT/Dragon -> attr 1, type 1).
        static readonly Dictionary<string, int> Attributes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "LIGHT", 1 }, { "DARK", 2 }, { "WATER", 3 }, { "FIRE", 4 }, { "EARTH", 5 }, { "WIND", 6 }, { "DIVINE", 7 },
        };

        static readonly Dictionary<string, int> Races = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dragon", 1 }, { "Zombie", 2 }, { "Fiend", 3 }, { "Pyro", 4 }, { "SeaSerpent", 5 },
            { "Rock", 6 }, { "Machine", 7 }, { "Fish", 8 }, { "Dinosaur", 9 }, { "Insect", 10 },
            { "Beast", 11 }, { "BeastWarrior", 12 }, { "Plant", 13 }, { "Aqua", 14 }, { "Warrior", 15 },
            { "WingedBeast", 16 }, { "Fairy", 17 }, { "Spellcaster", 18 }, { "Thunder", 19 }, { "Reptile", 20 },
            { "Psychic", 21 }, { "Wyrm", 22 }, { "Cyberse", 23 },
        };

        public RoguelikeDeckBuilder(string dataDir, Dictionary<int, int> cardRare)
        {
            _dataDir = dataDir;
            _cardRare = cardRare ?? new Dictionary<int, int>();
            _sim = new DuelSimulator(dataDir);
            _names = YdkHelper.LoadCardDataFromGame(dataDir);
        }

        public void Run(string[] args, int start)
        {
            if (!_sim.InitForCardQueries())
            {
                Console.WriteLine("[create-deck] failed to init card data (is the duel.dll reachable? run from the install root)");
                return;
            }

            if (HasFlag(args, start, "--dump-cards")) { DumpCards(); return; }
            if (HasFlag(args, start, "--dump-named")) { DumpNamed(); return; }
            if (HasFlag(args, start, "--card-links")) { int s; int.TryParse(Opt(args, start, "--card-links") ?? "", out s); CardLinksViaDll(s); return; }

            int[] budget = ParseBudget(Opt(args, start, "--rarity")); // {N,R,SR,UR}
            int regId = 0; int.TryParse(Opt(args, start, "--regulation") ?? "", out regId);
            Dictionary<int, int> regLimits = LoadRegulationLimits(regId); // cardId -> max copies (a0..a3)
            int count = 1; int.TryParse(Opt(args, start, "--count") ?? "", out count); if (count < 1) count = 1;

            if (HasFlag(args, start, "--all-elements"))
            {
                foreach (KeyValuePair<string, int> a in Attributes)
                    BuildAndSave(new Filters { Element = a.Value, ElementName = a.Key }, budget, null, regId, regLimits, count);
                return;
            }
            if (HasFlag(args, start, "--all-types"))
            {
                foreach (KeyValuePair<string, int> r in Races)
                    BuildAndSave(new Filters { Race = r.Value, RaceName = r.Key }, budget, null, regId, regLimits, count);
                return;
            }

            Filters f = ParseFilters(args, start);
            if (!f.Any) { Console.WriteLine("[create-deck] no filter given. Use --element/--type/--named/--link (or --dump-named / --card-links / --dump-cards)."); return; }
            BuildAndSave(f, budget, Opt(args, start, "--name"), regId, regLimits, count);
        }

        class Filters
        {
            public int? Element; public string ElementName;
            public int? Race; public string RaceName;
            public int? Named;   // CARD_Named archetype id (clean archetype membership)
            public int? LinkSeed; public int LinkDepth = 1; // CARD_Link related-cards graph (parsed directly)
            public bool Any { get { return Element.HasValue || Race.HasValue || Named.HasValue || LinkSeed.HasValue; } }
        }

        Filters ParseFilters(string[] args, int start)
        {
            Filters f = new Filters();
            string el = Opt(args, start, "--element");
            if (el != null) { f.Element = ResolveAttr(el); f.ElementName = NameOfAttr(f.Element.Value); }
            string ty = Opt(args, start, "--type");
            if (ty != null) { f.Race = ResolveRace(ty); f.RaceName = NameOfRace(f.Race.Value); }
            string nm = Opt(args, start, "--named");
            if (nm != null) { int n; if (int.TryParse(nm, out n)) f.Named = n; }
            string lk = Opt(args, start, "--link");
            if (lk != null) { int s; if (int.TryParse(lk, out s)) f.LinkSeed = s; }
            string ld = Opt(args, start, "--link-depth");
            if (ld != null) { int dd; if (int.TryParse(ld, out dd) && dd > 0) f.LinkDepth = dd; }
            return f;
        }

        void BuildAndSave(Filters f, int[] budget, string nameOverride, int regId, Dictionary<int, int> regLimits, int count)
        {
            HashSet<int> linkSet = f.LinkSeed.HasValue ? RelatedSet(f.LinkSeed.Value, f.LinkDepth) : null;

            List<int> pool = new List<int>();
            foreach (int id in _cardRare.Keys)
            {
                int lim; if (regLimits.TryGetValue(id, out lim) && lim == 0) continue; // banned by regulation (a0)
                if (f.Element.HasValue && _sim.GetAttribute(id) != f.Element.Value) continue;
                if (f.Race.HasValue && _sim.GetRace(id) != f.Race.Value) continue;
                if (f.Named.HasValue && _sim.CheckName(id, f.Named.Value) == 0) continue;
                if (linkSet != null && !linkSet.Contains(id)) continue;
                if (!IsMainDeckPlayable((CardFrame)_sim.GetFrame(id))) continue;
                pool.Add(id);
            }

            string baseName = nameOverride ?? AutoName(f);
            if (pool.Count == 0) { Console.WriteLine("[create-deck] '" + baseName + "': empty pool, skipped"); return; }

            int wanted = budget.Sum();
            for (int i = 1; i <= count; i++)
            {
                List<int> deck = BuildDeck(pool, budget, regLimits);
                if (deck.Count < wanted && i == 1)
                    Console.WriteLine("[create-deck] '" + baseName + "': pool only yields " + deck.Count + "/" + wanted +
                        " (need >= " + ((wanted + 2) / 3) + " distinct cards) — generating smaller decks");
                string saved = SaveDeck(baseName, deck, regId); // auto-numbers if the file exists
                Console.WriteLine("[create-deck] " + saved + "  (" + deck.Count + " cards from pool of " + pool.Count + ")");
            }
        }

        // Random fill per rarity tier, then top up the deficit from any rarity. Copies per card are
        // capped at 3, or lower if the regulation limits it (a1=1, a2=2).
        List<int> BuildDeck(List<int> pool, int[] budget, Dictionary<int, int> regLimits)
        {
            Dictionary<int, List<int>> byRarity = new Dictionary<int, List<int>>();
            foreach (int id in pool)
            {
                int r; if (!_cardRare.TryGetValue(id, out r)) continue;
                if (!byRarity.ContainsKey(r)) byRarity[r] = new List<int>();
                byRarity[r].Add(id);
            }
            Func<int, int> maxFor = id => { int lim; return regLimits.TryGetValue(id, out lim) ? Math.Min(3, lim) : 3; };

            List<int> deck = new List<int>();
            for (int tier = 0; tier < budget.Length; tier++)
            {
                int rarity = tier + 1; // budget[0]=N(1) .. budget[3]=UR(4)
                List<int> bucket; byRarity.TryGetValue(rarity, out bucket);
                if (bucket == null || bucket.Count == 0) continue;
                deck.AddRange(PickRandom(bucket, budget[tier], maxFor, deck));
            }

            int total = budget.Sum();
            if (deck.Count < total) deck.AddRange(PickRandom(pool, total - deck.Count, maxFor, deck));
            return deck;
        }

        // Pick `quota` ids (with repeats) from `from`, capping each card at maxFor(id) total across
        // the deck (counting copies already in `existing`). Pure random.
        List<int> PickRandom(List<int> from, int quota, Func<int, int> maxFor, List<int> existing)
        {
            Dictionary<int, int> have = existing.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            List<int> bag = new List<int>();
            foreach (int id in from)
            {
                int already; have.TryGetValue(id, out already);
                for (int k = already; k < maxFor(id); k++) bag.Add(id);
            }
            Shuffle(bag);
            List<int> picked = new List<int>();
            for (int i = 0; i < bag.Count && picked.Count < quota; i++) picked.Add(bag[i]);
            return picked;
        }

        // Saves to <name>.json, or the next free <name>_2/_3/... if it already exists (never
        // overwrites). Returns the file name written.
        string SaveDeck(string name, List<int> ids, int regId)
        {
            string dir = Path.Combine(_dataDir, "Roguelike", "Opponents");
            Utils.TryCreateDirectory(dir);
            string safe = SafeFile(name);
            string file = Path.Combine(dir, safe + ".json");
            for (int i = 2; File.Exists(file); i++) file = Path.Combine(dir, safe + "_" + i + ".json");
            DeckInfo deck = new DeckInfo();
            deck.Name = name;
            deck.RegulationId = regId;
            foreach (int id in ids) deck.MainDeckCards.Add(id);
            deck.File = file;
            deck.Save();
            return Path.GetFileName(file);
        }

        // cardId -> max copies from regulation `available` (a0=0 banned, a1=1, a2=2, a3=3).
        // Cards not listed are unconstrained (treated as 3). Empty when regId <= 0 or not found.
        Dictionary<int, int> LoadRegulationLimits(int regId)
        {
            Dictionary<int, int> limits = new Dictionary<int, int>();
            if (regId <= 0) return limits;
            string p = Path.Combine(_dataDir, "Regulation.json");
            if (!File.Exists(p)) { Console.WriteLine("[create-deck] Regulation.json not found"); return limits; }
            Dictionary<string, object> root = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>;
            Dictionary<string, object> reg = root != null ? Utils.GetValue<Dictionary<string, object>>(root, regId.ToString()) : null;
            Dictionary<string, object> avail = reg != null ? Utils.GetValue<Dictionary<string, object>>(reg, "available") : null;
            if (avail == null) { Console.WriteLine("[create-deck] regulation " + regId + " not found in Regulation.json"); return limits; }
            AddLimits(limits, avail, "a0", 0); AddLimits(limits, avail, "a1", 1);
            AddLimits(limits, avail, "a2", 2); AddLimits(limits, avail, "a3", 3);
            int banned = limits.Count(kv => kv.Value == 0);
            Console.WriteLine("[create-deck] regulation " + regId + ": " + limits.Count + " limited cards (" + banned + " banned)");
            return limits;
        }

        static void AddLimits(Dictionary<int, int> limits, Dictionary<string, object> avail, string key, int max)
        {
            List<object> list = Utils.GetValue<List<object>>(avail, key);
            if (list == null) return;
            foreach (object o in list) { try { limits[Convert.ToInt32(o)] = max; } catch { } }
        }

        void DumpCards()
        {
            List<string> lines = new List<string> { "id\tname\tattr\ttype\trarity\tframe" };
            foreach (int id in _cardRare.Keys.OrderBy(x => x))
            {
                int attr = _sim.GetAttribute(id), race = _sim.GetRace(id);
                CardFrame frame = (CardFrame)_sim.GetFrame(id);
                lines.Add(id + "\t" + NameOf(id) + "\t" + attr + " (" + NameOfAttr(attr) + ")\t" +
                    race + " (" + NameOfRace(race) + ")\t" + RarityName(_cardRare[id]) + "\t" + frame);
            }
            WriteTmp("cards.txt", lines);
            Console.WriteLine("[dump-cards] " + (lines.Count - 1) + " cards -> _tmp/cards.txt");
        }

        // Probe CARD_Named (archetypes) via DLL_CardCheckName(card, nameType). Lists each nameType
        // that has members, with count + example card names — so archetypes become identifiable.
        void DumpNamed()
        {
            const int maxName = 2048, examples = 8;
            List<int> ids = _cardRare.Keys.ToList();
            List<string> lines = new List<string> { "nameType\tcount\texamples" };
            int found = 0;
            for (int t = 1; t < maxName; t++)
            {
                int count = 0; List<string> ex = new List<string>();
                foreach (int id in ids)
                {
                    if (_sim.CheckName(id, t) == 0) continue;
                    count++;
                    if (ex.Count < examples) ex.Add(NameOf(id));
                }
                if (count > 0) { lines.Add(t + "\t" + count + "\t" + string.Join(", ", ex)); found++; }
            }
            WriteTmp("named.txt", lines);
            Console.WriteLine("[dump-named] " + found + " non-empty name groups (1.." + (maxName - 1) + ") -> _tmp/named.txt");
        }

        // Direct test of the duel.dll CARD_Link accessor for one card: prints link rating, arrow
        // mask, and each value GetLinkCards returns (annotated with the card name if it maps to one).
        void CardLinksViaDll(int cardId)
        {
            if (cardId <= 0) { Console.WriteLine("[card-links] usage: --card-links <cardId>"); return; }
            Console.WriteLine("[card-links] " + cardId + " (" + NameOf(cardId) + ")  frame=" + (CardFrame)_sim.GetFrame(cardId));
            Console.WriteLine("  DLL_CardGetLinkNum  = " + _sim.GetLinkNum(cardId) + "   (informativo, NÃO usado pra contar)");

            // Chamar DLL_CardGetLinkCards DIRETO, sem gate no LinkNum. Buffer grande; o retorno é a
            // contagem de cids de 16 bits (cada int empacota dois: lo16 e hi16).
            int[] buf = new int[1024];
            int ret = _sim.GetLinkCardsRaw(cardId, buf);
            Console.WriteLine("  DLL_CardGetLinkCards return = " + ret + " (cids de 16 bits)");
            int hit = 0, total = 0;
            for (int j = 0; j < ret && j < buf.Length * 2; j++)
            {
                int v = (j % 2 == 0) ? (buf[j / 2] & 0xFFFF) : ((buf[j / 2] >> 16) & 0xFFFF);
                if (v == 0) continue;
                total++;
                bool known = _names.ContainsKey(v);
                if (known) hit++;
                if (total <= 40)
                    Console.WriteLine("    " + v + "  " + (known ? NameOf(v) : "(não é card id)"));
            }
            Console.WriteLine("  cids não-zero: " + total + "  | resolvem pra nome: " + hit);
        }

        // Related-cards set for a seed via the duel.dll (Deck-Editor "Related Cards"), BFS to
        // `depth` levels: depth 1 = the seed's direct related list; deeper expands through them.
        HashSet<int> RelatedSet(int seed, int depth)
        {
            HashSet<int> visited = new HashSet<int> { seed };
            List<int> frontier = new List<int> { seed };
            for (int d = 0; d < Math.Max(1, depth); d++)
            {
                List<int> next = new List<int>();
                foreach (int c in frontier)
                    foreach (int rc in _sim.GetRelatedCards(c))
                        if (visited.Add(rc)) next.Add(rc);
                frontier = next;
                if (frontier.Count == 0) break;
            }
            visited.Remove(seed);
            return visited;
        }

        // ----- helpers -----

        static bool IsMainDeckPlayable(CardFrame f)
        {
            switch (f)
            {
                case CardFrame.Fusion: case CardFrame.Sync: case CardFrame.Dsync:
                case CardFrame.Xyz: case CardFrame.XyzPend: case CardFrame.SyncPend:
                case CardFrame.FusionPend: case CardFrame.Link: case CardFrame.Token:
                    return false;
                default:
                    return true;
            }
        }

        string AutoName(Filters f)
        {
            List<string> parts = new List<string>();
            if (f.Element.HasValue) parts.Add(f.ElementName ?? ("Attr" + f.Element.Value));
            if (f.Race.HasValue) parts.Add(f.RaceName ?? ("Type" + f.Race.Value));
            if (f.Named.HasValue) parts.Add("Archetype" + f.Named.Value);
            if (f.LinkSeed.HasValue) parts.Add("Related_" + NameOf(f.LinkSeed.Value));
            return string.Join("_", parts);
        }

        int ResolveAttr(string s) { int v; if (Attributes.TryGetValue(s, out v)) return v; return int.TryParse(s, out v) ? v : -1; }
        int ResolveRace(string s) { int v; if (Races.TryGetValue(s, out v)) return v; return int.TryParse(s, out v) ? v : -1; }
        static string NameOfAttr(int v) { foreach (var kv in Attributes) if (kv.Value == v) return kv.Key; return "?"; }
        static string NameOfRace(int v) { foreach (var kv in Races) if (kv.Value == v) return kv.Key; return "?"; }
        static string RarityName(int r) { switch (r) { case 1: return "N"; case 2: return "R"; case 3: return "SR"; case 4: return "UR"; default: return "?"; } }

        string NameOf(int id)
        {
            YdkHelper.GameCardInfo c;
            return _names.TryGetValue(id, out c) && !string.IsNullOrEmpty(c.Name) ? c.Name : ("#" + id);
        }

        static int[] ParseBudget(string s)
        {
            int[] def = { 30, 7, 2, 1 };
            if (string.IsNullOrEmpty(s)) return def;
            string[] parts = s.Split(',');
            int[] r = new int[4];
            for (int i = 0; i < 4; i++) { int v; r[i] = (i < parts.Length && int.TryParse(parts[i].Trim(), out v)) ? v : def[i]; }
            return r;
        }

        void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); int t = list[i]; list[i] = list[j]; list[j] = t; }
        }

        static void WriteTmp(string file, List<string> lines)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "_tmp");
            Utils.TryCreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), string.Join("\n", lines));
        }

        static string SafeFile(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static bool HasFlag(string[] args, int start, string flag)
        {
            for (int i = start; i < args.Length; i++) if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string Opt(string[] args, int start, string flag)
        {
            for (int i = start; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
