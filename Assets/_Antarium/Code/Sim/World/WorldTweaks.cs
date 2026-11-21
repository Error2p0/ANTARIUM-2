using System.Collections.Generic;
using Antarium.Content;

namespace Antarium.Sim
{
    /// <summary>How world modifiers change world generation: summed threshold offsets and a biome thickness multiplier.</summary>
    public struct WorldTweaks
    {
        /// <summary>Added to ore deposit thresholds (negative = more ore).</summary>
        public float OreThreshold;
        /// <summary>Added to every biome's cave threshold (negative = more caves).</summary>
        public float CaveThreshold;
        public float WaterThreshold, LavaThreshold, GasThreshold;
        /// <summary>Biome thickness multiplier (1 = as authored).</summary>
        public float BiomeScale;
        /// <summary>Per biome: the variant a world modifier puts in its place (10.6), or null. Null array = no variants.</summary>
        public BiomeVariantData[] Variants;

        public static WorldTweaks None => new() { BiomeScale = 1f };

        /// <summary>Combines world modifiers (indices into <see cref="ContentDB.Modifiers"/>).</summary>
        public static WorldTweaks From(ContentDB content, IReadOnlyList<int> modifiers)
        {
            var t = None;
            if (modifiers == null) return t;
            foreach (int m in modifiers)
            {
                if (m < 0 || m >= content.Modifiers.Length) continue;
                var def = content.Modifiers[m];
                t.OreThreshold += def.oreThreshold;
                t.CaveThreshold += def.caveThreshold;
                t.WaterThreshold += def.waterThreshold;
                t.LavaThreshold += def.lavaThreshold;
                t.GasThreshold += def.gasThreshold;
                t.BiomeScale *= def.biomeThickness > 0f ? def.biomeThickness : 1f;
                if (content.ModifierVariants == null) continue;
                foreach (var v in content.ModifierVariants[m])
                {
                    t.Variants ??= new BiomeVariantData[content.Biomes.Length];
                    t.Variants[v.Biome] ??= v; // the first modifier that changes a biome wins
                }
            }
            return t;
        }

        /// <summary>The variant of <paramref name="biome"/> in this world, or null.</summary>
        public BiomeVariantData VariantOf(int biome) =>
            Variants != null && biome >= 0 && biome < Variants.Length ? Variants[biome] : null;

        /// <summary>The base material of <paramref name="biome"/> in this world.</summary>
        public ushort BaseMaterial(int biome, ushort authored)
        {
            var v = VariantOf(biome);
            return v != null && v.BaseMaterial >= 0 ? (ushort)v.BaseMaterial : authored;
        }

        /// <summary>The creatures of <paramref name="biome"/>'s caves in this world.</summary>
        public int[] Creatures(int biome, int[] authored)
        {
            var v = VariantOf(biome);
            return v?.Creatures ?? authored;
        }

        public float PocketThreshold(FluidKind kind) => kind switch
        {
            FluidKind.Water => WaterThreshold,
            FluidKind.Lava => LavaThreshold,
            FluidKind.Gas => GasThreshold,
            _ => 0f,
        };
    }
}
