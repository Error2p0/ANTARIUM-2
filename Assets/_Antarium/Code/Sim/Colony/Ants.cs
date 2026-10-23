using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum AntState : byte
    {
        Idle,
        SeekDig,
        Digging,
        SeekItem,
        Carrying,
        Wander,
        Falling,
        SeekBuild,
        Building,
        /// <summary>Walking to the stock to fetch <see cref="Ant.Want"/> for a machine.</summary>
        SeekSupply,
        /// <summary>Soldier: closing in on an enemy.</summary>
        Hunting,
        /// <summary>Soldier: next to an enemy, biting.</summary>
        Fighting,
        /// <summary>Caught in deep water, dense gas or lava: getting out.</summary>
        Swimming,
        /// <summary>Killed this tick; removed by the simulation.</summary>
        Dead,
        /// <summary>Nurse: walking to an egg to carry it to a nursery.</summary>
        SeekEgg,
        /// <summary>Nurse: carrying an egg to a nursery.</summary>
        CarryEgg,
        /// <summary>Scout: walking to the edge of the known tunnels.</summary>
        Scouting,
        /// <summary>Scout: chasing a golden beetle.</summary>
        SeekCritter,
        /// <summary>Engineer: walking to a machine that wants tending.</summary>
        SeekMachine,
        /// <summary>Engineer: tending the machine at <see cref="Ant.Target"/> (it works faster).</summary>
        Tending,
        /// <summary>Answering a Pheromone Burst: hurrying to its spot for <see cref="Ant.Timer"/> seconds.</summary>
        Rallying,
    }

    public enum DeathCause : byte
    {
        None,
        Drowned,
        Burned,
        Gassed,
        Crushed,
        /// <summary>Killed by an enemy (<see cref="Ant.Killer"/>).</summary>
        Killed,
    }

    /// <summary>Where a carrying ant decided to take its load.</summary>
    public enum CarryDest : byte
    {
        Undecided,
        /// <summary>The stock: royal chamber, storage zones and belts leading there.</summary>
        Stock,
        /// <summary>A machine that needs the item, or a belt feeding one.</summary>
        Consumer,
    }

    /// <summary>One ant of any caste. Plain data; all ants live in one native list and are updated by Burst jobs.</summary>
    public struct Ant
    {
        public AntCaste Caste;
        public float Health;
        /// <summary>Seconds since the ant was last hurt (regeneration, hurt flash).</summary>
        public float SinceHurt;
        public DeathCause Cause;
        /// <summary>Enemy definition that killed the ant, or -1.</summary>
        public short Killer;

        /// <summary>Cell the ant is in, or is moving into.</summary>
        public int2 Cell;
        /// <summary>Cell the current step started from. Equal to <see cref="Cell"/> when not moving.</summary>
        public int2 From;
        public float Progress;
        public float StepLength;
        public int2 Heading;

        public AntState State;
        public ushort Carry;
        /// <summary>How many of <see cref="Carry"/> it holds (haulers carry several).</summary>
        public byte CarryCount;
        public CarryDest Dest;
        /// <summary>Item a supplying worker is on its way to fetch from the stock.</summary>
        public ushort Want;
        public int2 Target;
        public float Timer;
        public float WalkPhase;
        public Unity.Mathematics.Random Rng;

        /// <summary>Nurse carrying an egg (its caste and age travel with it).</summary>
        public bool CarryingEgg;
        public AntCaste EggCaste;
        public float EggAge;
        /// <summary>Controlled by the player: no AI decisions, moves by <see cref="AntTick.PossessDir"/>.</summary>
        public bool Possessed;
        /// <summary>Seconds of the bite animation left (the possessed ant digging or biting).</summary>
        public float ActTimer;

        /// <summary>Position in cell space at the end of the previous and current tick (for render interpolation).</summary>
        public float2 PrevPos;
        public float2 Pos;

        public bool IsMoving => !From.Equals(Cell);
        public bool IsCarrying => Carry != ContentDB.NoItem;
        public bool IsSoldier => Caste == AntCaste.Soldier;
        public bool IsDead => State == AntState.Dead;

        /// <summary>Takes the carried items away (they were delivered or dropped).</summary>
        public void ClearCarry()
        {
            Carry = ContentDB.NoItem;
            CarryCount = 0;
        }
    }

    public enum IntentKind : byte
    {
        Dig,
        Pickup,
        Deliver,
        Build,
        /// <summary>Put the carried items down as a pile: nothing reachable takes them right now.</summary>
        Drop,
        /// <summary>Take <see cref="Ant.Want"/> out of the stock at this cell (to carry to a machine).</summary>
        Withdraw,
        /// <summary>Nurse: pick up an egg at this cell.</summary>
        PickEgg,
        /// <summary>Nurse: lay the carried egg into the nursery at this cell.</summary>
        PlaceEgg,
        /// <summary>Nurse: no nursery to go to; put the egg down here.</summary>
        DropEgg,
        /// <summary>Scout: catch the golden beetle next to it.</summary>
        CatchCritter,
    }

    public struct AntIntent
    {
        public int Ant;
        public IntentKind Kind;
        public int2 Cell;
        public float Amount;
    }

    /// <summary>Chance per tick that an idle ant considers each kind of job: free openings divided by idle ants.</summary>
    public struct JobChances
    {
        public float Dig, Haul, Build, Supply, Nurse;
    }

    /// <summary>Per-tick inputs of the ant job.</summary>
    public struct AntTick
    {
        public float Dt;
        /// <summary>Multipliers on the base walking speed, dig power and build power (starvation, research, Frenzy).</summary>
        public float MoveFactor, DigFactor, BuildFactor;
        public float HazardResistance;
        public JobChances Chances;
        /// <summary>Walking-speed multipliers on trails and while answering a Pheromone Burst.</summary>
        public float TrailSpeed, RallySpeed;
        /// <summary>Rallying ants stop once they are this close (field units).</summary>
        public int RallyArrive;
        /// <summary>The possessed ant: walking-speed multiplier, share of damage taken, and the direction the player pushes.</summary>
        public float PossessSpeed, PossessDamageTaken;
        public int2 PossessDir;

        public static AntTick Default(float dt) => new()
        {
            Dt = dt, MoveFactor = 1f, DigFactor = 1f, BuildFactor = 1f, HazardResistance = 1f,
            Chances = new JobChances { Dig = 1f, Haul = 1f, Build = 1f, Supply = 1f, Nurse = 1f },
            TrailSpeed = 1f, RallySpeed = 1f, PossessSpeed = 1f, PossessDamageTaken = 1f,
        };
    }

    /// <summary>
    /// Ant update in two phases: a parallel Burst job moves ants along flow fields and decides what to do, emitting
    /// intents for anything that changes shared state; then a serial pass applies the intents in ant order
    /// (deterministic) on the main thread. Castes differ by numbers (<see cref="CasteSim"/>: speed, dig and build
    /// power, carrying, job weights) and by role: soldiers fight, nurses move eggs, scouts explore, engineers tend
    /// machines. The possessed ant takes no decisions; it steps where the player pushes it.
    /// </summary>
    public sealed class AntSystem : IDisposable
    {
        readonly WorkerConfig _config;
        readonly LogisticsConfig _logistics;
        readonly CombatConfig _combat;
        readonly FluidConfig _fluids;
        readonly NativeArray<CasteSim> _castes;
        /// <summary>Research multiplier on every ant's hit points (set by the simulation each tick).</summary>
        public float HealthMultiplier = 1f;
        public NativeList<Ant> Ants;
        NativeQueue<AntIntent> _intents;
        /// <summary>Seeds each new ant's random stream (saved, so loaded games keep diverging the same way).</summary>
        internal uint SpawnCounter;

        /// <summary>Per item: 1 if the stock has room for it right now (refreshed every tick).</summary>
        public NativeArray<byte> StockAccepts;
        /// <summary>Per item: index of its consumer flow field, or -1.</summary>
        public NativeArray<int> ConsumerField;
        /// <summary>Per item: index of the flow field to the stock containers holding it, or -1.</summary>
        public NativeArray<int> SupplyField;
        /// <summary>Per item: how many more units machines want fetched from the stock (see <see cref="UpdateSupplyNeed"/>).</summary>
        public NativeArray<int> SupplyNeed;

        public AntSystem(WorkerConfig config, LogisticsConfig logistics, CombatConfig combat, FluidConfig fluids, int itemCount,
            NativeArray<CasteSim> castes)
        {
            _config = config;
            _logistics = logistics;
            _combat = combat;
            _fluids = fluids;
            _castes = castes;
            Ants = new NativeList<Ant>(64, Allocator.Persistent);
            _intents = new NativeQueue<AntIntent>(Allocator.Persistent);
            StockAccepts = new NativeArray<byte>(Math.Max(1, itemCount), Allocator.Persistent);
            ConsumerField = new NativeArray<int>(Math.Max(1, itemCount), Allocator.Persistent);
            SupplyField = new NativeArray<int>(Math.Max(1, itemCount), Allocator.Persistent);
            SupplyNeed = new NativeArray<int>(Math.Max(1, itemCount), Allocator.Persistent);
            for (int i = 0; i < ConsumerField.Length; i++) ConsumerField[i] = SupplyField[i] = -1;
            _supplySeeking = new int[Math.Max(1, itemCount)];
            _supplyCarried = new int[Math.Max(1, itemCount)];
        }

        readonly int[] _supplySeeking;
        readonly int[] _supplyCarried;

        /// <summary>
        /// Units of each item to fetch from the stock for machines: free machine buffer space minus what is
        /// already on its way to a machine or being fetched, limited by what the stock holds. Returns the total.
        /// </summary>
        public int UpdateSupplyNeed(Inventory inventory)
        {
            Array.Clear(_supplySeeking, 0, _supplySeeking.Length);
            Array.Clear(_supplyCarried, 0, _supplyCarried.Length);
            for (int i = 0; i < Ants.Length; i++)
            {
                var a = Ants[i];
                if (a.State == AntState.SeekSupply) _supplySeeking[a.Want]++;
                else if (a.IsCarrying && a.Dest == CarryDest.Consumer) _supplyCarried[a.Carry] += math.max(1, (int)a.CarryCount);
            }
            int total = 0;
            for (int i = 0; i < SupplyNeed.Length && i < inventory.Items.Length; i++)
            {
                int need = SupplyField[i] < 0 ? 0 : math.min(inventory.Items[i] - _supplySeeking[i],
                    inventory.ConsumerRoom[i] - _supplyCarried[i] - _supplySeeking[i]);
                SupplyNeed[i] = math.max(0, need);
                total += SupplyNeed[i];
            }
            return total;
        }

        public int Count => Ants.Length;

        /// <summary>Full hit points of a caste (research included).</summary>
        public float MaxHealth(AntCaste caste) => _castes[(int)caste].Health * HealthMultiplier;

        public CasteSim Caste(AntCaste caste) => _castes[(int)caste];

        public int CountCaste(AntCaste caste)
        {
            int n = 0;
            for (int i = 0; i < Ants.Length; i++)
                if (Ants[i].Caste == caste) n++;
            return n;
        }

        /// <summary>Fills <paramref name="alive"/> with ants per caste; eggs carried by nurses count for their caste in <paramref name="withCarriedEggs"/>.</summary>
        public void CountCastes(int[] alive, int[] withCarriedEggs = null)
        {
            Array.Clear(alive, 0, alive.Length);
            if (withCarriedEggs != null) Array.Clear(withCarriedEggs, 0, withCarriedEggs.Length);
            for (int i = 0; i < Ants.Length; i++)
            {
                var a = Ants[i];
                if (a.IsDead) continue;
                alive[(int)a.Caste]++;
                if (withCarriedEggs == null) continue;
                withCarriedEggs[(int)a.Caste]++;
                if (a.CarryingEgg) withCarriedEggs[(int)a.EggCaste]++;
            }
        }

        /// <summary>Index of the ant nearest to <paramref name="cellPos"/> (cell space) within <paramref name="radius"/>, or -1.</summary>
        public int AntAt(float2 cellPos, float radius)
        {
            int best = -1;
            float bestDist = radius;
            for (int i = 0; i < Ants.Length; i++)
            {
                if (Ants[i].IsDead) continue;
                float d = math.distance(Ants[i].Pos, cellPos);
                if (d > bestDist) continue;
                bestDist = d;
                best = i;
            }
            return best;
        }

        /// <summary>Index of the possessed ant, or -1.</summary>
        public int PossessedIndex()
        {
            for (int i = 0; i < Ants.Length; i++)
                if (Ants[i].Possessed) return i;
            return -1;
        }

        public void Spawn(int2 cell, uint seed, AntCaste caste = AntCaste.Worker)
        {
            float2 pos = new float2(cell) + 0.5f;
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, ++SpawnCounter)) | 1u);
            Ants.Add(new Ant
            {
                Caste = caste,
                Health = MaxHealth(caste),
                SinceHurt = 1000f,
                Killer = -1,
                Cell = cell,
                From = cell,
                StepLength = 1f,
                Heading = new int2(rng.NextBool() ? 1 : -1, 0),
                State = AntState.Idle,
                Carry = ContentDB.NoItem,
                Rng = rng,
                WalkPhase = rng.NextFloat(4f),
                PrevPos = pos,
                Pos = pos,
            });
        }

        /// <summary>Removes every ant standing in the rectangle [<paramref name="min"/>, <paramref name="max"/>]. Returns how many.</summary>
        public int RemoveInside(int2 min, int2 max)
        {
            int removed = 0;
            for (int i = Ants.Length - 1; i >= 0; i--)
            {
                var c = Ants[i].Cell;
                if (math.any(c < min) || math.any(c > max)) continue;
                Ants.RemoveAtSwapBack(i);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Chance that an idle ant takes each kind of job this tick: free openings divided by idle ants.
        /// Keeps hundreds of ants from queueing behind a two-cell tunnel front instead of hauling.
        /// </summary>
        public JobChances Chances(int digSlots, int haulSlots, int buildSlots, int supplySlots, int nurseSlots)
        {
            // Openings are shared among the idle ants that would take that kind of job (an idle miner does not
            // make a worker less likely to haul).
            int idleDig = 0, idleHaul = 0, idleBuild = 0, idleSupply = 0, idleNurse = 0;
            int digging = 0, hauling = 0, building = 0, nursing = 0;
            for (int i = 0; i < Ants.Length; i++)
            {
                var a = Ants[i];
                if (a.IsSoldier) continue;
                switch (a.State)
                {
                    case AntState.Idle:
                    case AntState.Wander:
                    {
                        var c = _castes[(int)a.Caste];
                        if (c.DigJob > 0f) idleDig++;
                        if (c.HaulJob > 0f) idleHaul++;
                        if (c.BuildJob > 0f) idleBuild++;
                        if (c.SupplyJob > 0f) idleSupply++;
                        if (a.Caste == AntCaste.Nurse) idleNurse++;
                        break;
                    }
                    case AntState.SeekDig:
                    case AntState.Digging: digging++; break;
                    case AntState.SeekItem: hauling++; break;
                    case AntState.SeekBuild:
                    case AntState.Building: building++; break;
                    case AntState.SeekEgg:
                    case AntState.CarryEgg: nursing++; break;
                }
            }
            // Supply openings already exclude ants fetching (see UpdateSupplyNeed).
            return new JobChances
            {
                Dig = math.saturate((digSlots - digging) / (float)math.max(1, idleDig)),
                Haul = math.saturate((haulSlots - hauling) / (float)math.max(1, idleHaul)),
                Build = math.saturate((buildSlots - building) / (float)math.max(1, idleBuild)),
                Supply = math.saturate(supplySlots / (float)math.max(1, idleSupply)),
                Nurse = math.saturate((nurseSlots - nursing) / (float)math.max(1, idleNurse)),
            };
        }

        /// <summary>Runs the think/move job. Results are applied by <see cref="Resolve"/>.</summary>
        public void Think(WorldGrid world, FlowFields fields, PheromoneSystem pheromones, in AntTick tick)
        {
            _intents.Clear();
            if (Ants.Length == 0) return;
            float resist = 1f / math.max(0.01f, tick.HazardResistance);
            new ThinkJob
            {
                Ants = Ants.AsArray(),
                Nav = new NavGrid(world),
                Fields = fields.All,
                CellCount = fields.CellCount,
                Castes = _castes,
                Trail = pheromones.Of(PheromoneKind.Trail),
                Avoid = pheromones.Of(PheromoneKind.Avoid),
                StockAccepts = StockAccepts,
                ConsumerField = ConsumerField,
                SupplyField = SupplyField,
                SupplyNeed = SupplyNeed,
                Intents = _intents.AsParallelWriter(),
                Tick = tick,
                MoveSpeed = _config.moveSpeed * tick.MoveFactor,
                FallSpeed = _config.fallSpeed,
                DigPower = _config.digPower * tick.DigFactor,
                BuildPower = _logistics.buildPower * tick.BuildFactor,
                DigWeight = _config.digWeight,
                HaulWeight = _config.haulWeight,
                BuildWeight = _logistics.buildWeight,
                SupplyWeight = _logistics.supplyWeight,
                ConsumerBias = _logistics.consumerBias,
                WanderRadius = _config.wanderRadius * FlowFields.StraightCost,
                HealthMultiplier = HealthMultiplier,
                RegenPerSecond = _combat.regenPerSecond,
                RegenDelay = _combat.regenDelay,
                DrownDamage = _combat.drownDamage * resist,
                LavaDamage = _combat.lavaDamage * resist,
                GasDamage = _combat.gasDamage * resist,
                GasHarmLevel = (byte)math.clamp(_fluids.gasHarmLevel, 1, 255),
                SwimFactor = _combat.swimSpeedFactor,
                SoldierRange = (int)(_combat.soldierRange * FlowFields.StraightCost),
                GuardRadius = (int)(_combat.guardRadius * FlowFields.StraightCost),
            }.Schedule(Ants.Length, 32).Complete();
        }

        /// <summary>Applies this tick's intents in ant order through <paramref name="handler"/>.</summary>
        public void Resolve(IAntIntentHandler handler)
        {
            if (_intents.Count == 0) return;
            var intents = _intents.ToArray(Allocator.Temp);
            intents.Sort(new ByAnt());
            for (int i = 0; i < intents.Length; i++)
            {
                var intent = intents[i];
                var ant = Ants[intent.Ant];
                handler.Handle(intent, ref ant);
                Ants[intent.Ant] = ant;
            }
            intents.Dispose();
        }

        struct ByAnt : IComparer<AntIntent>
        {
            public int Compare(AntIntent a, AntIntent b) => a.Ant != b.Ant ? a.Ant.CompareTo(b.Ant) : ((byte)a.Kind).CompareTo((byte)b.Kind);
        }

        public void Dispose()
        {
            if (Ants.IsCreated) Ants.Dispose();
            if (_intents.IsCreated) _intents.Dispose();
            if (StockAccepts.IsCreated) StockAccepts.Dispose();
            if (ConsumerField.IsCreated) ConsumerField.Dispose();
            if (SupplyField.IsCreated) SupplyField.Dispose();
            if (SupplyNeed.IsCreated) SupplyNeed.Dispose();
        }

        [BurstCompile]
        struct ThinkJob : IJobParallelFor
        {
            public NativeArray<Ant> Ants;
            public NavGrid Nav;
            /// <summary>All flow fields, field-major (see <see cref="FlowFields.All"/>).</summary>
            [ReadOnly] public NativeArray<ushort> Fields;
            public int CellCount;
            [ReadOnly] public NativeArray<CasteSim> Castes;
            [ReadOnly] public NativeArray<byte> Trail;
            [ReadOnly] public NativeArray<byte> Avoid;
            [ReadOnly] public NativeArray<byte> StockAccepts;
            [ReadOnly] public NativeArray<int> ConsumerField;
            [ReadOnly] public NativeArray<int> SupplyField;
            [ReadOnly] public NativeArray<int> SupplyNeed;
            public NativeQueue<AntIntent>.ParallelWriter Intents;

            public AntTick Tick;
            public float MoveSpeed;
            public float FallSpeed;
            public float DigPower;
            public float BuildPower;
            public float DigWeight;
            public float HaulWeight;
            public float BuildWeight;
            public float SupplyWeight;
            public float ConsumerBias;
            public int WanderRadius;
            public float HealthMultiplier;
            public float RegenPerSecond;
            public float RegenDelay;
            public float DrownDamage;
            public float LavaDamage;
            public float GasDamage;
            public byte GasHarmLevel;
            public float SwimFactor;
            /// <summary>Soldiers: enemy field distance (field units) they react to / stay within of their post.</summary>
            public int SoldierRange;
            public int GuardRadius;

            const ushort Unreachable = FlowFields.Unreachable;
            const int DigField = (int)FieldKind.Dig;
            const int ItemsField = (int)FieldKind.Items;
            const int DropoffField = (int)FieldKind.Dropoff;
            const int HomeField = (int)FieldKind.Home;
            const int BuildField = (int)FieldKind.Build;
            const int EnemyField = (int)FieldKind.Enemies;
            const int GuardField = (int)FieldKind.Guard;
            const int BroodField = (int)FieldKind.Brood;
            const int NurseryField = (int)FieldKind.Nursery;
            const int FrontierField = (int)FieldKind.Frontier;
            const int MachinesField = (int)FieldKind.Machines;
            const int CrittersField = (int)FieldKind.Critters;
            const int RallyField = (int)FieldKind.Rally;

            float Dt => Tick.Dt;

            ushort F(int field, int cellIndex) => Fields[field * CellCount + cellIndex];

            public void Execute(int index)
            {
                var a = Ants[index];
                if (a.IsDead) return;
                a.PrevPos = a.Pos;
                var caste = Castes[(int)a.Caste];
                if (a.ActTimer > 0f) a.ActTimer -= Dt;
                if (a.State == AntState.Rallying) a.Timer -= Dt;

                // Hazards where the ant is; regeneration after a while without damage.
                float damage = Hazard(a.Cell, out var cause);
                if (damage > 0f)
                {
                    a.Health -= damage * (a.Possessed ? Tick.PossessDamageTaken : 1f) * Dt;
                    a.SinceHurt = 0f;
                    if (a.Health <= 0f)
                    {
                        a.State = AntState.Dead;
                        a.Cause = cause;
                        Ants[index] = a;
                        return;
                    }
                }
                else
                {
                    a.SinceHurt += Dt;
                    float max = caste.Health * HealthMultiplier;
                    if (a.SinceHurt >= RegenDelay && a.Health < max) a.Health = math.min(max, a.Health + RegenPerSecond * Dt);
                }

                if (a.IsMoving)
                {
                    float speed = a.State == AntState.Falling ? FallSpeed : MoveSpeed * caste.Speed;
                    if (a.State == AntState.Swimming) speed *= SwimFactor;
                    else if (a.State != AntState.Falling)
                    {
                        if (a.State == AntState.Rallying) speed *= Tick.RallySpeed;
                        if (a.Possessed) speed *= Tick.PossessSpeed;
                        if (OnTrail(a.From) && OnTrail(a.Cell)) speed *= Tick.TrailSpeed;
                    }
                    a.Progress += speed * Dt / a.StepLength;
                    a.WalkPhase += speed * Dt;
                    if (a.Progress >= 1f)
                    {
                        a.From = a.Cell;
                        a.Progress = 0f;
                    }
                }

                if (!a.IsMoving)
                    Decide(ref a, index, caste);

                a.Pos = math.lerp(new float2(a.From), new float2(a.Cell), a.Progress) + 0.5f;
                Ants[index] = a;
            }

            bool OnTrail(int2 c) => Nav.InBounds(c) && Trail[Nav.Index(c)] != 0;
            bool InAvoid(int2 c) => Nav.InBounds(c) && Avoid[Nav.Index(c)] != 0;

            float Hazard(int2 c, out DeathCause cause)
            {
                cause = DeathCause.None;
                var f = Nav.FluidAt(c);
                if (f.Level == 0) return 0f;
                switch (f.Kind)
                {
                    case FluidKind.Lava: cause = DeathCause.Burned; return LavaDamage;
                    case FluidKind.Water when f.Level >= Nav.DeepFluid: cause = DeathCause.Drowned; return DrownDamage;
                    case FluidKind.Gas when f.Level >= GasHarmLevel: cause = DeathCause.Gassed; return GasDamage;
                }
                return 0f;
            }

            /// <summary>
            /// Caught in deep water, dense gas or lava: step to the dry neighbour closest to home, otherwise swim
            /// up (down out of gas, which rises), otherwise anywhere open.
            /// </summary>
            void Swim(ref Ant a)
            {
                a.State = AntState.Swimming;
                int2 c = a.Cell;
                int2 best = c;
                ushort bestHome = Unreachable;
                bool found = false;
                for (int i = 0; i < NavGrid.DirectionCount; i++)
                {
                    int2 d = NavGrid.Direction(i);
                    if (!Nav.CanMove(c, d)) continue;
                    ushort h = F(HomeField, Nav.Index(c + d));
                    if (found && h >= bestHome) continue;
                    found = true;
                    bestHome = h;
                    best = c + d;
                }
                if (found)
                {
                    StartStep(ref a, best);
                    return;
                }
                int2 pref = Nav.FluidAt(c).Kind == FluidKind.Gas ? new int2(0, -1) : new int2(0, 1);
                if (Nav.CanSwim(c, pref))
                {
                    StartStep(ref a, c + pref);
                    return;
                }
                int start = a.Rng.NextInt(NavGrid.DirectionCount);
                for (int k = 0; k < NavGrid.DirectionCount; k++)
                {
                    int2 d = NavGrid.Direction((start + k) % NavGrid.DirectionCount);
                    if (d.y == -pref.y || !Nav.CanSwim(c, d)) continue;
                    StartStep(ref a, c + d);
                    return;
                }
            }

            void Decide(ref Ant a, int index, in CasteSim caste)
            {
                int2 c = a.Cell;
                if (Nav.IsFlooded(c))
                {
                    Swim(ref a);
                    return;
                }
                if (a.State == AntState.Swimming) a.State = AntState.Idle;
                if (!Nav.IsWalkable(c))
                {
                    int2 below = c + new int2(0, -1);
                    if (Nav.InBounds(below) && !Nav.IsSolid(below))
                    {
                        a.State = AntState.Falling;
                        StartStep(ref a, below);
                        return;
                    }
                    a.State = AntState.Idle; // wedged; wait for terrain to change
                    return;
                }
                if (a.State == AntState.Falling) a.State = AntState.Idle;
                if (a.Possessed)
                {
                    DecidePossessed(ref a);
                    return;
                }

                int ci = Nav.Index(c);
                if (a.State == AntState.Rallying)
                {
                    ushort r = F(RallyField, ci);
                    if (a.Timer <= 0f || r == Unreachable || r <= Tick.RallyArrive || !StepDown(ref a, RallyField, r)) a.State = AntState.Idle;
                    else return;
                }
                if (a.IsSoldier)
                {
                    DecideSoldier(ref a, ci);
                    return;
                }
                switch (a.State)
                {
                    case AntState.Idle:
                        ChooseJob(ref a, ci, caste);
                        break;

                    case AntState.SeekDig:
                    {
                        // Jobs outside Dig scent start "late" in the field, so their cells are local minima above 0.
                        ushort d = F(DigField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d != 0 && StepDown(ref a, DigField, d)) { }
                        else if (FindDesignated(c, out int2 target))
                        {
                            a.State = AntState.Digging;
                            a.Target = target;
                            a.Heading = target - c;
                        }
                        else a.State = AntState.Idle;
                        break;
                    }

                    case AntState.Digging:
                    {
                        if (!IsDesignatedSolid(a.Target)) { a.State = AntState.Idle; break; }
                        a.WalkPhase += Dt * 6f;
                        Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Dig, Cell = a.Target, Amount = DigPower * caste.Dig * Dt });
                        break;
                    }

                    case AntState.SeekItem:
                    {
                        ushort d = F(ItemsField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d != 0 && StepDown(ref a, ItemsField, d)) { }
                        else Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Pickup, Cell = c });
                        break;
                    }

                    case AntState.Carrying:
                    {
                        if (!a.IsCarrying) { a.State = AntState.Idle; break; }
                        // Choose once between the stock (incl. belts leading there, if it has room) and a machine
                        // that needs this item (incl. belts feeding it), biased toward machines; keep that choice
                        // until it becomes unreachable. (Re-deciding every step would always pick the stock once
                        // inside the royal chamber, even with a machine standing right there.)
                        ushort dd = StockAccepts[a.Carry] != 0 ? F(DropoffField, ci) : Unreachable;
                        int cf = ConsumerField[a.Carry];
                        ushort dc = cf >= 0 ? F(cf, ci) : Unreachable;
                        if (a.Dest == CarryDest.Consumer && dc == Unreachable) a.Dest = CarryDest.Undecided;
                        if (a.Dest == CarryDest.Stock && dd == Unreachable) a.Dest = CarryDest.Undecided;
                        if (a.Dest == CarryDest.Undecided)
                        {
                            if (dd == Unreachable && dc == Unreachable)
                            {
                                // Stock full and no machine wants it (or none reachable): put it down rather than
                                // walk around holding it; it is picked up again once it can be delivered.
                                Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Drop, Cell = c });
                                break;
                            }
                            float sd = dd == Unreachable ? float.MaxValue : dd;
                            float sc = dc == Unreachable ? float.MaxValue : dc * ConsumerBias;
                            a.Dest = sc < sd ? CarryDest.Consumer : CarryDest.Stock;
                        }
                        bool toConsumer = a.Dest == CarryDest.Consumer;
                        ushort dist = toConsumer ? dc : dd;
                        if (dist == 0) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Deliver, Cell = c });
                        else if (!StepDown(ref a, toConsumer ? cf : DropoffField, dist)) { a.State = AntState.Wander; a.Timer = 1f; }
                        break;
                    }

                    case AntState.SeekBuild:
                    {
                        ushort d = F(BuildField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0)
                        {
                            if (FindBlueprint(c, out int2 target))
                            {
                                a.State = AntState.Building;
                                a.Target = target;
                                if (!target.Equals(c)) a.Heading = target - c;
                            }
                            else a.State = AntState.Idle;
                        }
                        else if (!StepDown(ref a, BuildField, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.SeekSupply:
                    {
                        int field = SupplyField[a.Want];
                        ushort d = field >= 0 ? F(field, ci) : Unreachable;
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Withdraw, Cell = c });
                        else if (!StepDown(ref a, field, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.Building:
                    {
                        if (!IsBlueprint(a.Target)) { a.State = AntState.Idle; break; }
                        a.WalkPhase += Dt * 6f;
                        Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Build, Cell = a.Target, Amount = BuildPower * caste.Build * Dt });
                        break;
                    }

                    case AntState.SeekEgg:
                    {
                        ushort d = F(BroodField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.PickEgg, Cell = c });
                        else if (!StepDown(ref a, BroodField, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.CarryEgg:
                    {
                        if (!a.CarryingEgg) { a.State = AntState.Idle; break; }
                        ushort d = F(NurseryField, ci);
                        if (d == Unreachable) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.DropEgg, Cell = c });
                        else if (d == 0 || !StepDown(ref a, NurseryField, d)) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.PlaceEgg, Cell = c });
                        break;
                    }

                    case AntState.Scouting:
                    {
                        if (a.Timer > 0f)
                        {
                            // Looking around (the simulation senses the rock around scouts), then on to the next edge.
                            a.Timer -= Dt;
                            if (a.Timer <= 0f) a.State = AntState.Idle;
                            break;
                        }
                        ushort d = F(FrontierField, ci);
                        if (d != Unreachable && d != 0 && StepDown(ref a, FrontierField, d)) break;
                        a.Timer = a.Rng.NextFloat(1f, 2.5f);
                        break;
                    }

                    case AntState.SeekCritter:
                    {
                        ushort d = F(CrittersField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d <= FlowFields.DiagonalCost) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.CatchCritter, Cell = c });
                        else if (!StepDown(ref a, CrittersField, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.SeekMachine:
                    {
                        ushort d = F(MachinesField, ci);
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0)
                        {
                            a.State = AntState.Tending;
                            a.Target = c;
                        }
                        else if (!StepDown(ref a, MachinesField, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.Tending:
                        // The simulation sends it back to Idle when the machine is gone.
                        a.WalkPhase += Dt * 3f;
                        break;

                    case AntState.Wander:
                    {
                        a.Timer -= Dt;
                        if (a.Timer <= 0f) { a.State = AntState.Idle; break; }
                        ushort h = F(HomeField, ci);
                        if (h != Unreachable && h > WanderRadius) StepDown(ref a, HomeField, h);
                        else if (a.Rng.NextFloat() < 0.2f) RandomStep(ref a);
                        break;
                    }
                }
            }

            /// <summary>The player steers: step to the neighbour that best matches the pushed direction.</summary>
            void DecidePossessed(ref Ant a)
            {
                if (a.State != AntState.Idle && a.State != AntState.Carrying) a.State = AntState.Idle;
                int2 push = Tick.PossessDir;
                if (push.x == 0 && push.y == 0) return;
                float2 want = math.normalize(new float2(push));
                int2 c = a.Cell;
                int2 best = c;
                float bestScore = 0.3f;
                for (int i = 0; i < NavGrid.DirectionCount; i++)
                {
                    int2 d = NavGrid.Direction(i);
                    float score = math.dot(math.normalize(new float2(d)), want);
                    if (score <= bestScore || !Nav.CanMove(c, d)) continue;
                    bestScore = score;
                    best = c + d;
                }
                if (!best.Equals(c)) StartStep(ref a, best);
                else if (push.x != 0) a.Heading = new int2((int)math.sign(push.x), 0); // blocked: at least turn around
            }

            /// <summary>
            /// Soldiers: go after enemies within range (bite when next to one); otherwise wait around the nearest
            /// guard post or Defend scent, or near the queen if there is neither.
            /// </summary>
            void DecideSoldier(ref Ant a, int ci)
            {
                ushort e = F(EnemyField, ci);
                if (e != Unreachable && e <= SoldierRange)
                {
                    if (e <= FlowFields.DiagonalCost)
                    {
                        a.State = AntState.Fighting;
                        a.WalkPhase += Dt * 6f;
                        return;
                    }
                    a.State = AntState.Hunting;
                    if (!StepDown(ref a, EnemyField, e)) a.State = AntState.Fighting;
                    return;
                }
                if (a.State == AntState.Hunting || a.State == AntState.Fighting) a.State = AntState.Idle;

                int field = GuardField;
                ushort g = F(GuardField, ci);
                if (g == Unreachable)
                {
                    field = HomeField;
                    g = F(HomeField, ci);
                }
                if (g != Unreachable && g > GuardRadius) StepDown(ref a, field, g);
                else if (a.Rng.NextFloat() < 0.05f) RandomStep(ref a);
            }

            void ChooseJob(ref Ant a, int ci, in CasteSim caste)
            {
                if (a.IsCarrying)
                {
                    a.State = AntState.Carrying;
                    a.Dest = CarryDest.Undecided;
                    return;
                }
                if (a.CarryingEgg)
                {
                    a.State = AntState.CarryEgg;
                    return;
                }
                // Role jobs first; then the jobs every caste may share by its weights.
                switch (a.Caste)
                {
                    case AntCaste.Nurse:
                        if (a.Rng.NextFloat() < Tick.Chances.Nurse && F(BroodField, ci) != Unreachable)
                        {
                            a.State = AntState.SeekEgg;
                            return;
                        }
                        break;
                    case AntCaste.Scout:
                        if (F(CrittersField, ci) != Unreachable)
                        {
                            a.State = AntState.SeekCritter;
                            return;
                        }
                        if (F(FrontierField, ci) != Unreachable)
                        {
                            a.State = AntState.Scouting;
                            a.Timer = 0f;
                            return;
                        }
                        break;
                    case AntCaste.Engineer:
                        if (F(MachinesField, ci) != Unreachable)
                        {
                            a.State = AntState.SeekMachine;
                            return;
                        }
                        break;
                }

                ushort dig = caste.DigJob > 0f && a.Rng.NextFloat() < Tick.Chances.Dig ? F(DigField, ci) : Unreachable;
                ushort item = caste.HaulJob > 0f && a.Rng.NextFloat() < Tick.Chances.Haul ? F(ItemsField, ci) : Unreachable;
                ushort build = caste.BuildJob > 0f && a.Rng.NextFloat() < Tick.Chances.Build ? F(BuildField, ci) : Unreachable;
                float digScore = dig == Unreachable ? float.MaxValue : (dig + 1) * DigWeight * caste.DigJob;
                float itemScore = item == Unreachable ? float.MaxValue : (item + 1) * HaulWeight * caste.HaulJob;
                float buildScore = build == Unreachable ? float.MaxValue : (build + 1) * BuildWeight * caste.BuildJob;

                // Supply: the nearest stocked item a machine is waiting for.
                float supplyScore = float.MaxValue;
                ushort want = 0;
                if (caste.SupplyJob > 0f && a.Rng.NextFloat() < Tick.Chances.Supply)
                {
                    for (int i = 0; i < SupplyNeed.Length; i++)
                    {
                        if (SupplyNeed[i] <= 0 || SupplyField[i] < 0) continue;
                        ushort d = F(SupplyField[i], ci);
                        if (d == Unreachable) continue;
                        float score = (d + 1) * SupplyWeight * caste.SupplyJob;
                        if (score >= supplyScore) continue;
                        supplyScore = score;
                        want = (ushort)i;
                    }
                }

                float best = math.min(math.min(digScore, itemScore), math.min(buildScore, supplyScore));
                if (best == float.MaxValue)
                {
                    a.State = AntState.Wander;
                    a.Timer = a.Rng.NextFloat(0.8f, 2f);
                }
                else if (best == buildScore) a.State = AntState.SeekBuild;
                else if (best == supplyScore)
                {
                    a.State = AntState.SeekSupply;
                    a.Want = want;
                }
                else a.State = digScore <= itemScore ? AntState.SeekDig : AntState.SeekItem;
            }

            /// <summary>Steps to a neighbour with a lower field value (random among equals).</summary>
            bool StepDown(ref Ant a, int field, ushort current)
            {
                int2 c = a.Cell;
                int2 best = c;
                ushort bestValue = current;
                int ties = 0;
                for (int i = 0; i < NavGrid.DirectionCount; i++)
                {
                    int2 dir = NavGrid.Direction(i);
                    int2 n = c + dir;
                    if (!Nav.InBounds(n)) continue;
                    ushort v = F(field, Nav.Index(n));
                    if (v >= current || v > bestValue) continue;
                    if (!Nav.CanMove(c, dir)) continue;
                    if (v < bestValue) { bestValue = v; best = n; ties = 1; }
                    else if (a.Rng.NextInt(++ties) == 0) best = n;
                }
                if (best.Equals(c)) return false;
                StartStep(ref a, best);
                return true;
            }

            /// <summary>A random step; never into an Avoid area unless already inside one.</summary>
            void RandomStep(ref Ant a)
            {
                int2 c = a.Cell;
                bool inAvoid = InAvoid(c);
                int start = a.Rng.NextInt(NavGrid.DirectionCount);
                for (int k = 0; k < NavGrid.DirectionCount; k++)
                {
                    int2 dir = NavGrid.Direction((start + k) % NavGrid.DirectionCount);
                    if (!Nav.CanMove(c, dir)) continue;
                    if (!inAvoid && InAvoid(c + dir)) continue;
                    StartStep(ref a, c + dir);
                    return;
                }
            }

            void StartStep(ref Ant a, int2 next)
            {
                int2 d = next - a.Cell;
                a.From = a.Cell;
                a.Cell = next;
                a.Progress = 0f;
                a.StepLength = d.x != 0 && d.y != 0 ? 1.4142f : 1f;
                a.Heading = d;
            }

            bool FindDesignated(int2 c, out int2 target)
            {
                for (int i = 0; i < NavGrid.DirectionCount; i++)
                {
                    int2 n = c + NavGrid.Direction(i);
                    if (IsDesignatedSolid(n)) { target = n; return true; }
                }
                target = c;
                return false;
            }

            /// <summary>Blueprint cell at the ant's own cell or next to it.</summary>
            bool FindBlueprint(int2 c, out int2 target)
            {
                target = c;
                if (IsBlueprint(c)) return true;
                for (int i = 0; i < NavGrid.DirectionCount; i++)
                {
                    int2 n = c + NavGrid.Direction(i);
                    if (IsBlueprint(n)) { target = n; return true; }
                }
                return false;
            }

            bool IsBlueprint(int2 c) => Nav.InBounds(c) && (Nav.Cells[Nav.Index(c)].Flags & CellFlags.Blueprint) != 0;

            bool IsDesignatedSolid(int2 c)
            {
                if (!Nav.InBounds(c)) return false;
                var cell = Nav.Cells[Nav.Index(c)];
                return (cell.Flags & CellFlags.DigDesignated) != 0 && !cell.IsEmpty;
            }
        }
    }

    public interface IAntIntentHandler
    {
        void Handle(in AntIntent intent, ref Ant ant);
    }
}
