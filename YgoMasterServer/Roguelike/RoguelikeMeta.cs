using System.Collections.Generic;
using System.IO;

namespace YgoMaster
{
    // Per-player roguelike progress that must outlive a run (roguelike.json is deleted on loss/
    // abandon). Currently just the highest unlocked ascension.
    class RoguelikeMeta
    {
        public int MaxAscension;

        public static string PathFor(string playerDir) => Path.Combine(playerDir, "roguelike_meta.json");

        public static RoguelikeMeta Load(string playerDir)
        {
            string p = PathFor(playerDir);
            if (!File.Exists(p)) return new RoguelikeMeta();
            try
            {
                var d = MiniJSON.Json.DeserializeStripped(File.ReadAllText(p)) as Dictionary<string, object>;
                return new RoguelikeMeta { MaxAscension = Utils.GetValue<int>(d, "maxAscension", 0) };
            }
            catch { return new RoguelikeMeta(); }
        }

        public void Save(string playerDir)
        {
            Dictionary<string, object> d = new Dictionary<string, object> { { "maxAscension", MaxAscension } };
            File.WriteAllText(PathFor(playerDir), MiniJSON.Json.Format(MiniJSON.Json.Serialize(d)));
        }
    }
}
