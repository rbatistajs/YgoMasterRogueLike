using System;
using System.Collections.Generic;

namespace YgoMaster.Rewards
{
    // Goat: bloco `rewards` da entry GridGates.json. Substitui o que era
    // global em info/cosmetics_config.json (Python) — agora cada gate
    // configura seu próprio drop por chapter type e os pesos das
    // categorias de item que podem cair.
    //
    // Shape esperado dentro de uma entry:
    //   "rewards": {
    //     "boss_drop_chance":    1.0,
    //     "elite_drop_chance":   1.0,
    //     "reward_drop_chance":  0.15,
    //     "duel_drop_chance":    0.0,
    //     "category_weights": {
    //        "AVATAR":  1.0,
    //        "ICON":    1.0,
    //        "PROTECTOR": 1.0,
    //        ...
    //     }
    //   }
    //
    // Defaults: tudo zero. Gate só dropa items se explicitamente ativar.
    class RewardConfig
    {
        public double BossDropChance;
        public double EliteDropChance;
        public double RewardDropChance;
        public double DuelDropChance;
        // Categorias permitidas + pesos relativos. Vazio = TODAS as
        // categorias mapeáveis a reward ficam disponíveis com peso 1
        // (mesmo fallback do `_resolve_category_weights` Python).
        public Dictionary<string, double> CategoryWeights;

        public bool AnyDrop => BossDropChance > 0 || EliteDropChance > 0
                            || RewardDropChance > 0 || DuelDropChance > 0;

        public double DropChanceFor(string chapterType)
        {
            switch (chapterType)
            {
                case "boss":     return BossDropChance;
                case "elite":    return EliteDropChance;
                case "reward":   return RewardDropChance;
                case "treasure": return RewardDropChance;   // treasure = reward grande
                default:         return DuelDropChance;     // duel + qualquer outro
            }
        }

        // Parse do sub-bloco "rewards" de uma entry GridGates. null →
        // config default zerada (gate não dropa nada).
        public static RewardConfig Parse(Dictionary<string, object> entry)
        {
            RewardConfig cfg = new RewardConfig
            {
                CategoryWeights = new Dictionary<string, double>(),
            };
            if (entry == null) return cfg;
            Dictionary<string, object> r = Utils.GetValue<Dictionary<string, object>>(entry, "rewards");
            if (r == null) return cfg;

            cfg.BossDropChance   = GetDoubleOr(r, "boss_drop_chance",   0.0);
            cfg.EliteDropChance  = GetDoubleOr(r, "elite_drop_chance",  0.0);
            cfg.RewardDropChance = GetDoubleOr(r, "reward_drop_chance", 0.0);
            cfg.DuelDropChance   = GetDoubleOr(r, "duel_drop_chance",   0.0);

            Dictionary<string, object> weights =
                Utils.GetValue<Dictionary<string, object>>(r, "category_weights");
            if (weights != null)
            {
                foreach (KeyValuePair<string, object> kv in weights)
                {
                    if (kv.Value == null) continue;
                    double w;
                    try { w = Convert.ToDouble(kv.Value); } catch { continue; }
                    if (w > 0) cfg.CategoryWeights[kv.Key] = w;
                }
            }
            return cfg;
        }

        static double GetDoubleOr(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v); } catch { return fallback; }
        }
    }
}
