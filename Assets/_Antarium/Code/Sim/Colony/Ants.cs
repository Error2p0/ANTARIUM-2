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
    }

    /// <summary>One worker. Plain data; all ants live in one native list and are updated by Burst jobs.</summary>
    public struct Ant
    {
        /// <summary>Cell the ant is in, or is moving into.</summary>
        public int2 Cell;
        /// <summary>Cell the current step started from. Equal to <see cref="Cell"/> when not moving.</summary>
        public int2 From;
        public float Progress;
        public float StepLength;
        public int2 Heading;

        public AntState State;
        public ushort Carry;
        public int2 Target;
        public float Timer;
        public float WalkPhase;
        public Unity.Mathematics.Random Rng;

        /// <summary>Position in cell space at the end of the previous and current tick (for render interpolation).</summary>
        public float2 PrevPos;
        public float2 Pos;

        public bool IsMoving => !From.Equals(Cell);
        public bool IsCarrying => Carry != ContentDB.NoItem;
    }

    public enum IntentKind : byte
    {
        Dig,
        Pickup,
        Deliver,
    }

    public struct AntIntent
    {
        public int Ant;
        public IntentKind Kind;
        public int2 Cell;
        public float Amount;
    }

    /// <summary>
    /// Worker update in two phases: a parallel Burst job moves ants along flow fields and decides what
    /// to do, emitting intents for anything that changes shared state; then a serial pass applies the
    /// intents in ant order (deterministic) on the main thread.
    /// </summary>
    public sealed class AntSystem : IDisposable
    {
        readonly WorkerConfig _config;
        public NativeList<Ant> Ants;
        NativeQueue<AntIntent> _intents;
        uint _spawnCounter;

        public AntSystem(WorkerConfig config)
        {
            _config = config;
            Ants = new NativeList<Ant>(64, Allocator.Persistent);
            _intents = new NativeQueue<AntIntent>(Allocator.Persistent);
        }

        public int Count => Ants.Length;

        public void Spawn(int2 cell, uint seed)
        {
            float2 pos = new float2(cell) + 0.5f;
            var rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, ++_spawnCounter)) | 1u);
            Ants.Add(new Ant
            {
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

        /// <summary>
        /// Chance that an idle ant takes each kind of job this tick: free openings divided by idle ants.
        /// Keeps hundreds of ants from queueing behind a two-cell tunnel front instead of hauling.
        /// </summary>
        public float2 JobChances(int digSlots, int haulSlots)
        {
            int idle = 0, digging = 0, hauling = 0;
            for (int i = 0; i < Ants.Length; i++)
            {
                switch (Ants[i].State)
                {
                    case AntState.Idle:
                    case AntState.Wander: idle++; break;
                    case AntState.SeekDig:
                    case AntState.Digging: digging++; break;
                    case AntState.SeekItem: hauling++; break;
                }
            }
            float denom = math.max(1, idle);
            return new float2(math.saturate((digSlots - digging) / denom), math.saturate((haulSlots - hauling) / denom));
        }

        /// <summary>Runs the think/move job. Results are applied by <see cref="Resolve"/>.</summary>
        public void Think(WorldGrid world, FlowFields fields, float dt, float speedFactor, float2 jobChances)
        {
            _intents.Clear();
            if (Ants.Length == 0) return;
            new ThinkJob
            {
                Ants = Ants.AsArray(),
                Nav = new NavGrid(world),
                Dig = fields[FieldKind.Dig],
                Items = fields[FieldKind.Items],
                Dropoff = fields[FieldKind.Dropoff],
                Home = fields[FieldKind.Home],
                Intents = _intents.AsParallelWriter(),
                Dt = dt,
                MoveSpeed = _config.moveSpeed * speedFactor,
                FallSpeed = _config.fallSpeed,
                DigPower = _config.digPower * speedFactor,
                DigWeight = _config.digWeight,
                HaulWeight = _config.haulWeight,
                WanderRadius = _config.wanderRadius * FlowFields.StraightCost,
                DigChance = jobChances.x,
                HaulChance = jobChances.y,
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
        }

        [BurstCompile]
        struct ThinkJob : IJobParallelFor
        {
            public NativeArray<Ant> Ants;
            public NavGrid Nav;
            [ReadOnly] public NativeArray<ushort> Dig;
            [ReadOnly] public NativeArray<ushort> Items;
            [ReadOnly] public NativeArray<ushort> Dropoff;
            [ReadOnly] public NativeArray<ushort> Home;
            public NativeQueue<AntIntent>.ParallelWriter Intents;

            public float Dt;
            public float MoveSpeed;
            public float FallSpeed;
            public float DigPower;
            public float DigWeight;
            public float HaulWeight;
            public int WanderRadius;
            public float DigChance;
            public float HaulChance;

            const ushort Unreachable = FlowFields.Unreachable;

            public void Execute(int index)
            {
                var a = Ants[index];
                a.PrevPos = a.Pos;

                if (a.IsMoving)
                {
                    float speed = a.State == AntState.Falling ? FallSpeed : MoveSpeed;
                    a.Progress += speed * Dt / a.StepLength;
                    a.WalkPhase += speed * Dt;
                    if (a.Progress >= 1f)
                    {
                        a.From = a.Cell;
                        a.Progress = 0f;
                    }
                }

                if (!a.IsMoving)
                    Decide(ref a, index);

                a.Pos = math.lerp(new float2(a.From), new float2(a.Cell), a.Progress) + 0.5f;
                Ants[index] = a;
            }

            void Decide(ref Ant a, int index)
            {
                int2 c = a.Cell;
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

                int ci = Nav.Index(c);
                switch (a.State)
                {
                    case AntState.Idle:
                        ChooseJob(ref a, ci);
                        break;

                    case AntState.SeekDig:
                    {
                        ushort d = Dig[ci];
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0)
                        {
                            if (FindDesignated(c, out int2 target))
                            {
                                a.State = AntState.Digging;
                                a.Target = target;
                                a.Heading = target - c;
                            }
                            else a.State = AntState.Idle;
                        }
                        else if (!StepDown(ref a, Dig, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.Digging:
                    {
                        if (!IsDesignatedSolid(a.Target)) { a.State = AntState.Idle; break; }
                        a.WalkPhase += Dt * 6f;
                        Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Dig, Cell = a.Target, Amount = DigPower * Dt });
                        break;
                    }

                    case AntState.SeekItem:
                    {
                        ushort d = Items[ci];
                        if (d == Unreachable) a.State = AntState.Idle;
                        else if (d == 0) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Pickup, Cell = c });
                        else if (!StepDown(ref a, Items, d)) a.State = AntState.Idle;
                        break;
                    }

                    case AntState.Carrying:
                    {
                        ushort d = Dropoff[ci];
                        if (d == Unreachable) { a.State = AntState.Wander; a.Timer = 2f; }
                        else if (d == 0) Intents.Enqueue(new AntIntent { Ant = index, Kind = IntentKind.Deliver, Cell = c });
                        else if (!StepDown(ref a, Dropoff, d)) { a.State = AntState.Wander; a.Timer = 1f; }
                        break;
                    }

                    case AntState.Wander:
                    {
                        a.Timer -= Dt;
                        if (a.Timer <= 0f) { a.State = AntState.Idle; break; }
                        ushort h = Home[ci];
                        if (h != Unreachable && h > WanderRadius) StepDown(ref a, Home, h);
                        else if (a.Rng.NextFloat() < 0.2f) RandomStep(ref a);
                        break;
                    }
                }
            }

            void ChooseJob(ref Ant a, int ci)
            {
                if (a.IsCarrying) { a.State = AntState.Carrying; return; }
                ushort dig = a.Rng.NextFloat() < DigChance ? Dig[ci] : Unreachable;
                ushort item = a.Rng.NextFloat() < HaulChance ? Items[ci] : Unreachable;
                float digScore = dig == Unreachable ? float.MaxValue : (dig + 1) * DigWeight;
                float itemScore = item == Unreachable ? float.MaxValue : (item + 1) * HaulWeight;
                if (digScore == float.MaxValue && itemScore == float.MaxValue)
                {
                    a.State = AntState.Wander;
                    a.Timer = a.Rng.NextFloat(0.8f, 2f);
                }
                else a.State = digScore < itemScore ? AntState.SeekDig : AntState.SeekItem;
            }

            /// <summary>Steps to a neighbour with a lower field value (random among equals).</summary>
            bool StepDown(ref Ant a, NativeArray<ushort> field, ushort current)
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
                    ushort v = field[Nav.Index(n)];
                    if (v >= current || v > bestValue) continue;
                    if (!Nav.CanMove(c, dir)) continue;
                    if (v < bestValue) { bestValue = v; best = n; ties = 1; }
                    else if (a.Rng.NextInt(++ties) == 0) best = n;
                }
                if (best.Equals(c)) return false;
                StartStep(ref a, best);
                return true;
            }

            void RandomStep(ref Ant a)
            {
                int2 c = a.Cell;
                int start = a.Rng.NextInt(NavGrid.DirectionCount);
                for (int k = 0; k < NavGrid.DirectionCount; k++)
                {
                    int2 dir = NavGrid.Direction((start + k) % NavGrid.DirectionCount);
                    if (!Nav.CanMove(c, dir)) continue;
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
