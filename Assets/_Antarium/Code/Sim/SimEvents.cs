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

    /// <summary>A machine finished a craft. <see cref="Item"/> is the first product (NoItem if it only made knowledge).</summary>
    public readonly struct ItemCraftedEvent
    {
        public readonly int2 Origin;
        public readonly ushort Item;
        public readonly float Knowledge;
        public ItemCraftedEvent(int2 origin, ushort item, float knowledge) { Origin = origin; Item = item; Knowledge = knowledge; }
    }

    /// <summary>A research node was completed (by the player; not when loading a save).</summary>
    public readonly struct ResearchCompletedEvent
    {
        public readonly int Research;
        public ResearchCompletedEvent(int research) { Research = research; }
    }

    /// <summary>The player tried to research something and was refused.</summary>
    public readonly struct ResearchFailedEvent
    {
        public readonly int Research;
        public readonly ResearchResult Result;
        public ResearchFailedEvent(int research, ResearchResult result) { Research = research; Result = result; }
    }

    /// <summary>A contract reward was claimed.</summary>
    public readonly struct ContractClaimedEvent
    {
        public readonly int Contract;
        public ContractClaimedEvent(int contract) { Contract = contract; }
    }

    /// <summary>An active ability was used at <see cref="Cell"/>. <see cref="Amount"/>: cells revealed / eggs laid.</summary>
    public readonly struct AbilityUsedEvent
    {
        public readonly int Ability;
        public readonly int2 Cell;
        public readonly int Amount;
        public AbilityUsedEvent(int ability, int2 cell, int amount) { Ability = ability; Cell = cell; Amount = amount; }
    }

    public readonly struct AbilityFailedEvent
    {
        public readonly int Ability;
        public readonly AbilityResult Result;
        public AbilityFailedEvent(int ability, AbilityResult result) { Ability = ability; Result = result; }
    }

    public enum RandomEventPhase : byte { Started, Won, Lost }

    /// <summary>A random event started, or ended in the player's favour or not.</summary>
    public readonly struct RandomEventEvent
    {
        public readonly int Event;
        public readonly int Id;
        public readonly RandomEventPhase Phase;
        public readonly int2 Cell;
        public RandomEventEvent(int ev, int id, RandomEventPhase phase, int2 cell) { Event = ev; Id = id; Phase = phase; Cell = cell; }
    }
}
