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
    }

    /// <summary>An item riding a transport cell. <see cref="T"/> runs 0 → 1 along the cell.</summary>
    public struct ItemSlot
    {
        public ushort Item;
        public float T;
        /// <summary>Position in cell space at the end of the previous / current tick (render interpolation).</summary>
        public float2 Prev;
        public float2 Pos;

        public bool IsEmpty => Item == ContentDB.NoItem;
        public static ItemSlot Empty => new() { Item = ContentDB.NoItem };
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
        /// <summary>Belts and lifts: flow direction. Filters: exit for the filtered item.</summary>
        public int2 Dir;
        public BuildingState State;
        public float WorkLeft;
        public float WorkTotal;
        public bool Alive;

        // Transport
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

        public bool IsActive => Alive && State == BuildingState.Active;
        /// <summary>Belts, lifts, splitters and filters.</summary>
        public bool IsTransport => Kind <= BuildingKind.Filter;
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
        /// <summary>Engineers tending it this tick (each adds <see cref="BuildingSystem.EngineerBoost"/> speed).</summary>
        public int Tenders;

        public Machine(int building, BuildingData data)
        {
            Building = building;
            Data = data;
            Inputs = new int[data.InputItems.Length];
        }

        public int InputIndex(ushort item) => System.Array.IndexOf(Data.InputItems, item);

        /// <summary>Free buffer space for <paramref name="item"/> (0 if it is not an input).</summary>
        public int Room(ushort item)
        {
            int i = InputIndex(item);
            return i < 0 ? 0 : math.max(0, Data.InputCaps[i] - Inputs[i]);
        }

        public bool TryInsert(ushort item)
        {
            int i = InputIndex(item);
            if (i < 0 || Inputs[i] >= Data.InputCaps[i]) return false;
            Inputs[i]++;
            return true;
        }
    }
}
