using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// A fragment of the ruins' story (10.5), shown in the Codex. It is found once the colony has studied
    /// <see cref="count"/> of <see cref="item"/> in the Archive (relics for most, unique tablets for a few).
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Lore Fragment", fileName = "Lore")]
    public sealed class LoreDef : ContentDef
    {
        [Tooltip("Position in the Codex (lower first).")]
        public int order;
        [TextArea(4, 12)] public string text;
        [Tooltip("Studying this item in the Archive finds the fragment...")]
        public ItemDef item;
        [Tooltip("...once this many of it have been studied in this colony.")]
        [Min(1)] public int count = 1;
    }
}
