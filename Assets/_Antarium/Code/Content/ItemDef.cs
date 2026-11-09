using UnityEngine;

namespace Antarium.Content
{
    /// <summary>A haulable resource: ore chunks, food, collectibles.</summary>
    [CreateAssetMenu(menuName = "Antarium/Item", fileName = "Item")]
    public sealed class ItemDef : ContentDef
    {
        [Tooltip("Colour of piles and carried items.")]
        public Color color = Color.white;
        [Tooltip("Food points added to the colony on delivery. > 0 makes this a food item.")]
        [Min(0f)] public float foodValue;
        [Tooltip("Fuel (coal): a heated machine (next to lava or magma rock) runs its recipes without it.")]
        public bool fuel;

        public bool IsFood => foodValue > 0f;
    }
}
