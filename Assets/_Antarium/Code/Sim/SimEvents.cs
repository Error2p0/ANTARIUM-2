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

namespace Antarium.Sim
{
    /// <summary>An item pile appeared or grew (dug-out drop or surface forage).</summary>
    public readonly struct ItemDroppedEvent
    {
        public readonly int2 Cell;
        public readonly ushort Item;
        public ItemDroppedEvent(int2 cell, ushort item) { Cell = cell; Item = item; }
    }

    /// <summary>A worker delivered an item to the royal chamber or a storage zone.</summary>
    public readonly struct ItemDeliveredEvent
    {
        public readonly int2 Cell;
        public readonly ushort Item;
        public ItemDeliveredEvent(int2 cell, ushort item) { Cell = cell; Item = item; }
    }

    public readonly struct EggLaidEvent
    {
        public readonly int2 Cell;
        public EggLaidEvent(int2 cell) { Cell = cell; }
    }

    public readonly struct AntHatchedEvent
    {
        public readonly int2 Cell;
        public AntHatchedEvent(int2 cell) { Cell = cell; }
    }

    public readonly struct BuildingPlacedEvent
    {
        public readonly int Id;
        public readonly int Def;
        public readonly int2 Origin;
        public BuildingPlacedEvent(int id, int def, int2 origin) { Id = id; Def = def; Origin = origin; }
    }

    public readonly struct BuildingCompletedEvent
    {
        public readonly int Id;
        public readonly int Def;
        public readonly int2 Origin;
        public BuildingCompletedEvent(int id, int def, int2 origin) { Id = id; Def = def; Origin = origin; }
    }

    public readonly struct BuildingRemovedEvent
    {
        public readonly int Def;
        public readonly int2 Origin;
        public BuildingRemovedEvent(int def, int2 origin) { Def = def; Origin = origin; }
    }

    /// <summary>A placement command was refused (the HUD explains why).</summary>
    public readonly struct PlaceFailedEvent
    {
        public readonly int Def;
        public readonly int2 Origin;
        public readonly PlaceResult Result;
        public PlaceFailedEvent(int def, int2 origin, PlaceResult result) { Def = def; Origin = origin; Result = result; }
    }

    /// <summary>A machine finished a craft.</summary>
    public readonly struct ItemCraftedEvent
    {
        public readonly int2 Origin;
        public readonly ushort Item;
        public ItemCraftedEvent(int2 origin, ushort item) { Origin = origin; Item = item; }
    }
}
