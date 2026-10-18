using System;
using UnityEngine;

namespace Antarium.Content
{
    [Flags]
    public enum MaterialFlags
    {
        None = 0,
        /// <summary>Empty sky. Exactly one material must have this flag.</summary>
        Air = 1 << 0,
        Indestructible = 1 << 1,
        LightEmitting = 1 << 2,
        Flammable = 1 << 3,
        Conductive = 1 << 4,
        Unstable = 1 << 5,
    }

    /// <summary>Procedural pixel pattern used when no texture override is assigned.</summary>
    public enum PixelPattern
    {
        Soil,
        Pebbles,
        Rock,
        Strata,
        Ore,
        Crystal,
    }

    [CreateAssetMenu(menuName = "Antarium/Terrain Material", fileName = "Material")]
    public sealed class TerrainMaterialDef : ContentDef
    {
        [Header("Simulation")]
        [Tooltip("Digging resistance. A dig hit of power P removes P / hardness of a full cell.")]
        [Min(0.01f)] public float hardness = 1f;
        [Min(0)] public int value;
        public MaterialFlags flags;

        [Header("Drops")]
        [Tooltip("Item dropped when a cell of this material is dug out. Empty = nothing.")]
        public ItemDef drop;
        [Min(1)] public int dropAmount = 1;
        [Range(0f, 1f)] public float dropChance = 1f;

        [Header("Light")]
        [ColorUsage(false)] public Color emissionColor = Color.black;
        [Tooltip("Light emitted, as a fraction of the maximum light level.")]
        [Range(0f, 1f)] public float emissionStrength;
        [Tooltip("How much the material glows by itself regardless of surrounding light.")]
        [Range(0f, 1f)] public float selfGlow;

        [Header("Pixel art")]
        public PixelPattern pattern = PixelPattern.Soil;
        public Color baseColor = new(0.42f, 0.29f, 0.18f);
        public Color shadowColor = new(0.29f, 0.2f, 0.12f);
        public Color highlightColor = new(0.53f, 0.38f, 0.24f);
        public Color accentColor = new(0.6f, 0.58f, 0.54f);
        [Range(0f, 1f)] public float accentAmount = 0.3f;
        [Tooltip("Colour of the top edge facing open space (e.g. grass). Alpha 0 = use highlight.")]
        public Color topRimColor = new(0, 0, 0, 0);
        [Tooltip("Optional hand-drawn 32x32 tile (Read/Write enabled). Overrides the procedural pattern.")]
        public Texture2D textureOverride;

        public bool IsAir => (flags & MaterialFlags.Air) != 0;
    }
}
