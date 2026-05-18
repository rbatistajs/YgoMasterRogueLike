using System.Collections.Generic;

namespace YgoMaster.Builder
{
    // Port de scripts/_solo_helpers — gemas por level + helpers de bloco
    // de reward pro Solo.json. Bloco de reward shape:
    //   { "<category>": { "<item>": qty, ... } }
    // category 1 = gems (item 1 sempre); category 2 = card grants (item
    // = cid). Solo.json suporta mais categorias, mas builders só usam
    // essas duas.
    static class SoloRewards
    {
        // Gemas por nível de dificuldade. Index = level (0 = mais fácil,
        // 6 = mais difícil = boss tier). Duelos mais difíceis pagam mais.
        static readonly int[] GemsPerLevel = { 20, 30, 50, 75, 100, 150, 200 };

        public static int GemsForLevel(int level)
        {
            if (level < 0) level = 0;
            if (level > 6) level = 6;
            return GemsPerLevel[level];
        }

        // `{ "1": { "1": gems } }` — categoria 1 (gemas), item 1.
        public static Dictionary<string, object> GemRewardBlock(int gems)
        {
            return new Dictionary<string, object>
            {
                { "1", new Dictionary<string, object> { { "1", gems } } }
            };
        }

        // `{ "2": { "<cardId>": qty } }` — categoria 2 (cartas), item N.
        public static Dictionary<string, object> CardRewardBlock(int cardId, int qty = 1)
        {
            return new Dictionary<string, object>
            {
                { "2", new Dictionary<string, object> { { cardId.ToString(), qty } } }
            };
        }

        // Mescla múltiplos blocos `{cat: {item: qty}}` num só (qty
        // acumula se houver colisão). null/vazio é ignorado.
        public static Dictionary<string, object> Merge(params Dictionary<string, object>[] blocks)
        {
            Dictionary<string, object> outBlock = new Dictionary<string, object>();
            if (blocks == null) return outBlock;
            foreach (Dictionary<string, object> blk in blocks)
            {
                if (blk == null) continue;
                foreach (KeyValuePair<string, object> catKv in blk)
                {
                    Dictionary<string, object> items = catKv.Value as Dictionary<string, object>;
                    if (items == null) continue;
                    Dictionary<string, object> dst;
                    object existing;
                    if (outBlock.TryGetValue(catKv.Key, out existing))
                    {
                        dst = existing as Dictionary<string, object> ?? new Dictionary<string, object>();
                    }
                    else
                    {
                        dst = new Dictionary<string, object>();
                        outBlock[catKv.Key] = dst;
                    }
                    foreach (KeyValuePair<string, object> itemKv in items)
                    {
                        int cur = 0;
                        object curObj;
                        if (dst.TryGetValue(itemKv.Key, out curObj) && curObj != null)
                        {
                            try { cur = System.Convert.ToInt32(curObj); } catch { }
                        }
                        int add = 0;
                        try { add = System.Convert.ToInt32(itemKv.Value); } catch { }
                        dst[itemKv.Key] = cur + add;
                    }
                }
            }
            return outBlock;
        }
    }
}
