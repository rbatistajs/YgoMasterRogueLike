# Roguelike Mode — Design

Data: 2026-05-20

Um modo roguelike de Yu-Gi-Oh dentro do YgoMaster (fork Goat). Lógica isolada em
módulos `Roguelike/` próprios (server e client), separada do `Goat/`.

---

## Visão

Botão "Roguelike" na Home → tela do modo → continuar a run salva ou começar nova.
Numa run:
- Escolher 1 de 3 decks (de uma pool nossa).
- Navegar um **mapa estilo dungeon** (grid próprio, sem a limitação do solo de "1 pai
  por node").
- Nodes de tipos variados (duelo / elite / boss / evento / loja / reward).
- Ganhar duelo dá recompensa: **booster** (escolher até 3 cartas pra pool da run) +
  **moeda da run** (substitui gemas; usada em loja/eventos).
- **Editor de deck** filtrado pela pool de cartas da run.
- **Boss final** destranca após vencer **2 de 3 elites**. Vence a run ao derrotá-lo.
- Persistência por player em `roguelike.json`.
- Haverá 2 tipos de jogo; começamos pelo **"deck base"** (escolhe 1 de 3 decks). O 2º
  tipo pluga no mesmo modelo depois.

## Decisões de arquitetura

- **Server dono do estado e da lógica.** A run (persistência, geração de mapa,
  rewards, progressão) vive no server, como o resto dos dados do jogador. Client = só
  UI, conversa via **Acts custom** (`Roguelike.*`).
- **Persistência em arquivo separado** `Players/<id>/roguelike.json` (não dentro do
  `Player.json`), auto-contido — não mexe no `Player.cs`/`GameServer.State.cs`.
- **UI custom via overlay nosso** (GameObject root parenteado no canvas), clonando
  prefabs/componentes do jogo. Telas 100% novas não têm prefab pro
  `PushChildViewController`, então o padrão do modo é overlay reutilizável.
- **Módulos isolados:** `YgoMasterServer/Roguelike/` e `YgoMasterClient/Roguelike/`.
  Únicos toques no core: rotear `Roguelike.*` no dispatch do `GameServer.cs`, e
  registrar as classes do client em `Program.cs` + `.csproj`.

## Roadmap (milestones)

| # | Subsistema | Depende de |
|---|---|---|
| **M1** ✅ | Fundação: modelo+persistência da run, Acts, botão Home, overlay de entrada (Nova/Continuar/Abandonar) | — |
| **M2** ✅ | Início de run: pool de decks iniciais + escolher 1 de 3 | M1 |
| M3 | Mapa: grid próprio + UI do mapa + tipos de node + navegação | M1, M2 |
| M4 | Duelo + reward básico: iniciar duelo do node, vitória/derrota, moeda, avançar | M3 |
| M5 | Booster + pool de cartas: abrir pacote, escolher até 3 cartas, salvar na run | M4 |
| M6 | Editor de deck da run: estender o editor pra mostrar só a pool da run | M5 |
| M7 | Progressão: gating elite/boss (2/3 elites → boss), vitória/derrota da run | M3, M4 |
| M8 | Loja & eventos: nodes que gastam a moeda | M4 |

Cada milestone vira seu próprio ciclo spec → plano → implementação.

---

## M1 — Fundação (spec)

Objetivo: provar o **encanamento ponta-a-ponta** (Home → overlay → Act → persiste) e
estabelecer a base de UI/dados reutilizável. Nova/Continuar levam a placeholders.

### Fluxo
```
Home (botão "Roguelike")
   └─ clica → overlay "Roguelike" (GameObject nosso)
        ├─ ao abrir → Act "Roguelike.get_state"  → { active, gameType, ... }
        ├─ "Nova Run"     → (se ativa: confirma) → Act "Roguelike.start_run"  → toast "Run criada"
        ├─ "Continuar"    → (só se ativa)        → toast "abrindo run…"  (M3 abre o mapa)
        ├─ "Abandonar Run"→ (só se ativa) confirma → Act "Roguelike.abandon_run"
        └─ "Fechar"
```

### Modelo de dados (`roguelike.json`, M1 mínimo e extensível)
```json
{ "version": 1, "active": true, "gameType": "base_deck", "seed": 123456, "createdAt": "ISO-8601" }
```
Campos futuros (adicionados nos milestones): `deck`, `cardPool`, `coins`, `map`,
`position`, `progress`.

### Acts (server, `Roguelike.*`)
- `Roguelike.get_state` → lê o arquivo; responde `{ active, gameType, ... }` (ou
  `active:false` se não existe).
- `Roguelike.start_run` → cria/sobrescreve a run (sorteia `seed`, grava), responde o
  estado novo.
- `Roguelike.abandon_run` → desativa/apaga a run, responde `active:false`.

### Botão na Home (client)
- Hook em `HomeViewController.UpdateHome` → injeta botão "Roguelike" no `RootMenu`
  (clona botão existente, troca label TMP, liga `onClick` via
  `_UnityAction.CreateUnityAction`). Idempotente.

### Overlay de entrada (client) — base reutilizável das telas do modo
- GameObject root parenteado no canvas da Home: título + Nova Run + Continuar
  (habilitado só se `active`) + Abandonar Run (só se `active`) + Fechar.
- Botões clonados da UI do jogo, posicionados via `RectTransform`. Confirmações via
  `CommonDialog`.

### Organização do código
- **Server `YgoMasterServer/Roguelike/`:** `RoguelikeRun.cs` (modelo + load/save), 
  `RoguelikeActs.cs` (`Handle(request)`).
- **Client `YgoMasterClient/Roguelike/`:** `RoguelikeApi.cs` (chama os Acts),
  `RoguelikeHomeButton.cs`, `RoguelikeEntryOverlay.cs`.
- **Core (mínimo):** `GameServer.cs` roteia `Roguelike.*` → `RoguelikeActs.Handle`;
  `Program.cs` + `.csproj` registram as classes do client.

### Fora do M1 (stubs)
Deck-select (M2), mapa (M3), duelo+rewards (M4/M5), editor (M6), progressão (M7),
loja/eventos (M8). Nova/Continuar levam a placeholders no M1.

### Riscos / spikes
1. **Client → server (Act custom):** confirmar como o client dispara um Act e lê a
   resposta (o jogo já faz HTTP local; precisamos do helper). **Primeiro spike.**
2. Pegar o canvas certo pro overlay + limpeza ao trocar de cena.
3. Setar texto TMP nos botões clonados.

### Pronto quando (M1)
Da Home: clicar Roguelike abre o overlay; "Nova Run" cria e persiste `roguelike.json`;
reabrir mostra "Continuar"/"Abandonar" habilitados; "Abandonar" limpa o estado.

> **M1 concluído (2026-05-21).** Implementado com ActionSheet/CommonDialog (em vez do
> overlay custom) — pragmático e reaproveitável. O modo segue com ActionSheet até a UI
> do mapa (M3), quando construiremos telas mais ricas.

---

## M2 — Início de run: escolher 1 de 3 decks (spec)

Objetivo: ao iniciar uma run `base_deck`, o **server sorteia 3 decks** de um pool e o
jogador **escolhe 1**, que vira o deck da run (persistido). Reaproveita
ActionSheet/CommonDialog do M1. Sem arte de carta ainda (vem com a UI do mapa).

### Fluxo
```
Home → botão "ROGUELIKE" → menu:
  - Sem run:                 "Nova Run" → start_run (server sorteia 3)
                               → ActionSheet "Escolha seu deck" [Deck A / B / C]
                               → tap → choose_deck(i) → toast "Deck: X"
  - Run sem deck (deckChosen=false): "Continuar Run" reabre a seleção (resumível)
  - Run com deck:            "Continuar Run" → toast "Run em andamento (mapa: M3)"
                             "Abandonar Run" → confirma → abandon_run
```

### Pool de decks (nosso, fora do Goat)
- Fonte: **`DataLE/Roguelike/StartingDecks/*.json`** — decks iniciais nossos (formato
  player), curados/editáveis. **Não** reaproveitamos `DataLE/decks/` nem o
  `DeckPoolLoader` da pasta `Goat` (regra: nada de código do Goat). Em vez disso, um
  loader próprio `RoguelikeDeckPool` (em `YgoMasterServer/Roguelike/`, inspirado no
  DeckPoolLoader) lista e carrega os JSONs.
- Nome de exibição = nome do arquivo (sem extensão), pra curadoria simples. `bossCard` =
  campo `boss_card` do JSON, ou o 1º id do main como fallback.
- Seleção: RNG semeado pelo `seed` da run → 3 arquivos distintos; `file` salvo como
  caminho relativo ao `dataDirectory` (ex.: `Roguelike/StartingDecks/Chaos Turbo.json`).

### Modelo (`roguelike.json`, adições ao M1)
```json
{
  "version": 1, "active": true, "gameType": "base_deck", "seed": 123, "createdAt": "...",
  "deckChosen": false,
  "deckOffers": [
    {"name":"Chaos Turbo","bossCard":5835,"file":"Roguelike/StartingDecks/Chaos Turbo.json"},
    {"name":"Cat Control","bossCard":4456,"file":"Roguelike/StartingDecks/Cat Control.json"},
    {"name":"Final Countdown","bossCard":4321,"file":"Roguelike/StartingDecks/Final Countdown.json"}
  ],
  "deck": null
}
```
- `deckChosen=false`: `deckOffers` tem 3, `deck=null`.
- Após `choose_deck`: `deck` = `{name, bossCard, deck:<deck formato-player m/e/s>}`.
  Guardamos no formato player (não convertemos pro shape SoloDuel agora — isso é trabalho
  do M4, com conversor nosso/`DeckInfo` original); `deckOffers` esvaziado.

### Acts (server)
- `start_run` (evolui): cria run `active`, `deckChosen=false`, sorteia `seed` + 3 offers,
  persiste, responde o estado (com offers).
- `choose_deck { index }` (novo): valida `0..2` e run pendente;
  `RoguelikeDeckPool.LoadOne(offers[index].file)` → grava `deck`, `deckChosen=true`, limpa
  offers; responde estado. Idempotente (se já escolhido, no-op).
- `abandon_run`: inalterado.
- get_state (home piggyback): inclui `deckChosen`, `deck` (name/bossCard), `deckOffers`.

### Client
- `RoguelikeApi`: + `IsDeckChosen()`, `GetDeckOfferNames()` (lê `$.Roguelike.deckOffers`
  via `ClientWork.SerializePath`), `ChooseDeck(int index)` (act `choose_deck`).
- Reação assíncrona: `RoguelikeFlow.OnNetworkComplete(cmd)` chamado do hook
  `RequestStructure.Complete` (`DuelStarter.cs`, junto do `ProfileReplayViewController
  .OnNetworkComplete`). `start_run` completo → abre ActionSheet de seleção a partir do
  `$.Roguelike` já atualizado; `choose_deck` completo → toast confirmando.
- `RoguelikeHomeButton.OnMenuSelect`: "Nova Run" agora só dispara `start_run` (a seleção
  vem pela completion). "Continuar": se `active && !deckChosen` reabre a seleção; se deck
  escolhido, toast "mapa: M3".

### Fora do M2 (stubs)
Mapa (M3), uso do `deck` em duelo (M4). O `deck` salvo só é consumido no M4. UI rica com
arte de carta fica pra UI do mapa (M3).

### Pronto quando (M2)
"Nova Run" mostra 3 decks; escolher 1 grava `deck` + `deckChosen=true` no `roguelike.json`;
reabrir antes de escolher reabre a seleção; após escolher, "Continuar" indica run em
andamento; "Abandonar" limpa tudo.

> **M2 concluído (2026-05-21).** Pool próprio em `DataLE/Roguelike/StartingDecks/` (sem
> reuso de código do `Goat`); loader `RoguelikeDeckPool`. Verificado in-game: Nova Run →
> escolher 1 de 3 → persiste o deck.

---

## M2.5 — Tela de seleção de deck (UI rica) (spec)

Objetivo: trocar o ActionSheet do M2 por uma **tela própria** mostrando os 3 decks como
cards com a **carta principal em destaque**; clicar num card abre um **drawer** pra
**visualizar o deck** (reusando o viewer nativo) e **selecionar**, com **descrição**.

### Spike (concluído 2026-05-21)
Provado in-game que dá pra empurrar uma tela do jogo standalone e renderizar nosso
conteúdo nela (fundo + arte de carta). Ferramentas de dev criadas (mantidas):
`vcdump [depth]` (despeja hierarquia da top VC em `_tmp/vc_<classe>.txt`), `vclog`
(loga prefab-paths), `rgpush <path>` (empurra tela). Spike `rgproof` é descartável
(removido quando o M2.5 entrar).

### Base: `Solo/SoloMode`
Abre com **fundo animado** + header/back nativos e hospeda a `SoloPortalViewController`,
cujo conteúdo já tem **tiles de card prontas** (estrutura confirmada via `vcdump 12`):
```
SoloPortalUI(Clone) → Root
  TitleSafeArea/TitleGroup/NameText                 ← título  → "Escolha seu Deck"
  ButtonArea/MainGroup/RecommendGroup [HLayout]     ← seção dos cards
    RecommendText                                   ← label  → "Decks"
    RecommendButton1 / RecommendButton2             ← TILES (clonar p/ ter 3)
      Button [SelectionButton]                      ← clicável (onClick)
        …/ImageGate/SoloCardThumbMask/SoloCardThumbImage [RawImage, AutoReleaseCardIllust]  ← arte
        …/NameArea/…/TextGateName [ExtendedTextMeshProUGUI]                                  ← nome
  ButtonArea/MainGroup/LastPlayGroup                ← esconder
  ButtonArea/GateListGroup                          ← esconder (Histórias/Treinamento)
```

### Fluxo
```
Nova Run → start_run → push Solo/SoloMode → customiza SoloPortal:
  - título "Escolha seu Deck"; esconde LastPlayGroup + GateListGroup
  - 3 tiles (RecommendButton clonadas): arte da carta principal + nome do deck
  - tap numa tile → drawer:  [Ver deck]  [Selecionar]  + descrição
       Ver deck   → viewer nativo de deck via DeckView.SetCards (cartas + descrição)
       Selecionar → Roguelike.choose_deck(index) → fecha a tela
Continuar (deck não escolhido) → reabre essa tela.
```

### Mecanismo (tudo confirmado no spike)
- Empurrar/customizar: `ViewControllerManager.PushChildViewController("Solo/SoloMode")` →
  `GetViewController(manager, SoloPortalViewController)` + hook `OnCreatedView` → manipular
  GameObjects (clonar tiles, set texto, esconder grupos, wire onClick).
- Arte de carta: `AssetHelper.LoadImmediateAsset("Card/Images/Illust/tcg/<cid>")` +
  `AssetHelper.SpriteFromTexture` → `Image.sprite` (core, não-Goat). Alternativa: o binding
  nativo `BindingSoloCardThumb` da própria tile.
- Grid "ver deck": `YgomGame.Deck.DeckView.SetCards(ptr, mainColl, extraColl)` (DeckEditorUtils, core).

### Server / dados (adições ao M2)
- Enriquecer cada item de `deckOffers` com `description` + listas `main/extra/side` (ids),
  pra alimentar o viewer sem round-trip. Após `choose_deck`, `deck` também leva `description`.
- Novo campo `description` (e opcional `archetype`) nos JSONs de `Roguelike/StartingDecks`;
  `RoguelikeDeckPool.LoadOne` lê.

### Organização (código novo, isolado em `Roguelike/`)
- `RoguelikeCardImage` (client) — carrega/cacheia sprite de carta por cid (reusa AssetHelper).
- `RoguelikeDeckSelectScreen` (client) — push SoloMode + customiza SoloPortal + 3 tiles + drawer.
- `RoguelikeFlow` — passa a abrir essa tela (em vez do ActionSheet) na conclusão do `start_run`.
- Server: `RoguelikeDeckPool` + offers ganham `description` + card ids.

### Fora do M2.5
Uso do deck em duelo real (M4), mapa (M3). Tile "Ver deck" só visualiza.

### Pronto quando (M2.5)
Nova Run abre a tela SoloMode com 3 cards (arte + nome); tap abre drawer; "Ver deck" mostra
o deck no viewer nativo (com descrição); "Selecionar" grava o deck e fecha; "Continuar" sem
deck reabre a tela.

> **M2.5 concluído (2026-05-21).** Verificado in-game: Home → Nova Run abre a tela
> (`Solo/SoloMode` reaproveitado; hook em `SoloPortalViewController.OnCreatedView` gated por
> flag — Solo normal intacto); 3 tiles clonadas de `RecommendButton` com arte da carta
> principal (overlay `RoguelikeCardImage`: sprite próprio gchandle'd + DontDestroyOnLoad +
> AspectRatioFitter EnvelopeParent — preenche e sobrevive ao viewer); drawer (ActionSheet)
> com "Ver deck" (abre o `DeckBrowser` nativo via args IL2CPP **tipados**, evitando o cast
> Int64→Int32) e "Selecionar" (`choose_deck` + fecha). Dev tools mantidas:
> `vcdump`/`vclog`/`rgpush`/`rgdeck`.
>
> Pendências (não bloqueiam): `boss_card` por deck nas StartingDecks (Buster Blader/Cat
> Control caem no mesmo cid 5655 = 1º id do main); painel lateral de descrição do viewer
> (`shortcutSettings`); fundo `Base` do RecommendGroup escondido (revisitar o painel).
