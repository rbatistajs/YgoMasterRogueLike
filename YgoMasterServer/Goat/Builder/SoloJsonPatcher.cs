using System;
using System.Collections.Generic;
using System.IO;

namespace YgoMaster.Builder
{
    // Port de scripts/_solo_helpers + emit_gate (parte do Solo.json) —
    // abre DataLE/Solo.json, substitui o gate-target inteiro pelo novo
    // conteúdo, e salva. Preserva todo o resto (outros gates, packs,
    // structure decks etc).
    //
    // Operação de "upsert + scrub":
    //   1. Lê Solo.json
    //   2. Remove o gate target em `Solo.gate[<gateId>]`
    //   3. Remove `Solo.chapter[<gateId>]`
    //   4. Remove `Solo.reward[<chapterId>]` pra todo chapterId em
    //      [gateId*10000, (gateId+1)*10000)
    //   5. Idem pra `Solo.unlock`
    //   6. Insere o novo gate meta + chapter dict + reward + unlock
    //   7. Escreve Solo.json indentado
    static class SoloJsonPatcher
    {
        public static string SoloJsonPath(string dataDirectory)
        {
            return Path.Combine(dataDirectory, "Solo.json");
        }

        // gateMeta = dict no shape esperado por Solo.json (priority/
        //            parent_gate/view_gate/unlock_id/clear_chapter/
        //            regulation_name)
        // gateChapters = {chapterId: chapterDict}
        // rewards = {chapterId: rewardBlock}     ← chapterId string
        // unlocks = {chapterId: unlockSpec}      ← chapterId string
        public static void UpsertGate(string dataDirectory, int gateId,
            Dictionary<string, object> gateMeta,
            Dictionary<string, Dictionary<string, object>> gateChapters,
            Dictionary<string, Dictionary<string, object>> rewards,
            Dictionary<string, Dictionary<string, object>> unlocks)
        {
            string path = SoloJsonPath(dataDirectory);
            if (!File.Exists(path))
                throw new InvalidOperationException("Solo.json not found at " + path);

            Dictionary<string, object> root = MiniJSON.Json.DeserializeStripped(
                File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) throw new InvalidOperationException("Solo.json root malformed");

            Dictionary<string, object> master = Utils.GetValue<Dictionary<string, object>>(root, "Master");
            if (master == null) throw new InvalidOperationException("Solo.json missing Master");
            Dictionary<string, object> solo = Utils.GetValue<Dictionary<string, object>>(master, "Solo");
            if (solo == null) throw new InvalidOperationException("Solo.json missing Master.Solo");

            Dictionary<string, object> gatesRoot   = EnsureDict(solo, "gate");
            Dictionary<string, object> chaptersRoot= EnsureDict(solo, "chapter");
            Dictionary<string, object> rewardsRoot = EnsureDict(solo, "reward");
            Dictionary<string, object> unlocksRoot = EnsureDict(solo, "unlock");

            string gateKey = gateId.ToString();
            gatesRoot.Remove(gateKey);
            chaptersRoot.Remove(gateKey);

            // Scrub rewards/unlocks no range desse gate.
            int lo = gateId * 10000;
            int hi = (gateId + 1) * 10000;
            ScrubRange(rewardsRoot, lo, hi);
            ScrubRange(unlocksRoot, lo, hi);

            // Insere o novo gate.
            if (gateMeta != null)
                gatesRoot[gateKey] = gateMeta;

            if (gateChapters != null)
            {
                Dictionary<string, object> chMap = new Dictionary<string, object>(gateChapters.Count);
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in gateChapters)
                    chMap[kv.Key] = kv.Value;
                chaptersRoot[gateKey] = chMap;
            }
            if (rewards != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in rewards)
                    rewardsRoot[kv.Key] = kv.Value;
            }
            if (unlocks != null)
            {
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in unlocks)
                    unlocksRoot[kv.Key] = kv.Value;
            }

            File.WriteAllText(path, MiniJSON.Json.Serialize(root));
        }

        // Drop a gate completely (gate meta + chapters + rewards/unlocks
        // no range + SoloDuels do range). Usado pra `--grid-gate delete`
        // futuro.
        public static void DeleteGate(string dataDirectory, int gateId)
        {
            string path = SoloJsonPath(dataDirectory);
            if (File.Exists(path))
            {
                Dictionary<string, object> root = MiniJSON.Json.DeserializeStripped(
                    File.ReadAllText(path)) as Dictionary<string, object>;
                if (root != null)
                {
                    Dictionary<string, object> solo = Utils.GetValue<Dictionary<string, object>>(
                        Utils.GetValue<Dictionary<string, object>>(root, "Master") ?? new Dictionary<string, object>(),
                        "Solo");
                    if (solo != null)
                    {
                        string gateKey = gateId.ToString();
                        Dictionary<string, object> gatesRoot   = Utils.GetValue<Dictionary<string, object>>(solo, "gate");
                        Dictionary<string, object> chaptersRoot= Utils.GetValue<Dictionary<string, object>>(solo, "chapter");
                        Dictionary<string, object> rewardsRoot = Utils.GetValue<Dictionary<string, object>>(solo, "reward");
                        Dictionary<string, object> unlocksRoot = Utils.GetValue<Dictionary<string, object>>(solo, "unlock");
                        if (gatesRoot   != null) gatesRoot.Remove(gateKey);
                        if (chaptersRoot != null) chaptersRoot.Remove(gateKey);
                        int lo = gateId * 10000;
                        int hi = (gateId + 1) * 10000;
                        if (rewardsRoot != null) ScrubRange(rewardsRoot, lo, hi);
                        if (unlocksRoot != null) ScrubRange(unlocksRoot, lo, hi);
                        File.WriteAllText(path, MiniJSON.Json.Serialize(root));
                    }
                }
            }

            // SoloDuels: remove arquivos do range.
            string soloDuelsDir = Path.Combine(dataDirectory, "SoloDuels");
            if (Directory.Exists(soloDuelsDir))
            {
                int lo2 = gateId * 10000;
                int hi2 = (gateId + 1) * 10000;
                foreach (string p in Directory.GetFiles(soloDuelsDir, gateId + "*.json"))
                {
                    int cid;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(p), out cid)
                        && cid >= lo2 && cid < hi2)
                    {
                        try { File.Delete(p); } catch { }
                    }
                }
            }
        }

        static Dictionary<string, object> EnsureDict(Dictionary<string, object> parent, string key)
        {
            Dictionary<string, object> child = Utils.GetValue<Dictionary<string, object>>(parent, key);
            if (child == null)
            {
                child = new Dictionary<string, object>();
                parent[key] = child;
            }
            return child;
        }

        // Remove qualquer chave numérica em [lo, hi). Chaves não-numéricas
        // ficam intactas.
        static void ScrubRange(Dictionary<string, object> dict, int lo, int hi)
        {
            List<string> toRemove = new List<string>();
            foreach (string k in dict.Keys)
            {
                int v;
                if (int.TryParse(k, out v) && v >= lo && v < hi) toRemove.Add(k);
            }
            foreach (string k in toRemove) dict.Remove(k);
        }
    }
}
