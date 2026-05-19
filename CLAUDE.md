# YgoMaster fork — regras do projeto

## NUNCA usar `info/` folder pra dados de card

`info/` está OBSOLETO neste projeto. Não ler, não escrever, não copiar
dados de lá em script novo, refactor, ou módulo C#.

Pra obter informações de card (nome, tipo, ATK/DEF, attribute, race,
level, archetype, passcode, etc.) use:

- **No servidor C#** (`YgoMasterServer/`, `YgoMasterSettings/`):
  - `DuelDllProps.DLL_CardGetIdByName`, `DLL_CardGetName`,
    `DLL_CardGetAttribute`, `DLL_CardGetType`, etc. — APIs do DLL
    nativo que o servidor já carrega no boot.
  - `CardList.json` (em `DataLE/CardList.json`) — IDs válidos do
    install do user. Toda lookup de card deve filtrar por presença
    aqui (cards fora da CardList não existem no client e quebram).
  - Para reading de bytes brutos: `DataLE/CardData/#/CARD_*.bytes` ou
    `DataLE/CardData/TextAsset/` (decifrado).

- **Em scripts Python** (legacy): se realmente precisar, ler de
  `DataLE/CardData/TextAsset/` ou `CardList.json`. Mas idealmente
  portar o script pra C# e usar as APIs acima.

Por quê: `info/` é cache antigo, fora de sync com mudanças no CardData
custom (Rush cards 15000+, alt-arts, etc.). Usar de lá causa o bug
"CPU não joga Rush" e similares.
