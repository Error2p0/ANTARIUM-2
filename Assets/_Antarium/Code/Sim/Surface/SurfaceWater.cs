using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A lake or river on the surface (Milestone 11): a basin across columns <see cref="X0"/>..<see cref="X1"/>.</summary>
    public struct WaterBody
    {
        /// <summary>First and last column of the basin (its banks are the columns just outside).</summary>
        public int X0, X1;
        /// <summary>Cells the deepest point lies below the lower bank.</summary>
        public int Depth;
        /// <summary>A river: a flat bed with steep banks (seen end-on); else a lake: a round bowl.</summary>
        public bool River;

        public bool Contains(int x) => x >= X0 && x <= X1;
    }

    /// <summary>
    /// Where lakes and rivers lie: derived from the seed and the world size only, like <see cref="SurfacePlants"/>, so world
    /// generation, plants and the running colony agree. Kept <c>waterClearance</c> columns from the nest shaft (raids and
    /// night hunters appear closer than that, so they can always walk to the nest) and <c>waterSpacing</c> apart.
    /// </summary>
    public static class SurfaceWater
    {
        public static List<WaterBody> Place(int width, WorldGenSettings gen, uint seed)
        {
            var bodies = new List<WaterBody>();
            var rng = new Random(math.hash(new uint2(seed, 0x1A4Eu)) | 1u);
            int lakes = Count(ref rng, gen.lakeCount), rivers = Count(ref rng, gen.riverCount);
            int nest = width / 2, edge = gen.bedrockThickness + 4;
            int spacing = math.max(2, gen.waterSpacing), clear = gen.waterClearance + gen.shaftHalfWidth;
            for (int n = 0; n < lakes + rivers; n++)
            {
                bool river = n >= lakes;
                var w = river ? gen.riverWidth : gen.lakeWidth;
                var d = river ? gen.riverDepth : gen.lakeDepth;
                int lo = math.max(5, w.x), hi = math.max(lo, w.y);
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int size = rng.NextInt(lo, hi + 1);
                    int x0 = rng.NextInt(edge, math.max(edge + 1, width - edge - size));
                    int x1 = x0 + size - 1;
                    if (x1 >= width - edge || x1 >= nest - clear && x0 <= nest + clear) continue;
                    bool free = true;
                    foreach (var b in bodies)
                        if (x0 <= b.X1 + spacing && x1 >= b.X0 - spacing) { free = false; break; }
                    if (!free) continue;
                    int depthLo = math.max(1, d.x);
                    bodies.Add(new WaterBody { X0 = x0, X1 = x1, Depth = rng.NextInt(depthLo, math.max(depthLo, d.y) + 1), River = river });
                    break;
                }
            }
            bodies.Sort((a, b) => a.X0.CompareTo(b.X0));
            return bodies;
        }

        static int Count(ref Random rng, UnityEngine.Vector2Int range)
        {
            int lo = math.max(0, range.x);
            return rng.NextInt(lo, math.max(lo, range.y) + 1);
        }

        /// <summary>Index of the body whose basin or banks (<paramref name="margin"/> columns) cover column <paramref name="x"/>, or -1.</summary>
        public static int At(List<WaterBody> bodies, int x, int margin = 0)
        {
            for (int i = 0; i < bodies.Count; i++)
                if (x >= bodies[i].X0 - margin && x <= bodies[i].X1 + margin) return i;
            return -1;
        }

        /// <summary>How far below the lower bank the bed lies at column <paramref name="x"/> of the body (0 at the banks).</summary>
        public static int BedDepth(in WaterBody b, int x)
        {
            int width = b.X1 - b.X0 + 2;
            float t = (x - b.X0 + 1) / (float)width; // 0 and 1 are the bank columns
            if (b.River)
            {
                // Steep banks over the outer 2 cells, then a flat bed.
                int fromBank = math.min(x - b.X0, b.X1 - x) + 1;
                return math.min(b.Depth, (int)math.round(b.Depth * math.min(1f, fromBank / 2.5f)));
            }
            float bowl = math.sqrt(math.saturate(1f - (2f * t - 1f) * (2f * t - 1f)));
            return math.max(1, (int)math.round(b.Depth * bowl));
        }
    }
}
