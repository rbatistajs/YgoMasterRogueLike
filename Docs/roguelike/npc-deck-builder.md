# NPC Deck Builder — `--create-deck`

**Goal:** Server CLI helper that generates weak NPC decks for `DataLE/Roguelike/Opponents/`,
filtered by element / monster type (and, with caveats, genre / link), filling a rarity budget.

**Status:** Implemented. Element/type/rarity/combine/batch validated working. Genre and link are
present but limited (see Findings).

## Usage

Run from the install root (loads the duel.dll, same as `--cpucontest`):

```
YgoMaster.exe --create-deck <filters> [--rarity 30,7,2,1] [--regulation <id>] [--name <file>]
  --element <ATTR>          # LIGHT WATER FIRE WIND EARTH DARK DIVINE (or raw int)
  --type <RACE>            # Dragon Spellcaster Warrior ... (or raw int)
  --named <id>            # archetype (CARD_Named); ids discovered via --dump-named
  --link <cardId> [--link-depth N]   # related cards (Deck-Editor Related Cards, via duel.dll)
  --regulation <id>       # exclude cards banned (a0) in that Regulation.json entry; respect a1/a2 limits; tag the deck
  --count N               # generate N variants per deck in one run (each a fresh random draw)
  --all-elements          # batch: one deck per attribute
  --all-types             # batch: one deck per monster type
  --dump-cards            # diagnostic -> _tmp/cards.txt (validate attr/type numbering)
  --dump-named            # diagnostic -> _tmp/named.txt (archetype id -> members)
  --card-links <id>       # diagnostic: a card's related list (console), via duel.dll
```

Filters **combine with AND** (intersection): `--element DARK --type Dragon` = DARK Dragons;
`--named 9 --element DARK` = DARK Elemental HEROes. Output name auto-derives from filters
(`DARK_Dragon.json`, `Archetype21.json`, `Related_<seed>.json`); `--name` overrides
(e.g. `--named 21 --name Harpie`).

**Never overwrites:** if the target file exists, the next free number is used (`LIGHT.json` →
`LIGHT_2.json` → `LIGHT_3.json` …), so re-running accumulates variants. `--count N` makes N at once
(e.g. `--all-elements --count 5` → LIGHT, LIGHT_2..LIGHT_5, DARK, DARK_2..DARK_5, …), each a
different random deck — handy for opponent variety.

## How it works

- **Card universe + rarity:** `CardList.json` (`CardRare`): `{cardId: rarity}` (1=N,2=R,3=SR,4=UR).
- **Metadata:** the duel.dll via `DuelSimulator.InitForCardQueries()`, then `GetAttribute`,
  `GetRace`, `GetFrame`, `HasGenre`, `GetLinkedCards`. Names come from `YdkHelper.LoadCardDataFromGame`.
- **Pool:** every card in `CardRare` passing all active filters AND main-deck playable (excludes
  Fusion/Synchro/Xyz/Link/Token frames).
- **Build:** pure-random fill per rarity tier (up to 3 copies each), then top up any deficit from
  the remaining pool. 40-card main deck. If the pool has < ~14 distinct cards it warns and makes a
  smaller deck instead of over-duplicating.
- **Output:** `DeckInfo.Save` (canonical player-format JSON the Opponents loader round-trips).

## Enum numbering (important)

The duel.dll / `CARD_Prop` attribute & type values are **NOT** the standard MD "Content" enum.
Derived empirically via `--dump-cards` (e.g. Blue-Eyes = attr 1 / type 1):

- **Attribute:** 1=LIGHT, 2=DARK, 3=WATER, 4=FIRE, 5=EARTH, 6=WIND, 7=DIVINE.
- **Type:** 1=Dragon, 2=Zombie, 3=Fiend, 4=Pyro, 5=SeaSerpent, 6=Rock, 7=Machine, 8=Fish,
  9=Dinosaur, 10=Insect, 11=Beast, 12=BeastWarrior, 13=Plant, 14=Aqua, 15=Warrior, 16=WingedBeast,
  17=Fairy, 18=Spellcaster, 19=Thunder, 20=Reptile, 21=Psychic, 22=Wyrm, 23=Cyberse.

`--element`/`--type` accept the name (case-insensitive) or the raw int. `--dump-cards` is the
ground truth if a value ever looks off.

## Regulation (`--regulation <id>`)

`Regulation.json` is keyed by regulation id; each has `available: { a0, a1, a2, a3 }` where
**a0 = banned (0 copies)**, a1/a2/a3 = max 1/2/3 copies (per `DeckInfo.cs`). With `--regulation`:
- cards in a0 are excluded from the pool entirely;
- per-card copies are capped at `min(3, regulation limit)` (a1 → max 1, a2 → max 2);
- the saved deck's `regulation_id` is set to `<id>`.

Cards not listed in the regulation are unconstrained (treated as 3). Verified: `--type Dragon
--regulation 2005` narrowed the pool 242 → 91 and produced a 40-card deck with 0 banned cards,
tagged `regulation_id: 2005`. Without `--regulation`, decks are untagged (`regulation_id: 0`).

## Archetypes (`--named <id>`)

`CARD_Named` is the **archetypes** file (loaded via `DLL_SetCardNamed`); membership is queried
with `DLL_CardCheckName(cardId, nameType)`. `--dump-named` probes nameType 1..2047 and lists each
group's count + example cards, so archetype ids are identifiable (e.g. 1=Toon, 3=Gravekeeper's,
6=Amazoness, 9=Elemental HERO, 18=Ancient Gear, 21=Harpie, 45=Exodia, 47=Cyber Dragon). Verified:
`--named 21` produced a 40-card Harpie deck (Harpie Lady 1/2/3, Cyber Harpie, Harpie's Pet Dragon,
Harpie Lady Sisters, Channeler...). Archetypes range ~1–39 cards in this pool; small ones make a
smaller deck (graceful), big ones (Harpie/HERO/Skull) fill 40.

## Related cards (`--link <cardId>`)

The Deck-Editor **Related Cards** ARE exposed by the duel.dll via `DLL_CardGetLinkCards` — with two
catches that initially hid it:
1. Do **not** gate on `DLL_CardGetLinkNum`: that's the Link-**Monster** rating (0 for non-Link
   cards). Call `DLL_CardGetLinkCards(cardId, buf)` directly; its **return value** is the count of
   related ids.
2. The buffer packs **two 16-bit card ids per int** (lo16 then hi16) — unpack them.

`DuelSimulator.GetRelatedCards` does this (deduped). `--link <seed>` restricts the pool to the
seed's related set (BFS, `--link-depth` levels, default 1); `--card-links <id>` prints one card's
related list. Verified: `--link 3800` (Blue-Eyes) → a tight Blue-Eyes deck (Blue-Eyes White Dragon,
Paladin of White Dragon, Burst Stream of Destruction, Kaibaman, Engine of Destruction; the dll's
list also includes Maiden/Sage/Priestess with Eyes of Blue — which a name-substring filter would
miss). Only the related cards present in the playable pool (`CardRare`) are used.

(For reference: `CardData/MD/CARD_Link.bytes` is decrypted+decompressed — entropy 7.55 vs the raw
compressed asset's 8.00 — and stores the same `(cardId<<16)|relatedId` packing, but using the dll
is cleaner than parsing it.)

## Dead end (not used)

- **Genre** (`DLL_CardIsThisCardGenre`): broad categories, not archetypes — all 512 ids had
  305–2303 members each (one lumped Dark Magician + Blue-Eyes). Superseded by `--named` (clean
  archetypes) and `--link` (related cards). Not exposed by the builder.

## Files

- `YgoMasterServer/Roguelike/RoguelikeDeckBuilder.cs` — all builder logic (isolated).
- `YgoMasterServer/Sim/DuelSimulator.DllContent.cs` — public card-query getters
  (`InitForCardQueries`, `GetAttribute`/`GetRace`/`GetFrame`/`GetLevel`/`HasGenre`/`GetLinkedCards`).
- `YgoMasterServer/GameServer.State.cs` — `--create-deck` arg case.

## Verification (done)

- `--type Dragon` → 40 cards from a 242 pool; rarity exactly 30 N / 7 R / 2 SR / 1 UR.
- `--element DARK --type Dragon` → pool narrows to 63 (AND works); 40 cards.
- Output JSON has m/e/s → `DeckInfo.Load` round-trips for opponents.
- `--dump-cards` (4677 cards) and `--dump-genres` (512 genres) run without error.
