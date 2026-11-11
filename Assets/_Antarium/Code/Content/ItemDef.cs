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
        [Tooltip("Colour of this find on the ore overlay (10.7), bright so it stands out on the dimmed map. Alpha 0 = its colour, brightened.")]
        public Color mapColor = new(0f, 0f, 0f, 0f);

        public bool IsFood => foodValue > 0f;

        /// <summary>The ore overlay's colour: <see cref="mapColor"/>, or <see cref="color"/> at least 90% bright.</summary>
        public Color MapColor
        {
            get
            {
                if (mapColor.a > 0f) return new Color(mapColor.r, mapColor.g, mapColor.b, 1f);
                Color.RGBToHSV(color, out float h, out float s, out float v);
                return Color.HSVToRGB(h, s, Mathf.Max(v, 0.9f));
            }
        }
    }
}
