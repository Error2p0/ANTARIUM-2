using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Owns the simulation state and runs it on a fixed tick. Input arrives as commands that are applied
    /// at the start of the next tick; results leave as events on <see cref="Events"/>.
    /// </summary>
    public sealed class Simulation : IDisposable, IAntIntentHandler
    {
        enum CommandKind : byte { Dig, Designate, Zone, Build, Remove, SetFilter }

        readonly struct Command
        {
            public readonly CommandKind Kind;
            public readonly float2 CellPos;
            public readonly float Radius;
            public readonly float Power;
            public readonly bool Set;
            public readonly int Def;
            public readonly int2 Cell;
            public readonly int2 Dir;
            public readonly ushort Item;

            public Command(CommandKind kind, float2 cellPos, float radius, float power, bool set)
            {
                Kind = kind;
                CellPos = cellPos;
                Radius = radius;
                Power = power;
                Set = set;
                Def = 0;
                Cell = default;
                Dir = default;
                Item = 0;
            }

            public Command(CommandKind kind, int def, int2 cell, int2 dir, ushort item)
            {
                Kind = kind;
                CellPos = default;
                Radius = 0f;
                Power = 0f;
                Set = true;
                Def = def;
                Cell = cell;
                Dir = dir;
                Item = item;
            }
        }

        public readonly ContentDB Content;
        public readonly GameConfig Config;
        public readonly uint Seed;
        public readonly WorldGrid World;
        public readonly LightSystem Light;
        public readonly EventBus Events = new();
        public readonly SimClock Clock;
        public readonly FlowFields Fields;
        public readonly AntSystem Ants;
        public readonly Colony Colony;
        public readonly BuildingSystem Buildings;

        readonly List<Command> _pending = new();
        readonly List<int2> _hatched = new();
        readonly List<int2> _scratchCells = new();
        NativeList<int2> _broken;
        NativeList<int2> _breachStarts;
        Unity.Mathematics.Random _rng;
        bool _terrainChanged;
        bool _designationsChanged;
        bool _storageChanged;
        int _ticksSinceFieldRefresh;
        int _digSlots;
        int _haulSlots;

        public Simulation(ContentDB content, GameConfig config, uint seed)
        {
            Content = content;
            Config = config;
            Seed = seed;
            Clock = new SimClock(config.sim.ticksPerSecond, config.sim.maxTicksPerFrame);
            _rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xC0105u)) | 1u);

            World = WorldGenerator.Create(content, seed);
            Reveal.Initial(World, content.MaterialSim, config.dig.revealRadius);
            Light = new LightSystem(World, config.light);
            Light.RecomputeAll(World, content.MaterialSim);

            _broken = new NativeList<int2>(64, Allocator.Persistent);
            _breachStarts = new NativeList<int2>(16, Allocator.Persistent);

            Colony = new Colony(content, config.colony);
            Colony.QueenCell = FindQueenCell();
            Buildings = new BuildingSystem(content, config.logistics, World, Colony, Events);
            Fields = new FlowFields(World, content.ConsumerSlotCount);
            Ants = new AntSystem(config.worker, config.logistics, content.Items.Length);
            for (int i = 0; i < content.Items.Length; i++)
                Ants.ConsumerField[i] = content.ConsumerSlot[i] >= 0 ? FlowFields.ConsumerField(content.ConsumerSlot[i]) : -1;
            for (int i = 0; i < config.colony.startingWorkers; i++)
                Ants.Spawn(RandomRoyalFloorCell(), seed);
            RefreshFields(force: true);
        }

        public float TickDuration => (float)Clock.TickDuration;

        public void Enqueue(in DigCommand command) =>
            _pending.Add(new Command(CommandKind.Dig, command.CellPos, command.Radius, command.Power, true));

        /// <summary>Marks (or clears) solid cells in a circle for workers to mine.</summary>
        public void EnqueueDesignate(float2 cellPos, float radius, bool set) =>
            _pending.Add(new Command(CommandKind.Designate, cellPos, radius, 0f, set));

        /// <summary>Paints (or clears) open cells in a circle as a storage zone.</summary>
        public void EnqueueStorageZone(float2 cellPos, float radius, bool set) =>
            _pending.Add(new Command(CommandKind.Zone, cellPos, radius, 0f, set));

        /// <summary>Places a blueprint of building <paramref name="def"/> (index into <c>ContentDB.Buildings</c>) with its bottom-left at <paramref name="origin"/>.</summary>
        public void EnqueueBuild(int def, int2 origin, int2 dir, ushort filter = 0) =>
            _pending.Add(new Command(CommandKind.Build, def, origin, dir, filter));

        /// <summary>Removes the building covering <paramref name="cell"/> and refunds it.</summary>
        public void EnqueueRemove(int2 cell) => _pending.Add(new Command(CommandKind.Remove, 0, cell, default, 0));

        public void EnqueueSetFilter(int2 cell, ushort item) => _pending.Add(new Command(CommandKind.SetFilter, 0, cell, default, item));

        /// <summary>Advances the clock and runs the due ticks. Returns the number of ticks run.</summary>
        public int Advance(double deltaTime)
        {
            int ticks = Clock.Advance(deltaTime);
            for (int i = 0; i < ticks; i++)
                Tick();
            return ticks;
        }

        public void Tick()
        {
            float dt = TickDuration;
            for (int i = 0; i < _pending.Count; i++)
                Apply(_pending[i]);
            _pending.Clear();

            UpdateColony(dt);
            Buildings.Tick(dt);
            Colony.Inventory.UpdateRates(dt, Config.logistics.rateSmoothing);
            RefreshFields(force: false);

            var inventory = Colony.Inventory;
            for (int i = 0; i < Content.Items.Length; i++)
                Ants.StockAccepts[i] = inventory.StockAccepts((ushort)i) ? (byte)1 : (byte)0;
            Ants.Think(World, Fields, dt, Colony.SpeedFactor, Ants.JobChances(_digSlots, _haulSlots, Buildings.BuildSlots));
            Ants.Resolve(this);
            FlushBreaches();
        }

        /// <summary>Refreshes derived data (light) after ticks. Call once per frame.</summary>
        public void UpdateDerived()
        {
            Light.UpdateDirty(World, Content.MaterialSim);
        }

        /// <summary>Debug/cheat: adds <paramref name="items"/> of every non-food item and <paramref name="food"/> food to the stock.</summary>
        public void GiveResources(int items, float food)
        {
            var inv = Colony.Inventory;
            for (int i = 0; i < Content.Items.Length; i++)
                if (!Content.Items[i].IsFood) inv.Items[i] += items;
            inv.Food = math.min(inv.FoodCapacity, inv.Food + food);
            inv.AcceptanceChanged = true;
        }

        /// <summary>Debug/cheat: spawns workers in the royal chamber.</summary>
        public void SpawnWorkers(int count)
        {
            for (int i = 0; i < count; i++)
                Ants.Spawn(RandomRoyalFloorCell(), Seed);
        }

        // ---------------------------------------------------------------- commands

        void Apply(in Command cmd)
        {
            switch (cmd.Kind)
            {
                case CommandKind.Dig: ResolveDig(cmd); break;
                case CommandKind.Designate: ApplyBrush(cmd, designate: true); break;
                case CommandKind.Zone: ApplyBrush(cmd, designate: false); break;
                case CommandKind.Build: Buildings.Place(cmd.Def, cmd.Cell, cmd.Dir, cmd.Item); break;
                case CommandKind.Remove: Buildings.Remove(cmd.Cell); break;
                case CommandKind.SetFilter: Buildings.SetFilter(cmd.Cell, cmd.Item); break;
            }
        }

        void ResolveDig(in Command cmd)
        {
            _broken.Clear();
            var dig = new DigCommand(cmd.CellPos, cmd.Radius, cmd.Power);
            int damaged = Digging.Apply(World, Content.MaterialSim, dig, Config.dig.brushEdgeFactor, Events, _broken, out bool opened);
            if (opened) _terrainChanged = true;

            int breachesBefore = _breachStarts.Length;
            for (int i = 0; i < _broken.Length; i++)
                OnCellBroken(_broken[i]);
            int discovered = _breachStarts.Length > breachesBefore ? FlushBreaches() : 0;

            if (damaged > 0)
                Events.Publish(new DigResolvedEvent(cmd.CellPos, damaged, _broken.Length, discovered));
        }

        void ApplyBrush(in Command cmd, bool designate)
        {
            int2 min = (int2)math.floor(cmd.CellPos - cmd.Radius);
            int2 max = (int2)math.ceil(cmd.CellPos + cmd.Radius);
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!World.InBounds(x, y)) continue;
                if (math.distance(new float2(x + 0.5f, y + 0.5f), cmd.CellPos) > cmd.Radius) continue;
                int index = World.Index(x, y);
                var cell = World.Cells[index];
                // Mining orders may reach into the fog (workers only ever reach revealed frontier cells);
                // storage zones need visible open space.
                if (!designate && !cell.IsRevealed) continue;

                CellFlags flag;
                bool eligible;
                if (designate)
                {
                    var mat = Content.MaterialSim[cell.Material];
                    flag = CellFlags.DigDesignated;
                    eligible = !cell.IsEmpty && !mat.IsIndestructible && !mat.IsAir;
                }
                else
                {
                    flag = CellFlags.Storage;
                    eligible = cell.IsOpen && !Content.MaterialSim[cell.Material].IsAir;
                }

                bool has = (cell.Flags & flag) != 0;
                if (cmd.Set == has || (cmd.Set && !eligible)) continue;
                cell.Flags = cmd.Set ? cell.Flags | flag : cell.Flags & ~flag;
                World.Cells[index] = cell;
                World.MarkChanged(x, y, ChunkFlags.TerrainUpload);

                var set = designate ? Colony.Designated : Colony.StorageCells;
                if (cmd.Set) set.Add(index);
                else set.Remove(index);
                if (designate) _designationsChanged = true;
                else _storageChanged = true;
            }

            if (!designate && _storageChanged)
            {
                Colony.Inventory.SetStorageCells(Colony.StorageCells.Count);
                Buildings.MarkNetworkDirty(); // belts ending in a storage zone deliver to the stock
            }
        }

        // ---------------------------------------------------------------- terrain

        void OnCellBroken(int2 c)
        {
            _terrainChanged = true;
            int index = World.Index(c.x, c.y);
            if (Colony.Designated.Remove(index)) _designationsChanged = true;

            Reveal.Around(World, c, Config.dig.revealRadius);
            AddBreach(c + new int2(1, 0));
            AddBreach(c + new int2(-1, 0));
            AddBreach(c + new int2(0, 1));
            AddBreach(c + new int2(0, -1));

            var mat = Content.MaterialSim[World.Cells[index].Material];
            if (mat.DropItem != ContentDB.NoItem && _rng.NextFloat() < mat.DropChance)
                DropItem(c, mat.DropItem, mat.DropAmount);
        }

        /// <summary>
        /// Loose items fall straight down to the first floor. A belt or lift on the way catches them if it
        /// can deliver them; the rest land as a pile.
        /// </summary>
        void DropItem(int2 c, ushort item, int count)
        {
            int2 land = World.FloorBelow(c);
            for (int n = 0; n < count; n++)
            {
                bool caught = false;
                for (int y = c.y; y >= land.y && !caught; y--)
                    caught = Buildings.TryCatch(new int2(c.x, y), item);
                if (caught) continue;
                Colony.Piles.Add(land, item, 1);
                Events.Publish(new ItemDroppedEvent(land, item));
            }
        }

        void AddBreach(int2 c)
        {
            if (Reveal.IsUnexploredOpen(World, c.x, c.y))
                _breachStarts.Add(c);
        }

        int FlushBreaches()
        {
            if (_breachStarts.Length == 0) return 0;
            int discovered = Reveal.Flood(World, _breachStarts.AsArray(), Config.dig.revealRadius, Config.dig.caveRevealLimit);
            _breachStarts.Clear();
            if (discovered > 0) _terrainChanged = true;
            return discovered;
        }

        // ---------------------------------------------------------------- ant intents

        void IAntIntentHandler.Handle(in AntIntent intent, ref Ant ant)
        {
            var inventory = Colony.Inventory;
            switch (intent.Kind)
            {
                case IntentKind.Dig:
                {
                    var result = Digging.DamageCell(World, Content.MaterialSim, intent.Cell, intent.Amount, Events);
                    if (result == DamageResult.None) ant.State = AntState.Idle;
                    else if (result == DamageResult.Opened) _terrainChanged = true;
                    else if (result == DamageResult.Broken)
                    {
                        OnCellBroken(intent.Cell);
                        ant.State = AntState.Idle;
                    }
                    break;
                }
                case IntentKind.Pickup:
                    if (Colony.Piles.TryTake(intent.Cell, inventory, out ushort item))
                    {
                        ant.Carry = item;
                        ant.State = AntState.Carrying;
                        ant.Dest = CarryDest.Undecided;
                        inventory.BeginTransit(item);
                    }
                    else ant.State = AntState.Idle;
                    break;
                case IntentKind.Deliver:
                {
                    // A machine that needs the item first, then the stock, then a belt that can take it.
                    int2 c = intent.Cell;
                    ushort carried = ant.Carry;
                    bool delivered = ant.IsCarrying
                                     && (Buildings.TryFeed(c, carried)
                                         || (Colony.IsStockCell(c, World.Index(c.x, c.y)) && inventory.Deliver(carried))
                                         || Buildings.TryLoad(c, carried));
                    if (delivered)
                    {
                        Events.Publish(new ItemDeliveredEvent(c, carried));
                        ant.Carry = ContentDB.NoItem;
                        ant.State = AntState.Idle;
                    }
                    else
                    {
                        ant.State = AntState.Wander;
                        ant.Timer = 3f;
                    }
                    break;
                }
                case IntentKind.Build:
                    if (Buildings.Work(intent.Cell, intent.Amount) != WorkResult.Working) ant.State = AntState.Idle;
                    break;
                case IntentKind.Drop:
                    if (ant.IsCarrying)
                    {
                        int2 land = World.FloorBelow(intent.Cell);
                        inventory.CancelTransit(ant.Carry);
                        Colony.Piles.Add(land, ant.Carry, 1);
                        Events.Publish(new ItemDroppedEvent(land, ant.Carry));
                        ant.Carry = ContentDB.NoItem;
                    }
                    ant.State = AntState.Wander;
                    ant.Timer = 2f;
                    break;
            }
        }

        // ---------------------------------------------------------------- colony

        void UpdateColony(float dt)
        {
            Colony.UpdateUpkeep(Ants.Count, dt);

            if (TryFindEggCell(out int2 eggCell) && Colony.TryLayEgg(dt, eggCell))
                Events.Publish(new EggLaidEvent(eggCell));

            _hatched.Clear();
            Colony.UpdateEggs(dt, _hatched);
            foreach (var cell in _hatched)
            {
                Ants.Spawn(cell, Seed);
                Events.Publish(new AntHatchedEvent(cell));
            }

            UpdateForage(dt);
        }

        void UpdateForage(float dt)
        {
            var cfg = Config.colony;
            if (Content.SurfaceForage.Length == 0) return;
            Colony.ForageTimer += dt;
            if (Colony.ForageTimer < cfg.forageInterval) return;
            Colony.ForageTimer = 0f;

            int onSurface = 0;
            for (int i = 0; i < Colony.Piles.Count; i++)
            {
                var p = Colony.Piles[i];
                if (p.Cell.y > World.SurfaceY[p.Cell.x] - 2 && Array.IndexOf(Content.SurfaceForage, p.Item) >= 0)
                    onSurface += p.Count;
            }
            if (onSurface >= cfg.maxSurfaceFood) return;

            int x = math.clamp(Colony.QueenCell.x + _rng.NextInt(-cfg.forageRange, cfg.forageRange + 1),
                Config.colony.royalRadius, World.Width - 1 - Config.colony.royalRadius);
            int y = World.Height - 1;
            while (y > 0 && World.Cells[World.Index(x, y - 1)].IsOpen) y--;
            var nav = new NavGrid(World);
            if (!nav.IsWalkable(new int2(x, y)) || y <= World.SurfaceY[x] - 2) return;

            ushort item = Content.SurfaceForage[_rng.NextInt(Content.SurfaceForage.Length)];
            DropItem(new int2(x, y), item, 1);
        }

        int2 FindQueenCell()
        {
            int cx = World.Width / 2;
            var c = new int2(cx, World.SurfaceY[cx] - Content.WorldGen.nestDepth);
            while (c.y > 0 && World.Cells[World.Index(c.x, c.y - 1)].IsOpen) c.y--;
            return c;
        }

        /// <summary>Walkable floor cells within the royal radius (excluding the queen's own cell).</summary>
        List<int2> RoyalFloorCells()
        {
            _scratchCells.Clear();
            var nav = new NavGrid(World);
            int r = Config.colony.royalRadius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var c = Colony.QueenCell + new int2(dx, dy);
                if (dx == 0 && dy == 0) continue;
                if (nav.IsWalkable(c) && nav.IsSolid(c + new int2(0, -1)))
                    _scratchCells.Add(c);
            }
            return _scratchCells;
        }

        int2 RandomRoyalFloorCell()
        {
            var cells = RoyalFloorCells();
            return cells.Count > 0 ? cells[_rng.NextInt(cells.Count)] : Colony.QueenCell;
        }

        bool TryFindEggCell(out int2 cell)
        {
            var cells = RoyalFloorCells();
            for (int attempt = 0; attempt < 6 && cells.Count > 0; attempt++)
            {
                cell = cells[_rng.NextInt(cells.Count)];
                bool taken = false;
                foreach (var egg in Colony.Eggs)
                    if (egg.Cell.Equals(cell)) { taken = true; break; }
                if (!taken) return true;
            }
            cell = Colony.QueenCell;
            return false;
        }

        // ---------------------------------------------------------------- navigation

        void RefreshFields(bool force)
        {
            if (_terrainChanged) Fields.MarkAllDirty();
            if (_designationsChanged) Fields.MarkDirty(FieldKind.Dig);
            if (_storageChanged || Buildings.DropoffChanged) Fields.MarkDirty(FieldKind.Dropoff);
            if (Colony.Piles.Changed || Colony.Inventory.AcceptanceChanged) Fields.MarkDirty(FieldKind.Items);
            if (Buildings.BlueprintsChanged) Fields.MarkDirty(FieldKind.Build);
            if (Buildings.ConsumersChanged != 0)
            {
                for (int i = 0; i < Content.Items.Length; i++)
                {
                    int slot = Content.ConsumerSlot[i];
                    if (slot >= 0 && (Buildings.ConsumersChanged & BuildingSystem.Bit((ushort)i)) != 0)
                        Fields.MarkDirty(FlowFields.ConsumerField(slot));
                }
            }
            _terrainChanged = _designationsChanged = _storageChanged = false;
            Buildings.DropoffChanged = Buildings.BlueprintsChanged = false;
            Buildings.ConsumersChanged = 0;
            Colony.Piles.Changed = false;
            Colony.Inventory.AcceptanceChanged = false;

            _ticksSinceFieldRefresh++;
            if (!force && _ticksSinceFieldRefresh < Config.colony.fieldRefreshTicks) return;
            bool any = false;
            for (int i = 0; i < Fields.FieldCount; i++) any |= Fields.IsDirty(i);
            if (!any) return;
            _ticksSinceFieldRefresh = 0;

            var nav = new NavGrid(World);
            if (Fields.IsDirty(FieldKind.Dig))
            {
                var sources = Fields.Sources(FieldKind.Dig);
                sources.Clear();
                foreach (int index in Colony.Designated)
                {
                    int2 c = GridIndex.ToCell(index, World.ChunksX);
                    for (int d = 0; d < NavGrid.DirectionCount; d++)
                    {
                        int2 n = c + NavGrid.Direction(d);
                        if (nav.IsWalkable(n)) sources.Add(n);
                    }
                }
                _digSlots = sources.Length * Config.worker.diggersPerFrontierCell;
            }
            if (Fields.IsDirty(FieldKind.Items))
            {
                var sources = Fields.Sources(FieldKind.Items);
                sources.Clear();
                _haulSlots = 0;
                for (int i = 0; i < Colony.Piles.Count; i++)
                {
                    var p = Colony.Piles[i];
                    if (!Colony.Inventory.CanAccept(p.Item)) continue;
                    sources.Add(p.Cell);
                    _haulSlots += p.Count;
                }
            }
            if (Fields.IsDirty(FieldKind.Dropoff))
            {
                var sources = Fields.Sources(FieldKind.Dropoff);
                sources.Clear();
                AddRoyalCells(sources, Config.colony.royalRadius);
                foreach (int index in Colony.StorageCells)
                    sources.Add(GridIndex.ToCell(index, World.ChunksX));
                Buildings.AddDropoffSources(sources);
            }
            if (Fields.IsDirty(FieldKind.Build))
            {
                var sources = Fields.Sources(FieldKind.Build);
                sources.Clear();
                Buildings.AddBuildSources(sources);
            }
            for (int i = 0; i < Content.Items.Length; i++)
            {
                int slot = Content.ConsumerSlot[i];
                if (slot < 0 || !Fields.IsDirty(FlowFields.ConsumerField(slot))) continue;
                var sources = Fields.Sources(FlowFields.ConsumerField(slot));
                sources.Clear();
                Buildings.AddConsumerSources((ushort)i, sources);
            }
            if (Fields.IsDirty(FieldKind.Home))
            {
                var sources = Fields.Sources(FieldKind.Home);
                sources.Clear();
                AddRoyalCells(sources, 2);
            }
            Fields.Rebuild(World);
        }

        void AddRoyalCells(NativeList<int2> sources, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                sources.Add(Colony.QueenCell + new int2(dx, dy));
        }

        public void Dispose()
        {
            Ants.Dispose();
            Buildings.Dispose();
            Fields.Dispose();
            World.Dispose();
            Light.Dispose();
            if (_broken.IsCreated) _broken.Dispose();
            if (_breachStarts.IsCreated) _breachStarts.Dispose();
        }
    }
}
