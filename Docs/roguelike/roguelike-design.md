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
| **M1** | Fundação: modelo+persistência da run, Acts, botão Home, overlay de entrada (Nova/Continuar/Abandonar) | — |
| M2 | Início de run: pool de decks iniciais + escolher 1 de 3 | M1 |
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
