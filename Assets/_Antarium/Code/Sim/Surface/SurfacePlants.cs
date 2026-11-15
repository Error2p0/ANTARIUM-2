using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A plant on the ground surface: leaves fall under it by day, its roots reach into the topsoil.</summary>
    public struct SurfacePlant
    {
        /// <summary>Column it grows in (on the ground cell at <see cref="WorldGrid.SurfaceY"/>).</summary>
        public int X;
        /// <summary>Look: 0 fern, 1 bush, 2 tall stalk, 3 berry bush (<see cref="SurfacePlants.BerryBush"/>: berries fall under it, Milestone 11).</summary>
        public byte Kind;
        /// <summary>Random variation for the sprite (presentation).</summary>
        public byte Variant;
    }

    /// <summary>
    /// Where surface plants grow: derived from the seed and the world size only (not saved), so world generation (roots)
    /// and the running colony (leaves, drawing) agree.
    /// </summary>
    public static class SurfacePlants
    {
        /// <summary><see cref="SurfacePlant.Kind"/> of a berry bush.</summary>
        public const byte BerryBush = 3;

        /// <summary>
        /// Plants every <c>plantGap</c> columns, not by the nest shaft and not in or right beside lakes and rivers. A share
        /// (<c>berryBushShare</c>, by its own hash so the other plants keep their looks) are berry bushes.
        /// </summary>
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed) =>
            Place(width, gen, seed, SurfaceWater.Place(width, gen, seed));

        /// <summary>The same, around the given lakes and rivers (a world saved before Milestone 11 has none).</summary>
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed, List<WaterBody> water)
        {
            var plants = new List<SurfacePlant>();
            var rng = new Random(math.hash(new uint2(seed, 0x91A47u)) | 1u);
            int lo = math.max(2, gen.plantGap.x), hi = math.max(lo, gen.plantGap.y);
            int nest = width / 2, clear = gen.plantClearance + gen.shaftHalfWidth;
            int edge = gen.bedrockThickness + 2;
            for (int x = edge + rng.NextInt(lo); x < width - edge; x += rng.NextInt(lo, hi + 1))
            {
                if (math.abs(x - nest) <= clear) continue;
                var plant = new SurfacePlant { X = x, Kind = (byte)rng.NextInt(3), Variant = (byte)rng.NextInt(256) };
                if (SurfaceWater.At(water, x, 2) >= 0) continue;
                if ((math.hash(new uint2(seed, (uint)x * 0x9E37u + 0xBE77u)) & 0xFFFF) < gen.berryBushShare * 65536f) plant.Kind = BerryBush;
                plants.Add(plant);
            }
            return plants;
        }

        /// <summary>A plant lives while the ground it stands on is intact and the cell above it is open sky.</summary>
        public static bool IsAlive(WorldGrid world, in SurfacePlant plant)
        {
            int x = plant.X, y = world.SurfaceY[x];
            if (!world.InBounds(x, y + 1)) return false;
            var ground = world[x, y];
            var above = world[x, y + 1];
            return !ground.IsOpen && above.IsOpen && above.Material == ContentDB.AirIndex;
        }
    }
}
