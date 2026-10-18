using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A cell lost density from digging. <see cref="Broken"/> = it is now fully dug out.</summary>
    public readonly struct CellDamagedEvent
    {
        public readonly int2 Cell;
        public readonly ushort Material;
        public readonly byte Density;
        public readonly bool Broken;

        public CellDamagedEvent(int2 cell, ushort material, byte density, bool broken)
        {
            Cell = cell;
            Material = material;
            Density = density;
            Broken = broken;
        }
    }

    /// <summary>Summary of one resolved dig command.</summary>
    public readonly struct DigResolvedEvent
    {
        public readonly float2 CellPos;
        public readonly int CellsDamaged;
        public readonly int CellsBroken;
        public readonly int CaveCellsDiscovered;

        public DigResolvedEvent(float2 cellPos, int damaged, int broken, int caveCells)
        {
            CellPos = cellPos;
            CellsDamaged = damaged;
            CellsBroken = broken;
            CaveCellsDiscovered = caveCells;
        }
    }
}
