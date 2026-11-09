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
        /// <summary>Look: 0 fern, 1 bush, 2 tall stalk (presentation).</summary>
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
        public static List<SurfacePlant> Place(int width, WorldGenSettings gen, uint seed)
        {
            var plants = new List<SurfacePlant>();
            var rng = new Random(math.hash(new uint2(seed, 0x91A47u)) | 1u);
            int lo = math.max(2, gen.plantGap.x), hi = math.max(lo, gen.plantGap.y);
            int nest = width / 2, clear = gen.plantClearance + gen.shaftHalfWidth;
            int edge = gen.bedrockThickness + 2;
            for (int x = edge + rng.NextInt(lo); x < width - edge; x += rng.NextInt(lo, hi + 1))
            {
                if (math.abs(x - nest) <= clear) continue;
                plants.Add(new SurfacePlant { X = x, Kind = (byte)rng.NextInt(3), Variant = (byte)rng.NextInt(256) });
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
