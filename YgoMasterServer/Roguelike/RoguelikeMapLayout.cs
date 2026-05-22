using System.Collections.Generic;

namespace YgoMaster
{
    // Abstract map generator. Add new shapes by subclassing + registering in Create().
    abstract class RoguelikeMapLayout
    {
        public abstract RoguelikeMap Build(int seed, Dictionary<string, object> settings);

        public static RoguelikeMapLayout Create(string layout)
        {
            switch (layout)
            {
                case "slay_the_spire":
                default:
                    return new SlayTheSpireLayout();
            }
        }
    }
}
