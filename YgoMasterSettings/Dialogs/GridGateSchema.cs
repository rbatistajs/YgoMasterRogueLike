using System.Collections.Generic;

namespace YgoMasterSettings.Dialogs
{
    // Schema dos campos editáveis no GateEditDialog — mirror do
    // FORMAT_REGISTRY + GENERIC_SCHEMA + GENERIC_DEFAULTS do builder
    // Python (build_grid_gate_procedural.py). Hardcoded aqui pra que o
    // settings não dependa de subprocess pro Python pegar o schema.
    //
    // Quando adicionar formato novo no LayoutGenerator C#, atualize
    // aqui também.
    static class GridGateSchema
    {
        public static readonly string[] DuelTypes = { "Normal", "Rush" };
        public static readonly string[] DifficultyModes = { "basic", "easy", "default", "brutal", "custom" };
        public static readonly string[] CosmeticModes  = { "vanilla", "random" };

        public enum FieldKind { Int, IntOptional, Float, Preset }

        public class Field
        {
            public string Key;
            public FieldKind Kind;
            public string Label;
            public double Min, Max;
            public string[] Choices;   // só pra Preset
            public Field(string key, FieldKind kind, string label, double min = 0, double max = 0, string[] choices = null)
            { Key = key; Kind = kind; Label = label; Min = min; Max = max; Choices = choices; }
        }

        public class FormatMeta
        {
            public string Key;
            public string Label;
            public Dictionary<string, object> Defaults;
            public List<Field> Fields;
        }

        public static readonly FormatMeta[] Formats = {
            new FormatMeta {
                Key = "hourglass",
                Label = "Hourglass (trunk → fan → boss)",
                Defaults = new Dictionary<string, object> {
                    { "size", "medium" }, { "branching", "normal" },
                },
                Fields = new List<Field> {
                    new Field("size",      FieldKind.Preset, "Map size",
                              choices: new[] { "small", "medium", "large", "huge" }),
                    new Field("branching", FieldKind.Preset, "Side-branch density",
                              choices: new[] { "few", "normal", "many" }),
                },
            },
            new FormatMeta {
                Key = "dungeon",
                Label = "Dungeon (room-to-room with start/end)",
                Defaults = new Dictionary<string, object> {
                    { "grid_width", 10 }, { "grid_height", 10 }, { "room_count", 25 },
                    { "start_x", null }, { "start_y", null },
                    { "end_x", null }, { "end_y", null },
                    { "branching_chance", 0.55 },
                },
                Fields = new List<Field> {
                    new Field("grid_width",       FieldKind.Int,         "Grid width",       4, 20),
                    new Field("grid_height",      FieldKind.Int,         "Grid height",      4, 30),
                    new Field("room_count",       FieldKind.Int,         "Room count (target)", 5, 80),
                    new Field("start_x",          FieldKind.IntOptional, "Start X (blank = random)", 0, 30),
                    new Field("start_y",          FieldKind.IntOptional, "Start Y (blank = random)", 0, 30),
                    new Field("end_x",            FieldKind.IntOptional, "End/boss X (blank = random)", 0, 30),
                    new Field("end_y",            FieldKind.IntOptional, "End/boss Y (blank = random)", 0, 30),
                    new Field("branching_chance", FieldKind.Float,       "Branching chance (0..1)", 0.0, 1.0),
                },
            },
            new FormatMeta {
                Key = "tower",
                Label = "Tower (vertical trunk + branches per floor)",
                Defaults = new Dictionary<string, object> {
                    { "floor_count", 12 }, { "branch_count_per_floor", 2 },
                    { "floor_distance", 2 }, { "branch_length", 2 },
                },
                Fields = new List<Field> {
                    new Field("floor_count",            FieldKind.Int, "Floor count", 2, 30),
                    new Field("branch_count_per_floor", FieldKind.Int, "Branches per floor", 0, 6),
                    new Field("floor_distance",         FieldKind.Int, "Rows between floors", 1, 4),
                    new Field("branch_length",          FieldKind.Int, "Cells per branch", 1, 6),
                },
            },
        };

        // Generic params (mesma list pra todo formato).
        public static readonly Dictionary<string, object> GenericDefaults =
            new Dictionary<string, object>
        {
            { "difficulty_curve", "basic" },
            { "duel_level", 3 },
            { "elite_level", 5 },
            { "elite_count", 1 },
            { "boss_level", 6 },
            { "reward_count", 2 },
            { "treasure_count", 1 },
            { "lock_count", 0 },
            { "seed", null },
        };

        public static readonly List<Field> GenericFields = new List<Field> {
            new Field("difficulty_curve", FieldKind.Preset,      "Difficulty mode", choices: DifficultyModes),
            new Field("duel_level",       FieldKind.Int,         "Duel level (basic only)",       0, 6),
            new Field("elite_level",      FieldKind.Int,         "Elite level (0=easy, 6=hard)",  0, 6),
            new Field("elite_count",      FieldKind.Int,         "Elite count",                   0, 10),
            new Field("boss_level",       FieldKind.Int,         "Boss level (0=easy, 6=hard)",   0, 6),
            new Field("reward_count",     FieldKind.Int,         "Reward chest count",            0, 30),
            new Field("treasure_count",   FieldKind.Int,         "Treasure chest count",          0, 30),
            new Field("lock_count",       FieldKind.Int,         "Lock count",                    0, 10),
            new Field("seed",             FieldKind.IntOptional, "Seed (blank = hash of gate_id)", 0, 2147483647),
        };

        public static FormatMeta FindFormat(string key)
        {
            foreach (FormatMeta f in Formats)
                if (f.Key == key) return f;
            return Formats[0];   // hourglass default
        }
    }
}
