using System.Collections.Generic;

namespace YgoMaster
{
    class MapNode
    {
        public int Id;
        public string Type;          // duel|elite|event|shop|reward|boss
        public int Row;
        public int Col;
        public List<int> Next = new List<int>();   // connected nodes in Row+1
        public string Encounter;     // baked encounter id (baked types; chosen at map-gen)
        public string Name;          // display name for the baked encounter
        public string IconImage;     // baked encounter art ("card_<cid>" / "profile_<id>"), if any
        public int? EnemyLp;         // baked combat enemy starting LP (preview), if any
        public int? Reward;          // baked currency reward (preview), if any
        public Dictionary<string, object> Modifiers; // declared-modifier summary, or null

        public Dictionary<string, object> ToDictionary()
        {
            List<object> next = new List<object>();
            foreach (int n in Next) next.Add(n);
            Dictionary<string, object> d = new Dictionary<string, object>
            {
                { "id", Id }, { "type", Type }, { "row", Row }, { "col", Col }, { "next", next },
            };
            if (!string.IsNullOrEmpty(Encounter)) d["encounter"] = Encounter;
            if (!string.IsNullOrEmpty(Name)) d["name"] = Name;
            if (!string.IsNullOrEmpty(IconImage)) d["iconImage"] = IconImage;
            if (EnemyLp.HasValue) d["enemyLp"] = EnemyLp.Value;
            if (Reward.HasValue) d["reward"] = Reward.Value;
            if (Modifiers != null && Modifiers.Count > 0) d["modifiers"] = Modifiers;
            return d;
        }
    }

    class RoguelikeMap
    {
        public int Rows;
        public List<MapNode> Nodes = new List<MapNode>();

        public Dictionary<string, object> ToDictionary()
        {
            List<object> nodes = new List<object>();
            foreach (MapNode n in Nodes) nodes.Add(n.ToDictionary());
            return new Dictionary<string, object> { { "rows", Rows }, { "nodes", nodes } };
        }
    }
}
