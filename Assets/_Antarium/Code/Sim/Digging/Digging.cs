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

    public enum DamageResult : byte
    {
        /// <summary>Nothing happened (empty, indestructible, unrevealed or out of bounds).</summary>
        None,
        Damaged,
        /// <summary>The cell crossed from solid to open space, but is not fully dug out yet.</summary>
        Opened,
        /// <summary>The cell is fully dug out.</summary>
        Broken,
    }

    public static class Digging
    {
        /// <summary>
        /// Removes <paramref name="power"/> hardness units from one cell. Only revealed, destructible,
        /// non-empty cells are affected. Publishes <see cref="CellDamagedEvent"/> and marks the chunk dirty.
        /// </summary>
        public static DamageResult DamageCell(WorldGrid world, NativeArray<MaterialSim> materials, int2 c, float power, EventBus events)
        {
            if (!world.InBounds(c.x, c.y) || power <= 0f) return DamageResult.None;
            int i = world.Index(c.x, c.y);
            var cell = world.Cells[i];
            if (cell.Density == 0 || !cell.IsRevealed) return DamageResult.None;
            var mat = materials[cell.Material];
            if (mat.IsIndestructible || mat.IsAir) return DamageResult.None;

            int loss = (int)math.ceil(power / mat.Hardness * Cell.Full);
            if (loss <= 0) return DamageResult.None;

            bool wasOpen = cell.IsOpen;
            int density = cell.Density - loss;
            // A cell is either solid (eroding from Full down to OpenThreshold) or fully dug. Never a
            // half-open state: that would be walkable but not dug, with no drop and no reveal.
            cell.Density = density < Cell.OpenThreshold ? (byte)0 : (byte)density;
            bool broken = cell.Density == 0;
            if (broken)
                cell.Flags = (cell.Flags | CellFlags.Explored) & ~CellFlags.DigDesignated;
            world.Cells[i] = cell;
            world.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
            events.Publish(new CellDamagedEvent(c, cell.Material, cell.Density, broken));

            if (broken) return DamageResult.Broken;
            return !wasOpen && cell.IsOpen ? DamageResult.Opened : DamageResult.Damaged;
        }

        /// <summary>
        /// Applies a circular dig brush. Damage falls off linearly from the centre to
        /// <paramref name="edgeFactor"/> at the rim. Broken cells are appended to <paramref name="broken"/>.
        /// Returns the number of cells damaged; <paramref name="opened"/> is set if any cell became open space.
        /// </summary>
        public static int Apply(WorldGrid world, NativeArray<MaterialSim> materials, in DigCommand cmd,
            float edgeFactor, EventBus events, NativeList<int2> broken, out bool opened)
        {
            int damaged = 0;
            opened = false;
            int2 min = (int2)math.floor(cmd.CellPos - cmd.Radius);
            int2 max = (int2)math.ceil(cmd.CellPos + cmd.Radius);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                float dist = math.distance(new float2(x + 0.5f, y + 0.5f), cmd.CellPos);
                if (dist > cmd.Radius) continue;
                float falloff = math.lerp(1f, edgeFactor, dist / cmd.Radius);
                var result = DamageCell(world, materials, new int2(x, y), cmd.Power * falloff, events);
                if (result == DamageResult.None) continue;
                damaged++;
                if (result == DamageResult.Broken) broken.Add(new int2(x, y));
                if (result != DamageResult.Damaged) opened = true;
            }
            return damaged;
        }

        public static int Apply(WorldGrid world, NativeArray<MaterialSim> materials, in DigCommand cmd,
            float edgeFactor, EventBus events, NativeList<int2> broken) =>
            Apply(world, materials, cmd, edgeFactor, events, broken, out _);
    }
}
