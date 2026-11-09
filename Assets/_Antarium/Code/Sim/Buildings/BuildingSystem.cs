using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum WorkResult : byte { None, Working, Completed }

    /// <summary>
    /// Placed buildings and the transport network. Belts and lifts move items cell to cell and hand them
    /// to the next transport cell, a machine, or the colony stock at the end of a line; splitters and
    /// filters route items to their neighbours. Every transport cell knows which items can reach a sink
    /// from it (<see cref="Building.Mask"/>), so items are only loaded where they can be delivered.
    /// Machines run recipes from their input buffers and push products onto an output belt or leave them
    /// as a pile for workers. Defense buildings (gates, traps, guard posts, walls) are single cells that
    /// never carry items.
    /// </summary>
    public sealed partial class BuildingSystem : IDisposable
    {
        public const int None = -1;

        readonly ContentDB _content;
        readonly LogisticsConfig _config;
        readonly CombatConfig _combat;
        readonly WorldGrid _world;
        readonly Colony _colony;
        readonly EventBus _events;
        readonly float _spacing;

        public NativeList<Building> All;
        readonly Stack<int> _free = new();
        readonly Dictionary<int, int> _cells = new();
        readonly List<Machine> _machines = new();
        readonly Stack<int> _freeMachines = new();
        readonly List<int> _order = new();
        /// <summary>Finished unloaders (rebuilt with the network).</summary>
        readonly List<int> _unloaders = new();
        /// <summary>Finished chests (rebuilt with the network).</summary>
        readonly List<int> _chests = new();
        readonly List<ushort> _leftover = new();
        readonly int[] _room;
        /// <summary>
        /// Routers (splitters, filters): items each can deliver, per side the item came in from (5 per building: the four
        /// neighbours, then "from nowhere"). Routers never hand an item back where it came from, so a router next to a router
        /// only counts what the other one can deliver without going back.
        /// </summary>
        ulong[] _maskFrom = new ulong[0];
        bool _networkDirty = true;
        float _rateTimer;
        float _beltSpeed = 1f;

        /// <summary>Bit per item index, all items set.</summary>
        public readonly ulong AllItems;

        // Change flags for the flow fields; read and cleared by the simulation.
        public bool BlueprintsChanged = true;
        public bool DropoffChanged = true;
        /// <summary>Bit per item whose consumer field must be rebuilt.</summary>
        public ulong ConsumersChanged = ulong.MaxValue;
        /// <summary>Guard posts were built or removed (the guard field must be rebuilt).</summary>
        public bool GuardsChanged = true;
        /// <summary>Nurseries were built or removed (brood fields must be rebuilt).</summary>
        public bool NurseriesChanged = true;
        /// <summary>Machines were built or removed (the engineers' field must be rebuilt).</summary>
        public bool MachinesChanged = true;
        /// <summary>Extra machine speed per engineer tending it (from the engineer caste, set by the simulation).</summary>
        public float EngineerBoost;
        /// <summary>Walls finished this tick: the simulation turns their cells solid and removes them.</summary>
        public readonly List<int> CompletedWalls = new();

        public int BlueprintCount { get; private set; }
        public int BuildSlots { get; private set; }
        public int ActiveCount { get; private set; }

        public BuildingSystem(ContentDB content, LogisticsConfig config, WorldGrid world, Colony colony, EventBus events,
            CombatConfig combat = null, WaterworksConfig waterworks = null)
        {
            _content = content;
            _config = config;
            _combat = combat ?? new CombatConfig();
            _water = waterworks ?? new WaterworksConfig();
            _world = world;
            _colony = colony;
            _events = events;
            _spacing = math.clamp(config.itemSpacing, 0.5f, 1f);
            All = new NativeList<Building>(64, Allocator.Persistent);
            _room = new int[content.Items.Length];
            int items = content.Items.Length;
            AllItems = items >= 64 ? ulong.MaxValue : (1ul << items) - 1;
        }

        public static ulong Bit(ushort item) => 1ul << item;

        /// <summary>Re-links the transport network on the next tick (e.g. storage zones changed).</summary>
        public void MarkNetworkDirty() => _networkDirty = true;

        /// <summary>Transport cells in simulation order (downstream first).</summary>
        public IReadOnlyList<int> TransportOrder => _order;

        public ref Building Get(int id) => ref All.ElementAt(id);

        public Machine MachineOf(int id)
        {
            int m = All[id].Machine;
            return m >= 0 ? _machines[m] : null;
        }

        /// <summary>Building id covering <paramref name="c"/>, or <see cref="None"/>.</summary>
        public int At(int2 c) =>
            _world.InBounds(c.x, c.y) && _cells.TryGetValue(_world.Index(c.x, c.y), out int id) ? id : None;

        bool IsStock(int2 c) => _world.InBounds(c.x, c.y) && _colony.IsStockCell(c, _world.Index(c.x, c.y));

        int ActiveAt(int2 c)
        {
            int id = At(c);
            return id >= 0 && All[id].IsActive ? id : None;
        }

        // ---------------------------------------------------------------- placement

        /// <summary>Snaps a direction to what the building kind allows (belts: left/right, lifts: up/down).</summary>
        public static int2 NormalizeDir(BuildingKind kind, int2 dir)
        {
            switch (kind)
            {
                case BuildingKind.Belt: return new int2(dir.x < 0 ? -1 : 1, 0);
                case BuildingKind.Lift: return new int2(0, dir.y < 0 ? -1 : 1);
                case BuildingKind.Filter:
                case BuildingKind.Unloader:
                case BuildingKind.Pump:
                    if (dir.x == 0 && dir.y == 0) return new int2(1, 0);
                    return math.abs(dir.x) >= math.abs(dir.y) ? new int2((int)math.sign(dir.x), 0) : new int2(0, (int)math.sign(dir.y));
                default: return int2.zero;
            }
        }

        public PlaceResult CanPlace(int def, int2 origin, int2 dir) => Check(def, origin, dir, out _, out _);

        /// <summary>
        /// Placement rules. Over a building of the same kind: the same one with another direction is re-oriented
        /// (<paramref name="rotate"/>); another tier of it (iron belt over silk belt) is upgraded in place (<paramref name="upgrade"/>).
        /// </summary>
        PlaceResult Check(int def, int2 origin, int2 dir, out int rotate, out int upgrade)
        {
            rotate = None;
            upgrade = None;
            if (def < 0 || def >= _content.Buildings.Length) return PlaceResult.UnknownBuilding;
            if (!_colony.BuildingUnlocked[def]) return PlaceResult.Locked;
            var data = _content.Buildings[def];
            var bdef = data.Def;
            var size = new int2(bdef.Footprint.x, bdef.Footprint.y);
            int2 nd = NormalizeDir(bdef.kind, dir);
            var nav = new NavGrid(_world);

            bool machine = bdef.IsMultiCell;
            bool needsFloor = bdef.IsChamber || bdef.kind == BuildingKind.Trap;
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var c = origin + new int2(x, y);
                if (!_world.InBounds(c.x, c.y)) return PlaceResult.OutOfBounds;
                int index = _world.Index(c.x, c.y);
                var cell = _world.Cells[index];
                if (!cell.IsRevealed) return PlaceResult.Hidden;
                if (cell.IsOpen && nav.IsFlooded(c)) return PlaceResult.Flooded;
                if (machine ? !cell.IsOpen || cell.Material == ContentDB.AirIndex : !nav.IsWalkable(c))
                    return PlaceResult.NotOpen;
                int existing = At(c);
                if (existing >= 0)
                {
                    var e = All[existing];
                    if (machine) return PlaceResult.Occupied;
                    if (e.Def != def)
                    {
                        // Another tier of the same kind is swapped in place (chests keep their stock: never).
                        if (e.Kind != bdef.kind || bdef.kind == BuildingKind.Chest) return PlaceResult.Occupied;
                        upgrade = existing;
                    }
                    else
                    {
                        bool turns = bdef.IsTransport || bdef.kind == BuildingKind.Unloader || bdef.kind == BuildingKind.Pump;
                        if (!turns || e.Dir.Equals(nd)) return PlaceResult.Unchanged;
                        rotate = existing;
                    }
                }
                if (!bdef.IsTransport && c.Equals(_colony.QueenCell)) return PlaceResult.Occupied;
                if (bdef.kind == BuildingKind.Wall && (_colony.IsRoyalCell(c) || (cell.Flags & CellFlags.Storage) != 0))
                    return PlaceResult.Protected;
                if (bdef.kind == BuildingKind.Chest && (cell.Flags & CellFlags.Storage) != 0) return PlaceResult.Protected;
            }
            if (needsFloor)
            {
                for (int x = 0; x < size.x; x++)
                    if (!nav.IsSolid(origin + new int2(x, -1))) return PlaceResult.NeedsFloor;
            }
            if (rotate >= 0) return PlaceResult.Rotated;
            return _colony.Inventory.CanAfford(data) ? PlaceResult.Ok : PlaceResult.CantAfford;
        }

        /// <summary>Pays the cost and places a blueprint. Dragging a transport cell over one of the same type re-orients it.</summary>
        public PlaceResult Place(int def, int2 origin, int2 dir, ushort filter = 0)
        {
            var result = Check(def, origin, dir, out int rotate, out int upgrade);
            if (result == PlaceResult.Rotated)
            {
                ref var r = ref Get(rotate);
                r.Dir = NormalizeDir(r.Kind, dir);
                _networkDirty = true;
                return result;
            }
            if (result == PlaceResult.Unchanged) return result;
            if (result != PlaceResult.Ok)
            {
                _events.Publish(new PlaceFailedEvent(def, origin, result));
                return result;
            }
            if (upgrade >= 0)
            {
                Upgrade(upgrade, def, dir);
                _events.Publish(new BuildingPlacedEvent(upgrade, def, origin));
                return result;
            }

            _colony.Inventory.Spend(_content.Buildings[def]);
            int id = Create(def, origin, dir, filter);
            _events.Publish(new BuildingPlacedEvent(id, def, origin));
            return result;
        }

        /// <summary>
        /// Swaps a single-cell building for another tier of the same kind: the old one is refunded, the new one paid, and
        /// it becomes a blueprint for workers to build. Items riding a belt stay on it.
        /// </summary>
        void Upgrade(int id, int def, int2 dir)
        {
            ref var b = ref Get(id);
            _leftover.Clear();
            _colony.Inventory.Refund(_content.Buildings[b.Def], b.State == BuildingState.Blueprint ? 1f : _config.refundFraction, b.Origin, _leftover);
            foreach (ushort item in _leftover) DropPile(b.Origin, item, 1);
            var data = _content.Buildings[def];
            _colony.Inventory.Spend(data);
            b.Def = def;
            b.Dir = NormalizeDir(b.Kind, dir);
            b.WorkLeft = b.WorkTotal = data.Def.buildWork;
            b.State = BuildingState.Blueprint;
            b.Jammed = false;
            b.Stuck = 0f;
            ForEachCell(b.Origin, b.Size, index =>
            {
                var cell = _world.Cells[index];
                cell.Flags |= CellFlags.Blueprint;
                _world.Cells[index] = cell;
            });
            if (b.Kind == BuildingKind.GuardPost) GuardsChanged = true;
            _networkDirty = true;
            BlueprintsChanged = true;
            RecountBlueprints();
        }

        /// <summary>Adds a blueprint without paying or checking (placement and loading).</summary>
        int Create(int def, int2 origin, int2 dir, ushort filter)
        {
            var bdef = _content.Buildings[def].Def;
            var size = new int2(bdef.Footprint.x, bdef.Footprint.y);
            int id = _free.Count > 0 ? _free.Pop() : All.Length;
            var b = new Building
            {
                Def = def,
                Kind = bdef.kind,
                Origin = origin,
                Size = size,
                Dir = NormalizeDir(bdef.kind, dir),
                State = BuildingState.Blueprint,
                WorkLeft = bdef.buildWork,
                WorkTotal = bdef.buildWork,
                Alive = true,
                Filter = filter,
                Front = ItemSlot.Empty,
                Back = ItemSlot.Empty,
                Outputs = new int4(None),
                Machine = None,
            };
            if (id == All.Length) All.Add(b);
            else All[id] = b;

            ForEachCell(origin, size, index =>
            {
                _cells[index] = id;
                var cell = _world.Cells[index];
                cell.Flags |= CellFlags.Blueprint;
                _world.Cells[index] = cell;
            });
            BlueprintsChanged = true;
            RecountBlueprints();
            return id;
        }

        /// <summary>
        /// Loading a save: recreates a building without paying, as a blueprint with <paramref name="workLeft"/>
        /// or finished. Returns its id (machine state is restored by the caller through <see cref="MachineOf"/>).
        /// </summary>
        internal int Restore(int def, int2 origin, int2 dir, ushort filter, BuildingState state, float workLeft)
        {
            int id = Create(def, origin, dir, filter);
            if (state == BuildingState.Active) Activate(id, notify: false);
            else Get(id).WorkLeft = math.clamp(workLeft, 0.01f, Get(id).WorkTotal);
            return id;
        }

        /// <summary>Applies construction work to the blueprint covering <paramref name="cell"/>.</summary>
        public WorkResult Work(int2 cell, float amount)
        {
            int id = At(cell);
            if (id < 0) return WorkResult.None;
            ref var b = ref Get(id);
            if (b.State != BuildingState.Blueprint) return WorkResult.None;
            b.WorkLeft -= amount;
            if (b.WorkLeft > 0f) return WorkResult.Working;
            Activate(id, notify: true);
            return WorkResult.Completed;
        }

        void Activate(int id, bool notify)
        {
            ref var b = ref Get(id);
            b.State = BuildingState.Active;
            b.WorkLeft = 0f;
            ForEachCell(b.Origin, b.Size, index =>
            {
                var cell = _world.Cells[index];
                cell.Flags &= ~CellFlags.Blueprint;
                _world.Cells[index] = cell;
            });
            if (b.Kind == BuildingKind.Processor)
            {
                var m = new Machine(id, _content.Buildings[b.Def]);
                int slot = _freeMachines.Count > 0 ? _freeMachines.Pop() : _machines.Count;
                if (slot == _machines.Count) _machines.Add(m);
                else _machines[slot] = m;
                b.Machine = slot;
                ConsumersChanged |= m.Data.InputMask;
                MachinesChanged = true;
            }
            switch (b.Kind)
            {
                case BuildingKind.Gate: b.Health = GateMaxHealth(b.Def); break;
                case BuildingKind.Trap: b.Rearm = 0f; break;
                case BuildingKind.GuardPost: GuardsChanged = true; break;
                case BuildingKind.Wall: CompletedWalls.Add(id); break;
                case BuildingKind.Nursery: NurseriesChanged = true; break;
                case BuildingKind.Chest:
                    _colony.Inventory.AddChest(b.Origin, _world.Index(b.Origin.x, b.Origin.y), _content.Buildings[b.Def].Def.capacity);
                    DropoffChanged = true;
                    break;
            }
            if (_content.Buildings[b.Def].Def.BlocksFluid) SetFluidBlock(b, true);
            _networkDirty = true;
            BlueprintsChanged = true;
            RecountBlueprints();
            if (!notify) return;
            _colony.Stats.Built[b.Def]++;
            _events.Publish(new BuildingCompletedEvent(id, b.Def, b.Origin));
        }

        /// <summary>Debug / tests: finishes every blueprint immediately.</summary>
        public void CompleteAllBlueprints()
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Alive && All[i].State == BuildingState.Blueprint)
                    Activate(i, notify: true);
        }

        /// <summary>Gate hit points when intact (research included).</summary>
        public float GateMaxHealth(int def) => _content.Buildings[def].Def.maxHealth * _colony.Mods.Mult(StatKind.DefenseStrength);

        /// <summary>
        /// An enemy bites the gate <paramref name="id"/>. A broken gate turns back into a blueprint (enemies pass
        /// until workers repair it; repairs are free).
        /// </summary>
        public void DamageGate(int id, float damage)
        {
            ref var b = ref Get(id);
            if (!b.IsClosedGate) return;
            b.Health -= damage;
            if (b.Health > 0f) return;
            b.Health = 0f;
            b.State = BuildingState.Blueprint;
            b.WorkLeft = b.WorkTotal;
            ForEachCell(b.Origin, b.Size, index =>
            {
                var cell = _world.Cells[index];
                cell.Flags |= CellFlags.Blueprint;
                _world.Cells[index] = cell;
            });
            BlueprintsChanged = true;
            RecountBlueprints();
            _events.Publish(new GateBrokenEvent(b.Origin));
        }

        /// <summary>
        /// Removes the building covering <paramref name="cell"/>, refunding its cost (unless <paramref name="refund"/>
        /// is false: a finished wall). Carried items drop as piles.
        /// </summary>
        public bool Remove(int2 cell, bool refund = true)
        {
            int id = At(cell);
            if (id < 0) return false;
            var b = All[id];
            var data = _content.Buildings[b.Def];
            bool blueprint = b.State == BuildingState.Blueprint;
            if (b.Kind == BuildingKind.Chest && !blueprint)
            {
                // A chest's items move to other storage first; what does not fit drops where it stood.
                _leftover.Clear();
                _colony.Inventory.RemoveStorageCell(_world.Index(b.Origin.x, b.Origin.y), _leftover);
                foreach (ushort item in _leftover) DropPile(b.Origin, item, 1);
                DropoffChanged = true;
            }
            _leftover.Clear();
            if (refund) _colony.Inventory.Refund(data, blueprint ? 1f : _config.refundFraction, b.Origin, _leftover);
            foreach (ushort item in _leftover) DropPile(b.Origin, item, 1); // the stock is full
            if (b.Kind == BuildingKind.GuardPost) GuardsChanged = true;
            if (b.Kind == BuildingKind.Nursery) NurseriesChanged = true;
            if (b.Kind == BuildingKind.Processor) MachinesChanged = true;
            if (!blueprint && data.Def.BlocksFluid) SetFluidBlock(b, false);

            var inv = _colony.Inventory;
            if (!b.Front.IsEmpty) { inv.CancelTransit(b.Front.Item); DropPile(b.Origin, b.Front.Item, 1); }
            if (!b.Back.IsEmpty) { inv.CancelTransit(b.Back.Item); DropPile(b.Origin, b.Back.Item, 1); }
            if (b.Machine >= 0)
            {
                var m = _machines[b.Machine];
                for (int i = 0; i < m.Inputs.Length; i++)
                    if (m.Inputs[i] > 0) DropPile(m.OutputCell, m.Data.InputItems[i], m.Inputs[i]);
                foreach (ushort item in m.Pending) DropPile(m.OutputCell, item, 1);
                ConsumersChanged |= m.Data.InputMask;
                _machines[b.Machine] = null;
                _freeMachines.Push(b.Machine);
            }

            ForEachCell(b.Origin, b.Size, index =>
            {
                _cells.Remove(index);
                var c = _world.Cells[index];
                c.Flags &= ~CellFlags.Blueprint;
                _world.Cells[index] = c;
            });
            All[id] = new Building { Alive = false, Machine = None, Outputs = new int4(None), Front = ItemSlot.Empty, Back = ItemSlot.Empty };
            _free.Push(id);
            _networkDirty = true;
            if (blueprint)
            {
                BlueprintsChanged = true;
                RecountBlueprints();
            }
            if (refund) _events.Publish(new BuildingRemovedEvent(b.Def, b.Origin));
            return true;
        }

        /// <summary>Sets the filtered item of the filter at <paramref name="cell"/>, or the pheromone kind of an emitter there.</summary>
        public bool SetFilter(int2 cell, ushort item)
        {
            int id = At(cell);
            if (id < 0) return false;
            var kind = All[id].Kind;
            if (kind == BuildingKind.Emitter)
            {
                if (item >= Pheromones.Count) return false;
                Get(id).Filter = item;
                return true;
            }
            if (kind == BuildingKind.Unloader)
            {
                if (item >= _content.Items.Length && item != ContentDB.NoItem) return false;
                Get(id).Filter = item;
                return true;
            }
            if (kind != BuildingKind.Filter || item >= _content.Items.Length) return false;
            Get(id).Filter = item;
            _networkDirty = true;
            return true;
        }

        /// <summary>
        /// The machine covering <paramref name="cell"/> runs only its recipe <paramref name="choice"/> (index into its
        /// recipes), or any it can (-1). False if there is no finished machine there.
        /// </summary>
        public bool SetRecipe(int2 cell, int choice)
        {
            int id = At(cell);
            var m = id >= 0 ? MachineOf(id) : null;
            if (m == null) return false;
            if (m.Choice == choice) return true;
            m.SetChoice(choice, _content);
            // The current craft keeps going; what the machine takes in changes at once.
            ConsumersChanged |= m.Data.InputMask;
            _networkDirty = true;
            return true;
        }

        /// <summary>Where the unloader <paramref name="id"/> takes items from (the cell behind its arrow), or null.</summary>
        public StockContainer UnloaderSource(int id)
        {
            var b = All[id];
            int2 behind = b.Origin - b.Dir;
            if (!_world.InBounds(behind.x, behind.y)) return null;
            var inv = _colony.Inventory;
            return inv.ContainerAt(_world.Index(behind.x, behind.y)) ?? (_colony.IsRoyalCell(behind) ? inv.Royal : null);
        }

        void DropPile(int2 c, ushort item, int count)
        {
            var land = _world.FloorBelow(c);
            _colony.Piles.Add(land, item, count);
            _events.Publish(new ItemDroppedEvent(land, item));
        }

        void ForEachCell(int2 origin, int2 size, Action<int> action)
        {
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
                action(_world.Index(origin.x + x, origin.y + y));
        }

        void RecountBlueprints()
        {
            int count = 0, slots = 0;
            for (int i = 0; i < All.Length; i++)
            {
                var b = All[i];
                if (!b.Alive || b.State != BuildingState.Blueprint) continue;
                count++;
                slots += math.min(b.Size.x * b.Size.y, _config.buildersPerSite);
            }
            BlueprintCount = count;
            BuildSlots = slots;
        }

        // ---------------------------------------------------------------- items entering the network

        /// <summary>Puts a loose item (a falling drop) onto the transport cell at <paramref name="cell"/> if it can be delivered from there.</summary>
        public bool TryCatch(int2 cell, ushort item)
        {
            int id = ActiveAt(cell);
            if (id < 0 || !All[id].IsTransport || (All[id].Mask & Bit(item)) == 0) return false;
            if (!Insert(id, item, 0.5f, new float2(cell) + 0.5f, None)) return false;
            _colony.Inventory.BeginTransit(item);
            return true;
        }

        /// <summary>A worker puts a carried item (already counted in transit) onto the transport cell at <paramref name="cell"/>.</summary>
        public bool TryLoad(int2 cell, ushort item)
        {
            int id = ActiveAt(cell);
            if (id < 0 || !All[id].IsTransport || (All[id].Mask & Bit(item)) == 0) return false;
            return Insert(id, item, 0.5f, new float2(cell) + 0.5f, None);
        }

        /// <summary>A worker hands a carried item to the machine at <paramref name="cell"/>.</summary>
        public bool TryFeed(int2 cell, ushort item)
        {
            int id = ActiveAt(cell);
            if (id < 0 || All[id].Machine < 0) return false;
            if (!_machines[All[id].Machine].TryInsert(item)) return false;
            _colony.Inventory.CancelTransit(item);
            return true;
        }

        bool Insert(int id, ushort item, float t0, float2 prev, int from)
        {
            if (!CanInsert(id, t0)) return false;
            ref var b = ref Get(id);
            var slot = new ItemSlot { Item = item, T = t0, Prev = prev, Pos = SlotPos(b, t0), From = from };
            if (b.Front.IsEmpty) b.Front = slot;
            else b.Back = slot;
            return true;
        }

        bool CanInsert(int id, float t0)
        {
            var b = All[id];
            if (!b.Back.IsEmpty) return false;
            return b.Front.IsEmpty || b.Front.T - t0 >= _spacing - 1e-4f;
        }

        /// <summary>Where an item handed from <paramref name="from"/> starts on the transport cell <paramref name="t"/>.</summary>
        static float EntryT(in Building t, int2 from) => t.IsLine && !from.Equals(t.Origin - t.Dir) ? 0.5f : 0f;

        /// <summary>Items <paramref name="target"/> can deliver when handed one by building <paramref name="from"/> (routers exclude that side).</summary>
        public ulong AcceptMask(int target, int from)
        {
            var t = All[target];
            if (!t.IsRouter || from < 0) return t.Mask;
            return _maskFrom.Length > target * 5 + 4 ? _maskFrom[target * 5 + SideOf(t.Origin, All[from])] : t.Mask;
        }

        /// <summary>Cardinal index of a 1×1 neighbour <paramref name="other"/> as seen from <paramref name="origin"/>, or 4 (not a direct neighbour).</summary>
        static int SideOf(int2 origin, in Building other)
        {
            if (!other.Size.Equals(new int2(1, 1))) return 4;
            int2 d = other.Origin - origin;
            for (int k = 0; k < 4; k++)
                if (d.Equals(Cardinals[k])) return k;
            return 4;
        }

        /// <summary>True if <paramref name="target"/> (machine or transport) takes <paramref name="item"/> from building <paramref name="from"/> right now.</summary>
        bool CanTake(int target, ushort item, int from)
        {
            var t = All[target];
            if (!t.IsActive) return false;
            if (t.Machine >= 0) return _machines[t.Machine].Room(item) > 0;
            return (AcceptMask(target, from) & Bit(item)) != 0 && CanInsert(target, EntryT(t, All[from].Origin));
        }

        internal static float2 SlotPos(in Building b, float t)
        {
            float2 center = new float2(b.Origin) + 0.5f;
            return b.Kind == BuildingKind.Belt || b.Kind == BuildingKind.Lift ? center + new float2(b.Dir) * (t - 0.5f) : center;
        }

        // ---------------------------------------------------------------- tick

        public void Tick(float dt)
        {
            if (_networkDirty) Compile();
            _beltSpeed = _colony.Mods.Mult(StatKind.BeltSpeed);
            TickDefense(dt);

            for (int i = 0; i < _order.Count; i++)
            {
                ref var b = ref Get(_order[i]);
                if (!b.Front.IsEmpty) b.Front.Prev = b.Front.Pos;
                if (!b.Back.IsEmpty) b.Back.Prev = b.Back.Pos;
            }
            for (int i = 0; i < _order.Count; i++)
                Step(_order[i], dt);

            TickUnloaders(dt);
            UpdateChestLooks();
            _climateTimer += dt;
            if (_climateTimer >= _water.checkSeconds)
            {
                _climateTimer = 0f;
                UpdateClimate();
            }
            TickMachines(dt);
            UpdateConsumerRoom();

            _rateTimer += dt;
            if (_rateTimer >= 1f)
            {
                float a = _config.rateSmoothing;
                for (int i = 0; i < _order.Count; i++) UpdateRate(_order[i], a);
                foreach (int u in _unloaders) UpdateRate(u, a);
                foreach (int p in _pumps) UpdateRate(p, a);
                foreach (int w in _wheels) UpdateRate(w, a);
                _rateTimer = 0f;
            }
        }

        /// <summary>What chests show: how full they are (<see cref="Building.Craft"/>) and the item they hold most of (<see cref="Building.Filter"/>).</summary>
        void UpdateChestLooks()
        {
            var inv = _colony.Inventory;
            foreach (int id in _chests)
            {
                ref var b = ref Get(id);
                var c = inv.ContainerAt(_world.Index(b.Origin.x, b.Origin.y));
                if (c == null) continue;
                b.Craft = c.Capacity > 0 ? (float)c.Total / c.Capacity : 0f;
                int best = -1;
                for (int i = 0; i < c.Counts.Length; i++)
                    if (c.Counts[i] > 0 && (best < 0 || c.Counts[i] > c.Counts[best])) best = i;
                b.Filter = best >= 0 ? (ushort)best : ContentDB.NoItem;
            }
        }

        void UpdateRate(int id, float smoothing)
        {
            ref var b = ref Get(id);
            b.Rate = math.lerp(b.Rate, b.Passed * 60f / _rateTimer, smoothing);
            b.Passed = 0;
        }

        /// <summary>
        /// Unloaders take one item at a time (their filter item, or any) out of the storage behind them, as fast as their
        /// speed allows and only while what they feed takes it. Items kept in reserve for building stay.
        /// </summary>
        void TickUnloaders(float dt)
        {
            var inv = _colony.Inventory;
            int items = _content.Items.Length;
            foreach (int id in _unloaders)
            {
                ref var b = ref Get(id);
                if (!b.IsActive) continue;
                int target = b.Outputs.x;
                var source = UnloaderSource(id);
                if (target < 0 || source == null)
                {
                    b.Status = MachineStatus.Idle;
                    b.Craft = 0f;
                    continue;
                }
                b.Craft = math.min(1f, b.Craft + _content.Buildings[b.Def].Def.speed * _beltSpeed * dt);
                if (b.Craft < 1f) continue;

                ushort picked = ContentDB.NoItem;
                bool any = false;
                for (int k = 0; k < items && picked == ContentDB.NoItem; k++)
                {
                    var item = (ushort)((b.RoundRobin + k) % items);
                    if (b.Filter != ContentDB.NoItem && item != b.Filter) continue;
                    if (source.Counts[item] <= 0 || inv.Available(item) <= 0) continue;
                    any = true;
                    if (CanTake(target, item, id)) picked = item;
                }
                if (picked == ContentDB.NoItem)
                {
                    b.Status = any ? MachineStatus.OutputBlocked : MachineStatus.Idle;
                    continue;
                }
                inv.Unload(source, picked);
                var cellCenter = new float2(b.Origin) + 0.5f;
                Send(target, picked, id, b.Origin, cellCenter);
                b.Craft -= 1f;
                b.Passed++;
                b.Status = MachineStatus.Working;
                b.RoundRobin = (byte)((picked + 1) % math.max(1, math.min(items, 255)));
            }
        }

        void Step(int id, float dt)
        {
            ref var b = ref Get(id);
            float dv = _content.Buildings[b.Def].Def.speed * _beltSpeed * dt;
            bool emitted = false;
            if (!b.Front.IsEmpty)
            {
                b.Front.T = math.min(1f, b.Front.T + dv);
                if (b.Front.T >= 1f)
                {
                    if (Emit(id, ref b, b.Front)) emitted = true;
                    else b.Stuck += dt;
                }
                else b.Stuck = 0f;
            }
            if (!b.Back.IsEmpty)
            {
                float limit = !b.Front.IsEmpty && !emitted ? b.Front.T - _spacing : 1f;
                b.Back.T = math.max(b.Back.T, math.min(b.Back.T + dv, limit));
            }
            if (emitted)
            {
                b.Front = b.Back;
                b.Back = ItemSlot.Empty;
                b.Passed++;
                b.Stuck = 0f;
            }
            if (!b.Front.IsEmpty) b.Front.Pos = SlotPos(b, b.Front.T);
            if (!b.Back.IsEmpty) b.Back.Pos = SlotPos(b, b.Back.T);

            bool jammed = b.Stuck > _config.jamSeconds && !b.Back.IsEmpty;
            if (jammed != b.Jammed)
            {
                b.Jammed = jammed;
                if (b.Mask == AllItems) DropoffChanged = true;
                else ConsumersChanged |= b.Mask;
            }
        }

        bool Emit(int id, ref Building b, in ItemSlot slot)
        {
            ushort item = slot.Item;
            switch (b.Kind)
            {
                case BuildingKind.Belt:
                case BuildingKind.Lift:
                    if (b.Outputs.x >= 0) return Send(b.Outputs.x, item, id, b.Origin, slot.Pos);
                    if (b.Sink == SinkKind.Stock && DeliverToStock(b, item))
                    {
                        _events.Publish(new ItemDeliveredEvent(b.Origin, item));
                        return true;
                    }
                    return false;

                case BuildingKind.Splitter:
                    for (int k = 0; k < 4; k++)
                    {
                        int s = (b.RoundRobin + k) & 3;
                        int o = b.Outputs[s];
                        if (o < 0 || o == slot.From || !Send(o, item, id, b.Origin, slot.Pos)) continue;
                        b.RoundRobin = (byte)((s + 1) & 3);
                        return true;
                    }
                    return false;

                case BuildingKind.Filter:
                    if (item == b.Filter) return b.Outputs.x >= 0 && b.Outputs.x != slot.From && Send(b.Outputs.x, item, id, b.Origin, slot.Pos);
                    for (int k = 0; k < 3; k++)
                    {
                        int s = 1 + (b.RoundRobin + k) % 3;
                        int o = b.Outputs[s];
                        if (o < 0 || o == slot.From || !Send(o, item, id, b.Origin, slot.Pos)) continue;
                        b.RoundRobin = (byte)(s % 3);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        /// <summary>The end of a line: straight into a chest it points at (if the chest takes the item), else the nearest storage.</summary>
        bool DeliverToStock(in Building b, ushort item)
        {
            var inv = _colony.Inventory;
            int2 next = b.Origin + b.Dir;
            if (_world.InBounds(next.x, next.y))
            {
                var chest = inv.ContainerAt(_world.Index(next.x, next.y));
                if (chest != null && chest.IsChest && inv.DeliverInto(chest, item)) return true;
            }
            return inv.Deliver(item, b.Origin);
        }

        /// <summary>Hands an item from building <paramref name="fromId"/> at <paramref name="from"/> to building <paramref name="target"/>.</summary>
        bool Send(int target, ushort item, int fromId, int2 from, float2 prev)
        {
            var t = All[target];
            if (!t.IsActive) return false;
            if (t.Machine >= 0)
            {
                if (!_machines[t.Machine].TryInsert(item)) return false;
                _colony.Inventory.CancelTransit(item);
                return true;
            }
            if ((AcceptMask(target, fromId) & Bit(item)) == 0) return false;
            // Entering a belt from behind starts at its beginning; joining from the side starts mid-cell.
            return Insert(target, item, EntryT(t, from), prev, fromId);
        }

        /// <summary>Traps re-arm; intact gates slowly mend.</summary>
        void TickDefense(float dt)
        {
            for (int i = 0; i < All.Length; i++)
            {
                ref var b = ref All.ElementAt(i);
                if (!b.IsActive) continue;
                if (b.Kind == BuildingKind.Trap && b.Rearm > 0f) b.Rearm = math.max(0f, b.Rearm - dt);
                else if (b.Kind == BuildingKind.Gate)
                {
                    float max = GateMaxHealth(b.Def);
                    if (b.Health < max) b.Health = math.min(max, b.Health + _combat.gateRegenPerSecond * dt);
                }
            }
        }

        public void AddGuardSources(NativeList<int2> sources)
        {
            for (int i = 0; i < All.Length; i++)
            {
                var b = All[i];
                if (b.Kind == BuildingKind.GuardPost && b.IsActive) sources.Add(b.Origin);
            }
        }

        /// <summary>Every cell of active machines tended by fewer than <paramref name="maxTenders"/> engineers.</summary>
        public void AddMachineSources(NativeList<int2> sources, int maxTenders)
        {
            foreach (var m in _machines)
            {
                if (m == null || m.Tenders >= maxTenders) continue;
                var b = All[m.Building];
                if (!b.IsActive) continue;
                for (int y = 0; y < b.Size.y; y++)
                for (int x = 0; x < b.Size.x; x++)
                    sources.Add(b.Origin + new int2(x, y));
            }
        }

        /// <summary>Ids of finished buildings of <paramref name="kind"/> (nurseries, emitters).</summary>
        public void Collect(BuildingKind kind, List<int> ids)
        {
            ids.Clear();
            for (int i = 0; i < All.Length; i++)
                if (All[i].Kind == kind && All[i].IsActive) ids.Add(i);
        }

        /// <summary>Resets the engineer counts of every machine (the simulation recounts them each tick).</summary>
        public void ClearTenders()
        {
            foreach (var m in _machines)
                if (m != null) m.Tenders = 0;
        }

        void TickMachines(float dt)
        {
            float speed = _colony.Mods.Mult(StatKind.MachineSpeed);
            var stats = _colony.Stats;
            foreach (var m in _machines)
            {
                if (m == null) continue;
                ref var b = ref Get(m.Building);
                if (!b.IsActive) continue;

                while (m.Pending.Count > 0 && EmitProduct(m, m.Pending[0]))
                    m.Pending.RemoveAt(0);

                if (m.Pending.Count > 0) m.Status = MachineStatus.OutputBlocked;
                else
                {
                    bool noRoom = false;
                    if (m.Recipe < 0) TryStart(m, out noRoom);
                    if (m.Recipe >= 0)
                    {
                        var recipe = _content.Recipes[m.Recipe];
                        var def = m.Data.Def;
                        m.Status = MachineStatus.Working;
                        m.Progress += dt * speed * (1f + EngineerBoost * m.Tenders) * (1f + m.WheelBoost) * (m.Heated ? 1f + def.heatBonus : 1f);
                        if (m.Progress >= recipe.Duration)
                        {
                            // Irrigated: the machine drinks some water and every 1 / bonus crafts yields a whole extra batch.
                            int batches = 1;
                            if (def.irrigationBonus > 0f && recipe.OutputItems.Length > 0 && Irrigate(b))
                            {
                                m.IrrigationCarry += def.irrigationBonus;
                                int extra = (int)m.IrrigationCarry;
                                m.IrrigationCarry -= extra;
                                batches += extra;
                            }
                            for (int i = 0; i < recipe.OutputItems.Length; i++)
                            {
                                int made = recipe.OutputCounts[i] * batches;
                                stats.Crafted[recipe.OutputItems[i]] += made;
                                for (int n = 0; n < made; n++)
                                    m.Pending.Add(recipe.OutputItems[i]);
                            }
                            if (recipe.Knowledge > 0f)
                                _colony.Inventory.AddKnowledge(recipe.Knowledge * _colony.Mods.Mult(StatKind.KnowledgeGain), studied: true);
                            if (recipe.Princesses > 0)
                            {
                                _colony.Princesses += recipe.Princesses;
                                stats.PrincessesRaised += recipe.Princesses;
                                _events.Publish(new PrincessRaisedEvent(b.Origin, _colony.Princesses));
                            }
                            m.Recipe = -1;
                            m.Progress = 0f;
                            m.Crafted++;
                            _events.Publish(new ItemCraftedEvent(b.Origin, recipe.OutputItems.Length > 0 ? recipe.OutputItems[0] : ContentDB.NoItem,
                                recipe.Knowledge));
                            while (m.Pending.Count > 0 && EmitProduct(m, m.Pending[0]))
                                m.Pending.RemoveAt(0);
                        }
                    }
                    else m.Status = noRoom ? MachineStatus.OutputBlocked : MachineStatus.Idle;
                }
                b.Status = m.Status;
                b.Craft = m.Recipe >= 0 ? m.Progress / _content.Recipes[m.Recipe].Duration : 0f;
            }
        }

        /// <summary>
        /// Starts the next craftable recipe (round-robin, so one recipe cannot starve the others). Princess recipes wait
        /// while the colony already has (or is raising) as many princesses as count (<paramref name="noRoom"/>).
        /// </summary>
        void TryStart(Machine m, out bool noRoom)
        {
            noRoom = false;
            var recipes = m.Data.Recipes;
            for (int k = 0; k < recipes.Length; k++)
            {
                int ri = (m.NextRecipe + k) % recipes.Length;
                if (m.Choice >= 0 && ri != m.Choice) continue;
                var recipe = _content.Recipes[recipes[ri]];
                bool ok = true;
                for (int i = 0; i < recipe.InputItems.Length && ok; i++)
                    ok = m.SkipsFuel(recipe.InputItems[i]) || m.Inputs[m.InputIndex(recipe.InputItems[i])] >= recipe.InputCounts[i];
                if (!ok) continue;
                if (recipe.Princesses > 0 && _colony.Princesses + PrincessesInProgress() + recipe.Princesses > _colony.PrincessCap)
                {
                    noRoom = true;
                    continue;
                }
                for (int i = 0; i < recipe.InputItems.Length; i++)
                    if (!m.SkipsFuel(recipe.InputItems[i]))
                        m.Inputs[m.InputIndex(recipe.InputItems[i])] -= recipe.InputCounts[i];
                m.Recipe = recipes[ri];
                m.Progress = 0f;
                m.NextRecipe = (ri + 1) % recipes.Length;
                return;
            }
        }

        /// <summary>Princesses machines are raising right now.</summary>
        public int PrincessesInProgress()
        {
            int n = 0;
            foreach (var m in _machines)
                if (m != null && m.Recipe >= 0) n += _content.Recipes[m.Recipe].Princesses;
            return n;
        }

        bool EmitProduct(Machine m, ushort item)
        {
            if (m.OutputBelt >= 0)
            {
                var belt = All[m.OutputBelt];
                if (belt.IsActive && (AcceptMask(m.OutputBelt, m.Building) & Bit(item)) != 0)
                {
                    if (!Insert(m.OutputBelt, item, 0f, SlotPos(belt, 0f), m.Building)) return false;
                    _colony.Inventory.BeginTransit(item);
                    return true;
                }
            }
            if (_colony.Piles.CountAt(m.OutputCell, item) >= m.Data.Def.outputLimit) return false;
            _colony.Piles.Add(m.OutputCell, item, 1);
            _events.Publish(new ItemDroppedEvent(m.OutputCell, item));
            return true;
        }

        void UpdateConsumerRoom()
        {
            Array.Clear(_room, 0, _room.Length);
            foreach (var m in _machines)
            {
                if (m == null) continue;
                for (int i = 0; i < m.Inputs.Length; i++)
                {
                    ushort item = m.Data.InputItems[i];
                    int free = m.Room(item);
                    _room[item] += free;
                    // A machine joins or leaves the consumer field when its buffer fills up or frees.
                    bool needs = free > 0;
                    ulong bit = Bit(item);
                    if (needs != ((m.NeedMask & bit) != 0))
                    {
                        m.NeedMask ^= bit;
                        ConsumersChanged |= bit;
                    }
                }
            }
            var inv = _colony.Inventory;
            for (int i = 0; i < _room.Length; i++)
                if (inv.SetConsumerRoom((ushort)i, _room[i])) ConsumersChanged |= Bit((ushort)i);
        }

        // ---------------------------------------------------------------- network

        void Compile()
        {
            _networkDirty = false;
            int n = All.Length;
            int active = 0;
            _unloaders.Clear();
            _chests.Clear();
            _pumps.Clear();
            _wheels.Clear();
            for (int id = 0; id < n; id++)
            {
                ref var b = ref Get(id);
                if (!b.Alive) continue;
                if (b.IsActive) active++;
                b.Outputs = new int4(None);
                b.Sink = SinkKind.None;
                if (b.IsActive && b.IsTransport) Link(ref b);
                else if (b.IsActive && b.Kind == BuildingKind.Unloader)
                {
                    LinkUnloader(ref b);
                    _unloaders.Add(id);
                }
                else if (b.IsActive && b.Kind == BuildingKind.Chest) _chests.Add(id);
                else if (b.IsActive && b.Kind == BuildingKind.Pump) _pumps.Add(id);
                else if (b.IsActive && b.Kind == BuildingKind.WaterWheel) _wheels.Add(id);
            }
            ActiveCount = active;

            foreach (var m in _machines)
                if (m != null) LinkMachine(m);

            BuildOrder(n);
            ComputeMasks();
            DropoffChanged = true;
            ConsumersChanged = ulong.MaxValue;
        }

        static readonly int2[] Cardinals = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };

        void Link(ref Building b)
        {
            if (b.Kind == BuildingKind.Belt || b.Kind == BuildingKind.Lift)
            {
                int2 next = b.Origin + b.Dir;
                int t = ActiveAt(next);
                if (t >= 0 && All[t].Kind > BuildingKind.Processor) t = None; // defense buildings take no items
                if (t >= 0)
                {
                    var tb = All[t];
                    bool line = tb.Kind == BuildingKind.Belt || tb.Kind == BuildingKind.Lift;
                    if (!tb.IsTransport) { b.Outputs.x = t; b.Sink = SinkKind.Machine; }
                    else if (!(line && tb.Dir.Equals(-b.Dir))) b.Outputs.x = t; // head-on belts never connect
                }
                else if (IsStock(b.Origin) || IsStock(next)) b.Sink = SinkKind.Stock;
                return;
            }

            // Splitter / filter: every neighbour that is not feeding into it is an output. Neighbouring splitters and
            // filters are outputs of each other; an item never goes back to the router it came from.
            int free = b.Kind == BuildingKind.Filter ? 1 : 0;
            for (int k = 0; k < 4; k++)
            {
                int2 d = Cardinals[k];
                int t = ActiveAt(b.Origin + d);
                if (t < 0 || All[t].Kind > BuildingKind.Processor) continue;
                var tb = All[t];
                if (tb.IsLine && tb.Dir.Equals(-d)) continue;
                if (b.Kind == BuildingKind.Filter && d.Equals(b.Dir)) b.Outputs.x = t;
                else if (free < 4) b.Outputs[free++] = t;
            }
        }

        /// <summary>An unloader feeds the belt, lift, router or machine its arrow points at.</summary>
        void LinkUnloader(ref Building b)
        {
            int t = ActiveAt(b.Origin + b.Dir);
            if (t < 0) return;
            var tb = All[t];
            if (tb.Machine >= 0) b.Outputs.x = t;
            else if (tb.IsTransport && !(tb.IsLine && tb.Dir.Equals(-b.Dir))) b.Outputs.x = t;
        }

        void LinkMachine(Machine m)
        {
            var b = All[m.Building];
            m.OutputCell = new int2(b.Origin.x + b.Size.x / 2, b.Origin.y);
            m.OutputBelt = None;
            // First transport cell around the footprint that does not point back into the machine.
            for (int y = -1; y <= b.Size.y && m.OutputBelt < 0; y++)
            for (int x = -1; x <= b.Size.x && m.OutputBelt < 0; x++)
            {
                bool edgeX = x == -1 || x == b.Size.x, edgeY = y == -1 || y == b.Size.y;
                if (edgeX == edgeY) continue; // inside or a corner
                var c = b.Origin + new int2(x, y);
                int t = ActiveAt(c);
                if (t < 0 || !All[t].IsTransport) continue;
                if (b.Contains(c + All[t].Dir)) continue;
                m.OutputBelt = t;
            }
        }

        /// <summary>Kahn's algorithm on the reversed links: every cell is simulated after the cells it feeds.</summary>
        void BuildOrder(int n)
        {
            _order.Clear();
            var outCount = new int[n];
            var predHead = new int[n];
            var predNext = new List<int>();
            var predNode = new List<int>();
            for (int i = 0; i < n; i++) predHead[i] = None;
            var queue = new Queue<int>();
            var placed = new bool[n];

            for (int id = 0; id < n; id++)
            {
                var b = All[id];
                if (!b.IsActive || !b.IsTransport) continue;
                for (int k = 0; k < 4; k++)
                {
                    int o = b.Outputs[k];
                    if (o < 0 || !All[o].IsTransport) continue;
                    outCount[id]++;
                    predNode.Add(id);
                    predNext.Add(predHead[o]);
                    predHead[o] = predNode.Count - 1;
                }
                if (outCount[id] == 0) queue.Enqueue(id);
            }
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                placed[id] = true;
                _order.Add(id);
                for (int e = predHead[id]; e >= 0; e = predNext[e])
                    if (--outCount[predNode[e]] == 0) queue.Enqueue(predNode[e]);
            }
            for (int id = 0; id < n; id++) // loops: any order
                if (!placed[id] && All[id].IsActive && All[id].IsTransport) _order.Add(id);
        }

        void ComputeMasks()
        {
            int n = All.Length;
            if (_maskFrom.Length < n * 5) _maskFrom = new ulong[math.max(n * 5, _maskFrom.Length * 2)];
            else Array.Clear(_maskFrom, 0, _maskFrom.Length);
            for (int i = 0; i < _order.Count; i++) Get(_order[i]).Mask = 0;
            // Downstream-first order converges in one pass without loops; masks only grow, so loops settle too.
            for (int pass = 0; pass < 256; pass++)
            {
                bool changed = false;
                for (int i = 0; i < _order.Count; i++)
                {
                    int id = _order[i];
                    ref var b = ref Get(id);
                    if (b.IsRouter)
                    {
                        for (int side = 0; side < 5; side++)
                        {
                            ulong rm = RouterMask(id, b, side);
                            if (rm == _maskFrom[id * 5 + side]) continue;
                            _maskFrom[id * 5 + side] = rm;
                            changed = true;
                        }
                        b.Mask = _maskFrom[id * 5 + 4];
                        continue;
                    }
                    ulong m = b.Outputs.x >= 0 ? EdgeMask(id, b.Outputs.x) : b.Sink == SinkKind.Stock ? AllItems : 0;
                    if (m == b.Mask) continue;
                    b.Mask = m;
                    changed = true;
                }
                if (!changed) break;
            }
        }

        /// <summary>
        /// Items router <paramref name="id"/> can deliver when the item came in from side <paramref name="side"/>
        /// (0–3: <see cref="Cardinals"/>; 4: from nowhere, e.g. loaded by an ant): every output but that side.
        /// </summary>
        ulong RouterMask(int id, in Building b, int side)
        {
            int back = side < 4 ? At(b.Origin + Cardinals[side]) : None;
            ulong Out(int o) => o >= 0 && o != back ? EdgeMask(id, o) : 0;
            if (b.Kind == BuildingKind.Splitter)
                return Out(b.Outputs.x) | Out(b.Outputs.y) | Out(b.Outputs.z) | Out(b.Outputs.w);
            ulong bit = Bit(b.Filter);
            ulong rest = Out(b.Outputs.y) | Out(b.Outputs.z) | Out(b.Outputs.w);
            return (Out(b.Outputs.x) & bit) | (rest & ~bit);
        }

        /// <summary>Items building <paramref name="from"/> can get delivered by handing them to its output <paramref name="o"/>.</summary>
        ulong EdgeMask(int from, int o)
        {
            if (o < 0) return 0;
            var t = All[o];
            if (t.Machine >= 0) return _machines[t.Machine].ActiveMask;
            if (t.IsRouter) return _maskFrom[o * 5 + SideOf(t.Origin, All[from])];
            return t.Mask;
        }

        // ---------------------------------------------------------------- flow field sources

        /// <summary>Transport cells that deliver every item to the stock (or beyond) and are not jammed.</summary>
        public void AddDropoffSources(NativeList<int2> sources)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                var b = All[_order[i]];
                if (b.Mask == AllItems && !b.Jammed) sources.Add(b.Origin);
            }
        }

        /// <summary>Machines that need <paramref name="item"/>, and belts that only lead to such machines.</summary>
        public void AddConsumerSources(ushort item, NativeList<int2> sources)
        {
            ulong bit = Bit(item);
            foreach (var m in _machines)
            {
                if (m == null || m.Room(item) == 0) continue;
                var b = All[m.Building];
                for (int y = 0; y < b.Size.y; y++)
                for (int x = 0; x < b.Size.x; x++)
                    sources.Add(b.Origin + new int2(x, y));
            }
            if (_colony.Inventory.ConsumerRoom[item] == 0) return;
            for (int i = 0; i < _order.Count; i++)
            {
                var b = All[_order[i]];
                if ((b.Mask & bit) != 0 && b.Mask != AllItems && !b.Jammed) sources.Add(b.Origin);
            }
        }

        public void AddBuildSources(NativeList<int2> sources)
        {
            for (int i = 0; i < All.Length; i++)
            {
                var b = All[i];
                if (!b.Alive || b.State != BuildingState.Blueprint) continue;
                for (int y = 0; y < b.Size.y; y++)
                for (int x = 0; x < b.Size.x; x++)
                    sources.Add(b.Origin + new int2(x, y));
            }
        }

        public void Dispose()
        {
            if (All.IsCreated) All.Dispose();
        }
    }
}
