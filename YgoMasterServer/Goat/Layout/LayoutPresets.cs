using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Mirrors the SIZE_PRESETS / BRANCHING_PRESETS / DIFFICULTY_PRESETS
    // tables from build_grid_gate_procedural.py. Keep in sync if the
    // Python side adds a preset.
    static class LayoutPresets
    {
        // Hourglass size — drives grid bounds + trunk/branch counts.
        public class Size
        {
            public int GridWidth, GridHeight, TrunkLength;
            public int FanCount, BranchLength, NarrowingLength;
        }
        public static readonly Dictionary<string, Size> SizePresets = new Dictionary<string, Size>
        {
            { "small",  new Size { GridWidth=7,  GridHeight=10, TrunkLength=2,
                                   FanCount=3, BranchLength=3, NarrowingLength=2 } },
            { "medium", new Size { GridWidth=9,  GridHeight=14, TrunkLength=3,
                                   FanCount=5, BranchLength=5, NarrowingLength=4 } },
            { "large",  new Size { GridWidth=11, GridHeight=18, TrunkLength=3,
                                   FanCount=5, BranchLength=7, NarrowingLength=5 } },
            { "huge",   new Size { GridWidth=13, GridHeight=22, TrunkLength=4,
                                   FanCount=7, BranchLength=9, NarrowingLength=6 } },
        };

        public class Branching
        {
            public double SideBranchChance;
            public int SideBranchLength;
        }
        public static readonly Dictionary<string, Branching> BranchingPresets = new Dictionary<string, Branching>
        {
            { "few",    new Branching { SideBranchChance=0.10, SideBranchLength=2 } },
            { "normal", new Branching { SideBranchChance=0.30, SideBranchLength=2 } },
            { "many",   new Branching { SideBranchChance=0.50, SideBranchLength=3 } },
        };

        // Difficulty curve band: (y_min, y_max, {level -> weight})
        public class CurveBand
        {
            public int YMin, YMax;
            public Dictionary<int, double> Weights;
        }
        public static readonly Dictionary<string, List<CurveBand>> DifficultyPresets =
            new Dictionary<string, List<CurveBand>>
        {
            { "easy", new List<CurveBand> {
                new CurveBand { YMin=0,  YMax=4,  Weights=new Dictionary<int,double>{{0,0.7},{1,0.3}} },
                new CurveBand { YMin=5,  YMax=9,  Weights=new Dictionary<int,double>{{1,0.4},{2,0.4},{3,0.2}} },
                new CurveBand { YMin=10, YMax=99, Weights=new Dictionary<int,double>{{3,0.4},{4,0.4},{5,0.2}} },
            }},
            { "default", new List<CurveBand> {
                new CurveBand { YMin=0,  YMax=3,  Weights=new Dictionary<int,double>{{0,0.6},{1,0.4}} },
                new CurveBand { YMin=4,  YMax=7,  Weights=new Dictionary<int,double>{{1,0.3},{2,0.4},{3,0.3}} },
                new CurveBand { YMin=8,  YMax=11, Weights=new Dictionary<int,double>{{3,0.3},{4,0.4},{5,0.3}} },
                new CurveBand { YMin=12, YMax=99, Weights=new Dictionary<int,double>{{5,0.3},{6,0.7}} },
            }},
            { "brutal", new List<CurveBand> {
                new CurveBand { YMin=0, YMax=2,  Weights=new Dictionary<int,double>{{1,0.5},{2,0.5}} },
                new CurveBand { YMin=3, YMax=6,  Weights=new Dictionary<int,double>{{2,0.3},{3,0.4},{4,0.3}} },
                new CurveBand { YMin=7, YMax=99, Weights=new Dictionary<int,double>{{4,0.3},{5,0.4},{6,0.3}} },
            }},
        };
    }
}
