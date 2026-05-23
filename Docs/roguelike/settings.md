# Roguelike `Settings.json` Reference

The roguelike mode is fully data-driven by **`DataLE/Roguelike/Settings.json`**. Edit this file to
tune the map shape, node distribution, run length (acts), difficulty (ascension), and combat HP —
no recompiling. Every key is optional; missing keys fall back to the defaults below, so the mode
runs even with an empty `{}`.

**When changes apply:** the file is read once and cached, so **restart the server** after editing.
Changes affect **new runs** only (an in-progress run keeps the map/HP it was generated with).

---

## Contents

- [Props at a glance](#props-at-a-glance)
- [Map shape](#map-shape) — [`layout`](#layout) · [`floors`](#floors) · [`width`](#width) · [`paths`](#paths)
- [Run structure (acts & ascension)](#run-structure-acts--ascension) — [`acts`](#acts) · [`ascensions`](#ascensions) · [`interActHealPercent`](#interacthealpercent)
- [Combat & HP](#combat--hp) — [`playerMaxHp`](#playermaxhp) · [`healPercentPerCombat`](#healpercentpercombat) · [`enemyHp`](#enemyhp)
- [Node types & distribution](#node-types--distribution) — [`typeWeights`](#typeweights) · [`bands`](#bands) · [`typeRules`](#typerules) · [`forcedRows`](#forcedrows)
- [Row indexing (negative indices)](#row-indexing-negative-indices)
- [Per-act & per-ascension overrides](#per-act--per-ascension-overrides) — [`perAct` / `perAscension`](#peract-and-perascension) · [`ascensionScale`](#ascensionscale) · [Resolution order](#resolution-order)
- [Full annotated example](#full-annotated-example)
- [Why this is useful (for mod users)](#why-this-is-useful-for-mod-users)

---

## Props at a glance

| Key | Type | Default | Summary |
|---|---|---|---|
| [`layout`](#layout) | string | `"slay_the_spire"` | Map generator to use. |
| [`floors`](#floors) | int | `8` | Number of rows; the top row is the act boss. |
| [`width`](#width) | int | `4` | Number of grid columns. |
| [`paths`](#paths) | int | `6` | Bottom→top paths carved through the grid. |
| [`acts`](#acts) | int | `3` | Acts per run (each act is a fresh map + boss). |
| [`ascensions`](#ascensions) | int | `20` | Number of ascension difficulty tiers. |
| [`interActHealPercent`](#interacthealpercent) | number | `0.0` | HP healed (fraction of max) when starting each act after the first. |
| [`playerMaxHp`](#playermaxhp) | int | `8000` | Run HP: starting value and cap. |
| [`healPercentPerCombat`](#healpercentpercombat) | number | `0.10` | HP healed (fraction of max) after every won combat. |
| [`enemyHp`](#enemyhp) | object | `{default:2000}` | Enemy starting LP per node type. |
| [`typeWeights`](#typeweights) | object | see below | Base random weights for node types. |
| [`bands`](#bands) | array | `[]` | Per-row-zone weight tables. |
| [`typeRules`](#typerules) | object | `{}` | Per-type floor ranges (min/max). |
| [`forcedRows`](#forcedrows) | object | `{}` | Force a whole row to one node type. |
| [`perAct`](#peract-and-perascension) | array | `[]` | Per-act config overrides (deep-merge). |
| [`perAscension`](#peract-and-perascension) | array | `[]` | Per-ascension config overrides (deep-merge). |
| [`ascensionScale`](#ascensionscale) | object | `{}` | Multiplicative scaling applied per ascension level. |

---

## Map shape

### `layout`

*(string, default `"slay_the_spire"`)*
Which generator builds the map. Currently only `slay_the_spire` exists: it carves `paths` routes
from the bottom row to the top, branching one column at a time and never crossing edges; nodes
exist only where a route passes, and routes that merge create multi-parent nodes. The top row is a
single **boss** node that the last body row funnels into.

### `floors`

*(int, default 8)*
Number of rows, bottom (row 0, your entry) to top. The top row (`floors-1`) is the act boss. More
floors = a longer act.

### `width`

*(int, default 4)*
Grid columns. Wider maps allow more parallel branches per row.

### `paths`

*(int, default 6)*
How many bottom→top routes are carved. More paths = denser maps with more route choices and more
merges. Paths beyond `width` simply overlap.

---

## Run structure (acts & ascension)

### `acts`

*(int, default 3, min 1)*
A run is divided into this many acts. Each act is its own freshly generated map ending in a boss.
Beat the boss → the next act's map is generated and you continue with your **same deck, currency,
and HP**. Beat the **final** act's boss → the run is won.

### `ascensions`

*(int, default 20, min 1)*
The number of ascension tiers (0-based, so `0 .. ascensions-1`). Ascension is a persistent
difficulty ladder: winning a run at ascension *A* unlocks ascension *A+1* (saved per player in
`roguelike_meta.json`, which survives losing/abandoning a run). When starting a run you pick any
ascension from `0` up to your highest unlocked.

### `interActHealPercent`

*(number, default 0.0)*
Fraction of **max HP** healed when a new act begins (after the first act). `0.0` = carry HP with no
heal (attrition); `1.0` = full heal each act. Because it's just another config key, it can be
overridden per act/ascension (e.g. heal more in later acts, less at high ascension).

---

## Combat & HP

The player has a persistent **run HP** that doubles as the starting Life Points of every duel. HP
carries across duels and acts; reaching 0 ends the run.

### `playerMaxHp`

*(int, default 8000)*
The run's starting HP and its cap — heals never exceed it. Each duel begins with the player's
current run HP as their LP.

### `healPercentPerCombat`

*(number, default 0.10)*
After winning a combat, run HP becomes `min(maxHp, remainingLP + healPercentPerCombat × maxHp)`.
So `0.10` restores 10% of max on top of whatever LP you finished the duel with. Losing a duel
(LP 0) ends the run.

### `enemyHp`

*(object, default `{ "default": 2000 }`)*
Enemy starting LP, looked up by the combat node's type. The `default` entry covers any type without
its own entry; if `enemyHp` is missing entirely the fallback is `2000`.

```json
"enemyHp": { "default": 2000, "elite": 4000, "boss": 8000 }
```

---

## Node types & distribution

Each map node has a **type**. Combat types launch a duel; non-combat types are markers (their
actions are future work).

| Type | Combat? | Notes |
|---|---|---|
| `duel` | yes | Standard fight. Row 0 is always `duel`. |
| `elite` | yes | Tougher fight (give it more `enemyHp`, bigger reward). |
| `boss` | yes | One per act, fixed on the top row (structural — not drawn from weights). |
| `event` | no | Marker (future). |
| `shop` | no | Marker (future). |
| `reward` | no | Marker (future). |

Type assignment for a node on floor `r` resolves in this order:
1. If `forcedRows` covers `r` → use that type.
2. Else pick from the first `bands` zone covering `r`, or `typeWeights` if no band matches.
3. Drop any type excluded by `typeRules` at floor `r`.
4. Weighted-random pick of what remains (fallback `duel`). Row 0 always resolves to `duel`.

The boss is placed structurally on the top row and is never affected by weights/bands/rules.

### `typeWeights`

*(object, default `{duel:0.6, elite:0.12, event:0.12, shop:0.08, reward:0.08}`)*
Relative weights for the random type pick on rows not covered by a band. Values are relative (they
needn't sum to 1).

### `bands`

*(array, default `[]`)*
Row "zones", each with its own `weights`, for a different feel by depth. `from`/`to` are **inclusive**
row indices (negatives allowed — see [Row indexing](#row-indexing-negative-indices)). Rows outside every band fall back to
`typeWeights`. First matching band wins; order bands narrow→broad or non-overlapping.

```json
"bands": [
  { "from": 1, "to": 3,  "weights": { "duel": 0.7, "event": 0.2, "shop": 0.1 } },
  { "from": 4, "to": -3, "weights": { "duel": 0.5, "elite": 0.2, "reward": 0.2 } }
]
```

### `typeRules`

*(object, default `{}`)*
Per-type floor locks applied **after** a band/weights table is chosen — a global "this type may only
appear in this floor range". `minFloor`/`maxFloor` are inclusive; either may be omitted (open-ended).
Negatives allowed.

```json
"typeRules": {
  "elite":  { "minFloor": 2 },
  "shop":   { "minFloor": 2, "maxFloor": -2 },
  "reward": { "minFloor": 3 }
}
```

### `forcedRows`

*(object, default `{}`)*
Force every node on a row to one type — overrides bands and rules. Keys are row indices (negatives
allowed), values are a type name.

```json
"forcedRows": { "0": "duel", "-2": "reward" }
```

---

## Row indexing (negative indices)

Anywhere a row index appears (`bands.from`/`to`, `typeRules.minFloor`/`maxFloor`, `forcedRows` keys)
you may use a **negative** value, counted from the top: `-1` = the top row (the boss), `-2` = the row
just below the boss, and so on. This lets you write "the row before the boss" without knowing
`floors`. Negative indices are normalized to `floors + n` and clamped to `[0, floors-1]`.

---

## Per-act & per-ascension overrides

The same keys above can be tuned per act and per ascension, so act 3 can be deadlier than act 1 and
ascension 5 deadlier than ascension 0. There are two mechanisms:

### `perAct` and `perAscension`

*(arrays, default `[]`)*
Each is an array indexed by act / ascension. Each entry is a **partial** `Settings` object that is
**deep-merged** over the base (nested objects merge key-by-key; anything else replaces). Use these
for explicit, hand-tuned overrides.

```json
"perAct": [
  {},                                                              // act 1 = base
  { "floors": 10, "enemyHp": { "default": 2500, "boss": 10000 } }, // act 2
  { "floors": 12, "enemyHp": { "default": 3000, "boss": 12000 } }  // act 3
]
```

### `ascensionScale`

*(object, default `{}`)*
Progressive **multiplicative** scaling so you don't have to write 20 explicit `perAscension` entries.
Each `key: factor` multiplies the matching numeric value by `(1 + factor × ascension)`. It applies to:
- a top-level numeric key (e.g. `playerMaxHp`),
- every numeric entry of a top-level object (e.g. `enemyHp`),
- a matching entry inside `typeWeights` (e.g. `elite`).

```json
"ascensionScale": { "enemyHp": 0.05, "elite": 0.03 }
```
At ascension 4 this makes enemy LP `×1.20` and the `elite` weight `×1.12`.

### Resolution order

For a node/duel at act `A`, ascension `S`, the effective settings are:

```
base  →  deep-merge perAscension[S]  →  deep-merge perAct[A]  →  apply ascensionScale × S
```

So **act overrides ascension overrides base**, and scaling is applied last on numeric values.

---

## Full annotated example

```json
{
  "layout": "slay_the_spire",
  "floors": 8, "width": 4, "paths": 6,

  "acts": 3,
  "ascensions": 20,
  "interActHealPercent": 0.30,

  "typeWeights": { "duel": 0.6, "elite": 0.12, "event": 0.12, "shop": 0.08, "reward": 0.08 },

  "playerMaxHp": 8000,
  "enemyHp": { "default": 2000, "elite": 4000, "boss": 8000 },
  "healPercentPerCombat": 0.10,

  "bands": [
    { "from": 1, "to": 3,  "weights": { "duel": 0.7, "event": 0.2, "shop": 0.1 } },
    { "from": 4, "to": -3, "weights": { "duel": 0.5, "elite": 0.2, "reward": 0.2, "shop": 0.1 } }
  ],
  "typeRules": {
    "elite":  { "minFloor": 2 },
    "shop":   { "minFloor": 2, "maxFloor": -2 },
    "reward": { "minFloor": 3 }
  },
  "forcedRows": { "0": "duel", "-2": "reward" },

  "perAct": [
    {},
    { "floors": 10, "enemyHp": { "default": 2500, "elite": 5000, "boss": 10000 } },
    { "floors": 12, "enemyHp": { "default": 3000, "elite": 6000, "boss": 12000 } }
  ],
  "ascensionScale": { "enemyHp": 0.05 }
}
```

This run: 3 acts (8/10/12 floors), 30% inter-act heal, elite/reward gated to deeper floors, a reward
row before each boss, escalating enemy HP per act, and +5% enemy HP per ascension level.

---

## Why this is useful (for mod users)

- **No code, no rebuild.** Difficulty curves, map size/shape, run length, and the ascension ladder
  are all just JSON. Edit, restart the server, start a new run.
- **Make your own difficulty.** Tune `enemyHp`, `playerMaxHp`, `healPercentPerCombat`, and
  `interActHealPercent` for a casual or brutal run; layer `ascensionScale`/`perAscension` for a
  long-term challenge ladder.
- **Shape the journey.** `bands`, `typeRules`, and `forcedRows` control where elites, rewards, and
  shops appear (e.g. a guaranteed reward before each boss, no elites in act 1).
- **Per-act pacing.** `perAct` lets later acts be bigger and harder without touching the base.
- **Safe defaults.** Anything you omit uses a sensible default, so partial configs always work and
  upgrades won't break your file.
