using System.Collections.Generic;

namespace YgoMaster
{
    // Goat: extends DuelSettings with a side table the Python builder
    // populates when emitting `cmds` containing negative cid markers.
    // Each key matches `abs(cid)` (string-form) of a marker in cmds.
    // RuntimeRandomResolver consumes this at solo duel start.
    //
    // Declared as fields (not properties) so DuelSettings.FromDictionary's
    // reflection-based loader picks them up — the property loop only
    // handles array/list types, fields go through Convert.ChangeType which
    // returns same-typed values as-is.
    //
    //   random_specs   single-level Dict<string, object>; each value is
    //                  itself a Dict<string, object> (cast at access time).
    //   regulation_name  per-duel label ("Goat Format", "Rush Duel", …) —
    //                  used by the resolver to scope the "any" cid pool.
    partial class DuelSettings
    {
        // CS0649: fields are assigned via reflection in FromDictionary,
        // the compiler can't see that. Suppress the false positive.
#pragma warning disable 0649
        public Dictionary<string, object> random_specs;
        public string regulation_name;
#pragma warning restore 0649

        // `cmds` has a `private set;` upstream. Goat code (resolver) needs
        // to compact the outer array after dropping unresolvable cmds —
        // this helper exposes the assignment without touching upstream.
        public void SetCmds(List<int>[] newCmds) { cmds = newCmds; }
    }
}
