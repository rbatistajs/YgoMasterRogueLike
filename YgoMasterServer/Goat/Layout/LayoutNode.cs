using System.Collections.Generic;

namespace YgoMaster.Layout
{
    // Mirrors Python's `Node` (build_grid_gate_procedural.py). One per
    // chapter; the generator returns a list of these for the layout +
    // a root + a boss reference. Post-processing helpers (TypeAssigner,
    // LevelAssigner, ProgressionSetter) mutate these in-place.
    class LayoutNode
    {
        public int X;
        public int Y;
        public LayoutNode Parent;
        public List<LayoutNode> Children = new List<LayoutNode>();
        public int ChapterId;
        public string ChapterType = "";
        public int Level = 6;
        public bool IsMainPath;
        public bool IsLeafTerminal;
        // Set by the manual generator — post-processors skip these so
        // edits stick.
        public bool IsManualCell;

        public LayoutNode(int x, int y, LayoutNode parent)
        {
            X = x;
            Y = y;
            Parent = parent;
        }
    }
}
