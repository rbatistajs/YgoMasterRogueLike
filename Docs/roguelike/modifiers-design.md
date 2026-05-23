# Duel Modifiers — Design

**Goal:** Port the upstream Goat "modifiers" system to the roguelike so a duel can start with a
**scripted board** (cards on field / in hand, per side) plus **LP/hand deltas**, configured per
encounter and via global/per-act/per-ascension defaults — and reusable later as the substrate for a
player **relics** system. Modifiers compile to the engine's `cmds` (the same `DLL_DuelComCheatCard`
pipeline the client already runs).

## Background — the mechanism already exists

- **`DuelSettings.cmds`** (`List<int>[]`, serialized) carries placement commands. The client hook
  **`EngineInitializer.InitEngine`** reads `$.Duel.cmds` after the engine inits and applies each:
  `[0, player, position, index, cid, prm, df]` → `DLL_DuelComCheatCard(...)` (place a card).
- **Path is fully wired for our duel:** `BuildRoguelikeDuel` ships `duelStarterData = ds.ToDictionary()`
  → `Act_DuelBegin` does `FromDictionary` + `$.Duel = ToDictionary()` (incl. `cmds`) → `InitEngine`
  applies. So we only need to **populate `cmds`** server-side; no client change.
- Upstream `YgoMaster/YgoMasterServer/Goat/` has the full encoder (`Modifiers/ModifierApplier.cs`),
  the random resolver (`RuntimeRandomResolver.cs`), and `DuelSettings.Goat.cs` (`SetCmds`,
  `random_specs`, `regulation_name`). We port these (adapted) — they were stripped when the
  roguelike was branched off clean upstream.

## Config schema

A `modifiers` object with **`player`** (duel index 0) and **`enemy`** (index 1). Each side:

```jsonc
"modifiers": {
  "enemy": {
    "fieldSpell": { "cid": 30241314 },                       // 1 card, face-up active
    "monsters":   [ { "cid": 77585513, "pos": "atk" }, null, // slots 0..4 (M1..M5)
                    { "cid": 22222, "pos": "def" } ],
    "spellTraps": [ { "cid": 44095762, "pos": "set" } ],     // slots 0..4 (S1..S5)
    "hand":       [ { "cid": 55144522 } ],                   // extra cards in hand
    "extraLp":    2000,                                       // additive delta (can be negative)
    "extraHand":  1                                           // additive delta
  },
  "player": { "extraHand": -1 }
}
```

**Card spec:** `{ "cid": N }` (pinned — `cid` = the internal card id, same numbers as deck `ids`) or
a random marker (phase 2/3):
`{ "random": "monster", "subtype": "effect", "minAtk": 2000, "source": "deck", "deck_owner": "own" }`
(`random`: monster/main_monster/extra_monster/spell/trap/field_spell/spell_or_trap · `source`:
deck|any · `deck_owner`: own|rival). `pos` lives on the card for monsters/spellTraps.

`extraLp` / `extraHand` are **deltas summed across layers**, applied on top of our base values — NOT
resets. Base LP = run LP (player) / `enemyLp` (enemy); base hand = engine default (5 Normal / 4 Rush).
There is intentionally no separate `enemyHand` key — use `enemy.extraHand`.

### Attachment & merge

- **Per encounter** (`Encounters.json`): `modifiers` field on an encounter.
- **Defaults** (`Settings.json`): `modifierDefaults` keyed by node type; e.g.
  `"modifierDefaults": { "boss": { "enemy": { "extraLp": 2000 } } }`. Because it's a Settings key, the
  existing `Effective()` pipeline already lets `perAct` / `perAscension` override it (ascension
  handicaps for free).
- **Relics** (future): a player-targeted layer pulled from run state.

Merge order (low → high), per upstream `MergeModifiers`:
```
Settings.modifierDefaults[nodeType]   (already scaled via perAscension/perAct)
        ↓
Run relics (player)                   (future)
        ↓
Encounter.modifiers
```
Per-field merge: `fieldSpell` overrides; `monsters`/`spellTraps`/`hand` merge by index (null =
defer); `extraLp`/`extraHand` sum; `graveyard` dropped (engine has no grave/banish placement).

## Encoding (`cmds`)

One `[0, player, position, index, cid, prm, df]` tuple per placed card (→ one `DLL_DuelComCheatCard`).

Positions (confirmed in-game upstream): `M1..M5 = 0..4`, extra monster `5..6`, `S1..S5 = 7..11`,
field `12`, hand `13`, extra-deck `14`, deck `15`, grave `16`, banish `17`.

`prm` = face state (0 down / 1 up); `df` = defense flag (0 atk / 1 def). Orientation map:
`atk → (1,0)`, `def → (1,1)`, `set`/`facedown`/`def_fd → (0,1)`, `atk_fd → (0,0)`; spell/trap
`set → (0,0)`, `face_up`/`active → (1,0)`. Field spell always `(1,0)`. Hand uses an incrementing
index, `(0,0)`.

## Integration (`GameServer.Roguelike.cs`)

In `BuildRoguelikeDuel`, after `ds.life`/`ds.hnum` bases are set and `ds.ToDictionary()` is built:

1. Gather layers: `RoguelikeSettings.ModifierDefaults(eff, nodeType)`, then `encounter.Modifiers`.
   Remap config keys `player→p1`, `enemy→p2` for the encoder.
2. `merged = ModifierApplier.MergeModifiers(layers)`.
3. `ModifierApplier.Apply(dsDict, merged, baseLife, baseHand)` — writes `cmds` into `dsDict`;
   `life`/`hnum` = clamp(base + delta, ≥1), where `baseLife = { ds.life[0], ds.life[1] }` (run LP /
   enemyLp) and `baseHand = { default, default }` (5 Normal / 4 Rush). **Adapted from upstream**,
   which used a fixed 8000/5 base.
4. `duelDto["duelStarterData"] = dsDict`.

Determinism: pinned modifiers are static → resume-safe. Random (phase 2+) must use the duel seed
(`DuelRngSeed`) instead of a static `Random`, so a resumed duel re-rolls identically.

## What we port

- **`ModifierApplier.cs`** (`YgoMaster.Modifiers`) — depends only on `Utils`. Adapt: `Apply` takes
  base life/hand and adds deltas (no 8000/5 reset); keep `MergeModifiers`/encode as-is.
- **`DuelSettings.Goat.cs`** — `SetCmds` + `random_specs` + `regulation_name`. Needed for phase 2/3
  (random). Phase 1 only needs the `cmds` key round-tripping through `FromDictionary`/`ToDictionary`
  (verify the rogue fork's `FromDictionary` reconstructs `cmds`).
- **`RuntimeRandomResolver.cs`** — phase 2/3. Deps present in the fork (`DeckInfo.GetAllCards`,
  `RegulationIdsByName`, `CardKind`) except `DuelDllProps` (card-data DLL queries) needed only for
  spec filtering in phase 3.

## Phasing

- **Phase 1 — pinned + deltas:** `ModifierApplier` (cmds for pinned cids) + `extraLp`/`extraHand`
  deltas + config (`Encounters.modifiers`, `Settings.modifierDefaults`) + `BuildRoguelikeDuel` wire.
  No resolver, no `DuelDllProps`. Covers scripted boards/hands/field-spells and LP/hand handicaps.
- **Phase 2 — random (unfiltered):** port `RuntimeRandomResolver` + `DuelSettings.Goat`; seed it
  with the duel seed; pick from deck/regulation pool without atk/type filtering.
- **Phase 3 — random (filtered):** port `DuelDllProps` (card-data DLL load + `DLL_CardGet*`) for
  full `random`/`subtype`/`minAtk`… filtering.
- **Later — relics:** add a player-modifier layer sourced from run state, plus the reward flow that
  grants relics.

## Edge cases

- Modifiers absent on all layers → no `cmds`, LP/hand untouched (no-op).
- Bad/unknown `cid` → `DLL_DuelComCheatCard` no-ops on the client (no crash).
- `extraLp`/`extraHand` clamp the result to ≥ 1.
- More cards than slots (monsters/spellTraps > 5) → extra entries past the slot count are ignored.
- Random spec with no resolver (phase 1) → not supported; document "pinned only" until phase 2.
- `player`/`enemy` both optional; either may carry only deltas (no cards).

## Verification (no unit tests — IL2CPP)

1. Build server (client unchanged).
2. Author an encounter with `modifiers.enemy` placing a field spell + a face-up monster + a set
   trap, and `extraLp: 2000`.
3. Reach that node → confirm the enemy starts with that exact board and +2000 LP over `enemyLp`.
4. Add `Settings.modifierDefaults.boss.enemy.extraHand: 1` → confirm every boss draws +1.
5. `modifiers.player.hand: [{cid}]` → confirm you open with that card (relic dry-run).
6. Resume: enter the node, quit mid-duel, re-enter → identical board (pinned = deterministic).
