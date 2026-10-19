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
    /// as a pile for workers.
    /// </summary>
    public sealed class BuildingSystem : IDisposable
    {
        public const int None = -1;

        readonly ContentDB _content;
        readonly LogisticsConfig _config;
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
        readonly int[] _room;
        bool _networkDirty = true;
        float _rateTimer;

        /// <summary>Bit per item index, all items set.</summary>
        public readonly ulong AllItems;

        // Change flags for the flow fields; read and cleared by the simulation.
        public bool BlueprintsChanged = true;
        public bool DropoffChanged = true;
        /// <summary>Bit per item whose consumer field must be rebuilt.</summary>
        public ulong ConsumersChanged = ulong.MaxValue;

        public int BlueprintCount { get; private set; }
        public int BuildSlots { get; private set; }
        public int ActiveCount { get; private set; }

        public BuildingSystem(ContentDB content, LogisticsConfig config, WorldGrid world, Colony colony, EventBus events)
        {
            _content = content;
            _config = config;
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
                    if (dir.x == 0 && dir.y == 0) return new int2(1, 0);
                    return math.abs(dir.x) >= math.abs(dir.y) ? new int2((int)math.sign(dir.x), 0) : new int2(0, (int)math.sign(dir.y));
                default: return int2.zero;
            }
        }

        public PlaceResult CanPlace(int def, int2 origin, int2 dir) => Check(def, origin, dir, out _);

        PlaceResult Check(int def, int2 origin, int2 dir, out int rotate)
        {
            rotate = None;
            if (def < 0 || def >= _content.Buildings.Length) return PlaceResult.UnknownBuilding;
            var data = _content.Buildings[def];
            var bdef = data.Def;
            var size = new int2(bdef.Footprint.x, bdef.Footprint.y);
            int2 nd = NormalizeDir(bdef.kind, dir);
            var nav = new NavGrid(_world);

            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var c = origin + new int2(x, y);
                if (!_world.InBounds(c.x, c.y)) return PlaceResult.OutOfBounds;
                var cell = _world.Cells[_world.Index(c.x, c.y)];
                if (!cell.IsRevealed) return PlaceResult.Hidden;
                if (bdef.IsTransport ? !nav.IsWalkable(c) : !cell.IsOpen || cell.Material == ContentDB.AirIndex)
                    return PlaceResult.NotOpen;
                int existing = At(c);
                if (existing >= 0)
                {
                    var e = All[existing];
                    if (!bdef.IsTransport || e.Def != def) return PlaceResult.Occupied;
                    if (e.Dir.Equals(nd)) return PlaceResult.Unchanged;
                    rotate = existing;
                }
                if (!bdef.IsTransport && c.Equals(_colony.QueenCell)) return PlaceResult.Occupied;
            }
            if (!bdef.IsTransport)
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
            var result = Check(def, origin, dir, out int rotate);
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

            var data = _content.Buildings[def];
            var bdef = data.Def;
            _colony.Inventory.Spend(data);
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
            _events.Publish(new BuildingPlacedEvent(id, def, origin));
            return result;
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
            Activate(id);
            return WorkResult.Completed;
        }

        void Activate(int id)
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
            }
            _networkDirty = true;
            BlueprintsChanged = true;
            RecountBlueprints();
            _events.Publish(new BuildingCompletedEvent(id, b.Def, b.Origin));
        }

        /// <summary>Debug / tests: finishes every blueprint immediately.</summary>
        public void CompleteAllBlueprints()
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Alive && All[i].State == BuildingState.Blueprint)
                    Activate(i);
        }

        /// <summary>Removes the building covering <paramref name="cell"/>, refunding its cost. Carried items drop as piles.</summary>
        public bool Remove(int2 cell)
        {
            int id = At(cell);
            if (id < 0) return false;
            var b = All[id];
            var data = _content.Buildings[b.Def];
            bool blueprint = b.State == BuildingState.Blueprint;
            _colony.Inventory.Refund(data, blueprint ? 1f : _config.refundFraction);

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
            _events.Publish(new BuildingRemovedEvent(b.Def, b.Origin));
            return true;
        }

        /// <summary>Sets the filtered item of the filter at <paramref name="cell"/>.</summary>
        public bool SetFilter(int2 cell, ushort item)
        {
            int id = At(cell);
            if (id < 0 || All[id].Kind != BuildingKind.Filter || item >= _content.Items.Length) return false;
            Get(id).Filter = item;
            _networkDirty = true;
            return true;
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
            if (!Insert(id, item, 0.5f, new float2(cell) + 0.5f)) return false;
            _colony.Inventory.BeginTransit(item);
            return true;
        }

        /// <summary>A worker puts a carried item (already counted in transit) onto the transport cell at <paramref name="cell"/>.</summary>
        public bool TryLoad(int2 cell, ushort item)
        {
            int id = ActiveAt(cell);
            if (id < 0 || !All[id].IsTransport || (All[id].Mask & Bit(item)) == 0) return false;
            return Insert(id, item, 0.5f, new float2(cell) + 0.5f);
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

        bool Insert(int id, ushort item, float t0, float2 prev)
        {
            ref var b = ref Get(id);
            if (!b.Back.IsEmpty) return false;
            var slot = new ItemSlot { Item = item, T = t0, Prev = prev, Pos = SlotPos(b, t0) };
            if (b.Front.IsEmpty)
            {
                b.Front = slot;
                return true;
            }
            if (b.Front.T - t0 < _spacing - 1e-4f) return false;
            b.Back = slot;
            return true;
        }

        static float2 SlotPos(in Building b, float t)
        {
            float2 center = new float2(b.Origin) + 0.5f;
            return b.Kind == BuildingKind.Belt || b.Kind == BuildingKind.Lift ? center + new float2(b.Dir) * (t - 0.5f) : center;
        }

        // ---------------------------------------------------------------- tick

        public void Tick(float dt)
        {
            if (_networkDirty) Compile();

            for (int i = 0; i < _order.Count; i++)
            {
                ref var b = ref Get(_order[i]);
                if (!b.Front.IsEmpty) b.Front.Prev = b.Front.Pos;
                if (!b.Back.IsEmpty) b.Back.Prev = b.Back.Pos;
            }
            for (int i = 0; i < _order.Count; i++)
                Step(_order[i], dt);

            TickMachines(dt);
            UpdateConsumerRoom();

            _rateTimer += dt;
            if (_rateTimer >= 1f)
            {
                float a = _config.rateSmoothing;
                for (int i = 0; i < _order.Count; i++)
                {
                    ref var b = ref Get(_order[i]);
                    b.Rate = math.lerp(b.Rate, b.Passed * 60f / _rateTimer, a);
                    b.Passed = 0;
                }
                _rateTimer = 0f;
            }
        }

        void Step(int id, float dt)
        {
            ref var b = ref Get(id);
            float dv = _content.Buildings[b.Def].Def.speed * dt;
            bool emitted = false;
            if (!b.Front.IsEmpty)
            {
                b.Front.T = math.min(1f, b.Front.T + dv);
                if (b.Front.T >= 1f)
                {
                    if (Emit(ref b, b.Front)) emitted = true;
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

        bool Emit(ref Building b, in ItemSlot slot)
        {
            ushort item = slot.Item;
            switch (b.Kind)
            {
                case BuildingKind.Belt:
                case BuildingKind.Lift:
                    if (b.Outputs.x >= 0) return Send(b.Outputs.x, item, b.Origin, slot.Pos);
                    if (b.Sink == SinkKind.Stock && _colony.Inventory.Deliver(item))
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
                        if (o < 0 || !Send(o, item, b.Origin, slot.Pos)) continue;
                        b.RoundRobin = (byte)((s + 1) & 3);
                        return true;
                    }
                    return false;

                case BuildingKind.Filter:
                    if (item == b.Filter) return b.Outputs.x >= 0 && Send(b.Outputs.x, item, b.Origin, slot.Pos);
                    for (int k = 0; k < 3; k++)
                    {
                        int s = 1 + (b.RoundRobin + k) % 3;
                        int o = b.Outputs[s];
                        if (o < 0 || !Send(o, item, b.Origin, slot.Pos)) continue;
                        b.RoundRobin = (byte)(s % 3);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        /// <summary>Hands an item from a transport cell at <paramref name="from"/> to building <paramref name="target"/>.</summary>
        bool Send(int target, ushort item, int2 from, float2 prev)
        {
            var t = All[target];
            if (!t.IsActive) return false;
            if (t.Machine >= 0)
            {
                if (!_machines[t.Machine].TryInsert(item)) return false;
                _colony.Inventory.CancelTransit(item);
                return true;
            }
            if ((t.Mask & Bit(item)) == 0) return false;
            bool line = t.Kind == BuildingKind.Belt || t.Kind == BuildingKind.Lift;
            // Entering a belt from behind starts at its beginning; joining from the side starts mid-cell.
            float t0 = line && !from.Equals(t.Origin - t.Dir) ? 0.5f : 0f;
            return Insert(target, item, t0, prev);
        }

        void TickMachines(float dt)
        {
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
                    if (m.Recipe < 0) TryStart(m);
                    if (m.Recipe >= 0)
                    {
                        var recipe = _content.Recipes[m.Recipe];
                        m.Status = MachineStatus.Working;
                        m.Progress += dt;
                        if (m.Progress >= recipe.Duration)
                        {
                            for (int i = 0; i < recipe.OutputItems.Length; i++)
                            for (int n = 0; n < recipe.OutputCounts[i]; n++)
                                m.Pending.Add(recipe.OutputItems[i]);
                            m.Recipe = -1;
                            m.Progress = 0f;
                            m.Crafted++;
                            _events.Publish(new ItemCraftedEvent(b.Origin, recipe.OutputItems[0]));
                            while (m.Pending.Count > 0 && EmitProduct(m, m.Pending[0]))
                                m.Pending.RemoveAt(0);
                        }
                    }
                    else m.Status = MachineStatus.Idle;
                }
                b.Status = m.Status;
                b.Craft = m.Recipe >= 0 ? m.Progress / _content.Recipes[m.Recipe].Duration : 0f;
            }
        }

        /// <summary>Starts the next craftable recipe (round-robin, so one recipe cannot starve the others).</summary>
        void TryStart(Machine m)
        {
            var recipes = m.Data.Recipes;
            for (int k = 0; k < recipes.Length; k++)
            {
                int ri = (m.NextRecipe + k) % recipes.Length;
                var recipe = _content.Recipes[recipes[ri]];
                bool ok = true;
                for (int i = 0; i < recipe.InputItems.Length && ok; i++)
                    ok = m.Inputs[m.InputIndex(recipe.InputItems[i])] >= recipe.InputCounts[i];
                if (!ok) continue;
                for (int i = 0; i < recipe.InputItems.Length; i++)
                    m.Inputs[m.InputIndex(recipe.InputItems[i])] -= recipe.InputCounts[i];
                m.Recipe = recipes[ri];
                m.Progress = 0f;
                m.NextRecipe = (ri + 1) % recipes.Length;
                return;
            }
        }

        bool EmitProduct(Machine m, ushort item)
        {
            if (m.OutputBelt >= 0)
            {
                var belt = All[m.OutputBelt];
                if (belt.IsActive && (belt.Mask & Bit(item)) != 0)
                {
                    if (!Insert(m.OutputBelt, item, 0f, SlotPos(belt, 0f))) return false;
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
                    int free = math.max(0, m.Data.InputCaps[i] - m.Inputs[i]);
                    ushort item = m.Data.InputItems[i];
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
            for (int id = 0; id < n; id++)
            {
                ref var b = ref Get(id);
                if (!b.Alive) continue;
                if (b.IsActive) active++;
                b.Outputs = new int4(None);
                b.Sink = SinkKind.None;
                if (b.IsActive && b.IsTransport) Link(ref b);
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

            // Splitter / filter: every neighbour that is not feeding into it is an output. Splitters and
            // filters never connect directly to each other (items would bounce between them).
            int free = b.Kind == BuildingKind.Filter ? 1 : 0;
            for (int k = 0; k < 4; k++)
            {
                int2 d = Cardinals[k];
                int t = ActiveAt(b.Origin + d);
                if (t < 0) continue;
                var tb = All[t];
                if (tb.IsTransport)
                {
                    if (tb.Kind == BuildingKind.Splitter || tb.Kind == BuildingKind.Filter) continue;
                    if (tb.Dir.Equals(-d)) continue;
                }
                if (b.Kind == BuildingKind.Filter && d.Equals(b.Dir)) b.Outputs.x = t;
                else if (free < 4) b.Outputs[free++] = t;
            }
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
            for (int i = 0; i < _order.Count; i++) Get(_order[i]).Mask = 0;
            // Downstream-first order converges in one pass without loops; masks only grow, so loops settle too.
            for (int pass = 0; pass < 256; pass++)
            {
                bool changed = false;
                for (int i = 0; i < _order.Count; i++)
                {
                    ref var b = ref Get(_order[i]);
                    ulong m = MaskOf(b);
                    if (m == b.Mask) continue;
                    b.Mask = m;
                    changed = true;
                }
                if (!changed) break;
            }
        }

        ulong MaskOf(in Building b)
        {
            switch (b.Kind)
            {
                case BuildingKind.Belt:
                case BuildingKind.Lift:
                    if (b.Outputs.x >= 0) return OutMask(b.Outputs.x);
                    return b.Sink == SinkKind.Stock ? AllItems : 0;
                case BuildingKind.Splitter:
                    return OutMask(b.Outputs.x) | OutMask(b.Outputs.y) | OutMask(b.Outputs.z) | OutMask(b.Outputs.w);
                case BuildingKind.Filter:
                {
                    ulong bit = Bit(b.Filter);
                    ulong rest = OutMask(b.Outputs.y) | OutMask(b.Outputs.z) | OutMask(b.Outputs.w);
                    return (OutMask(b.Outputs.x) & bit) | (rest & ~bit);
                }
            }
            return 0;
        }

        ulong OutMask(int o)
        {
            if (o < 0) return 0;
            var t = All[o];
            return t.Machine >= 0 ? _machines[t.Machine].Data.InputMask : t.Mask;
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
