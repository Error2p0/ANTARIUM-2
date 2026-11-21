using System.Collections.Generic;
using Antarium.Content;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The help topics in list order and which of them a colony has revealed: topics about deeper layers, research or the
    /// Nuptial Flight stay "???" until the colony gets there, so the help never gives away what lies ahead.
    /// </summary>
    public sealed class HelpLibrary
    {
        public readonly HelpTopicDef[] Topics;

        public HelpLibrary(IEnumerable<HelpTopicDef> topics)
        {
            var list = new List<HelpTopicDef>();
            if (topics != null)
                foreach (var t in topics)
                    if (t != null) list.Add(t);
            // Stable: topics with the same order keep their asset order.
            Topics = list.ToArray();
            var keys = new int[Topics.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = Topics[i].order * 4096 + i;
            System.Array.Sort(keys, Topics);
        }

        public int IndexOf(HelpTopicDef topic) => topic == null ? -1 : System.Array.IndexOf(Topics, topic);

        /// <summary>Whether <paramref name="sim"/>'s colony has seen enough to read <paramref name="topic"/> (no colony: only Always topics).</summary>
        public static bool Revealed(HelpTopicDef topic, Simulation sim)
        {
            if (topic == null) return false;
            switch (topic.reveal)
            {
                case HelpReveal.Always: return true;
                case HelpReveal.Biome:
                {
                    if (sim == null) return false;
                    int index = BiomeIndex(topic, sim);
                    return index < 0 || sim.BiomeAtDepth(sim.Colony.Stats.MaxDepth) >= index;
                }
                case HelpReveal.Research:
                    return sim != null && ResearchDone(topic, sim);
                case HelpReveal.Flight:
                    return sim != null && (sim.Legacy.Awakened || sim.Legacy.Flights > 0 || ResearchDone(topic, sim));
                default: return true;
            }
        }

        /// <summary>What reveals a locked topic, for its "???" page.</summary>
        public static string LockedHint(HelpTopicDef topic)
        {
            if (topic == null) return "";
            return topic.reveal switch
            {
                HelpReveal.Biome when topic.revealBiome != null => $"Your colony hasn't found this yet. Dig down to the {topic.revealBiome.DisplayName} to learn about it.",
                HelpReveal.Research when topic.revealResearch != null => $"Your colony hasn't found this yet. Research {topic.revealResearch.DisplayName} to learn about it.",
                HelpReveal.Flight when topic.revealResearch != null => $"Your colony hasn't found this yet. Research {topic.revealResearch.DisplayName} to learn about it.",
                _ => "Your colony hasn't found this yet.",
            };
        }

        static int BiomeIndex(HelpTopicDef topic, Simulation sim)
        {
            var biomes = sim.Content.WorldGen != null ? sim.Content.WorldGen.biomes : null;
            return topic.revealBiome == null || biomes == null ? -1 : biomes.IndexOf(topic.revealBiome);
        }

        static bool ResearchDone(HelpTopicDef topic, Simulation sim) =>
            topic.revealResearch != null && sim.Content.TryGetResearchIndex(topic.revealResearch.Id, out int r) && sim.Research.Done[r];
    }
}
