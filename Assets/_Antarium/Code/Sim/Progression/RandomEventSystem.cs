using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>A random event waiting for the player (beetle on the loose, ants to rescue).</summary>
    public struct ActiveEvent
    {
        public int Id;
        /// <summary>Index into <see cref="ContentDB.Events"/>.</summary>
        public int Def;
        public RandomEventKind Kind;
        /// <summary>Where to look.</summary>
        public int2 Cell;
        /// <summary>Trapped ants: the pocket (inclusive).</summary>
        public int2 Min, Max;
        /// <summary>Seconds left to act; the event is lost at 0.</summary>
        public float TimeLeft;
        public int Count;
    }

    /// <summary>A creature wandering the tunnels (the golden beetle). Moves like an ant, without jobs.</summary>
    public struct Critter
    {
        public int EventId;
        public int2 Cell;
        public int2 From;
        public float Progress;
        public float StepLength;
        public int2 Heading;
        public float WalkPhase;
        public float2 PrevPos;
        public float2 Pos;

        public bool IsMoving => !From.Equals(Cell);
    }

    /// <summary>
    /// Random events that reward attention: every few minutes one is picked by weight among those the colony
    /// qualifies for (by depth). Meteorites and rich veins change the terrain and are done at once; beetles
    /// and trapped ants wait for the player with a timer.
    /// </summary>
    public sealed class RandomEventSystem
    {
        readonly ContentDB _content;
        readonly ProgressionConfig _config;
        readonly Simulation _sim;

        public readonly List<ActiveEvent> Active = new();
        public readonly List<Critter> Critters = new();
        /// <summary>Seconds until the next event tries to start.</summary>
        internal float Timer;
        internal int NextId = 1;
        internal Unity.Mathematics.Random Rng;

        public RandomEventSystem(ContentDB content, ProgressionConfig config, Simulation sim, uint seed)
        {
            _content = content;
            _config = config;
            _sim = sim;
            Timer = config.firstEventDelay;
            Rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xE7E7u)) | 1u);
        }

        public float SecondsToNext => Timer;

        public bool TryGetActive(int id, out ActiveEvent active)
        {
            foreach (var e in Active)
            {
                if (e.Id != id) continue;
                active = e;
                return true;
            }
            active = default;
            return false;
        }

        internal void Tick(float dt)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var e = Active[i];
                if (e.Kind == RandomEventKind.TrappedAnts && IsFreed(e))
                {
                    End(i, won: true);
                    continue;
                }
                if (e.TimeLeft <= 0f) continue;
                e.TimeLeft -= dt;
                Active[i] = e;
                if (e.TimeLeft <= 0f) End(i, won: false);
            }
            MoveCritters(dt);

            Timer -= dt;
            if (Timer > 0f) return;
            if (Active.Count >= _config.maxActiveEvents || !TryStartRandom())
            {
                Timer = 20f; // try again soon
                return;
            }
            Timer = Rng.NextFloat(_config.eventIntervalMin, math.max(_config.eventIntervalMin, _config.eventIntervalMax));
        }

        /// <summary>Offline time passed: timed events run out, the next event is due soon after.</summary>
        internal void SkipTime(float seconds)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var e = Active[i];
                if (e.TimeLeft <= 0f) continue;
                e.TimeLeft -= seconds;
                Active[i] = e;
                if (e.TimeLeft <= 0f) End(i, won: false);
            }
            Timer = math.max(10f, Timer - seconds);
        }

        bool TryStartRandom()
        {
            float total = 0f;
            for (int i = 0; i < _content.Events.Length; i++)
                if (Qualifies(i)) total += _content.Events[i].Def.weight;
            for (int attempt = 0; attempt < 3 && total > 0f; attempt++)
            {
                float roll = Rng.NextFloat(total);
                for (int i = 0; i < _content.Events.Length; i++)
                {
                    if (!Qualifies(i)) continue;
                    roll -= _content.Events[i].Def.weight;
                    if (roll >= 0f) continue;
                    if (Trigger(i)) return true;
                    break;
                }
            }
            return false;
        }

        bool Qualifies(int index)
        {
            var def = _content.Events[index].Def;
            return def.weight > 0f && def.minDepth <= _sim.Colony.Stats.MaxDepth;
        }

        /// <summary>Starts event <paramref name="index"/> now (also debug and tests). False if no place for it was found.</summary>
        public bool Trigger(int index)
        {
            if (index < 0 || index >= _content.Events.Length) return false;
            var data = _content.Events[index];
            int id = NextId;
            int2 cell;
            bool ok;
            switch (data.Def.kind)
            {
                case RandomEventKind.Meteorite: ok = TryMeteorite(data, out cell); break;
                case RandomEventKind.RichVein: ok = TryVein(data, out cell); break;
                case RandomEventKind.GoldenBeetle: ok = TryBeetle(index, id, out cell); break;
                case RandomEventKind.TrappedAnts: ok = TryTrapped(index, id, out cell); break;
                default: return false;
            }
            if (!ok) return false;
            NextId++;
            _sim.Events.Publish(new RandomEventEvent(index, id, RandomEventPhase.Started, cell));
            return true;
        }

        void End(int activeIndex, bool won)
        {
            var e = Active[activeIndex];
            Active.RemoveAt(activeIndex);
            for (int i = Critters.Count - 1; i >= 0; i--)
                if (Critters[i].EventId == e.Id) Critters.RemoveAt(i);
            if (!won && e.Kind == RandomEventKind.TrappedAnts)
                _sim.Ants.RemoveInside(e.Min, e.Max);
            if (won) _sim.Colony.Stats.EventsWon++;
            _sim.Events.Publish(new RandomEventEvent(e.Def, e.Id, won ? RandomEventPhase.Won : RandomEventPhase.Lost, e.Cell));
        }

        // ---------------------------------------------------------------- critters

        /// <summary>Index of the critter within the catch radius of <paramref name="cellPos"/> (cell space), or -1.</summary>
        public int CritterAt(float2 cellPos)
        {
            int best = -1;
            float bestDist = _config.catchRadius;
            for (int i = 0; i < Critters.Count; i++)
            {
                float d = math.distance(Critters[i].Pos, cellPos);
                if (d > bestDist) continue;
                bestDist = d;
                best = i;
            }
            return best;
        }

        /// <summary>Catches the critter near <paramref name="cellPos"/>: its event is won and its reward granted.</summary>
        internal bool Catch(float2 cellPos)
        {
            int c = CritterAt(cellPos);
            if (c < 0) return false;
            int id = Critters[c].EventId;
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i].Id != id) continue;
                var e = Active[i];
                e.Cell = Critters[c].Cell;
                Active[i] = e;
                var data = _content.Events[e.Def];
                _sim.GrantReward(data.RewardItems, data.RewardCounts, 1f, data.Def.rewardFood, data.Def.rewardKnowledge);
                End(i, won: true);
                return true;
            }
            Critters.RemoveAt(c);
            return false;
        }

        void MoveCritters(float dt)
        {
            if (Critters.Count == 0) return;
            var nav = new NavGrid(_sim.World);
            float speed = _config.critterSpeed;
            for (int i = 0; i < Critters.Count; i++)
            {
                var c = Critters[i];
                c.PrevPos = c.Pos;
                if (c.IsMoving)
                {
                    c.Progress += speed * dt / c.StepLength;
                    c.WalkPhase += speed * dt;
                    if (c.Progress >= 1f)
                    {
                        c.From = c.Cell;
                        c.Progress = 0f;
                    }
                }
                if (!c.IsMoving) StepCritter(ref c, nav);
                c.Pos = math.lerp(new float2(c.From), new float2(c.Cell), c.Progress) + 0.5f;
                Critters[i] = c;
            }
        }

        /// <summary>Wanders: keeps its heading while it can, otherwise turns at random; falls when unsupported.</summary>
        void StepCritter(ref Critter c, NavGrid nav)
        {
            int2 at = c.Cell;
            if (!nav.IsWalkable(at))
            {
                int2 below = at + new int2(0, -1);
                if (nav.InBounds(below) && !nav.IsSolid(below)) Step(ref c, below);
                return;
            }
            if (!c.Heading.Equals(int2.zero) && Rng.NextFloat() < 0.8f && nav.CanMove(at, c.Heading))
            {
                Step(ref c, at + c.Heading);
                return;
            }
            int start = Rng.NextInt(NavGrid.DirectionCount);
            for (int k = 0; k < NavGrid.DirectionCount; k++)
            {
                int2 d = NavGrid.Direction((start + k) % NavGrid.DirectionCount);
                if (!nav.CanMove(at, d)) continue;
                Step(ref c, at + d);
                return;
            }
        }

        static void Step(ref Critter c, int2 next)
        {
            int2 d = next - c.Cell;
            c.From = c.Cell;
            c.Cell = next;
            c.Progress = 0f;
            c.StepLength = d.x != 0 && d.y != 0 ? 1.4142f : 1f;
            c.Heading = d;
        }

        // ---------------------------------------------------------------- placement

        /// <summary>A random ant's cell below ground, or false if there are none.</summary>
        bool RandomAntCell(out int2 cell, int minDepth)
        {
            var ants = _sim.Ants.Ants;
            var nav = new NavGrid(_sim.World);
            for (int attempt = 0; attempt < 12 && ants.Length > 0; attempt++)
            {
                var a = ants[Rng.NextInt(ants.Length)];
                if (a.State == AntState.Falling || !nav.IsWalkable(a.Cell)) continue;
                if (_sim.World.Depth(a.Cell.x, a.Cell.y) < minDepth) continue;
                cell = a.Cell;
                return true;
            }
            cell = default;
            return false;
        }

        bool TryBeetle(int index, int id, out int2 cell)
        {
            if (!RandomAntCell(out cell, 2)) return false;
            var data = _content.Events[index];
            Active.Add(new ActiveEvent
            {
                Id = id, Def = index, Kind = RandomEventKind.GoldenBeetle, Cell = cell,
                TimeLeft = math.max(5f, data.Def.duration), Count = 1,
            });
            float2 pos = new float2(cell) + 0.5f;
            Critters.Add(new Critter
            {
                EventId = id, Cell = cell, From = cell, StepLength = 1f, PrevPos = pos, Pos = pos,
                Heading = new int2(Rng.NextBool() ? 1 : -1, 0),
            });
            return true;
        }

        /// <summary>True if the cell can be turned into terrain by an event (solid, diggable, no orders or buildings).</summary>
        bool IsPlainSolid(int2 c)
        {
            var world = _sim.World;
            if (!world.InBounds(c.x, c.y)) return false;
            var cell = world[c.x, c.y];
            if (cell.IsOpen || cell.Material == ContentDB.AirIndex) return false;
            if (_content.MaterialSim[cell.Material].IsIndestructible) return false;
            return (cell.Flags & (CellFlags.DigDesignated | CellFlags.Blueprint | CellFlags.Storage)) == 0;
        }

        bool TryTrapped(int index, int id, out int2 cell)
        {
            var data = _content.Events[index];
            var world = _sim.World;
            float dist = math.max(3f, data.Def.radius);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (!RandomAntCell(out int2 anchor, 4)) break;
                float angle = Rng.NextFloat(math.PI * 2f);
                float r = Rng.NextFloat(dist, dist * 2f);
                int2 p = anchor + (int2)math.round(new float2(math.cos(angle), math.sin(angle)) * r);
                // Pocket of 3x2 open cells inside a ring of at least two solid cells.
                bool ok = world.Depth(p.x, p.y + 4) >= 3;
                for (int y = -2; y <= 3 && ok; y++)
                for (int x = -2; x <= 4 && ok; x++)
                    ok = IsPlainSolid(p + new int2(x, y));
                if (!ok) continue;

                for (int y = 0; y <= 1; y++)
                for (int x = 0; x <= 2; x++)
                {
                    var c = p + new int2(x, y);
                    var cl = world[c.x, c.y];
                    cl.Density = 0;
                    cl.Flags |= CellFlags.Revealed | CellFlags.Explored;
                    _sim.EditCell(c, cl);
                }
                Reveal.Around(world, p + new int2(1, 0), 3);
                int count = math.max(1, data.Def.count);
                for (int n = 0; n < count; n++)
                    _sim.Ants.Spawn(p + new int2(n % 3, 0), _sim.Seed);
                cell = p + new int2(1, 0);
                Active.Add(new ActiveEvent
                {
                    Id = id, Def = index, Kind = RandomEventKind.TrappedAnts, Cell = cell,
                    Min = p, Max = p + new int2(2, 1), TimeLeft = math.max(5f, data.Def.duration), Count = count,
                });
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>Freed once the pocket is connected to the nest (reachable on the Home flow field).</summary>
        bool IsFreed(in ActiveEvent e)
        {
            var world = _sim.World;
            for (int y = e.Min.y; y <= e.Max.y; y++)
            for (int x = e.Min.x; x <= e.Max.x; x++)
            {
                var c = new int2(x, y);
                if (world[x, y].IsOpen && _sim.Fields.Get(FieldKind.Home, world, c) != FlowFields.Unreachable) return true;
            }
            return false;
        }

        bool TryVein(RandomEventData data, out int2 cell)
        {
            var world = _sim.World;
            float radius = math.max(1.5f, data.Def.radius);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                if (!RandomAntCell(out int2 anchor, 3)) break;
                float angle = Rng.NextFloat(math.PI * 2f);
                int2 c = anchor + (int2)math.round(new float2(math.cos(angle), math.sin(angle)) * Rng.NextFloat(radius + 1f, radius + 5f));
                if (!IsPlainSolid(c)) continue;
                int layer = _sim.BiomeAtDepth(world.Depth(c.x, c.y));
                ushort ore = data.Materials[math.min(layer, data.Materials.Length - 1)];

                int converted = 0;
                int r = (int)math.ceil(radius);
                for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    var p = c + new int2(x, y);
                    if (math.length(new float2(x, y) * new float2(1f, 1.3f)) > radius || !IsPlainSolid(p)) continue;
                    var cl = world[p.x, p.y];
                    cl.Material = ore;
                    cl.Flags |= CellFlags.Revealed | CellFlags.Scanned;
                    _sim.EditCell(p, cl);
                    converted++;
                }
                if (converted == 0) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        bool TryMeteorite(RandomEventData data, out int2 cell)
        {
            var world = _sim.World;
            var colony = _sim.Colony;
            float radius = math.max(1f, data.Def.radius);
            float crater = radius + 1.5f;
            int reach = (int)math.ceil(crater) + 1;
            int range = math.max(16, _sim.Config.colony.forageRange);
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int offset = Rng.NextInt(12, range + 1) * (Rng.NextBool() ? 1 : -1);
                int x = math.clamp(colony.QueenCell.x + offset, reach + 2, world.Width - reach - 3);
                int top = world.Height - 1;
                while (top > 0 && world[x, top].IsOpen) top--;
                if (top < world.SurfaceY[x] - 3) continue; // someone dug a pit here
                if (!IsClear(new int2(x - reach, top - reach), new int2(x + reach, top + reach))) continue;

                // Crater: open everything diggable in a circle around the impact point.
                float2 impact = new float2(x + 0.5f, top + 1f);
                for (int y = top - reach; y <= top + reach; y++)
                for (int cx = x - reach; cx <= x + reach; cx++)
                {
                    if (!world.InBounds(cx, y)) continue;
                    if (math.distance(new float2(cx + 0.5f, y + 0.5f), impact) > crater) continue;
                    var cl = world[cx, y];
                    if (cl.IsOpen || _content.MaterialSim[cl.Material].IsIndestructible) continue;
                    cl.Density = 0;
                    cl.Flags |= CellFlags.Revealed | CellFlags.Explored;
                    _sim.EditCell(new int2(cx, y), cl);
                }
                // The lump sits at the bottom of the crater.
                float2 lump = new float2(x + 0.5f, top + 1f - crater + radius * 0.8f);
                ushort rock = data.Materials[0];
                for (int y = top - reach; y <= top + reach; y++)
                for (int cx = x - reach; cx <= x + reach; cx++)
                {
                    if (!world.InBounds(cx, y)) continue;
                    if (math.distance(new float2(cx + 0.5f, y + 0.5f), lump) > radius) continue;
                    var cl = world[cx, y];
                    if (_content.MaterialSim[cl.Material].IsIndestructible) continue;
                    _sim.EditCell(new int2(cx, y), new Cell(rock, Cell.Full, (cl.Flags | CellFlags.Revealed) & ~CellFlags.Explored));
                }
                Reveal.Around(world, new int2(x, top), reach + 3);
                _sim.LiftAntsOutOfSolid(new int2(x - reach, top - reach), new int2(x + reach, top + reach));
                cell = new int2(x, top);
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>No buildings, storage, piles or the royal chamber in the rectangle.</summary>
        bool IsClear(int2 min, int2 max)
        {
            var world = _sim.World;
            var colony = _sim.Colony;
            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                if (!world.InBounds(x, y)) return false;
                var c = new int2(x, y);
                if (colony.IsRoyalCell(c) || _sim.Buildings.At(c) >= 0) return false;
                if ((world[x, y].Flags & (CellFlags.Storage | CellFlags.Blueprint)) != 0) return false;
            }
            for (int i = 0; i < colony.Piles.Count; i++)
            {
                var p = colony.Piles[i].Cell;
                if (math.all(p >= min) && math.all(p <= max)) return false;
            }
            return true;
        }
    }
}
