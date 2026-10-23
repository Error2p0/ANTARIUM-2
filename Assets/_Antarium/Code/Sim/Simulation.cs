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
    public sealed partial class Simulation : IDisposable, IAntIntentHandler
    {
        enum CommandKind : byte
        {
            Dig, Designate, Zone, Build, Remove, SetFilter, Research, Ability, Claim, Catch, TriggerEvent, CasteShare, TriggerRaid,
            Pheromone, Possess, Release, PossessAct, PossessInteract,
        }

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

            public Command(CommandKind kind, float2 cellPos, float radius, float power, bool set, int def = 0)
            {
                Kind = kind;
                CellPos = cellPos;
                Radius = radius;
                Power = power;
                Set = set;
                Def = def;
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
        public readonly ResearchSystem Research;
        public readonly AbilitySystem Abilities;
        public readonly ContractSystem Contracts;
        public readonly RandomEventSystem RandomEvents;
        public readonly FluidSystem Fluids;
        public readonly EnemySystem Enemies;
        public readonly PheromoneSystem Pheromones;

        /// <summary>Soldiers alive (counted once per tick).</summary>
        public int Soldiers => _alive[(int)AntCaste.Soldier];
        public int Workers => _alive[(int)AntCaste.Worker];
        /// <summary>Ants alive per <see cref="AntCaste"/> (counted once per tick).</summary>
        public IReadOnlyList<int> AliveByCaste => _alive;
        readonly int[] _alive = new int[Castes.Count];
        readonly int[] _aliveWithEggs = new int[Castes.Count];

        /// <summary>Seconds of simulated time in this colony (saved).</summary>
        public double PlayTime { get; internal set; }

        readonly List<Command> _pending = new();
        readonly List<Egg> _hatched = new();
        readonly List<int2> _scratchCells = new();
        readonly List<ushort> _evicted = new();
        NativeList<int2> _broken;
        NativeList<int2> _breachStarts;
        NativeList<int2> _discovered;
        Unity.Mathematics.Random _rng;
        bool _terrainChanged;
        bool _designationsChanged;
        bool _storageChanged;
        int _ticksSinceFieldRefresh;
        int _digSlots;
        int _haulSlots;

        /// <summary>Starts a new colony on a world generated from <paramref name="seed"/>.</summary>
        public Simulation(ContentDB content, GameConfig config, uint seed) : this(content, config, seed, null) { }

        /// <summary>
        /// With <paramref name="loadedWorld"/> (loading a save) nothing is generated or spawned: the save fills in
        /// the rest and calls <see cref="FinishLoad"/>.
        /// </summary>
        internal Simulation(ContentDB content, GameConfig config, uint seed, WorldGrid loadedWorld)
        {
            Content = content;
            Config = config;
            Seed = seed;
            Clock = new SimClock(config.sim.ticksPerSecond, config.sim.maxTicksPerFrame);
            _rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xC0105u)) | 1u);

            bool fresh = loadedWorld == null;
            World = loadedWorld ?? WorldGenerator.Create(content, seed);
            Fluids = new FluidSystem(World, config.fluids);
            Fluids.SetReactionMaterial(content.FluidReactionMaterial != ContentDB.AirIndex
                ? content.FluidReactionMaterial : content.GetMaterialIndex(content.WorldGen.biomes[0].baseMaterial));
            if (fresh)
            {
                Fluids.Settle(config.fluids.settleSteps);
                Reveal.Initial(World, content.MaterialSim, config.dig.revealRadius);
            }
            Light = new LightSystem(World, config.light);
            var lava = config.fluids.lavaLight;
            Light.LavaEmission = new float3(lava.r, lava.g, lava.b) * config.fluids.lavaLightStrength;
            Light.RecomputeAll(World, content.MaterialSim);

            _broken = new NativeList<int2>(64, Allocator.Persistent);
            _breachStarts = new NativeList<int2>(16, Allocator.Persistent);
            _discovered = new NativeList<int2>(256, Allocator.Persistent);

            Colony = new Colony(content, config.colony, config.castes);
            Colony.QueenCell = FindQueenCell();
            FindRoyalDisplayCells();
            Buildings = new BuildingSystem(content, config.logistics, World, Colony, Events, config.combat);
            Buildings.EngineerBoost = content.Caste(AntCaste.Engineer).machineBoost;
            int slots = content.ConsumerSlotCount;
            Fields = new FlowFields(World, slots * 2); // consumer fields, then supply fields
            Pheromones = new PheromoneSystem(World, config.pheromones);
            Ants = new AntSystem(config.worker, config.logistics, config.combat, config.fluids, content.Items.Length, content.CasteSim);
            for (int i = 0; i < content.Items.Length; i++)
            {
                int slot = content.ConsumerSlot[i];
                Ants.ConsumerField[i] = slot >= 0 ? FlowFields.ConsumerField(slot) : -1;
                Ants.SupplyField[i] = slot >= 0 ? FlowFields.SupplyField(slot, slots) : -1;
            }
            Research = new ResearchSystem(content, Colony, Events);
            Abilities = new AbilitySystem(content, Colony);
            Contracts = new ContractSystem(content, config.progression, this, seed);
            RandomEvents = new RandomEventSystem(content, config.progression, this, seed);
            Enemies = new EnemySystem(content, config, this, seed);
            if (!fresh) return;

            for (int i = 0; i < config.colony.startingWorkers; i++)
                Ants.Spawn(RandomRoyalFloorCell(), seed);
            RefreshFields(force: true);
            Contracts.Tick();
        }

        /// <summary>Rebuilds derived state after a save was loaded into this simulation.</summary>
        internal void FinishLoad()
        {
            Colony.Designated.Clear();
            Colony.StorageCells.Clear();
            for (int i = 0; i < World.Cells.Length; i++)
            {
                var flags = World.Cells[i].Flags;
                if ((flags & CellFlags.DigDesignated) != 0) Colony.Designated.Add(i);
                if ((flags & CellFlags.Storage) != 0)
                {
                    Colony.StorageCells.Add(i);
                    Colony.Inventory.AddStorageCell(GridIndex.ToCell(i, World.ChunksX), i); // no-op if the save restored it
                }
            }

            // Items in transit = carried by ants + riding belts.
            var transit = new int[Content.Items.Length];
            foreach (var a in Ants.Ants)
                if (a.IsCarrying) transit[a.Carry] += math.max(1, (int)a.CarryCount);
            foreach (var b in Buildings.All)
            {
                if (!b.Alive) continue;
                if (!b.Front.IsEmpty) transit[b.Front.Item]++;
                if (!b.Back.IsEmpty) transit[b.Back.Item]++;
            }
            Colony.Inventory.SetInTransit(transit);
            Colony.Inventory.RefreshCapacity();
            Colony.Piles.Changed = true;

            Buildings.MarkNetworkDirty();
            _terrainChanged = _designationsChanged = _storageChanged = true;
            Fields.MarkAllDirty();
            RefreshFields(force: true);
            World.MarkAll(ChunkFlags.TerrainUpload | ChunkFlags.LightUpload | ChunkFlags.FluidUpload);
            Fluids.WakeAllWithFluid();
            Ants.CountCastes(_alive, _aliveWithEggs);
            ValidateNurseryEggs();
            Contracts.Tick();
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

        /// <summary>Researches node <paramref name="research"/> (index into <c>ContentDB.Research</c>) if affordable.</summary>
        public void EnqueueResearch(int research) => _pending.Add(new Command(CommandKind.Research, research, default, default, 0));

        /// <summary>Uses ability <paramref name="ability"/> aimed at <paramref name="cell"/>.</summary>
        public void EnqueueAbility(int ability, int2 cell) => _pending.Add(new Command(CommandKind.Ability, ability, cell, default, 0));

        /// <summary>Claims the reward of the finished contract in board slot <paramref name="slot"/>.</summary>
        public void EnqueueClaim(int slot) => _pending.Add(new Command(CommandKind.Claim, slot, default, default, 0));

        /// <summary>Tries to catch a critter near <paramref name="cellPos"/> (cell space).</summary>
        public void EnqueueCatch(float2 cellPos) => _pending.Add(new Command(CommandKind.Catch, cellPos, 0f, 0f, true));

        /// <summary>Debug: starts random event <paramref name="index"/> now (-1 = a random one).</summary>
        public void EnqueueTriggerEvent(int index) => _pending.Add(new Command(CommandKind.TriggerEvent, index, default, default, 0));

        /// <summary>Sets the share of a specialist caste's eggs (0..1, snapped to the configured step and limits).</summary>
        public void EnqueueCasteShare(AntCaste caste, float share) =>
            _pending.Add(new Command(CommandKind.CasteShare, default, 0f, share, true, (int)caste));

        /// <summary>Debug: a raid arrives now.</summary>
        public void EnqueueTriggerRaid() => _pending.Add(new Command(CommandKind.TriggerRaid, 0, default, default, 0));

        /// <summary>Paints (or erases) a circle of pheromone <paramref name="kind"/>.</summary>
        public void EnqueuePheromone(PheromoneKind kind, float2 cellPos, float radius, bool set) =>
            _pending.Add(new Command(CommandKind.Pheromone, cellPos, radius, 0f, set, (int)kind));

        /// <summary>Takes control of the ant nearest to <paramref name="cellPos"/> (cell space).</summary>
        public void EnqueuePossess(float2 cellPos) => _pending.Add(new Command(CommandKind.Possess, cellPos, 0f, 0f, true));

        /// <summary>Lets go of the possessed ant.</summary>
        public void EnqueueRelease() => _pending.Add(new Command(CommandKind.Release, 0, default, default, 0));

        /// <summary>The possessed ant bites toward <paramref name="cellPos"/>: an enemy there, else the rock (dig power times <paramref name="combo"/>).</summary>
        public void EnqueuePossessAct(float2 cellPos, float combo) => _pending.Add(new Command(CommandKind.PossessAct, cellPos, 0f, combo, true));

        /// <summary>The possessed ant picks up a pile next to it, or delivers / puts down what it carries.</summary>
        public void EnqueuePossessInteract() => _pending.Add(new Command(CommandKind.PossessInteract, 0, default, default, 0));

        /// <summary>Direction the player pushes the possessed ant (held keys; zero = stand still). Read every tick.</summary>
        public int2 PossessInput { get; set; }

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

            PlayTime += dt;
            UpdateColony(dt);
            Abilities.Tick(dt);
            UpdateEngineers();
            Buildings.Tick(dt);
            Colony.Inventory.UpdateRates(dt, Config.logistics.rateSmoothing);
            Colony.Meter.Update(dt);
            StepHazards(dt);
            Pheromones.Tick(dt, SustainEmitters);
            UpdateScouts(dt);
            UpdateRally(dt);
            RefreshFields(force: _forceFields);
            _forceFields = false;

            var inventory = Colony.Inventory;
            for (int i = 0; i < Content.Items.Length; i++)
                Ants.StockAccepts[i] = inventory.StockAccepts((ushort)i) ? (byte)1 : (byte)0;
            int supplySlots = Ants.UpdateSupplyNeed(inventory);
            var mods = Colony.Mods;
            float work = Colony.SpeedFactor * Abilities.WorkFactor;
            Ants.HealthMultiplier = mods.Mult(StatKind.AntHealth);
            var tick = AntTick.Default(dt);
            tick.MoveFactor = work * mods.Mult(StatKind.WorkerSpeed);
            tick.DigFactor = work * mods.Mult(StatKind.WorkerDig);
            tick.BuildFactor = work * mods.Mult(StatKind.BuildSpeed);
            tick.HazardResistance = mods.Mult(StatKind.HazardResistance);
            tick.Chances = Ants.Chances(_digSlots, _haulSlots, Buildings.BuildSlots, supplySlots, NurseSlots());
            tick.TrailSpeed = Config.pheromones.trailSpeed;
            tick.RallySpeed = _rallySpeed;
            tick.RallyArrive = RallyArriveCells * FlowFields.StraightCost;
            tick.PossessSpeed = Config.possession.speed;
            tick.PossessDamageTaken = Config.possession.damageTaken;
            tick.PossessDir = PossessInput;
            Ants.Think(World, Fields, Pheromones, tick);
            Ants.Resolve(this);
            ReinforceTrails();
            ProcessCompletedWalls();
            FlushBreaches();
            Enemies.Tick(dt);
            RemoveDeadAnts();
            RandomEvents.Tick(dt);
            Contracts.Tick();
        }

        /// <summary>Refreshes derived data (light) after ticks. Call once per frame.</summary>
        public void UpdateDerived()
        {
            Light.UpdateDirty(World, Content.MaterialSim);
        }

        /// <summary>Debug/cheat: adds up to <paramref name="items"/> of every non-food item (as far as the stock has room) and <paramref name="food"/> food.</summary>
        public void GiveResources(int items, float food)
        {
            var inv = Colony.Inventory;
            for (int i = 0; i < Content.Items.Length; i++)
                if (!Content.Items[i].IsFood) inv.Add((ushort)i, items, Colony.QueenCell);
            inv.Food = math.min(inv.FoodCapacity, inv.Food + food);
            inv.AcceptanceChanged = true;
        }

        /// <summary>Debug / tests: replaces a cell (terrain is re-uploaded, relit and re-routed).</summary>
        public void SetCell(int2 c, Cell cell) => EditCell(c, cell);

        /// <summary>Debug/cheat: spawns workers in the royal chamber.</summary>
        public void SpawnWorkers(int count) => SpawnAnts(count, AntCaste.Worker);

        /// <summary>Spawns ants of a caste on the royal chamber floor (offline hatching, debug).</summary>
        public void SpawnAnts(int count, AntCaste caste)
        {
            for (int i = 0; i < count; i++)
                Ants.Spawn(RandomRoyalFloorCell(), Seed, caste);
            Ants.CountCastes(_alive, _aliveWithEggs);
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
                case CommandKind.Research: Research.Research(cmd.Def); break;
                case CommandKind.Ability: UseAbility(cmd.Def, cmd.Cell); break;
                case CommandKind.Claim: Contracts.Claim(cmd.Def); break;
                case CommandKind.Catch: RandomEvents.Catch(cmd.CellPos); break;
                case CommandKind.CasteShare: Colony.SetCasteShare((AntCaste)cmd.Def, cmd.Power); break;
                case CommandKind.TriggerRaid: Enemies.TriggerRaid(); break;
                case CommandKind.Pheromone:
                    if (cmd.Def >= 0 && cmd.Def < Antarium.Content.Pheromones.Count)
                        Pheromones.Paint((PheromoneKind)cmd.Def, cmd.CellPos, cmd.Radius, cmd.Set);
                    break;
                case CommandKind.Possess: Possess(cmd.CellPos); break;
                case CommandKind.Release: Release(PossessionPhase.Released); break;
                case CommandKind.PossessAct: PossessAct(cmd.CellPos, cmd.Power); break;
                case CommandKind.PossessInteract: PossessInteract(); break;
                case CommandKind.TriggerEvent:
                {
                    int n = Content.Events.Length;
                    if (n == 0) break;
                    int start = cmd.Def >= 0 ? cmd.Def : _rng.NextInt(n);
                    for (int k = 0; k < (cmd.Def >= 0 ? 1 : n); k++)
                        if (RandomEvents.Trigger((start + k) % n)) break;
                    break;
                }
            }
        }

        void ResolveDig(in Command cmd) => ResolveDig(new DigCommand(cmd.CellPos, cmd.Radius, cmd.Power));

        void ResolveDig(DigCommand command)
        {
            _broken.Clear();
            var cmd = command;
            var dig = new DigCommand(cmd.CellPos, cmd.Radius, cmd.Power * Colony.Mods.Mult(StatKind.PlayerDig));
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
                if (designate)
                {
                    _designationsChanged = true;
                    continue;
                }
                _storageChanged = true;
                if (cmd.Set) Colony.Inventory.AddStorageCell(new int2(x, y), index);
                else EvictStorage(new int2(x, y), index);
            }

            if (!designate && _storageChanged)
                Buildings.MarkNetworkDirty(); // belts ending in a storage zone deliver to the stock
        }

        /// <summary>Items in an erased storage cell move to other storage; what does not fit drops to the floor.</summary>
        void EvictStorage(int2 c, int index)
        {
            _evicted.Clear();
            Colony.Inventory.RemoveStorageCell(index, _evicted);
            var land = World.FloorBelow(c);
            foreach (ushort item in _evicted)
            {
                Colony.Piles.Add(land, item, 1);
                Events.Publish(new ItemDroppedEvent(land, item));
            }
        }

        // ---------------------------------------------------------------- terrain

        void OnCellBroken(int2 c)
        {
            _terrainChanged = true;
            var stats = Colony.Stats;
            stats.CellsDug++;
            stats.MaxDepth = math.max(stats.MaxDepth, World.Depth(c.x, c.y));
            int index = World.Index(c.x, c.y);
            if (Colony.Designated.Remove(index)) _designationsChanged = true;

            Reveal.Around(World, c, Config.dig.revealRadius);
            Fluids.Wake(c);
            CheckUnstableAbove(c);
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
            _discovered.Clear();
            int discovered = Reveal.Flood(World, _breachStarts.AsArray(), Config.dig.revealRadius, Config.dig.caveRevealLimit, _discovered);
            if (discovered > 0)
            {
                _terrainChanged = true;
                Enemies.OnCaveDiscovered(_discovered, _breachStarts.AsArray());
            }
            _breachStarts.Clear();
            return discovered;
        }

        // ---------------------------------------------------------------- ant intents

        void IAntIntentHandler.Handle(in AntIntent intent, ref Ant ant)
        {
            var inventory = Colony.Inventory;
            if (HandleCasteIntent(intent, ref ant)) return;
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
                        // Haulers take more of the same item from the pile while it still fits somewhere.
                        int capacity = Ants.Caste(ant.Caste).Carry;
                        int count = 1;
                        while (count < capacity && Colony.Piles.TryTakeItem(intent.Cell, item, inventory)) count++;
                        ant.Carry = item;
                        ant.CarryCount = (byte)count;
                        ant.State = AntState.Carrying;
                        ant.Dest = CarryDest.Undecided;
                        for (int n = 0; n < count; n++) inventory.BeginTransit(item);
                    }
                    else ant.State = AntState.Idle;
                    break;
                case IntentKind.Deliver:
                {
                    // A machine that needs the item first, then the stock, then a belt that can take it.
                    int2 c = intent.Cell;
                    int delivered = DeliverCarried(ref ant, c);
                    if (!ant.IsCarrying) ant.State = AntState.Idle;
                    else if (delivered > 0)
                    {
                        ant.State = AntState.Carrying; // some left over: pick a new destination for the rest
                        ant.Dest = CarryDest.Undecided;
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
                case IntentKind.Withdraw:
                {
                    int2 c = intent.Cell;
                    int index = World.Index(c.x, c.y);
                    bool royal = Colony.IsRoyalCell(c);
                    if (!ant.IsCarrying && inventory.TryWithdraw(ant.Want, index, royal))
                    {
                        // Haulers fetch as many as machines still want (shared with the others fetching this tick).
                        int count = 1, capacity = Ants.Caste(ant.Caste).Carry;
                        while (count < capacity && Ants.SupplyNeed[ant.Want] > 0 && inventory.TryWithdraw(ant.Want, index, royal))
                        {
                            Ants.SupplyNeed[ant.Want]--;
                            count++;
                        }
                        ant.Carry = ant.Want;
                        ant.CarryCount = (byte)count;
                        ant.State = AntState.Carrying;
                        ant.Dest = CarryDest.Consumer;
                        for (int n = 0; n < count; n++) inventory.BeginTransit(ant.Carry);
                    }
                    else ant.State = AntState.Idle;
                    break;
                }
                case IntentKind.Drop:
                    DropCarried(ref ant, intent.Cell);
                    ant.State = AntState.Wander;
                    ant.Timer = 2f;
                    break;
            }
        }

        /// <summary>
        /// Hands carried items over at <paramref name="c"/>, one at a time: a machine that needs them first, then the stock,
        /// then a belt that can take them. Returns how many were delivered.
        /// </summary>
        int DeliverCarried(ref Ant ant, int2 c)
        {
            var inventory = Colony.Inventory;
            bool stockCell = Colony.IsStockCell(c, World.Index(c.x, c.y));
            int delivered = 0;
            while (ant.IsCarrying)
            {
                ushort carried = ant.Carry;
                bool ok = Buildings.TryFeed(c, carried) || (stockCell && inventory.Deliver(carried, c)) || Buildings.TryLoad(c, carried);
                if (!ok) break;
                Events.Publish(new ItemDeliveredEvent(c, carried));
                delivered++;
                if (ant.CarryCount > 1) ant.CarryCount--;
                else ant.ClearCarry();
            }
            return delivered;
        }

        /// <summary>Puts everything the ant carries down as a pile below <paramref name="c"/>.</summary>
        void DropCarried(ref Ant ant, int2 c)
        {
            if (!ant.IsCarrying) return;
            int2 land = World.FloorBelow(c);
            int count = math.max(1, (int)ant.CarryCount);
            for (int n = 0; n < count; n++) Colony.Inventory.CancelTransit(ant.Carry);
            Colony.Piles.Add(land, ant.Carry, count);
            Events.Publish(new ItemDroppedEvent(land, ant.Carry));
            ant.ClearCarry();
        }

        // ---------------------------------------------------------------- abilities, rewards, event helpers

        void UseAbility(int index, int2 cell)
        {
            var result = Abilities.Check(index);
            int amount = 0;
            if (result == AbilityResult.Ok)
            {
                var def = Content.Abilities[index];
                switch (def.kind)
                {
                    case AbilityKind.SeismicSense: amount = RevealOre(cell, def.radius); break;
                    case AbilityKind.RoyalDecree:
                        amount = LayEggsNow(def.count);
                        if (amount == 0) result = AbilityResult.NoEffect;
                        break;
                    case AbilityKind.PheromoneBurst:
                        amount = StartRally(cell, def);
                        if (amount == 0) result = AbilityResult.NoEffect;
                        break;
                }
            }
            if (result != AbilityResult.Ok)
            {
                Events.Publish(new AbilityFailedEvent(index, result));
                return;
            }
            Abilities.Begin(index);
            Events.Publish(new AbilityUsedEvent(index, cell, amount));
        }

        /// <summary>Reveals every solid cell that drops something (ore, grubs...) within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        int RevealOre(int2 center, float radius)
        {
            int r = (int)math.ceil(radius), revealed = 0;
            for (int y = center.y - r; y <= center.y + r; y++)
            for (int x = center.x - r; x <= center.x + r; x++)
            {
                if (!World.InBounds(x, y) || math.distancesq(new float2(x, y), new float2(center)) > radius * radius) continue;
                int i = World.Index(x, y);
                var cell = World.Cells[i];
                if (cell.IsOpen || (cell.Flags & CellFlags.Scanned) != 0 || Content.MaterialSim[cell.Material].DropItem == ContentDB.NoItem) continue;
                cell.Flags |= CellFlags.Revealed | CellFlags.Scanned;
                World.Cells[i] = cell;
                World.MarkChanged(x, y, ChunkFlags.TerrainUpload);
                revealed++;
            }
            return revealed;
        }

        int LayEggsNow(int count)
        {
            var cells = RoyalFloorCells();
            if (cells.Count == 0) return 0;
            for (int i = 0; i < count; i++)
            {
                var cell = cells[_rng.NextInt(cells.Count)];
                Colony.LayEggNow(cell);
                Events.Publish(new EggLaidEvent(cell));
            }
            return count;
        }

        /// <summary>Adds a reward to the colony: items to the stock (what does not fit drops by the queen), food, knowledge.</summary>
        internal void GrantReward(ushort[] items, int[] counts, float scale, float food, float knowledge)
        {
            var inv = Colony.Inventory;
            for (int i = 0; i < items.Length; i++)
            {
                int n = math.max(1, (int)math.round(counts[i] * scale));
                int missing = inv.Add(items[i], n, Colony.QueenCell);
                if (missing <= 0) continue;
                var land = World.FloorBelow(Colony.QueenCell);
                Colony.Piles.Add(land, items[i], missing);
                Events.Publish(new ItemDroppedEvent(land, items[i]));
            }
            if (food > 0f) inv.Food = math.min(inv.FoodCapacity, inv.Food + food);
            inv.AddKnowledge(knowledge, studied: false);
            inv.AcceptanceChanged = true;
        }

        /// <summary>Changes a cell from an event (meteorite, vein, pocket): uploads it, relights and re-routes.</summary>
        internal void EditCell(int2 c, Cell cell)
        {
            World.Cells[World.Index(c.x, c.y)] = cell;
            World.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload | ChunkFlags.LightRecompute);
            _terrainChanged = true;
            Fluids.Wake(c);
        }

        /// <summary>Ants inside cells that just became solid climb up to the first open cell.</summary>
        internal void LiftAntsOutOfSolid(int2 min, int2 max)
        {
            for (int i = 0; i < Ants.Count; i++)
            {
                var a = Ants.Ants[i];
                if (math.any(a.Cell < min) || math.any(a.Cell > max) || World[a.Cell.x, a.Cell.y].IsOpen) continue;
                var c = a.Cell;
                while (c.y < World.Height - 1 && !World[c.x, c.y].IsOpen) c.y++;
                a.Cell = a.From = c;
                a.Progress = 0f;
                a.Pos = a.PrevPos = new float2(c) + 0.5f;
                a.State = AntState.Idle;
                Ants.Ants[i] = a;
            }
        }

        // ---------------------------------------------------------------- colony

        void UpdateColony(float dt)
        {
            Ants.CountCastes(_alive, _aliveWithEggs);
            Colony.UpdateUpkeep(_alive, dt);
            Colony.QueenDisturbed = Enemies.RaidersInChamber;

            if (TryFindEggCell(out int2 eggCell) && Colony.TryLayEgg(dt, eggCell, _aliveWithEggs))
            {
                Events.Publish(new EggLaidEvent(eggCell));
                _broodChanged = true;
            }

            _hatched.Clear();
            Colony.UpdateEggs(dt, _hatched, NurseryBonus);
            foreach (var egg in _hatched) Hatch(egg.Cell, egg.Caste);
            if (_hatched.Count > 0) _broodChanged = true;
            AgeCarriedEggs(dt);

            UpdateForage(dt);
        }

        void Hatch(int2 cell, AntCaste caste)
        {
            Ants.Spawn(cell, Seed, caste);
            _alive[(int)caste]++;
            Colony.Stats.AntsHatched++;
            Colony.Stats.HatchedCaste[(int)caste]++;
            Events.Publish(new AntHatchedEvent(cell, caste));
        }

        void UpdateForage(float dt)
        {
            var cfg = Config.colony;
            if (Content.SurfaceForage.Length == 0) return;
            float forage = Colony.Mods.Mult(StatKind.ForageRate);
            Colony.ForageTimer += dt;
            if (Colony.ForageTimer < cfg.forageInterval / forage) return;
            Colony.ForageTimer = 0f;

            int onSurface = 0;
            for (int i = 0; i < Colony.Piles.Count; i++)
            {
                var p = Colony.Piles[i];
                if (p.Cell.y > World.SurfaceY[p.Cell.x] - 2 && Array.IndexOf(Content.SurfaceForage, p.Item) >= 0)
                    onSurface += p.Count;
            }
            if (onSurface >= cfg.maxSurfaceFood * forage) return;

            int x = math.clamp(Colony.QueenCell.x + _rng.NextInt(-cfg.forageRange, cfg.forageRange + 1),
                Config.colony.royalRadius, World.Width - 1 - Config.colony.royalRadius);
            int y = World.Height - 1;
            while (y > 0 && World.Cells[World.Index(x, y - 1)].IsOpen) y--;
            var nav = new NavGrid(World);
            if (!nav.IsWalkable(new int2(x, y)) || y <= World.SurfaceY[x] - 2) return;

            ushort item = Content.SurfaceForage[_rng.NextInt(Content.SurfaceForage.Length)];
            DropItem(new int2(x, y), item, 1);
        }

        /// <summary>Open cells of the royal chamber where its stock is shown: floor level first, nearest the queen first.</summary>
        void FindRoyalDisplayCells()
        {
            var cells = Colony.RoyalDisplayCells;
            cells.Clear();
            int r = Config.colony.royalRadius;
            var q = Colony.QueenCell;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var c = q + new int2(dx, dy);
                if (!World.InBounds(c.x, c.y)) continue;
                if (math.abs(dx) <= 1 && dy >= 0 && dy <= 1) continue; // behind the queen's sprite
                var cell = World[c.x, c.y];
                if (cell.IsOpen && cell.Material != ContentDB.AirIndex) cells.Add(c);
            }
            cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y)
                : math.abs(a.x - q.x) != math.abs(b.x - q.x) ? math.abs(a.x - q.x).CompareTo(math.abs(b.x - q.x))
                : a.x.CompareTo(b.x));
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
                    if (!egg.InNursery && egg.Cell.Equals(cell)) { taken = true; break; }
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
            if (Buildings.GuardsChanged) Fields.MarkDirty(FieldKind.Guard);
            // Moving targets: rebuilt at the refresh rate while anyone needs them.
            if (Enemies.Changed && Soldiers > 0)
            {
                Fields.MarkDirty(FieldKind.Enemies);
                Enemies.Changed = false;
            }
            if (Enemies.AnyHunting) Fields.MarkDirty(FieldKind.Ants);
            if (Enemies.AnyLeaving) Fields.MarkDirty(FieldKind.Exit);
            ulong locations = Colony.Inventory.LocationsChanged;
            Colony.Inventory.LocationsChanged = 0;
            for (int i = 0; i < Content.Items.Length && locations != 0; i++)
            {
                int slot = Content.ConsumerSlot[i];
                if (slot >= 0 && (locations & BuildingSystem.Bit((ushort)i)) != 0)
                    Fields.MarkDirty(FlowFields.SupplyField(slot, Content.ConsumerSlotCount));
            }
            if (Buildings.ConsumersChanged != 0)
            {
                for (int i = 0; i < Content.Items.Length; i++)
                {
                    int slot = Content.ConsumerSlot[i];
                    if (slot >= 0 && (Buildings.ConsumersChanged & BuildingSystem.Bit((ushort)i)) != 0)
                        Fields.MarkDirty(FlowFields.ConsumerField(slot));
                }
            }
            // Pheromones: trails and Avoid change step costs; Dig / Gather / Defend change which jobs come first.
            int scents = Pheromones.ChangedKinds;
            Pheromones.ChangedKinds = 0;
            if ((scents & (Bit(PheromoneKind.Trail) | Bit(PheromoneKind.Avoid))) != 0) Fields.MarkScentedDirty();
            if ((scents & Bit(PheromoneKind.Dig)) != 0) Fields.MarkDirty(FieldKind.Dig);
            if ((scents & Bit(PheromoneKind.Gather)) != 0) Fields.MarkDirty(FieldKind.Items);
            if ((scents & Bit(PheromoneKind.Defend)) != 0) Fields.MarkDirty(FieldKind.Guard);
            MarkCasteFieldsDirty();

            _terrainChanged = _designationsChanged = _storageChanged = false;
            Buildings.DropoffChanged = Buildings.BlueprintsChanged = Buildings.GuardsChanged = false;
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
            int priorityOffset = Config.pheromones.priorityCells * FlowFields.StraightCost;
            if (Fields.IsDirty(FieldKind.Dig))
            {
                // Orders inside Dig scent start at 0, the others "late"; none inside Avoid scent.
                var sources = Fields.Sources(FieldKind.Dig);
                var late = Fields.LateSources(FieldKind.Dig, priorityOffset);
                sources.Clear();
                late.Clear();
                bool anyScent = Pheromones.Cells[(int)PheromoneKind.Dig] > 0;
                foreach (int index in Colony.Designated)
                {
                    if (Pheromones.Has(PheromoneKind.Avoid, index)) continue;
                    int2 c = GridIndex.ToCell(index, World.ChunksX);
                    bool scented = Pheromones.Has(PheromoneKind.Dig, index);
                    for (int d = 0; d < NavGrid.DirectionCount; d++)
                    {
                        int2 n = c + NavGrid.Direction(d);
                        if (!nav.IsWalkable(n) || Pheromones.Has(PheromoneKind.Avoid, n)) continue;
                        bool first = !anyScent || scented || Pheromones.Has(PheromoneKind.Dig, n);
                        (first ? sources : late).Add(n);
                    }
                }
                _digSlots = (sources.Length + late.Length) * Config.worker.diggersPerFrontierCell;
            }
            if (Fields.IsDirty(FieldKind.Items))
            {
                var sources = Fields.Sources(FieldKind.Items);
                var late = Fields.LateSources(FieldKind.Items, priorityOffset);
                sources.Clear();
                late.Clear();
                _haulSlots = 0;
                bool anyScent = Pheromones.Cells[(int)PheromoneKind.Gather] > 0;
                for (int i = 0; i < Colony.Piles.Count; i++)
                {
                    var p = Colony.Piles[i];
                    if (!Colony.Inventory.CanAccept(p.Item) || Pheromones.Has(PheromoneKind.Avoid, p.Cell)) continue;
                    bool first = !anyScent || Pheromones.Has(PheromoneKind.Gather, p.Cell);
                    (first ? sources : late).Add(p.Cell);
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
                for (int i = sources.Length - 1; i >= 0; i--)
                    if (Pheromones.Has(PheromoneKind.Avoid, sources[i])) sources.RemoveAtSwapBack(i);
            }
            for (int i = 0; i < Content.Items.Length; i++)
            {
                int slot = Content.ConsumerSlot[i];
                if (slot < 0 || !Fields.IsDirty(FlowFields.ConsumerField(slot))) continue;
                var sources = Fields.Sources(FlowFields.ConsumerField(slot));
                sources.Clear();
                Buildings.AddConsumerSources((ushort)i, sources);
            }
            for (int i = 0; i < Content.Items.Length; i++)
            {
                int slot = Content.ConsumerSlot[i];
                if (slot < 0) continue;
                int field = FlowFields.SupplyField(slot, Content.ConsumerSlotCount);
                if (!Fields.IsDirty(field)) continue;
                var sources = Fields.Sources(field);
                sources.Clear();
                foreach (var container in Colony.Inventory.Containers)
                {
                    if (container.Counts[i] == 0) continue;
                    if (container.IsRoyal) AddRoyalCells(sources, Config.colony.royalRadius);
                    else sources.Add(container.Cell);
                }
            }
            if (Fields.IsDirty(FieldKind.Home))
            {
                var sources = Fields.Sources(FieldKind.Home);
                sources.Clear();
                AddRoyalCells(sources, 2);
            }
            if (Fields.IsDirty(FieldKind.Enemies))
            {
                var sources = Fields.Sources(FieldKind.Enemies);
                sources.Clear();
                Enemies.AddEnemySources(sources);
            }
            if (Fields.IsDirty(FieldKind.Guard))
            {
                var sources = Fields.Sources(FieldKind.Guard);
                sources.Clear();
                Buildings.AddGuardSources(sources);
                Pheromones.AddCells(PheromoneKind.Defend, sources);
            }
            FillCasteSources();
            if (Fields.IsDirty(FieldKind.Ants))
            {
                var sources = Fields.Sources(FieldKind.Ants);
                sources.Clear();
                foreach (var a in Ants.Ants)
                    if (!a.IsDead) sources.Add(a.Cell);
            }
            if (Fields.IsDirty(FieldKind.Exit))
            {
                var sources = Fields.Sources(FieldKind.Exit);
                sources.Clear();
                if (Enemies.AnyLeaving) sources.Add(Enemies.RaidCell);
            }
            Fields.Rebuild(World, Pheromones);
        }

        static int Bit(PheromoneKind kind) => 1 << (int)kind;

        void AddRoyalCells(NativeList<int2> sources, int radius)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                sources.Add(Colony.QueenCell + new int2(dx, dy));
        }

        /// <summary>Random state of the simulation itself (saved).</summary>
        internal Unity.Mathematics.Random Rng
        {
            get => _rng;
            set => _rng = value;
        }

        public void Dispose()
        {
            Pheromones.Dispose();
            if (_frontier.IsCreated) _frontier.Dispose();
            Ants.Dispose();
            Buildings.Dispose();
            Fields.Dispose();
            Fluids.Dispose();
            Enemies.Dispose();
            World.Dispose();
            Light.Dispose();
            if (_broken.IsCreated) _broken.Dispose();
            if (_breachStarts.IsCreated) _breachStarts.Dispose();
            if (_discovered.IsCreated) _discovered.Dispose();
        }
    }
}
