using System.Runtime.CompilerServices;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Crawling movement rules, usable from Burst jobs.
    ///
    /// Like ants in an ant farm, workers crawl on any surface: the floor, walls and ceiling of a tunnel and
    /// the back wall behind it, so every dug-out cell is walkable. Open sky has no back wall, so above
    /// ground ants must touch the ground. Diagonal steps may not squeeze between two solid cells, and in
    /// the sky they may only wrap around a corner. The rules are symmetric, so distance fields can be
    /// descended by any ant.
    /// </summary>
    public readonly struct NavGrid
    {
        [ReadOnly] public readonly NativeArray<Cell> Cells;
        public readonly int Width;
        public readonly int Height;
        public readonly int ChunksX;

        public const int DirectionCount = 8;

        /// <summary>The 8 neighbour offsets: 0-3 straight, 4-7 diagonal.</summary>
        public static int2 Direction(int i) => i switch
        {
            0 => new int2(1, 0),
            1 => new int2(-1, 0),
            2 => new int2(0, 1),
            3 => new int2(0, -1),
            4 => new int2(1, 1),
            5 => new int2(-1, 1),
            6 => new int2(1, -1),
            _ => new int2(-1, -1),
        };

        public NavGrid(WorldGrid world)
        {
            Cells = world.Cells;
            Width = world.Width;
            Height = world.Height;
            ChunksX = world.ChunksX;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int2 c) => (uint)c.x < (uint)Width && (uint)c.y < (uint)Height;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Index(int2 c) => GridIndex.Of(c.x, c.y, ChunksX);

        /// <summary>Out-of-bounds counts as solid.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsSolid(int2 c) => !InBounds(c) || !Cells[Index(c)].IsOpen;

        /// <summary>Underground cells have a back wall (their material is kept after digging); sky does not.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBackWall(int2 c) => InBounds(c) && Cells[Index(c)].Material != ContentDB.AirIndex;

        public bool TouchesSolid(int2 c) =>
            IsSolid(c + new int2(1, 0)) || IsSolid(c + new int2(-1, 0))
            || IsSolid(c + new int2(0, 1)) || IsSolid(c + new int2(0, -1));

        public bool IsWalkable(int2 c)
        {
            if (!InBounds(c) || !Cells[Index(c)].IsOpen) return false;
            return HasBackWall(c) || TouchesSolid(c);
        }

        /// <summary>True if an ant standing on walkable <paramref name="from"/> can step by <paramref name="d"/>.</summary>
        public bool CanMove(int2 from, int2 d)
        {
            int2 to = from + d;
            if (!IsWalkable(to)) return false;
            if (d.x == 0 || d.y == 0) return true;
            int2 sideA = from + new int2(d.x, 0);
            int2 sideB = from + new int2(0, d.y);
            bool a = IsSolid(sideA);
            bool b = IsSolid(sideB);
            if (a && b) return false;      // crack between two solid cells
            if (a != b) return true;       // wrapping a corner or climbing a step
            return IsWalkable(sideA) && IsWalkable(sideB); // across the back wall
        }

        /// <summary>Unit vector pointing away from the surface the ant clings to (floor first, then walls, then ceiling).</summary>
        public int2 SurfaceUp(int2 c)
        {
            if (IsSolid(c + new int2(0, -1))) return new int2(0, 1);
            if (IsSolid(c + new int2(1, 0))) return new int2(-1, 0);
            if (IsSolid(c + new int2(-1, 0))) return new int2(1, 0);
            if (IsSolid(c + new int2(0, 1))) return new int2(0, -1);
            return new int2(0, 1);
        }
    }
}
