using System;
using System.Collections.Generic;
using YgoMaster.Cosmetics;
using YgoMaster.Modifiers;

namespace YgoMaster.Builder
{
    // Port de scripts/_solo_helpers.build_soloduel — constrói o
    // payload completo de SoloDuels/<chapterId>.json (a casca
    // externa `{ "Duel": {...} }`).
    //
    // Usado tanto pelo bake (--grid-gate gen) quanto pelo runtime
    // (RuntimeGateGenerator.BuildDuelDict). O runtime hoje duplica
    // parte dessa lógica — depois da fase 3 vamos consolidar lá.
    //
    // Cosmetics: por enquanto só vanilla (mesmo set que o runtime usa).
    // Cosmetics randomization (port de _cosmetics.py) vem depois.
    static class SoloDuelBuilder
    {
        // Vanilla cosmetic baselines — mirror de
        // `_solo_helpers.VANILLA_COSMETICS` (chapter 40001 = Yugi vs Joey).
        // P1's actual values come from MyDeck at duel start; estes são
        // placeholders pra que o JSON tenha o shape esperado.
        public static readonly List<object> DefaultMat        = new List<object> { 1090016, 1090016 };
        public static readonly List<object> DefaultDuelObject = new List<object> { 1100016, 1100016 };
        public static readonly List<object> DefaultAvatarHome = new List<object> { 1110016, 1110016 };
        public static readonly List<object> DefaultSleeve     = new List<object> { 0,       1070052 };
        public static readonly List<object> DefaultIcon       = new List<object> { 0,       1011047 };
        public static readonly List<object> DefaultIconFrame  = new List<object> { 0,       1032001 };
        public static readonly List<object> DefaultAvatar     = new List<object> { 0,       1000028 };

        // Regulation names — devem bater com DeckInfo.RegulationIdsByName.
        const string RegulationGoat = "Goat Format";
        const string RegulationRush = "Rush Duel";

        // duel_type_field por duelType: Rush precisa "Duel.Type = 4" no
        // shape do SoloDuel; Normal não precisa (default = 1).
        static int? DuelTypeField(string duelType)
        {
            return duelType == "Rush" ? (int?)4 : null;
        }

        static string RegulationName(string duelType)
        {
            return duelType == "Rush" ? RegulationRush : RegulationGoat;
        }

        // Modo de cosmetics aplicado ao duel. `Vanilla` = sempre o
        // baseline do chapter 40001 (Yugi vs Joey). `Random` = sorteia
        // de ItemID.Values por categoria (pool é o que a config liberou).
        public enum CosmeticMode { Vanilla, Random }

        // Constrói o duel dict interno (sem a casca `{Duel:...}`). Usado
        // pelo runtime que injeta direto no SoloData.
        //
        // `deckSection` é o dict já no shape SoloDuel
        // ({Main:{CardIds,Rare}, Extra:..., Side:...}) — produzido pelo
        // DeckPoolLoader.LoadOne.
        //
        // `cosmeticMode` controla os 7 campos visuais (mat/sleeve/icon/...):
        //   Vanilla — defaults fixos (mesmo set sempre)
        //   Random  — CosmeticsPicker sorteia via ItemID.GetRandomId.
        //             Precisa de `rng` non-null nesse modo.
        //
        // `modifierLayers` é a lista de modifier dicts em ordem low→high
        // (gate-level → deck-embedded → chapter-override). Vazio/null =
        // sem modifiers (sem `cmds`/`life`/`hnum`/`random_specs`).
        public static Dictionary<string, object> BuildInner(
            int chapterId,
            Dictionary<string, object> deckSection,
            string cpuName,
            string duelType,
            CosmeticMode cosmeticMode = CosmeticMode.Vanilla,
            Random rng = null,
            params Dictionary<string, object>[] modifierLayers)
        {
            Dictionary<string, object> duel = new Dictionary<string, object>
            {
                { "Deck",            new List<object> { deckSection, deckSection } },
                { "chapter",         chapterId },
                { "name",            new List<object> { "", cpuName ?? "" } },
                { "dialog_intro",    "" },
                { "dialog_outro",    "" },
                // Habilita IA "Hard" no CPU (vs Fool/Simple/etc).
                { "cpu",             100 },
                { "cpuflag",         "None" },
                // O RuntimeRandomResolver bucketa o pool `any` por
                // (Type, regulation_name) — mantém duels do format certo.
                { "regulation_name", RegulationName(duelType) },
            };
            ApplyCosmetics(duel, cosmeticMode, rng);

            int? typeField = DuelTypeField(duelType);
            if (typeField.HasValue) duel["Type"] = typeField.Value;

            // Roda a pipeline de modifiers se houver. Adiciona
            // cmds/life/hnum/random_specs in-place no `duel`.
            if (modifierLayers != null && modifierLayers.Length > 0)
            {
                bool any = false;
                foreach (Dictionary<string, object> l in modifierLayers)
                {
                    if (l != null && l.Count > 0) { any = true; break; }
                }
                if (any)
                {
                    ModifierApplier.ApplyFullPipeline(duel, duelType, modifierLayers);
                }
            }
            return duel;
        }

        // Constrói o payload completo (com casca `{Duel:...}`) pronto pra
        // serializar e escrever em SoloDuels/<chapterId>.json (bake path).
        public static Dictionary<string, object> Build(
            int chapterId,
            Dictionary<string, object> deckSection,
            string cpuName,
            string duelType,
            CosmeticMode cosmeticMode = CosmeticMode.Vanilla,
            Random rng = null,
            params Dictionary<string, object>[] modifierLayers)
        {
            return new Dictionary<string, object>
            {
                { "Duel", BuildInner(chapterId, deckSection, cpuName, duelType,
                                      cosmeticMode, rng, modifierLayers) }
            };
        }

        // Aplica os 7 campos visuais. Modo Random sem rng cai pro Vanilla
        // (seguro — não dá pra reproduzir sorteios sem seed).
        static void ApplyCosmetics(Dictionary<string, object> duel,
                                    CosmeticMode mode, Random rng)
        {
            if (mode == CosmeticMode.Random && rng != null)
            {
                CosmeticsPicker.ApplyTo(duel, rng);
                return;
            }
            duel["mat"]         = DefaultMat;
            duel["duel_object"] = DefaultDuelObject;
            duel["avatar_home"] = DefaultAvatarHome;
            duel["sleeve"]      = DefaultSleeve;
            duel["icon"]        = DefaultIcon;
            duel["icon_frame"]  = DefaultIconFrame;
            duel["avatar"]      = DefaultAvatar;
        }
    }
}
