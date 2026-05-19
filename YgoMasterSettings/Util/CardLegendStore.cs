using System;
using System.Collections.Generic;
using System.IO;
using YgoMaster;

namespace YgoMasterSettings.Util
{
    // Load/save de DataLE/CardLegend.json — set de cids marcados como Legend.
    // Independente do Regulation; serve como source-of-truth pra UI + client
    // legend-limit hook (1 Legend por Type no deck).
    class CardLegendStore
    {
        public HashSet<int> Legends { get; private set; }
        readonly string _path;
        bool _dirty;

        public CardLegendStore(string dataDir)
        {
            _path = Path.Combine(dataDir, "CardLegend.json");
            Legends = new HashSet<int>();
            Load();
        }

        public bool IsDirty { get { return _dirty; } }

        public bool IsLegend(int cid) { return Legends.Contains(cid); }

        public bool SetLegend(int cid, bool isLegend)
        {
            bool changed;
            if (isLegend) changed = Legends.Add(cid);
            else          changed = Legends.Remove(cid);
            if (changed) _dirty = true;
            return changed;
        }

        public void ClearDirty() { _dirty = false; }

        void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                Dictionary<string, object> root =
                    MiniJSON.Json.Deserialize(File.ReadAllText(_path)) as Dictionary<string, object>;
                if (root == null) return;
                object listObj;
                if (!root.TryGetValue("legends", out listObj)) return;
                List<object> list = listObj as List<object>;
                if (list == null) return;
                foreach (object o in list)
                {
                    try { Legends.Add(Convert.ToInt32(o)); } catch { }
                }
            }
            catch { /* silent — store fica vazio */ }
        }

        public void Save()
        {
            List<int> sorted = new List<int>(Legends);
            sorted.Sort();
            Dictionary<string, object> doc = new Dictionary<string, object>();
            List<object> arr = new List<object>(sorted.Count);
            foreach (int cid in sorted) arr.Add(cid);
            doc["legends"] = arr;
            JsonFileWriter.SaveAtomic(_path, MiniJSON.Json.Serialize(doc), "CardLegend");
            _dirty = false;
        }
    }
}
