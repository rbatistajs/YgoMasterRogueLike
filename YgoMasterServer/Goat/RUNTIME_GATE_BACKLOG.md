# Runtime Gate — Backlog

Living doc for the runtime-gate feature. Trim entries as they ship,
add new ones as they surface. Order within a phase isn't strict;
group by area.

---

## Shipped (MVP)

- [x] Per-player runtime-gate generator (`RuntimeGateGenerator.cs`)
- [x] Persistent storage at `Players/<code>/RuntimeGates/<gateId>.json`
- [x] Regen on boss-clear (next `Solo.info` detects + rebuilds)
- [x] Solo.json injection (mutates global — single-player only — see notes)
- [x] Reward registration per chapter set id (`Solo.reward[<id>]`)
- [x] Hooks: `Act_SoloInfo`, `Act_GateEntry`, `GetSoloDuelSettings`
- [x] **Single source of truth**: `DataLE/GridGates.json` (Python builder
      writes, C# server reads — same file). Entries flagged
      `runtime: true` get per-player regen.
- [x] CLI `add --runtime` + `edit --runtime/--no-runtime` flag — same
      pipeline as baked gates otherwise.
- [x] Python `_run_gen` branches: runtime entries skip the baked
      chapter/SoloDuel pipeline and clean up any pre-existing baked
      output (so toggling baked→runtime wipes the orphaned files).
- [x] GUI checkbox **Runtime gate** in `GateEditDialog` — no UI change
      beyond the checkbox; same form fills the entry, flag persisted.
- [x] DuelSettings hydration parity with `LoadSoloDuels` (Deck IDs,
      `SetRequiredDefaults`, cosmetics, life, hnum)
- [x] Storage round-trip without `ToDictionary` (avoid 4-slot Deck padding)
- [x] IDS_SOLO pre-allocation (99 chapters per runtime gate)
- [x] Cover-card entry in `SoloGateCards.txt`
- [x] `ResponseDebugDumper` tool (off by default; flip on for diffs)

---

## Phase 2 — feature parity with baked gates

### Modifiers in runtime config — shipped
- [x] Python precomputes templates per chapter type from
      `generic_params.modifier_defaults`, stashes on the GridGates
      entry under `runtime_templates` (uses the existing
      `_modifiers.apply_modifiers` pipeline).
- [x] Server attaches the template (cmds / random_specs / life / hnum)
      to each generated chapter by type. Boss gets `boss` template,
      others get `duel`.
- [x] `RuntimeRandomResolver` resolves the negative-cid markers per
      duel — fresh card picks every session even though the cmds
      layout is template-stable.
- [ ] Per-chapter overrides (post-MVP — currently every duel-type
      chapter shares the same template; only boss differs).

### Layout variety — shipped (C# generators)
- [x] Python precomputes the layout for any format
      (hourglass / dungeon / tower / manual) and stashes chapter dicts +
      per-chapter metadata. *(Superseded — layout now generated in C#
      per-call; Python keeps the helpers only for the local
      `--resolve-chapter` preview path.)*
- [x] **Format generators ported to C#** under
      `YgoMaster/Goat/Layout/`: HourglassGenerator, DungeonGenerator,
      TowerGenerator, ManualGenerator + shared LayoutNode / LayoutPresets
      / PostProcessors (assign_types / setup_progression / assign_levels
      / assign_levels_by_curve). LayoutGenerator.Generate is the entry
      point — RuntimeGateGenerator.BuildLayout calls it per (player,
      regen). Pool field on GridGates entries is gone.
- [x] Server iterates generated chapters, picks per-player decks +
      attaches type-appropriate modifier template.
- [x] Falls back to built-in linear chain when format isn't one of
      hourglass / dungeon / tower / manual (e.g. `linear`).
- [x] **True per-player unique layouts**: every (player, regen) call
      builds a brand-new layout with a fresh seed. No pool, no
      duplicates across players (Random source is
      `DateTime.UtcNow.Ticks`).

#### Notes / follow-ups on the C# port
- [ ] `_precompute_runtime_layout` in `build_grid_gate_procedural.py`
      is now legacy — kept for the GUI `--resolve-chapter` preview
      path but the runtime emit no longer calls it. Confirm preview
      still works after this change, otherwise migrate the preview to
      shell out to C# (or drop preview entirely).
- [ ] Random sequences differ between Python's Mersenne Twister and
      C#'s `System.Random` for the same seed. We don't claim parity —
      same params produce *similar-shape* maps but exact placement
      differs. Document this if anyone tries to repro a Python preview
      against a server gen.
- [ ] Dungeon generator uses Box-Muller for gauss; Python uses
      `random.gauss`. Different sequence but same distribution shape;
      noted in code.
- [ ] `force_special` / `modifiers_override` / `gems_override` /
      `card_rewards_override` from the Python `Node` are NOT ported —
      they're only relevant to baked emit. If we ever want per-cell
      overrides honored at runtime, expand `LayoutNode` + read them
      from manual cells.

### Tier / difficulty scaling
- [ ] `tier_range` (e.g. `[3, 6]`) drives per-chapter deck-pool tier
      (currently every chapter uses the single `deck_pool`).
- [ ] Difficulty curve by row (basic / easy / default / brutal / custom),
      mirroring the baked `generic_params.difficulty_curve`.
- [ ] `boss_deck_pool` / `boss_tier` override (boss harder than path).

### Cosmetics
- [ ] Per-deck boss portrait lookup via `info/boss_portraits.json`
      (currently uses fixed default for `p1_img`/`p2_img`).
- [ ] Randomized `mat` / `sleeve` / `avatar` / `icon` (Python builder
      cycles through cosmetic presets; runtime uses vanilla defaults).
- [ ] Optional `cover_card` randomization per gate (pick a representative
      cid from the first chapter's deck).

### Rewards
- [ ] Scaled gems per chapter type (boss > elite > duel > etc.).
- [ ] Optional card reward for boss (drop one cid from the boss's deck).
- [ ] Per-tier multipliers.

---

## Phase 3 — GUI polish

- [ ] Runtime-params editor in `GateEditDialog`: `chapter_count`,
      `chapter_id_base`, `deck_pool` (combo), `layout` (combo). Currently
      only the on/off toggle exists; everything else comes from defaults
      or hand-edited `RuntimeGates.json`.
- [ ] Boss / elite / reward-count knobs (mirror baked gates' generic params).
- [ ] Preview pane showing the chapter list a fresh seed would produce
      (helpful when iterating on params).
- [ ] Show **R** marker in `cmd_list` output for runtime gates
      (currently prints `format` = `"runtime"`).

---

## Phase 4 — server-side architecture

- [ ] **Multi-player**: SoloData mutation is process-global. If two
      players share the same runtime gate they'll fight over the
      injected chapter dict. Either clone-on-request (per-call deep
      copy of `Solo.chapter` + `Solo.gate`) or move runtime chapters
      to a per-player lookup path GetChapterSetIds also consults.
- [ ] Confirm `Solo.gate_entry` regen path actually fires when a player
      has cleared the boss and re-enters the gate (logged the path but
      no end-to-end test yet).
- [ ] Handle `dllReady == false` gracefully in `RuntimeGateGenerator`
      (today, deck-pool selection works without it; only filter step
      is affected — verified safe).

---

## Bugs / unknowns

- [ ] **"Emprestado" tab still shows on runtime chapter detail** even
      with `set_id = 0`. Same chapter dict shape as gate-20 baked
      chapters — needs side-by-side compare of `Solo.detail` dumps to
      pinpoint the differentiating field. Dumper is in place, just
      need to flip it on and capture both flows.
- [ ] **Play end-to-end not yet confirmed** — user got past chapter
      detail; need a full duel run + result hand-off (`Duel.end`).
- [ ] **Boss completion → regen on re-entry** — code path exists
      (`IsBossCleared` check in `EnsureGeneratedForPlayer`) but not
      yet verified live.

---

## Tooling / housekeeping

- [ ] Move pre-populated IDS_SOLO runtime block out of `IDS_SOLO.txt`
      (currently a manual block at the bottom). Either patch via the
      builder's IDS rewrite pass so it's regenerated alongside baked
      gates, or generate-on-demand per runtime gate.
- [ ] Document the runtime-gate flow in `Goat/README.md` (modules,
      hooks, debug commands, troubleshooting checklist).
- [ ] Add a unit-ish test or `--resolve-runtime` CLI that exercises the
      generator without spinning up the full server, similar to
      `--resolve-chapter` for baked SoloDuels.

---

## Open questions

- Should runtime regen also clear stale `Players/<code>/RuntimeGates/<id>.json`
  files when a gate is deleted/recreated, or rely on the cache TTL /
  per-boot fresh-load to overwrite?
- IDS allocation: 99 chapters per runtime gate is fine for MVP but
  long-term we'll want per-gate ranges that don't collide. Reserve
  `<gateId> * 10000 + 1..99`? Document in the Goat README.
- For Phase 2 modifiers: do we want gate-wide modifier defaults +
  per-chapter overrides (like Python `modifier_defaults` +
  `chapter_overrides`)? Or just gate-wide for runtime?
