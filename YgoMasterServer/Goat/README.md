# Goat — server-side customizations

Code isolated from the upstream YgoMaster server. Upstream files only
get tiny callouts (1-line additions) to invoke logic that lives here.

## Modules

- **`DuelSettings.Goat.cs`** — `partial class DuelSettings` adding the
  `random_specs` and `regulation_name` fields, plus a `SetCmds` helper
  (cmds has `private set;` upstream; the resolver needs to compact the
  outer array after dropping unresolvable cmds).

- **`DuelDllProps.cs`** — `[DllImport]` wrappers around `duel.dll`'s
  `DLL_CardGetAtk` / `DLL_CardGetDef` / `DLL_CardGetKind` / etc., plus
  a `LoadAllCardData(cardDataDir)` that calls the six setters required
  before any `DLL_CardGet*` is safe (CARD_IntID is critical — without
  it, every CardGet* AVs).

- **`RuntimeRandomResolver.cs`** — at solo duel start, walks
  `DuelSettings.cmds`; for every negative cid marker, picks a real cid
  matching the corresponding `random_specs[abs(cid)]` filters.
  - `source=deck` (+ `deck_owner=own|rival|p1|p2`) draws from the
    relevant player's deck.
  - `source=any` draws from the duel's regulation pool (CardList.json
    minus the regulation's `available.a0` forbidden list).
  - Cards are categorized via `CardKind` → `CardCategory`
    (MainDeckMonster, ExtraDeckMonster, Spell, Trap, Token, Other).

- **`GameServer.Goat.cs`** — boot hook that calls
  `RuntimeRandomResolver.Init()` after `LoadSoloDuels()` and dispatches
  Goat-specific CLI args.

## CLI args (Goat)

Dispatched after `RuntimeRandomResolver.Init` so DLL queries are safe.

- **`--card-info <cid>`** — dumps every prop the resolver reads for
  that cid (kind, category, atk/def, level, attr, icon, matching
  subtypes). Useful for confirming why a card was accepted/rejected
  from a random pool. Multiple `--card-info <cid>` can be chained.

  ```
  YgoMaster.exe --card-info 4011 --card-info 5210
  [card 4011]
    loaded:   True
    kind:     Normal (0)
    category: MainDeckMonster
    atk/def:  1000/500
    level:    3
    attr:     2
    icon:     0 (normal)
    subtypes: normal
  ```

## Upstream changes (kept minimal)

- `DuelSettings.cs`: `class DuelSettings` → `partial class DuelSettings`
- `GameServer.State.cs:LoadSettings` after `LoadSoloDuels()`: 1 line
  invoking `InitGoat()`.
- `Acts/Act_Duel.cs:CreateSoloDuelSettingsInstance` after the existing
  `CopyFrom` call: 1 line invoking `RuntimeRandomResolver.OnSoloDuelStart`.

Everything else lives here.
