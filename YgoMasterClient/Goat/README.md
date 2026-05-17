# Goat — custom hooks for the YgoMasterLE - Goat install

Everything in this folder is **not** part of upstream pixeltris/YgoMaster.
It's project-specific code for the LE-Goat fork.

## Convention

- One `.cs` file per hook / feature, named after what it does
  (e.g. `SoloDeckRegulationFilter.cs`, `SoloGateScrollEnabler.cs`).
- Files live in the `YgoMasterClient` namespace (no nested namespace) so
  `Program.cs` can register them with `nativeTypes.Add(typeof(<Name>))`
  without extra `using` lines.
- Each file owns its IL2CPP reflection state (class/field/method lookups)
  in its own static ctor. Errors during init are funneled to
  `Utils.LogWarning` so a missing field doesn't take the whole client
  down.
- No `Goat.*` sub-namespace yet — keeping things flat. Revisit if the
  folder grows past ~20 files.

## Registering a new hook

1. Drop the `.cs` file here.
2. Add it to `YgoMasterClient.csproj` under the `<ItemGroup>` of
   `<Compile Include=... />` lines (alongside the other `Goat/*.cs`
   entries).
3. Add `nativeTypes.Add(typeof(MyHook));` to `Program.cs` so the
   `RuntimeHelpers.RunClassConstructor` loop fires its static ctor.
4. Rebuild via `Build.bat` (or MSBuild directly) and copy the new
   `YgoMasterClient.exe` over the install.

## Why a separate folder

Easy to:
- See at a glance what's ours vs upstream (`ls YgoMasterClient/Goat/`).
- Cherry-pick / re-apply when rebasing on a fresh pixeltris pull.
- Skip / disable our customizations as a unit if needed (delete the
  folder + csproj lines + Program.cs registrations).

## Current hooks (in this folder)

| File | Purpose |
|---|---|
| `SoloDeckRegulationFilter.cs` | Filters Solo deck-select list to decks whose RegulationId matches the active gate's `regulation_name` (from `Solo.deck_info.regulation`). |
| `SoloGateScrollEnabler.cs` | Re-anchors `ChapterMap` to top, extends its `sizeDelta.y`, and flips `ExtendedScrollRect.dragScrollEnabled` + `ScrollRect.vertical`/`movementType=Clamped` so gates with >3 lanes (e.g. lower-tier Goat) are drag-scrollable vertically. Invoked from `SoloVisualNovelChapterView.OnCreatedView` (one-line callout) because the detours backend only allows one Hook<> per method. |
| `SoloGateGridLayout.cs` | Overrides Konami's automatic chapter layout when a chapter declares `grid_x` / `grid_y` in `Solo.json`. Resizes the `ChapterMap` to fit, repositions each chapter (anchor-corrected from chapter's `(0.5, 0.5)` anchor), then re-aims each existing edge in `RootEdges` to span its corresponding parent→child pair as an axis-aligned rectangle (H/V only — diagonals not supported). |

## Goat customizations that DIDN'T fit the convention

Inline edits to upstream files. Recorded here so we know where to look
when rebasing on a fresh pixeltris pull.

### Client (`YgoMasterClient/`)

| Upstream file | What's ours | Tracked in commits |
|---|---|---|
| `DuelStarter.cs` | `PvpInjections` nested class + Room_room_create hook to inject `room_settings.duel_type`; UI button injection in `RoomCreateViewController.OnCreatedView` / `CallAPIRoomCreate` | `18de039`, `b5ab6d7`, `f959e61`, `906b01c` |
| `ActionSheetViewController.cs` | New `TryOpenRadio` helper used by the Duel Type dropdown | `f959e61` |
| `ClientSettings.cs` | PvP DuelType-related settings keys | `18de039` |
| `SoloVisualNovelChapterView.cs` | One-line callout to `Goat/SoloGateScrollEnabler.OnCreatedView` (workaround for single-hook-per-method constraint) | — |
| `TradeUtils.cs` | One-line callout to `Goat/SoloGateScrollEnabler.Update` from the `NetworkMain.Update` dispatcher (per-frame re-apply of `dragScrollEnabled` because Konami resets it after our OnCreatedView hook) | — |

### Server (`YgoMasterServer/`)

| Upstream file | What's ours | Tracked in commits |
|---|---|---|
| `Acts/Act_Solo.cs` | `regulation_name` lookup in `UpdateSoloDeckValidation` (already upstream, kept) | `027d1c2` |
| `GameServer.Writers.cs` | `regulation` field in `Solo.deck_info` (mirrors `Act_Room`) | `027d1c2` |
| `Acts/Act_Room.cs` | Reads `room_settings.duel_type` from create payload | `18de039` etc. |
| `Pvp.cs` (or equiv.) | Hand/life defaults by DuelType | `c853441`, `b5ab6d7` |
| `Sim/CpuContest.cs`, `Sim/DuelSimulator.cs`, `Sim/MatchmakingStrategy.cs` | `Utils.SafeReadAllText`/`SafeWriteAllText`/`SafeFileCopy` (`RetryIO` wrappers) | `30ff639` |
| `Utils.cs` | `RetryIO`/`SafeReadAllText`/`SafeWriteAllText`/`SafeFileCopy` helpers | `30ff639` |

Going forward, **all new client-side features land in this folder** as
standalone files. Inline upstream edits should be limited to one-line
hooks calling out to a new `Goat/<Feature>.cs` module, so the convention
stays clean.
