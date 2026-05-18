using System;
using System.Collections.Generic;

namespace YgoMaster.Cosmetics
{
    // Port-equivalente de scripts/_cosmetics.pick_cosmetic_set. Sorteia
    // um set de cosmetics aleatório (mat/sleeve/icon/etc) usando a lista
    // de IDs que o `ItemID` (Enums/ItemID.cs) já carregou no boot do
    // servidor a partir de `DataLE/ItemID.json`.
    //
    // Output: dict pronto pra Update() em cima do duelDict do SoloDuel.
    // Cada campo é `[player_slot, cpu_slot]` — o CPU é o que efetivamente
    // aparece visualmente (player vem da MyDeck do jogador em runtime).
    static class CosmeticsPicker
    {
        // SoloDuel field → ItemID category. Mirror exato do
        // COSMETIC_FIELD_TO_CATEGORY do Python.
        static readonly Dictionary<string, ItemID.Category> FieldToCategory =
            new Dictionary<string, ItemID.Category>
        {
            { "mat",         ItemID.Category.FIELD       },
            { "duel_object", ItemID.Category.FIELD_OBJ   },
            { "avatar_home", ItemID.Category.AVATAR_HOME },
            { "sleeve",      ItemID.Category.PROTECTOR   },
            { "icon",        ItemID.Category.ICON        },
            { "icon_frame",  ItemID.Category.ICON_FRAME  },
            { "avatar",      ItemID.Category.AVATAR      },
        };

        // Retorna um dict {field: [0, randomId]} pra cada campo cosmetic.
        // O slot 0 (player) fica 0 porque o engine substitui pelos cosmetics
        // do MyDeck do player em runtime. CPU slot recebe o sorteado.
        //
        // Se `defaults` for passado, campos cuja categoria estiver vazia
        // no ItemID.json caem no default em vez de sumir do dict.
        public static Dictionary<string, object> PickSet(
            Random rng, Dictionary<string, List<object>> defaults = null)
        {
            Dictionary<string, object> outDict = new Dictionary<string, object>();
            foreach (KeyValuePair<string, ItemID.Category> kv in FieldToCategory)
            {
                int[] pool;
                if (!ItemID.Values.TryGetValue(kv.Value, out pool) || pool.Length == 0)
                {
                    if (defaults != null && defaults.ContainsKey(kv.Key))
                    {
                        outDict[kv.Key] = new List<object>(defaults[kv.Key]);
                    }
                    continue;
                }
                int cpuPick = pool[rng.Next(pool.Length)];
                outDict[kv.Key] = new List<object> { 0, cpuPick };
            }
            return outDict;
        }

        // Helper: aplica direto sobre um duelDict in-place. Substitui os
        // 7 fields se a categoria tiver items.
        public static void ApplyTo(Dictionary<string, object> duel, Random rng)
        {
            Dictionary<string, object> picked = PickSet(rng);
            foreach (KeyValuePair<string, object> kv in picked)
            {
                duel[kv.Key] = kv.Value;
            }
        }
    }
}
