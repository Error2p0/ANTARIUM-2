using UnityEngine;

namespace Antarium.Content
{
    /// <summary>When a help topic shows its page; until then it is listed as "???" (no spoilers for what lies deeper).</summary>
    public enum HelpReveal : byte
    {
        /// <summary>From the start.</summary>
        Always,
        /// <summary>Once the colony has dug into <see cref="HelpTopicDef.revealBiome"/> (or a deeper layer).</summary>
        Biome,
        /// <summary>Once <see cref="HelpTopicDef.revealResearch"/> is researched in this colony.</summary>
        Research,
        /// <summary>Once <see cref="HelpTopicDef.revealResearch"/> is researched, or the bloodline has flown before.</summary>
        Flight,
    }

    /// <summary>
    /// A page of the in-game help (F1): how one part of the game works. The body may name keys with placeholders such as
    /// <c>{Mine}</c> or <c>{Help}</c>; they are replaced by the keys currently bound (see <c>HelpText</c>).
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Help Topic", fileName = "HelpTopic")]
    public sealed class HelpTopicDef : ContentDef
    {
        [Tooltip("Position in the help list (lower first); topics are grouped by category in this order.")]
        public int order;
        [Tooltip("Heading the topic is listed under (Basics, Building, Colony, World, Dangers, Late game).")]
        public string category = "Basics";
        [Tooltip("Icon from Art/UI/Icons (without the folder), shown next to the title. Optional.")]
        public string icon;
        [TextArea(6, 20)] public string body;
        public HelpReveal reveal;
        [Tooltip("Reveal = Biome: the layer the colony must reach.")]
        public BiomeDef revealBiome;
        [Tooltip("Reveal = Research / Flight: the research that reveals it.")]
        public ResearchDef revealResearch;
    }
}
