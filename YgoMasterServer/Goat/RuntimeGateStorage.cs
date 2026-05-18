using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Goat: persists per-player runtime-gate state. One JSON per gate per
    // player so iteration doesn't have to load everything at once.
    //
    // Layout:
    //   <playerDir>/RuntimeGates/<gateId>.json
    //
    // File schema:
    //   {
    //     "seed": <long>,                 // RNG seed used to generate
    //     "generated_at": <epoch>,        // unix timestamp
    //     "boss_chapter_id": <int>,       // for regen-on-clear detection
    //     "chapters": { "<id>": { ... chapter dict ... }, ... },
    //     "solo_duels": { "<id>": { ... DuelSettings.ToDictionary ... }, ... }
    //   }
    static class RuntimeGateStorage
    {
        const string SubDir = "RuntimeGates";

        public static GeneratedGate Load(string playerDir, int gateId)
        {
            string path = PathFor(playerDir, gateId);
            if (!File.Exists(path)) return null;

            try
            {
                Dictionary<string, object> doc = MiniJSON.Json.DeserializeStripped(
                    File.ReadAllText(path)) as Dictionary<string, object>;
                if (doc == null) return null;

                GeneratedGate gate = new GeneratedGate
                {
                    GateId         = gateId,
                    Seed           = Utils.GetValue<long>(doc, "seed"),
                    GeneratedAt    = Utils.GetValue<long>(doc, "generated_at"),
                    BossChapterId  = Utils.GetValue<int>(doc, "boss_chapter_id"),
                    Chapters       = new Dictionary<int, Dictionary<string, object>>(),
                    SoloDuels      = new Dictionary<int, DuelSettings>(),
                    SoloDuelDicts  = new Dictionary<int, Dictionary<string, object>>(),
                    ItemDrops      = new Dictionary<int, int[]>(),
                };
                Dictionary<string, object> drops = Utils.GetValue<Dictionary<string, object>>(doc, "item_drops");
                if (drops != null)
                {
                    foreach (KeyValuePair<string, object> kv in drops)
                    {
                        int cid; if (!int.TryParse(kv.Key, out cid)) continue;
                        List<object> pair = kv.Value as List<object>;
                        if (pair == null || pair.Count != 2) continue;
                        try
                        {
                            gate.ItemDrops[cid] = new[]
                            {
                                Convert.ToInt32(pair[0]),
                                Convert.ToInt32(pair[1]),
                            };
                        }
                        catch { }
                    }
                }

                Dictionary<string, object> chapters = Utils.GetValue<Dictionary<string, object>>(doc, "chapters");
                if (chapters != null)
                {
                    foreach (KeyValuePair<string, object> kv in chapters)
                    {
                        int cid;
                        if (!int.TryParse(kv.Key, out cid)) continue;
                        gate.Chapters[cid] = kv.Value as Dictionary<string, object>;
                    }
                }

                Dictionary<string, object> duels = Utils.GetValue<Dictionary<string, object>>(doc, "solo_duels");
                if (duels != null)
                {
                    foreach (KeyValuePair<string, object> kv in duels)
                    {
                        int cid;
                        if (!int.TryParse(kv.Key, out cid)) continue;
                        Dictionary<string, object> dsDict = kv.Value as Dictionary<string, object>;
                        if (dsDict == null) continue;
                        gate.SoloDuelDicts[cid] = dsDict;
                        gate.SoloDuels[cid] = RuntimeGateGenerator.HydrateDuelSettings(dsDict, cid);
                    }
                }
                return gate;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RuntimeGateStorage] failed to load " + path + ": " + ex.Message);
                return null;
            }
        }

        public static void Save(string playerDir, GeneratedGate gate)
        {
            string path = PathFor(playerDir, gate.GateId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            Dictionary<string, object> chapters = new Dictionary<string, object>();
            foreach (KeyValuePair<int, Dictionary<string, object>> kv in gate.Chapters)
            {
                chapters[kv.Key.ToString()] = kv.Value;
            }

            // Save the raw input dicts (not ds.ToDictionary()): keeps the
            // file size minimal and matches the Python-builder format
            // exactly (no extra MaxPlayers-padded Deck slots, no upstream
            // default fields). The DuelSettings instance is rebuilt from
            // these dicts on load.
            Dictionary<string, object> duels = new Dictionary<string, object>();
            foreach (KeyValuePair<int, Dictionary<string, object>> kv in gate.SoloDuelDicts)
            {
                duels[kv.Key.ToString()] = kv.Value;
            }

            Dictionary<string, object> doc = new Dictionary<string, object>
            {
                { "seed",            gate.Seed },
                { "generated_at",    gate.GeneratedAt },
                { "boss_chapter_id", gate.BossChapterId },
                { "chapters",        chapters },
                { "solo_duels",      duels },
            };
            if (gate.ItemDrops != null && gate.ItemDrops.Count > 0)
            {
                Dictionary<string, object> drops = new Dictionary<string, object>();
                foreach (KeyValuePair<int, int[]> kv in gate.ItemDrops)
                {
                    drops[kv.Key.ToString()] = new List<object> { kv.Value[0], kv.Value[1] };
                }
                doc["item_drops"] = drops;
            }
            File.WriteAllText(path, MiniJSON.Json.Serialize(doc));
        }

        public static void Delete(string playerDir, int gateId)
        {
            string path = PathFor(playerDir, gateId);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex)
            {
                Console.WriteLine("[RuntimeGateStorage] failed to delete " + path + ": " + ex.Message);
            }
        }

        static string PathFor(string playerDir, int gateId)
        {
            return Path.Combine(playerDir, SubDir, gateId + ".json");
        }
    }

    // In-memory view of a generated gate for a single player.
    class GeneratedGate
    {
        public int GateId;
        public long Seed;
        public long GeneratedAt;
        public int BossChapterId;
        public Dictionary<int, Dictionary<string, object>> Chapters;
        public Dictionary<int, DuelSettings> SoloDuels;
        // Raw duel dicts kept alongside the parsed DuelSettings for
        // round-trip-free persistence (see Save).
        public Dictionary<int, Dictionary<string, object>> SoloDuelDicts;
        // chapterId → (rewardCategoryId, itemId). Sorteado uma vez por
        // regen via RewardPicker; salvo em disco pra ficar estável até o
        // próximo regen (player abre o menu N vezes, drop é o mesmo).
        public Dictionary<int, int[]> ItemDrops;
    }
}
