# Roguelike `Encounters.json` Reference

The roguelike's combat opponents are a **curated pool** defined in
**`DataLE/Roguelike/Encounters.json`**. Each combat node (`duel`, `elite`, `boss`) draws its enemy
from this pool instead of picking a random deck. An encounter names the deck to fight, plus optional
**gating** (which act / floor / ascension it may appear at) and **overrides** (enemy LP, reward, who
goes first).

**Strict mode:** this file is **required**. If a combat node finds no eligible encounter, it logs an
error and **does not start a duel** (no random fallback). Author enough encounters to cover every
combat node your map can produce.

**When changes apply:** the file is read once and cached, so **restart the server** after editing.
Changes affect **new picks** only (an in-progress duel keeps the enemy it was built with).

**Decks live in `DataLE/Roguelike/Opponents/`** (`.json` or `.ydk`). `Encounters.json` references
them by filename via the `deck` field — that folder is the deck store; this file is the curation
layer on top of it.

---

## Contents

- [Structure](#structure)
- [Fields](#fields)
- [How an enemy is chosen](#how-an-enemy-is-chosen)
- [Gating (act / floor / ascension)](#gating-act--floor--ascension)
- [Overrides (LP, reward, first player)](#overrides-lp-reward-first-player)
- [Modifiers (starting board)](#modifiers-starting-board)
- [Defaults & relationship to Settings.json](#defaults--relationship-to-settingsjson)
- [Strict coverage — avoiding soft-locks](#strict-coverage--avoiding-soft-locks)
- [Full example](#full-example)

---

## Structure

A JSON object **keyed by node type**. Each value is an array of encounter objects. The type is
implied by the key, so entries have no `type` field.

```json
{
  "duel":  [ { ...encounter... }, { ...encounter... } ],
  "elite": [ { ...encounter... } ],
  "boss":  [ { ...encounter... } ]
}
```

Only `duel`, `elite`, and `boss` are combat today. `event` / `shop` / `reward` keys are allowed and
parsed but inert until those node actions exist.

---

## Fields

| Field | Required | Default / fallback | Notes |
|---|---|---|---|
| `id` | yes | — | Unique across the whole file. |
| `name` | no | the `deck` filename (no extension) | Shown on the boss node and as the opponent's name. |
| `text` | no | `""` | Flavor; reserved for future `event` nodes. |
| `deck` | yes | — | File in `Roguelike/Opponents/` (`.json`/`.ydk`). An extension is recommended. |
| `act` | no | always eligible | `{ "min": x, "max": y }`, inclusive, 0-based act. |
| `floor` | no | always eligible | `{ "min": x, "max": y }`, inclusive; floor = the node's row. |
| `ascension` | no | always eligible | `{ "min": x, "max": y }`, inclusive. |
| `weight` | no | `1.0` | Relative weight when several encounters are eligible (all-equal = uniform). |
| `enemyLp` | no | `Settings.enemyLp[type]` (scaled) | Override the enemy's starting LP. |
| `reward` | no | `RewardFor(type)` (boss 1000 / elite 250 / duel 100) | Override run currency on win. |
| `firstPlayer` | no | random (seeded) | `0` = you go first, `1` = the enemy goes first. |
| `cpuRate` | no | `Settings.cpuRate` (100) | CPU AI strength −100…100 (100 = max). |
| `cpuFlag` | no | `Settings.cpuFlag` (None) | AI behavior: `None`/`Def`/`Fool`/`Light`/`MyTurnOnly`/`AttackOnly`/`Simple`. |
| `modifiers` | no | none | Scripted starting board + LP/hand deltas — see [Modifiers](#modifiers-starting-board). |

In each range block (`act` / `floor` / `ascension`) either `min` or `max` may be omitted for an
open-ended bound. An omitted block means "no restriction on that axis".

---

## How an enemy is chosen

- **`duel` / `elite` — chosen on arrival.** When you step onto the node, the server lists every
  encounter of that type whose ranges contain the current act, the node's floor, and your ascension,
  then picks one at random by `weight`. The pick is deterministic (seeded by the run + node), so an
  unfinished duel **resumes with the same enemy and the same draws** — you can't quit to re-roll.
- **`boss` — chosen when the map is generated.** Each act's boss is picked the moment the act's map
  is built and baked into the boss node, so the **map shows the boss's `name` before you fight it**.

---

## Gating (act / floor / ascension)

Ranges restrict where an encounter may appear. All present ranges must contain the value for the
encounter to be eligible.

```json
{ "id": "act1_only",   "deck": "X.json", "act":   { "min": 0, "max": 0 } }
{ "id": "deep_floors", "deck": "Y.json", "floor": { "min": 4 } }
{ "id": "high_asc",    "deck": "Z.json", "ascension": { "min": 3 } }
```

- `act` 0 = the first act. The boss lives on the top row of each act, so gate bosses by **act**, not
  floor.
- `floor` is the node's row (0 = entry row). A `duel`/`elite` with `floor:{min:4}` only appears from
  row 4 up.
- `ascension` lets you reserve nastier decks for higher difficulty tiers.

---

## Overrides (LP, reward, first player)

Any encounter can override the per-type defaults:

```json
{ "id": "boss_exodia", "name": "The Forbidden One", "deck": "Exodia.json",
  "act": { "min": 2 }, "enemyLp": 13000, "reward": 4000 }
```

- `enemyLp` — absolute LP for this enemy. **Note:** an explicit value bypasses the per-act /
  ascension scaling that `Settings.json` applies to the type default. Omit it to keep that scaling.
- `reward` — flat run currency credited on win, replacing the type's default reward.
- `firstPlayer` — force the turn order (`0` you, `1` enemy). Omit for a seeded coin flip.
- `cpuRate` / `cpuFlag` — tune the opponent's AI. `cpuRate` is its strength (−100…100, default 100 =
  full); `cpuFlag` picks a behavior mode — `Fool` (plays badly), `Light` (weaker logic), `MyTurnOnly`
  (won't respond on your turn), `AttackOnly`, `Simple`, `Def` (defensive), or `None` (default full
  AI). Handy for making early/low-tier duels easier without changing the deck.

---

## Modifiers (starting board)

`modifiers` scripts the **opening board** of a duel — cards already on the field / in hand for each
side — plus small **LP/hand deltas**. Two sides: **`player`** (you) and **`enemy`** (this opponent).

```json
"modifiers": {
  "enemy": {
    "fieldSpell": { "cid": 30241314 },
    "monsters":   [ { "cid": 5381, "pos": "atk" }, null, { "cid": 22222, "pos": "def" } ],
    "spellTraps": [ { "cid": 44095762, "pos": "set" } ],
    "hand":       [ { "cid": 55144522 } ],
    "extraLp":    2000,
    "extraHand":  1
  },
  "player": { "extraHand": -1 }
}
```

- **`fieldSpell`** — one card, placed face-up active.
- **`monsters`** — slots 0–4 (M1–M5); `null` skips a slot. `pos`: `atk` / `def` / `set` (face-down
  defense) / `facedown` / `atk_fd` / `def_fd` (default `atk`).
- **`spellTraps`** — slots 0–4 (S1–S5). `pos`: `set` (face-down, default) / `face_up`.
- **`hand`** — extra cards placed in hand.
- **`extraLp`** / **`extraHand`** — additive deltas (may be negative). They **sum across layers** and
  apply on top of the base LP (`enemyLp` / your run LP) and base hand (5 Normal / 4 Rush) — they do
  not replace those. There is no separate hand-size key; use `extraHand`.

`cid` is the card's internal id — the same numbers used in deck `ids`. Cards past the 5 slots are
ignored; an unknown `cid` is silently skipped by the engine.

Modifiers merge with per-type defaults from `Settings.json` (`modifierDefaults`): **defaults first,
then the encounter on top** — cards merge by slot, `extraLp`/`extraHand` sum.

> Random cards (e.g. "a random Effect monster from the deck") are planned for a later phase — for now
> use pinned `cid`s.

---

## Defaults & relationship to `Settings.json`

`Encounters.json` decides **which deck** you fight; `Settings.json` still provides the **defaults**
for anything an encounter doesn't override:

- enemy LP → `Settings.enemyLp[type]` (with `perAct` / `perAscension` / `ascensionScale` applied),
- reward → the built-in per-type amount,
- turn order → a seeded coin flip,
- CPU AI → `Settings.cpuRate` (100 = max) and `Settings.cpuFlag` (None = full default AI).

So the simplest encounter — just `id` + `deck` (+ optional `name`) — inherits the difficulty curve
you already tuned in `Settings.json`. Reach for overrides only when a specific enemy needs to break
from the curve.

---

## Strict coverage — avoiding soft-locks

Because there is no random fallback, **every combat node your map can generate must have at least one
eligible encounter**. Practical rules:

- Provide several **ungated** `duel` encounters — they cover every floor of every act (floor 0 is
  always a duel).
- Provide `elite` encounters that cover wherever elites can spawn (see `Settings.typeRules.elite`).
- Provide a `boss` for **every act index** `0 .. acts-1`. A boss with an open-ended `act` (e.g.
  `{ "min": 2 }`) conveniently covers that act and all later ones.

If a node finds nothing eligible, the server logs e.g.
`[Roguelike] no encounter for type elite act 1 floor 5 asc 0` (or `no boss encounter for act N`),
and the node won't start a duel. Watch the server console the first time you run a new pool.

---

## Full example

```json
{
  "duel": [
    { "id": "duel_beatdown", "deck": "Beatdown.json" },
    { "id": "duel_warrior",  "deck": "Warrior.json" },
    { "id": "duel_zombie",   "deck": "Zombie.json" },
    { "id": "duel_stall",    "name": "Stall Tactician", "deck": "Final Countdown.json",
      "floor": { "min": 4 }, "firstPlayer": 1 }
  ],

  "elite": [
    { "id": "elite_goat_control", "name": "Goat Master", "deck": "Goat Control.json", "enemyLp": 5000 },
    { "id": "elite_monarch",      "deck": "Monarch.json" },
    { "id": "elite_chaos_turbo",  "deck": "Chaos Turbo.json" }
  ],

  "boss": [
    { "id": "boss_a1_chaos",  "name": "Lord of Chaos",  "deck": "Chaos Control.json",       "act": { "max": 0 } },
    { "id": "boss_a2_ritual", "name": "The Ritual Lord", "deck": "Relinquished Control.json", "act": { "min": 1, "max": 1 } },
    { "id": "boss_a3_exodia", "name": "The Forbidden One", "deck": "Exodia.json",
      "act": { "min": 2 }, "enemyLp": 13000, "reward": 4000 }
  ]
}
```

This pool: three ungated duels (cover every floor) plus a deep-floor stall that goes first; three
elites (one tankier); and one boss per act, with the act-3 boss tankier and worth more — every other
LP/reward value comes from `Settings.json`.
