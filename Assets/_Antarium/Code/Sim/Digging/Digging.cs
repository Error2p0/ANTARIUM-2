using Antarium.Content;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public readonly struct DigCommand
    {
        /// <summary>Brush centre in cell space (1 unit = 1 cell).</summary>
        public readonly float2 CellPos;
        public readonly float Radius;
        /// <summary>Hardness units removed at the brush centre.</summary>
        public readonly float Power;

        public DigCommand(float2 cellPos, float radius, float power)
        {
            CellPos = cellPos;
            Radius = radius;
            Power = power;
        }
    }

    public static class Digging
    {
        /// <summary>
        /// Applies a circular dig brush. Only revealed, destructible, non-empty cells are affected.
        /// Damage falls off linearly from the centre to <paramref name="edgeFactor"/> at the rim.
        /// Broken cells are appended to <paramref name="broken"/>.
        /// </summary>
        public static int Apply(WorldGrid world, NativeArray<MaterialSim> materials, in DigCommand cmd,
            float edgeFactor, EventBus events, NativeList<int2> broken)
        {
            int damaged = 0;
            int2 min = (int2)math.floor(cmd.CellPos - cmd.Radius);
            int2 max = (int2)math.ceil(cmd.CellPos + cmd.Radius);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!world.InBounds(x, y)) continue;
                float dist = math.distance(new float2(x + 0.5f, y + 0.5f), cmd.CellPos);
                if (dist > cmd.Radius) continue;

                int i = world.Index(x, y);
                var cell = world.Cells[i];
                if (cell.Density == 0 || !cell.IsRevealed) continue;
                var mat = materials[cell.Material];
                if (mat.IsIndestructible || mat.IsAir) continue;

                float falloff = math.lerp(1f, edgeFactor, dist / cmd.Radius);
                int loss = (int)math.ceil(cmd.Power * falloff / mat.Hardness * Cell.Full);
                if (loss <= 0) continue;

                cell.Density = (byte)math.max(0, cell.Density - loss);
                bool isBroken = cell.Density == 0;
                if (isBroken)
                {
                    cell.Flags |= CellFlags.Explored;
                    broken.Add(new int2(x, y));
                }
                world.Cells[i] = cell;
                world.MarkChanged(x, y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
                events.Publish(new CellDamagedEvent(new int2(x, y), cell.Material, cell.Density, isBroken));
                damaged++;
            }
            return damaged;
        }
    }
}
