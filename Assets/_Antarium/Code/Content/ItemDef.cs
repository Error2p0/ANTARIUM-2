using UnityEngine;

namespace Antarium.Content
{
    /// <summary>How a loose item lies on the ground (Milestone 11): a heap tinted by its colour, or its own sprite.</summary>
    public enum PileArt : byte
    {
        Heap,
        Mushroom,
        GlowMushroom,
        Toadstool,
        Berries,
        /// <summary>An apple (batch 6).</summary>
        Apple,
        /// <summary>The Elder Tree's golden apple: drawn at full brightness (batch 6).</summary>
        GoldenApple,
    }

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
        [Tooltip("How it lies on the ground (Milestone 11): a heap in its colour, or its own sprite (mushrooms, berries).")]
        public PileArt pileArt = PileArt.Heap;
        [Tooltip("Light given off while it grows in the world (glowing mushrooms, Milestone 11): rgb = colour, alpha = strength. Alpha 0 = none.")]
        public Color growthLight = new(0f, 0f, 0f, 0f);

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
