using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum BuildingState : byte
    {
        /// <summary>Placed and paid for; workers still have to build it.</summary>
        Blueprint,
        Active,
    }

    public enum MachineStatus : byte
    {
        /// <summary>Waiting for inputs.</summary>
        Idle,
        Working,
        /// <summary>Finished products have nowhere to go (output belt full or pile at the limit).</summary>
        OutputBlocked,
    }

    /// <summary>Where the front item of a belt or lift goes when there is no next transport cell.</summary>
    public enum SinkKind : byte
    {
        None,
        /// <summary>The line ends in the royal chamber or a storage zone.</summary>
        Stock,
        /// <summary>The line ends in a machine (see <see cref="Building.Outputs"/>.x).</summary>
        Machine,
    }

    public enum PlaceResult : byte
    {
        Ok,
        /// <summary>Re-oriented an existing transport cell instead of placing a new one.</summary>
        Rotated,
        /// <summary>The same building with the same direction is already there; nothing to do.</summary>
        Unchanged,
        OutOfBounds,
        Hidden,
        NotOpen,
        Occupied,
        NeedsFloor,
        CantAfford,
        UnknownBuilding,
        /// <summary>Still locked behind research.</summary>
        Locked,
        /// <summary>The cell is full of water, gas or lava.</summary>
        Flooded,
        /// <summary>Walls can't go into the royal chamber or a storage zone.</summary>
        Protected,
        /// <summary>Only one of this building per colony (the Ancient Engine).</summary>
        Unique,
        /// <summary>Must be built in another biome (the Ancient Engine: in the ruins).</summary>
        WrongBiome,
        /// <summary>A Crystal Resonator must touch conductive rock (glow crystals, copper or iron ore).</summary>
        NeedsCrystal,
        /// <summary>Removing: the Ancient Engine can't be taken down once it stands.</summary>
        Permanent,
        /// <summary>The Queen's Throne must be built around the queen (her cell in its bottom row).</summary>
        NeedsQueen,
    }

    /// <summary>An item riding a transport cell. <see cref="T"/> runs 0 → 1 along the cell.</summary>
    public struct ItemSlot
    {
        public ushort Item;
        public float T;
        /// <summary>Building the item came from (-1: loaded by an ant, caught, unloaded). Routers never hand it back.</summary>
        public int From;
        /// <summary>Position in cell space at the end of the previous / current tick (render interpolation).</summary>
        public float2 Prev;
        public float2 Pos;

        public bool IsEmpty => Item == ContentDB.NoItem;
        public static ItemSlot Empty => new() { Item = ContentDB.NoItem, From = -1 };
    }

    /// <summary>
    /// One placed building. Plain data in a native list so rendering can read it from a Burst job.
    /// Transport cells (belts, lifts, splitters, filters) hold up to two items; machines keep their extra
    /// state in a <see cref="Machine"/>.
    /// </summary>
    public struct Building
    {
        public int Def;
        public BuildingKind Kind;
        public int2 Origin;
        public int2 Size;
        /// <summary>Belts and lifts: flow direction. Filters: exit for the filtered item. Unloaders: where items go (they come from behind).</summary>
        public int2 Dir;
        public BuildingState State;
        public float WorkLeft;
        public float WorkTotal;
        public bool Alive;

        // Transport
        /// <summary>Filters: the filtered item. Unloaders: the item they take (<see cref="ContentDB.NoItem"/> = any). Emitters: pheromone kind.</summary>
        public ushort Filter;
        /// <summary>Front item (further along the cell) and the one behind it.</summary>
        public ItemSlot Front, Back;
        /// <summary>Building ids items can leave to, or -1. Belts/lifts use x; filters keep their arrow exit in x.</summary>
        public int4 Outputs;
        public SinkKind Sink;
        /// <summary>Bit per item: items that can reach a sink from here.</summary>
        public ulong Mask;
        public byte RoundRobin;
        /// <summary>Seconds the front item has waited at the end of the cell.</summary>
        public float Stuck;
        public bool Jammed;
        public int Passed;
        /// <summary>Items per minute that left this cell (smoothed).</summary>
        public float Rate;

        // Machines
        public int Machine;
        public MachineStatus Status;
        /// <summary>0..1 progress of the running recipe.</summary>
        public float Craft;

        // Defense
        /// <summary>Gate: hit points left.</summary>
        public float Health;
        /// <summary>Trap: seconds until it is armed again (0 = armed).</summary>
        public float Rearm;

        // Ancient Engine (10.5)
        /// <summary>Restoration steps finished (0 = the bare ruin after placing it). Picks the sprite.</summary>
        public byte Stage;
        /// <summary>A restoration step is paid and waits for workers (the building is a blueprint meanwhile).</summary>
        public bool Restoring;

        public bool IsActive => Alive && State == BuildingState.Active;
        /// <summary>Belts, lifts, splitters and filters.</summary>
        public bool IsTransport => Kind <= BuildingKind.Filter;
        /// <summary>Splitters and filters: hand items to several neighbours.</summary>
        public bool IsRouter => Kind == BuildingKind.Splitter || Kind == BuildingKind.Filter;
        /// <summary>Belts and lifts: carry items along their direction.</summary>
        public bool IsLine => Kind == BuildingKind.Belt || Kind == BuildingKind.Lift;
        /// <summary>An intact gate: ants pass, enemies are stopped.</summary>
        public bool IsClosedGate => Kind == BuildingKind.Gate && IsActive;
        public bool Contains(int2 c) => math.all(c >= Origin) && math.all(c < Origin + Size);
    }

    /// <summary>Recipe state of a processing building.</summary>
    public sealed class Machine
    {
        public readonly int Building;
        public readonly BuildingData Data;
        /// <summary>Buffered inputs, parallel to <see cref="BuildingData.InputItems"/>.</summary>
        public readonly int[] Inputs;
        public readonly List<ushort> Pending = new();
        public int Recipe = -1;
        public float Progress;
        public int NextRecipe;
        public MachineStatus Status;
        public int2 OutputCell;
        /// <summary>Transport building that takes products away, or -1 (products are left as a pile).</summary>
        public int OutputBelt = -1;
        public int Crafted;
        /// <summary>Bit per input item with free buffer space (tracked to rebuild consumer fields on change).</summary>
        public ulong NeedMask;
        /// <summary>Engineers tending it this tick.</summary>
        public int Tenders;
        /// <summary>Their combined skill: each adds 1 (+ its experience bonus); times <see cref="BuildingSystem.EngineerBoost"/> is the speed-up.</summary>
        public float TendPower;
        /// <summary>The only recipe it runs (index into <see cref="BuildingData.Recipes"/>), or -1 for any it can.</summary>
        public int Choice { get; private set; } = -1;
        /// <summary>Bit per item it takes in now: every input, or only the chosen recipe's (fuel left out while heated).</summary>
        public ulong ActiveMask { get; private set; }
        /// <summary>Lava, magma rock or fire touches it (machines with a heat bonus only): faster, and fuel is not needed.</summary>
        public bool Heated { get; private set; }
        /// <summary>Water stands in or next to it (machines with an irrigation bonus; shown in the hover panel, crafts check again).</summary>
        public bool Irrigated;
        /// <summary>Extra speed from the best turning water wheel nearby (0.5 = +50%).</summary>
        public float WheelBoost;
        /// <summary>Extra speed from the best resonating Crystal Resonator nearby (10.5).</summary>
        public float ResonanceBoost;
        /// <summary>Irrigation bonus not paid out yet (a whole extra batch comes every 1 / bonus crafts).</summary>
        public float IrrigationCarry;
        ulong _choiceMask;
        ulong _fuelMask;

        public Machine(int building, BuildingData data)
        {
            Building = building;
            Data = data;
            Inputs = new int[data.InputItems.Length];
            ActiveMask = _choiceMask = data.InputMask;
        }

        /// <summary>Runs only recipe <paramref name="choice"/> (index into the building's recipes; -1 = any). Its other inputs stay buffered.</summary>
        public void SetChoice(int choice, ContentDB content)
        {
            if (choice < -1 || choice >= Data.Recipes.Length) choice = -1;
            Choice = choice;
            if (choice < 0) _choiceMask = Data.InputMask;
            else
            {
                ulong mask = 0;
                foreach (ushort item in content.Recipes[Data.Recipes[choice]].InputItems) mask |= 1ul << item;
                _choiceMask = mask;
            }
            ActiveMask = _choiceMask & ~(Heated ? _fuelMask : 0ul);
        }

        /// <summary>Heat on or off (fuel items stop or start being taken in). Returns true if it changed.</summary>
        public bool SetHeated(bool heated, ulong fuelMask)
        {
            if (heated == Heated && fuelMask == _fuelMask) return false;
            Heated = heated;
            _fuelMask = fuelMask;
            ActiveMask = _choiceMask & ~(Heated ? _fuelMask : 0ul);
            return true;
        }

        /// <summary>A fuel input the machine skips while heated.</summary>
        public bool SkipsFuel(ushort item) => Heated && (_fuelMask & (1ul << item)) != 0;

        public int InputIndex(ushort item) => System.Array.IndexOf(Data.InputItems, item);

        bool Takes(ushort item) => (ActiveMask & (1ul << item)) != 0;

        /// <summary>Free buffer space for <paramref name="item"/> (0 if it is not an input of a recipe it runs).</summary>
        public int Room(ushort item)
        {
            int i = InputIndex(item);
            return i < 0 || !Takes(item) ? 0 : math.max(0, Data.InputCaps[i] - Inputs[i]);
        }

        public bool TryInsert(ushort item)
        {
            int i = InputIndex(item);
            if (i < 0 || !Takes(item) || Inputs[i] >= Data.InputCaps[i]) return false;
            Inputs[i]++;
            return true;
        }
    }
}
