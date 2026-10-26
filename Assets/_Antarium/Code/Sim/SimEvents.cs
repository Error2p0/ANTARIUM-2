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
        public readonly Antarium.Content.AntCaste Caste;
        public AntHatchedEvent(int2 cell, Antarium.Content.AntCaste caste = Antarium.Content.AntCaste.Worker) { Cell = cell; Caste = caste; }
    }

    /// <summary>The player took control of an ant, let go, lost it, or clicked where there was none.</summary>
    public readonly struct PossessionEvent
    {
        public readonly PossessionPhase Phase;
        public readonly Antarium.Content.AntCaste Caste;
        public readonly int2 Cell;
        public PossessionEvent(PossessionPhase phase, Antarium.Content.AntCaste caste, int2 cell) { Phase = phase; Caste = caste; Cell = cell; }
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

namespace Antarium.Sim
{
    /// <summary>An ant died (and was removed). <see cref="Killer"/> is the enemy definition for <see cref="DeathCause.Killed"/>, else -1.</summary>
    public readonly struct AntDiedEvent
    {
        public readonly int2 Cell;
        public readonly Antarium.Content.AntCaste Caste;
        public readonly DeathCause Cause;
        public readonly int Killer;
        public AntDiedEvent(int2 cell, Antarium.Content.AntCaste caste, DeathCause cause, int killer) { Cell = cell; Caste = caste; Cause = cause; Killer = killer; }
    }

    public readonly struct EnemyKilledEvent
    {
        public readonly int Def;
        public readonly int2 Cell;
        public EnemyKilledEvent(int def, int2 cell) { Def = def; Cell = cell; }
    }

    /// <summary>A raider got away with <see cref="Food"/> food.</summary>
    public readonly struct EnemyEscapedEvent
    {
        public readonly int Def;
        public readonly int2 Cell;
        public readonly float Food;
        public EnemyEscapedEvent(int def, int2 cell, float food) { Def = def; Cell = cell; Food = food; }
    }

    /// <summary>The colony broke into a cave where <see cref="Count"/> creatures live (the first is <see cref="Def"/>).</summary>
    public readonly struct CreaturesFoundEvent
    {
        public readonly int Def;
        public readonly int Count;
        public readonly int2 Cell;
        public CreaturesFoundEvent(int def, int count, int2 cell) { Def = def; Count = count; Cell = cell; }
    }

    public enum RaidPhase : byte
    {
        /// <summary>Raiders will arrive in <c>raidWarningSeconds</c>.</summary>
        Warning,
        Started,
        /// <summary>Every raider was killed before taking any food.</summary>
        Repelled,
        /// <summary>The raid is over, but raiders got away with food.</summary>
        Ended,
    }

    public readonly struct RaidEvent
    {
        public readonly RaidPhase Phase;
        public readonly int Id;
        /// <summary>Where the raiders come from (on the surface).</summary>
        public readonly int2 Cell;
        public readonly int Size;
        public readonly float FoodLost;
        public RaidEvent(RaidPhase phase, int id, int2 cell, int size, float foodLost) { Phase = phase; Id = id; Cell = cell; Size = size; FoodLost = foodLost; }
    }

    public readonly struct TrapTriggeredEvent
    {
        public readonly int2 Cell;
        public readonly int Enemy;
        public TrapTriggeredEvent(int2 cell, int enemy) { Cell = cell; Enemy = enemy; }
    }

    /// <summary>Enemies broke a gate; it is a blueprint again until workers repair it.</summary>
    public readonly struct GateBrokenEvent
    {
        public readonly int2 Cell;
        public GateBrokenEvent(int2 cell) { Cell = cell; }
    }

    /// <summary>A cell of unstable material (sand) fell into <see cref="Cell"/>.</summary>
    public readonly struct CaveInEvent
    {
        public readonly int2 Cell;
        public readonly ushort Material;
        public CaveInEvent(int2 cell, ushort material) { Cell = cell; Material = material; }
    }

    /// <summary>Lava met water at <see cref="Cell"/> and turned to stone.</summary>
    public readonly struct FluidReactionEvent
    {
        public readonly int2 Cell;
        public FluidReactionEvent(int2 cell) { Cell = cell; }
    }

    /// <summary>A wall was finished: its cell is solid again.</summary>
    public readonly struct WallBuiltEvent
    {
        public readonly int2 Cell;
        public WallBuiltEvent(int2 cell) { Cell = cell; }
    }
}

namespace Antarium.Sim
{
    /// <summary>A level of a genetic trait was bought.</summary>
    public readonly struct GeneticBoughtEvent
    {
        public readonly int Trait;
        public readonly int Level;
        public GeneticBoughtEvent(int trait, int level) { Trait = trait; Level = level; }
    }

    /// <summary>Buying a genetic trait was refused.</summary>
    public readonly struct GeneticFailedEvent
    {
        public readonly int Trait;
        public readonly GeneticResult Result;
        public GeneticFailedEvent(int trait, GeneticResult result) { Trait = trait; Result = result; }
    }

    /// <summary>A princess chamber raised a princess; <see cref="Total"/> are now waiting.</summary>
    public readonly struct PrincessRaisedEvent
    {
        public readonly int2 Cell;
        public readonly int Total;
        public PrincessRaisedEvent(int2 cell, int total) { Cell = cell; Total = total; }
    }
}
