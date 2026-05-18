using System;
using System.Collections.Generic;

namespace YgoMaster.Rewards
{
    // Port-equivalente de scripts/_cosmetics.pick_reward_item + roll_reward_item.
    // Stateless — usa ItemID.Values (carregado no boot do server) e a
    // RewardConfig da gate pra sortear category + item.
    //
    // Retorno é (rewardCategoryId, itemId) — compatível com o shape do
    // Solo.json reward block: { "<rewardCategoryId>": { "<itemId>": qty } }
    static class RewardPicker
    {
        // ItemID.Category → reward category id (per SoloFileFormat).
        // Categorias fora desse mapa não podem ser drop de reward
        // (CARD = 2 fica de fora — é tratada como boss-card grant
        // separado).
        static readonly Dictionary<ItemID.Category, int> CategoryToRewardId =
            new Dictionary<ItemID.Category, int>
        {
            { ItemID.Category.CONSUME,     1 },
            { ItemID.Category.AVATAR,      3 },
            { ItemID.Category.ICON,        4 },
            { ItemID.Category.PROFILE_TAG, 5 },
            { ItemID.Category.ICON_FRAME,  6 },
            { ItemID.Category.PROTECTOR,   7 },
            { ItemID.Category.DECK_CASE,   8 },
            { ItemID.Category.FIELD,       9 },
            { ItemID.Category.FIELD_OBJ,   10 },
            { ItemID.Category.AVATAR_HOME, 11 },
            { ItemID.Category.STRUCTURE,   12 },
            { ItemID.Category.WALLPAPER,   13 },
            { ItemID.Category.PACK_TICKET, 14 },
        };

        // Rola o chance configurado; em sucesso retorna o drop. Null no
        // contrário (sem drop esse chapter / cfg desabilitada).
        public static Tuple<int, int> Roll(Random rng, double chance, RewardConfig cfg)
        {
            if (chance <= 0 || cfg == null) return null;
            if (rng.NextDouble() >= chance) return null;
            return Pick(rng, cfg);
        }

        // Sorteia (rewardCategoryId, itemId) usando os pesos da cfg.
        // Categorias com peso 0 ou vazio (sem items no ItemID.json) são
        // ignoradas. Null se nada sortear.
        public static Tuple<int, int> Pick(Random rng, RewardConfig cfg)
        {
            // Monta a lista de candidatos: só categorias que (1) têm peso
            // > 0 na cfg ou cfg vazia (= todas), (2) mapeiam pra reward
            // id, e (3) têm pelo menos 1 item carregado de ItemID.json.
            List<ItemID.Category> cats = new List<ItemID.Category>();
            List<double> weights = new List<double>();
            bool useAllAtOne = cfg.CategoryWeights.Count == 0;

            foreach (KeyValuePair<ItemID.Category, int> kv in CategoryToRewardId)
            {
                int[] pool;
                if (!ItemID.Values.TryGetValue(kv.Key, out pool) || pool.Length == 0) continue;

                double weight;
                if (useAllAtOne)
                {
                    weight = 1.0;
                }
                else if (!cfg.CategoryWeights.TryGetValue(kv.Key.ToString(), out weight) || weight <= 0)
                {
                    continue;
                }
                cats.Add(kv.Key);
                weights.Add(weight);
            }
            if (cats.Count == 0) return null;

            // Sample weighted (acumula pesos, sorteia uniforme, encontra).
            double total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            double pick = rng.NextDouble() * total;
            double acc = 0;
            int picked = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                acc += weights[i];
                if (pick <= acc) { picked = i; break; }
            }
            ItemID.Category cat = cats[picked];
            int[] items = ItemID.Values[cat];
            int itemId = items[rng.Next(items.Length)];
            return Tuple.Create(CategoryToRewardId[cat], itemId);
        }

        // Builds a reward-block fragment. Compatível com SoloRewards.Merge.
        public static Dictionary<string, object> ItemRewardBlock(int rewardCategoryId, int itemId)
        {
            return new Dictionary<string, object>
            {
                { rewardCategoryId.ToString(),
                    new Dictionary<string, object> { { itemId.ToString(), 1 } } }
            };
        }
    }
}
