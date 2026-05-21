# Roguelike M2 (Deck Select) — Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or
> superpowers:executing-plans to implement task-by-task. Steps use checkbox
> (`- [ ]`) syntax.

**Goal:** Starting a `base_deck` run rolls **3 random decks** server-side; the player
**picks 1**, which is persisted as the run's deck in `roguelike.json`.

**Architecture:** Server owns the roll + choice (`YgoMasterServer/Roguelike/`). The deck
pool reuses the existing `DataLE/decks/normal/{0..6}/*.json` (player format), loaded with
`DeckPoolLoader.LoadOne`. The client reacts to the `start_run` response in the
`RequestStructure.Complete` hook (`DuelStarter.cs`) and opens an **ActionSheet** with the
3 deck names; tapping one issues `choose_deck`. No card art yet (deferred to M3 map UI).

**Tech stack:** C# (.NET Framework), MiniJSON, `DeckPoolLoader`, IL2CPP reflection,
`Request.Entry`, `ClientWork` (`GetByJsonPath`/`SerializePath`),
`ActionSheetViewController`, `CommonDialogViewController`.

**Verification reality:** IL2CPP mod injected into a running game — **no unit tests**.
"Verify" = build + launch server + game + read console / observe behavior.
- Client build (auto-copies to install): `MSBuild YgoMasterClient.csproj -t:Build -p:Configuration=Release -v:minimal -nologo`.
- Server build: `MSBuild YgoMasterServer/YgoMaster.csproj -t:Build -p:Configuration=Release -p:Platform=x64 -v:minimal -nologo`. Add `-p:GoatInstallDir=""` to skip the install-copy if `YgoMaster.exe` is running/locked.

**Reused facts (already verified this milestone):**
- `dataDirectory` is a field on `GameServer` (GameServer.State.cs:28); Act handlers in the
  `partial class GameServer` (`Roguelike/GameServer.Roguelike.cs`) can use it directly.
- `DeckPoolLoader.LoadOne(string fullPath)` → `LoadedDeck { string Name; Dictionary<string,object> SoloDuelDeck /* {Main,Extra,Side} */; int BossCard; }` (Goat/DeckPoolLoader.cs).
- `GetPlayerDirectory(player)` returns the player dir (where `roguelike.json` lives).
- Client completion hook: `DuelStarter.cs` `Complete(thisPtr)` switch (~line 419), with a
  `ProfileReplayViewController.OnNetworkComplete(thisPtr, cmd)` call right after (~line 442).
- `ClientWork.SerializePath("Roguelike.deckOffers")` returns the subtree as a JSON string
  (same call used for `"Duel"` at DuelStarter.cs:425) → `MiniJSON.Json.Deserialize` to a list.
- M1 menu lives in `RoguelikeHomeButton.OnMenuSelect`; `RoguelikeApi.Call(act, args)`
  already issues acts via `Request.Entry`.

---

## Task 1 — Server: extend `RoguelikeRun` model (deckChosen / deckOffers / deck)

**Files:** `YgoMasterServer/Roguelike/RoguelikeRun.cs`

- [ ] Add the three fields and round-trip them through `ToDictionary` / `FromDictionary`.
  `DeckOffers` is a `List<object>` of `Dictionary<string,object>`; `Deck` is a
  `Dictionary<string,object>` (or null). Keep JSON-friendly shapes (MiniJSON handles
  `List<object>` / `Dictionary<string,object>`).

```csharp
// add fields (after CreatedAt)
public bool DeckChosen;
public List<object> DeckOffers;            // each item: {name, bossCard, file}
public Dictionary<string, object> Deck;    // {name, bossCard, deck:{Main,Extra,Side}} or null
```

```csharp
// ToDictionary(): add these entries to the returned dict
{ "deckChosen", DeckChosen },
{ "deckOffers", DeckOffers ?? new List<object>() },
{ "deck", Deck },
```

```csharp
// FromDictionary(): add these to the constructed RoguelikeRun
DeckChosen = Utils.GetValue<bool>(d, "deckChosen", false),
DeckOffers = Utils.GetValue<List<object>>(d, "deckOffers"),
Deck       = Utils.GetValue<Dictionary<string, object>>(d, "deck"),
```

- [ ] Build server: `MSBuild YgoMasterServer/YgoMaster.csproj -t:Build -p:Configuration=Release -p:Platform=x64 -v:minimal -nologo -p:GoatInstallDir=""`. Expected: build succeeds.
- [ ] Commit: `feat(roguelike): extend run model with deck offers + chosen deck`

---

## Task 2 — Server: roll 3 offers in `start_run` + new `choose_deck` act + dispatch

**Files:** `YgoMasterServer/Roguelike/GameServer.Roguelike.cs`, modify `YgoMasterServer/GameServer.cs` (dispatch).

- [ ] Add a private deck-pool roll helper to the partial. It enumerates every `*.json`
  under `decks/normal/{0..6}`, picks up to 3 distinct files with a seeded RNG, and loads
  each (name + bossCard) via `DeckPoolLoader.LoadOne`. The `file` stored is **relative** to
  `dataDirectory` (portable across machines).

```csharp
// in partial class GameServer (Roguelike/GameServer.Roguelike.cs)
List<object> RollDeckOffers(int seed, int count)
{
    List<string> files = new List<string>();
    for (int level = 0; level <= 6; level++)
    {
        string dir = System.IO.Path.Combine(dataDirectory, "decks", "normal", level.ToString());
        if (!System.IO.Directory.Exists(dir)) continue;
        files.AddRange(System.IO.Directory.GetFiles(dir, "*.json"));
    }
    List<object> offers = new List<object>();
    if (files.Count == 0)
    {
        Console.WriteLine("[Roguelike] no starter decks under decks/normal/*");
        return offers;
    }
    Random rng = new Random(seed);
    // Fisher-Yates shuffle, then take the first `count`.
    for (int i = files.Count - 1; i > 0; i--)
    {
        int j = rng.Next(i + 1);
        string tmp = files[i]; files[i] = files[j]; files[j] = tmp;
    }
    int take = Math.Min(count, files.Count);
    for (int i = 0; i < take; i++)
    {
        try
        {
            DeckPoolLoader.LoadedDeck d = DeckPoolLoader.LoadOne(files[i]);
            if (d == null) continue;
            string rel = files[i].StartsWith(dataDirectory)
                ? files[i].Substring(dataDirectory.Length).TrimStart('\\', '/')
                : files[i];
            offers.Add(new Dictionary<string, object>
            {
                { "name", d.Name }, { "bossCard", d.BossCard }, { "file", rel },
            });
        }
        catch (Exception ex) { Console.WriteLine("[Roguelike] offer load EX: " + ex.Message); }
    }
    return offers;
}
```

- [ ] Evolve `Act_RoguelikeStartRun` to set `DeckChosen=false` and roll offers from the new
  run's seed (replace the existing method body):

```csharp
void Act_RoguelikeStartRun(GameServerWebRequest request)
{
    string gameType = "base_deck";
    if (request.ActParams != null)
        gameType = Utils.GetValue<string>(request.ActParams, "gameType", "base_deck");
    int seed = new Random().Next();
    RoguelikeRun run = new RoguelikeRun
    {
        Active = true,
        GameType = gameType,
        Seed = seed,
        CreatedAt = DateTime.UtcNow.ToString("o"),
        DeckChosen = false,
        DeckOffers = RollDeckOffers(seed, 3),
        Deck = null,
    };
    run.Save(GetPlayerDirectory(request.Player));
    request.Response["Roguelike"] = run.ToDictionary();
    request.Remove("Roguelike");
}
```

- [ ] Add `Act_RoguelikeChooseDeck` (new). Validates index + pending state, loads the chosen
  offer's deck file, stores it as the run's deck, marks chosen, clears offers:

```csharp
void Act_RoguelikeChooseDeck(GameServerWebRequest request)
{
    RoguelikeRun run = RoguelikeRun.Load(GetPlayerDirectory(request.Player));
    if (run.Active && !run.DeckChosen && run.DeckOffers != null)
    {
        int index = request.ActParams != null ? Utils.GetValue<int>(request.ActParams, "index", -1) : -1;
        if (index >= 0 && index < run.DeckOffers.Count)
        {
            Dictionary<string, object> offer = run.DeckOffers[index] as Dictionary<string, object>;
            string rel = offer != null ? Utils.GetValue<string>(offer, "file") : null;
            if (!string.IsNullOrEmpty(rel))
            {
                string full = System.IO.Path.Combine(dataDirectory, rel);
                DeckPoolLoader.LoadedDeck d = DeckPoolLoader.LoadOne(full);
                if (d != null)
                {
                    run.Deck = new Dictionary<string, object>
                    {
                        { "name", d.Name }, { "bossCard", d.BossCard }, { "deck", d.SoloDuelDeck },
                    };
                    run.DeckChosen = true;
                    run.DeckOffers = new List<object>();
                    run.Save(GetPlayerDirectory(request.Player));
                }
            }
        }
    }
    request.Response["Roguelike"] = run.ToDictionary();
    request.Remove("Roguelike");
}
```

- [ ] In `GameServer.cs` dispatch switch, add next to the existing `Roguelike.*` cases:
  `case "Roguelike.choose_deck": Act_RoguelikeChooseDeck(gameServerWebRequest); break;`
- [ ] Build server (same command as Task 1). Expected: build succeeds.
- [ ] Commit: `feat(roguelike): roll 3 deck offers on start + choose_deck act`

---

## Task 3 — Client: `RoguelikeApi` additions (deckChosen / offer names / choose_deck)

**Files:** `YgoMasterClient/Roguelike/RoguelikeApi.cs`

- [ ] Add three members. `GetDeckOfferNames` reads the offers subtree via
  `ClientWork.SerializePath` and extracts each `name`:

```csharp
// True when the player has finalized a deck for the active run.
public static bool IsDeckChosen()
{
    return YgomSystem.Utility.ClientWork.GetByJsonPath<bool>("Roguelike.deckChosen");
}

// Names of the 3 (or fewer) decks offered for the pending run.
public static string[] GetDeckOfferNames()
{
    try
    {
        string json = YgomSystem.Utility.ClientWork.SerializePath("Roguelike.deckOffers");
        if (string.IsNullOrEmpty(json)) return new string[0];
        List<object> list = MiniJSON.Json.Deserialize(json) as List<object>;
        if (list == null) return new string[0];
        List<string> names = new List<string>();
        foreach (object o in list)
        {
            Dictionary<string, object> item = o as Dictionary<string, object>;
            names.Add(item != null && item.ContainsKey("name") ? Convert.ToString(item["name"]) : "Deck");
        }
        return names.ToArray();
    }
    catch (Exception ex) { Console.WriteLine("[Roguelike] offer names EX: " + ex); return new string[0]; }
}

public static void ChooseDeck(int index)
{
    Call("Roguelike.choose_deck", new Dictionary<string, object> { { "index", index } });
}
```

- [ ] Build client. Expected: build succeeds.
- [ ] Commit: `feat(roguelike): client api — deckChosen, offer names, choose_deck`

---

## Task 4 — Client: deck-select reaction (`RoguelikeFlow`) + completion hook + menu wiring

**Files:** Create `YgoMasterClient/Roguelike/RoguelikeFlow.cs`; modify
`YgoMasterClient/DuelStarter.cs` (one call in `Complete`), `YgoMasterClient/Roguelike/RoguelikeHomeButton.cs`
(menu wiring), `YgoMasterClient.csproj` (Compile include).

- [ ] Create `RoguelikeFlow.cs`: opens the deck-select ActionSheet and reacts to act
  completions. Mirrors how `RoguelikeHomeButton` already calls `ActionSheetViewController`
  / `CommonDialogViewController` / `RoguelikeApi`.

```csharp
using System;

namespace YgoMasterClient
{
    // Run-flow reactions: opens the deck-select ActionSheet (3 deck names) and handles
    // the async responses of Roguelike acts (driven from DuelStarter's Complete hook).
    static class RoguelikeFlow
    {
        // Open the "choose 1 of 3" sheet from the current $.Roguelike offers.
        public static void OpenDeckSelect()
        {
            string[] names = RoguelikeApi.GetDeckOfferNames();
            if (names.Length == 0)
            {
                YgomGame.Menu.CommonDialogViewController.OpenAlertDialog("Roguelike",
                    "Nenhum deck disponivel no pool.", () => { });
                return;
            }
            YgomGame.Menu.ActionSheetViewController.Open("Escolha seu deck", names, OnDeckPicked);
        }

        static void OnDeckPicked(IntPtr ctx, int index)
        {
            RoguelikeApi.ChooseDeck(index);
        }

        // Called from DuelStarter.Complete for every completed network command.
        public static void OnNetworkComplete(string cmd)
        {
            if (cmd == "Roguelike.start_run")
            {
                OpenDeckSelect();
            }
            else if (cmd == "Roguelike.choose_deck")
            {
                string name = YgomSystem.Utility.ClientWork.GetByJsonPath<string>("Roguelike.deck.name");
                YgomGame.Menu.CommonDialogViewController.OpenAlertDialog("Roguelike",
                    string.IsNullOrEmpty(name) ? "Deck escolhido!" : ("Deck escolhido: " + name), () => { });
            }
        }
    }
}
```

- [ ] In `DuelStarter.cs` `Complete(thisPtr)`, add the Roguelike reaction right after the
  existing `ProfileReplayViewController.OnNetworkComplete(thisPtr, cmd);` line (~442):
  `YgoMasterClient.RoguelikeFlow.OnNetworkComplete(cmd);`
- [ ] Update `RoguelikeHomeButton.OnMenuSelect` so "Nova Run" only fires `start_run` (the
  sheet now comes from the completion), and "Continuar" resumes the selection when the deck
  is not yet chosen:

```csharp
static void OnMenuSelect(IntPtr ctx, int index)
{
    if (_menuRunActive)
    {
        if (index == 0) // Continuar Run
        {
            if (!RoguelikeApi.IsDeckChosen())
                RoguelikeFlow.OpenDeckSelect();
            else
                YgomGame.Menu.CommonDialogViewController.OpenAlertDialog("Roguelike",
                    "Run em andamento — o mapa vem no proximo milestone.", () => { });
        }
        else if (index == 1) // Abandonar Run
        {
            YgomGame.Menu.CommonDialogViewController.OpenYesNoConfirmationDialog("Abandonar Run",
                "Tem certeza? A run atual sera perdida.", () => { RoguelikeApi.AbandonRun(); }, () => { });
        }
    }
    else if (index == 0) // Nova Run
    {
        RoguelikeApi.StartRun(); // deck-select sheet opens on start_run completion
    }
}
```

- [ ] Add to `YgoMasterClient.csproj` (near the other `Roguelike\*` includes):
  `<Compile Include="Roguelike\RoguelikeFlow.cs" />`
- [ ] Build client. Expected: build succeeds.
- [ ] Commit: `feat(roguelike): deck-select sheet on start + choose wiring`

---

## Task 5 — End-to-end verification (Definition of Done)

Run the server (`YgoMaster.exe`), then launch the game. Use a player with **no**
`roguelike.json` (or abandon any existing run first).

- [ ] Home → ROGUELIKE → "Nova Run" → an ActionSheet titled "Escolha seu deck" lists 3
  deck names.
- [ ] `roguelike.json` now has `active:true`, `deckChosen:false`, `deckOffers` with 3 items.
- [ ] Pick a deck → toast "Deck escolhido: <name>"; `roguelike.json` now has
  `deckChosen:true`, `deck` populated (name/bossCard/deck), `deckOffers` empty.
- [ ] Reopen ROGUELIKE before choosing (start a fresh run, dismiss the sheet) → "Continuar
  Run" reopens the same selection.
- [ ] After choosing, "Continuar Run" shows the "Run em andamento" toast.
- [ ] "Abandonar Run" (confirm) → `roguelike.json` removed.
- [ ] Commit any fixups: `chore(roguelike): M2 verification fixups`

---

## Self-review notes
- **Spec coverage:** model (T1), roll + choose act + dispatch (T2), client api (T3),
  deck-select sheet + completion reaction + menu wiring (T4), end-to-end DoD (T5).
- **Type consistency:** `DeckOffers` is `List<object>` of `Dictionary<string,object>`
  everywhere; `Deck` is `Dictionary<string,object>` with `{name, bossCard, deck}`; the
  client reads `Roguelike.deckChosen` (bool), `Roguelike.deckOffers` (list, via
  `SerializePath`), `Roguelike.deck.name` (string).
- **Honest gaps (resolve during execution, not faked):** (1) confirm `MiniJSON.Json`
  (managed) is the right namespace in `RoguelikeApi.cs` (DuelStarter.cs uses it at :425);
  (2) confirm `ActionSheetViewController.Open` signature matches the M1 usage in
  `RoguelikeHomeButton` (it does — reused).
