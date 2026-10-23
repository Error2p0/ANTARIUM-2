using System;
using System.Collections.Generic;
using Antarium.Content;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum EnemyState : byte
    {
        /// <summary>Waiting in its cave until an ant comes close.</summary>
        Dormant,
        /// <summary>Awake, no prey in reach: wanders around where it spawned.</summary>
        Wander,
        /// <summary>Going after the nearest ant.</summary>
        Hunt,
        /// <summary>Raider heading for the royal chamber (and eating there).</summary>
        Raid,
        /// <summary>Raider that has eaten its fill, heading back to where it came from.</summary>
        Leave,
    }

    /// <summary>One hostile creature. Moves like an ant (crawling rules), but cannot pass closed gates.</summary>
    public struct Enemy
    {
        /// <summary>Index into <see cref="ContentDB.Enemies"/>.</summary>
        public int Def;
        public EnemyState State;
        public int2 Cell, From;
        public float Progress, StepLength;
        public int2 Heading;
        public float WalkPhase;
        public float2 PrevPos, Pos;
        public float Health;
        public float AttackTimer;
        public float SinceHurt;
        /// <summary>Seconds a trap still holds it in place.</summary>
        public float Held;
        /// <summary>Seconds until a dormant creature looks for prey again.</summary>
        public float Think;
        /// <summary>Where it spawned (cave creatures stay near it).</summary>
        public int2 Home;
        /// <summary>Raiders: food eaten so far.</summary>
        public float Eaten;
        /// <summary>Raid it belongs to, 0 = none.</summary>
        public int Raid;
        /// <summary>Biting an ant or a gate right now (animation).</summary>
        public bool Attacking;
        /// <summary>A raider that got away with its food; removed without loot.</summary>
        public bool Escaped;

        public bool IsMoving => !From.Equals(Cell);
        public bool IsDead => Health <= 0f || Escaped;
    }

    /// <summary>
    /// Threats: creatures that live in the caves of deeper biomes (they appear when the colony breaks into
    /// a cave and wake when ants come close) and raids from the surface that escalate with colony size and
    /// depth. Enemies bite ants next to them; ants bite back (soldiers much harder). Gates stop enemies until
    /// broken; traps hurt and hold the first enemy that steps on them.
    /// </summary>
    public sealed class EnemySystem : IDisposable
    {
        readonly ContentDB _content;
        readonly ThreatConfig _threats;
        readonly CombatConfig _combat;
        readonly FluidConfig _fluids;
        readonly PossessionConfig _possession;
        readonly Simulation _sim;
        NativeParallelMultiHashMap<int, int> _antsByCell;

        public readonly List<Enemy> Enemies = new();
        internal Unity.Mathematics.Random Rng;

        // Raids (saved).
        /// <summary>Seconds until the next raid arrives (the warning comes <c>raidWarningSeconds</c> earlier).</summary>
        internal float RaidTimer;
        internal bool RaidWarned;
        internal int2 RaidSpawn;
        internal int RaidCount;
        /// <summary>Id of the raid in progress, 0 = none.</summary>
        internal int RaidActive;
        internal int RaidSize;
        internal float RaidFoodLost;

        /// <summary>Some enemy is hunting, raiding or wandering (the ants flow field is needed).</summary>
        public bool AnyHunting { get; private set; }
        /// <summary>Some raider is leaving (the exit flow field is needed).</summary>
        public bool AnyLeaving { get; private set; }
        /// <summary>Raiders are in the royal chamber: the queen does not lay.</summary>
        public bool RaidersInChamber { get; private set; }
        /// <summary>Enemies moved or appeared this tick (the enemy flow field must be rebuilt).</summary>
        public bool Changed;

        public bool RaidIncoming => RaidWarned;
        public float SecondsToRaid => RaidTimer;
        public int2 RaidCell => RaidSpawn;
        /// <summary>Raids started so far (the next one gets this + 1 as its id).</summary>
        public int RaidsStarted => RaidCount;
        /// <summary>Id of the raid in progress, 0 = none.</summary>
        public int ActiveRaid => RaidActive;

        public EnemySystem(ContentDB content, GameConfig config, Simulation sim, uint seed)
        {
            _content = content;
            _threats = config.threats;
            _combat = config.combat;
            _fluids = config.fluids;
            _possession = config.possession;
            _sim = sim;
            Rng = new Unity.Mathematics.Random(math.hash(new uint2(seed, 0xE11Eu)) | 1u);
            RaidTimer = math.max(_threats.firstRaidSeconds, _threats.raidWarningSeconds + 1f);
            _antsByCell = new NativeParallelMultiHashMap<int, int>(256, Allocator.Persistent);
        }

        public int Count => Enemies.Count;

        public int CountRaiders()
        {
            int n = 0;
            foreach (var e in Enemies)
                if (e.Raid != 0) n++;
            return n;
        }

        /// <summary>Index of the enemy within <paramref name="radius"/> of <paramref name="cellPos"/> (cell space), or -1.</summary>
        public int EnemyAt(float2 cellPos, float radius = 1f)
        {
            int best = -1;
            float bestDist = radius;
            for (int i = 0; i < Enemies.Count; i++)
            {
                float d = math.distance(Enemies[i].Pos, cellPos);
                if (d > bestDist) continue;
                bestDist = d;
                best = i;
            }
            return best;
        }

        public float MaxHealth(int def) => _content.Enemies[def].Def.health;

        // ---------------------------------------------------------------- spawning

        /// <summary>Adds an enemy (tests, debug, spawning).</summary>
        public void Spawn(int def, int2 cell, EnemyState state, int raid = 0)
        {
            float2 pos = new float2(cell) + 0.5f;
            Enemies.Add(new Enemy
            {
                Def = def,
                State = state,
                Cell = cell,
                From = cell,
                StepLength = 1f,
                Heading = new int2(Rng.NextBool() ? 1 : -1, 0),
                PrevPos = pos,
                Pos = pos,
                Health = _content.Enemies[def].Def.health,
                SinceHurt = 1000f,
                Think = Rng.NextFloat(0.5f),
                Home = cell,
                Raid = raid,
                WalkPhase = Rng.NextFloat(4f),
            });
            Changed = true;
        }

        /// <summary>
        /// The colony broke into a cave: creatures of the cave's biome appear in it (one per
        /// <c>caveCellsPerCreature</c> cells, at most <c>maxCreaturesPerCave</c>), away from the breach.
        /// </summary>
        internal void OnCaveDiscovered(NativeList<int2> cells, NativeArray<int2> breaches)
        {
            if (cells.Length < 12) return;
            var world = _sim.World;
            int2 mid = cells[cells.Length / 2];
            int biome = _content.BiomeAtDepth(world.Depth(mid.x, mid.y));
            if (biome >= _content.Biomes.Length) return;
            var data = _content.Biomes[biome];
            if (data.Creatures.Length == 0 || data.Def == null) return;

            float expected = cells.Length / (float)math.max(1, data.Def.caveCellsPerCreature);
            int count = (int)expected;
            if (Rng.NextFloat() < expected - count) count++;
            count = math.min(count, data.Def.maxCreaturesPerCave);
            if (count == 0) return;

            var nav = new NavGrid(world);
            float minDist = _threats.minSpawnDistance;
            int spawned = 0, firstDef = -1;
            int2 firstCell = mid;
            for (int n = 0; n < count; n++)
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int2 c = cells[Rng.NextInt(cells.Length)];
                    if (!nav.IsWalkable(c) || !nav.IsSolid(c + new int2(0, -1))) continue;
                    bool far = true;
                    for (int b = 0; b < breaches.Length && far; b++)
                        far = math.distance(new float2(c), new float2(breaches[b])) >= minDist;
                    if (!far) continue;
                    int def = data.Creatures[Rng.NextInt(data.Creatures.Length)];
                    Spawn(def, c, EnemyState.Dormant);
                    if (spawned == 0) { firstDef = def; firstCell = c; }
                    spawned++;
                    break;
                }
            }
            if (spawned > 0) _sim.Events.Publish(new CreaturesFoundEvent(firstDef, spawned, firstCell));
        }

        // ---------------------------------------------------------------- raids

        /// <summary>Raiders the next raid brings: grows with the colony and with the depth reached.</summary>
        public int NextRaidSize()
        {
            int biome = _content.BiomeAtDepth(_sim.Colony.Stats.MaxDepth);
            float size = _threats.raidBaseSize + _sim.Ants.Count * _threats.raidPerAnt + biome * _threats.raidPerBiome;
            return math.clamp((int)math.round(size), 1, _threats.raidMaxSize);
        }

        bool RaidsQualify() =>
            _content.Raiders.Length > 0 && _sim.PlayTime >= _threats.firstRaidSeconds && _sim.Ants.Count >= _threats.raidMinAnts;

        void UpdateRaids(float dt)
        {
            if (_content.Raiders.Length == 0) return;
            RaidTimer -= dt;
            float warning = _threats.raidWarningSeconds;
            if (!RaidWarned && RaidTimer <= warning)
            {
                if (RaidActive != 0 || !RaidsQualify() || !FindRaidSpawn(out RaidSpawn))
                {
                    RaidTimer = warning + 30f; // look again soon
                    return;
                }
                RaidWarned = true;
                _sim.Events.Publish(new RaidEvent(RaidPhase.Warning, RaidCount + 1, RaidSpawn, NextRaidSize(), 0f));
            }
            if (RaidWarned && RaidTimer <= 0f)
            {
                RaidWarned = false;
                StartRaid(RaidSpawn);
                RaidTimer = Rng.NextFloat(_threats.raidIntervalMin, math.max(_threats.raidIntervalMin, _threats.raidIntervalMax));
            }
        }

        /// <summary>Debug / tests: a raid arrives right now. False if no place for it was found.</summary>
        public bool TriggerRaid()
        {
            if (_content.Raiders.Length == 0 || !FindRaidSpawn(out var spawn)) return false;
            RaidWarned = false;
            RaidSpawn = spawn;
            StartRaid(spawn);
            return true;
        }

        void StartRaid(int2 spawn)
        {
            var world = _sim.World;
            int size = NextRaidSize();
            int id = ++RaidCount;
            RaidActive = id;
            RaidSize = size;
            RaidFoodLost = 0f;
            var nav = new NavGrid(world);
            for (int n = 0; n < size; n++)
            {
                int2 c = spawn;
                int offset = (n % 7) - 3;
                int2 near = GroundAt(spawn.x + offset);
                if (nav.IsWalkable(near)) c = near;
                Spawn(_content.Raiders[Rng.NextInt(_content.Raiders.Length)], c, EnemyState.Raid, id);
            }
            _sim.Events.Publish(new RaidEvent(RaidPhase.Started, id, spawn, size, 0f));
        }

        /// <summary>The open cell on the ground surface of column <paramref name="x"/>.</summary>
        int2 GroundAt(int x)
        {
            var world = _sim.World;
            x = math.clamp(x, 0, world.Width - 1);
            int y = world.Height - 1;
            while (y > 0 && world[x, y - 1].IsOpen) y--;
            return new int2(x, y);
        }

        /// <summary>A spot on the surface some distance from the nest from which raiders can walk to the queen.</summary>
        bool FindRaidSpawn(out int2 cell)
        {
            var world = _sim.World;
            var queen = _sim.Colony.QueenCell;
            int lo = math.max(1, _threats.raidDistance.x), hi = math.max(lo, _threats.raidDistance.y);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int side = Rng.NextBool() ? 1 : -1;
                int x = math.clamp(queen.x + side * Rng.NextInt(lo, hi + 1), 6, world.Width - 7);
                var c = GroundAt(x);
                if (_sim.Fields.Get(FieldKind.Home, world, c) == FlowFields.Unreachable) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        // ---------------------------------------------------------------- tick

        internal void Tick(float dt)
        {
            UpdateRaids(dt);
            AnyHunting = AnyLeaving = RaidersInChamber = false;
            if (Enemies.Count == 0) return;

            BuildAntLookup();
            var nav = new NavGrid(_sim.World);
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                Update(ref e, dt, nav);
                Enemies[i] = e;
            }
            AntsBite(dt);
            RemoveDead();
        }

        void BuildAntLookup()
        {
            var ants = _sim.Ants.Ants;
            _antsByCell.Clear();
            if (_antsByCell.Capacity < ants.Length) _antsByCell.Capacity = ants.Length;
            var world = _sim.World;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                if (a.IsDead) continue;
                _antsByCell.Add(world.Index(a.Cell.x, a.Cell.y), i);
            }
        }

        void Update(ref Enemy e, float dt, NavGrid nav)
        {
            var def = _content.Enemies[e.Def].Def;
            e.PrevPos = e.Pos;
            e.SinceHurt += dt;
            e.AttackTimer -= dt;
            if (e.Held > 0f) e.Held -= dt;

            // Hazards.
            var f = nav.FluidAt(e.Cell);
            if (f.Level > 0)
            {
                if (f.Kind == FluidKind.Lava && !def.lavaImmune) Hurt(ref e, _combat.lavaDamage * dt);
                else if (f.Kind == FluidKind.Water && f.Level >= nav.DeepFluid && !def.swims) Hurt(ref e, _combat.drownDamage * dt);
                else if (f.Kind == FluidKind.Gas && f.Level >= _fluids.gasHarmLevel && !def.gasImmune) Hurt(ref e, _combat.gasDamage * dt);
                if (e.IsDead) return;
            }

            if (e.IsMoving)
            {
                float speed = def.speed * (nav.IsFlooded(e.Cell) ? _combat.swimSpeedFactor : 1f);
                e.Progress += speed * dt / e.StepLength;
                e.WalkPhase += speed * dt;
                if (e.Progress >= 1f)
                {
                    e.From = e.Cell;
                    e.Progress = 0f;
                    Arrive(ref e);
                }
            }
            if (!e.IsMoving && !e.IsDead) Decide(ref e, def, dt, nav);
            e.Pos = math.lerp(new float2(e.From), new float2(e.Cell), e.Progress) + 0.5f;

            if (e.State != EnemyState.Dormant) AnyHunting = true;
            if (e.State == EnemyState.Leave) AnyLeaving = true;
            if (e.IsMoving) Changed = true;
        }

        void Hurt(ref Enemy e, float damage)
        {
            if (damage <= 0f) return;
            e.Health -= damage;
            e.SinceHurt = 0f;
            if (e.State == EnemyState.Dormant) e.State = EnemyState.Hunt;
        }

        /// <summary>Stepped onto a cell: an armed trap there goes off.</summary>
        void Arrive(ref Enemy e)
        {
            var buildings = _sim.Buildings;
            int id = buildings.At(e.Cell);
            if (id < 0) return;
            ref var b = ref buildings.Get(id);
            if (b.Kind != BuildingKind.Trap || !b.IsActive || b.Rearm > 0f) return;
            var def = _content.Buildings[b.Def].Def;
            Hurt(ref e, def.trapDamage * _sim.Colony.Mods.Mult(StatKind.DefenseStrength));
            e.Held = def.trapHold;
            b.Rearm = def.trapRearm;
            _sim.Events.Publish(new TrapTriggeredEvent(e.Cell, e.Def));
        }

        void Decide(ref Enemy e, EnemyDef def, float dt, NavGrid nav)
        {
            int2 c = e.Cell;
            if (nav.IsSolid(c))
            {
                // Inside rock (spawned or buried there): climb out.
                var up = _sim.World.OpenAbove(c);
                e.Cell = e.From = up;
                e.Progress = 0f;
                return;
            }
            var f = nav.FluidAt(c);
            bool comfortable = (f.Kind == FluidKind.Lava && def.lavaImmune) || (f.Kind == FluidKind.Water && def.swims)
                               || (f.Kind == FluidKind.Gas && def.gasImmune);
            if (nav.IsFlooded(c) && !comfortable)
            {
                Escape(ref e, nav);
                return;
            }
            if (!nav.IsWalkable(c) && !nav.IsFlooded(c))
            {
                int2 below = c + new int2(0, -1);
                if (nav.InBounds(below) && !nav.IsSolid(below)) StartStep(ref e, below);
                return;
            }

            // Bite an ant next to it.
            if (FindAdjacentAnt(c, out int ant))
            {
                e.Attacking = true;
                if (e.State == EnemyState.Dormant || e.State == EnemyState.Wander) e.State = EnemyState.Hunt;
                var a = _sim.Ants.Ants[ant];
                int2 toward = a.Cell - c;
                if (!toward.Equals(int2.zero)) e.Heading = toward;
                if (e.AttackTimer <= 0f)
                {
                    e.AttackTimer = def.attackInterval;
                    Bite(ant, e.Def, def.damage);
                }
                return;
            }
            e.Attacking = false;
            if (e.Held > 0f) return;

            var world = _sim.World;
            var fields = _sim.Fields;
            float aggro = def.aggroRadius * FlowFields.StraightCost;
            switch (e.State)
            {
                case EnemyState.Dormant:
                    e.Think -= dt;
                    if (e.Think > 0f) return;
                    e.Think = 0.5f;
                    if (AntWithin(c, def.aggroRadius)) e.State = EnemyState.Hunt;
                    return;

                case EnemyState.Wander:
                {
                    ushort d = fields.Get(FieldKind.Ants, world, c);
                    if (d != FlowFields.Unreachable && d <= aggro) { e.State = EnemyState.Hunt; return; }
                    if (Rng.NextFloat() < 0.4f) return; // amble
                    WanderStep(ref e, nav);
                    return;
                }

                case EnemyState.Hunt:
                {
                    ushort d = fields.Get(FieldKind.Ants, world, c);
                    if (d == FlowFields.Unreachable || d > aggro * 1.5f)
                    {
                        e.State = e.Raid != 0 ? EnemyState.Raid : EnemyState.Wander;
                        return;
                    }
                    MoveAlong(ref e, def, FieldKind.Ants, d, nav);
                    return;
                }

                case EnemyState.Raid:
                {
                    if (_sim.Colony.IsRoyalCell(c))
                    {
                        RaidersInChamber = true;
                        var inv = _sim.Colony.Inventory;
                        float bite = math.min(inv.Food, def.foodPerSecond * dt);
                        inv.Food -= bite;
                        e.Eaten += bite;
                        RaidFoodLost += bite;
                        if (e.Eaten >= def.carryFood || inv.Food <= 0.01f) e.State = EnemyState.Leave;
                        else if (Rng.NextFloat() < 0.05f) WanderStep(ref e, nav);
                        return;
                    }
                    ushort d = fields.Get(FieldKind.Home, world, c);
                    if (d == FlowFields.Unreachable) { WanderStep(ref e, nav); return; }
                    MoveAlong(ref e, def, FieldKind.Home, d, nav);
                    return;
                }

                case EnemyState.Leave:
                {
                    ushort d = fields.Get(FieldKind.Exit, world, c);
                    if (d == 0 || math.distance(new float2(c), new float2(RaidSpawn)) < 1.5f)
                    {
                        e.Escaped = true;
                        return;
                    }
                    if (d == FlowFields.Unreachable) { WanderStep(ref e, nav); return; }
                    MoveAlong(ref e, def, FieldKind.Exit, d, nav);
                    return;
                }
            }
        }

        /// <summary>Steps down a flow field; a closed gate in the way is attacked instead.</summary>
        void MoveAlong(ref Enemy e, EnemyDef def, FieldKind field, ushort current, NavGrid nav)
        {
            var world = _sim.World;
            var fields = _sim.Fields;
            int2 c = e.Cell;
            int2 best = c;
            ushort bestValue = current;
            int ties = 0;
            for (int i = 0; i < NavGrid.DirectionCount; i++)
            {
                int2 d = NavGrid.Direction(i);
                int2 n = c + d;
                ushort v = fields.Get(field, world, n);
                if (v >= current || v > bestValue) continue;
                if (!nav.CanMove(c, d)) continue;
                if (v < bestValue) { bestValue = v; best = n; ties = 1; }
                else if (Rng.NextInt(++ties) == 0) best = n;
            }
            if (best.Equals(c))
            {
                WanderStep(ref e, nav);
                return;
            }
            int gate = _sim.Buildings.At(best);
            if (gate >= 0 && _sim.Buildings.Get(gate).IsClosedGate)
            {
                e.Attacking = true;
                e.Heading = best - c;
                if (e.AttackTimer <= 0f)
                {
                    e.AttackTimer = def.attackInterval;
                    _sim.Buildings.DamageGate(gate, def.damage);
                }
                return;
            }
            StartStep(ref e, best);
        }

        /// <summary>A random step, drifting back toward home when too far from it; never through a closed gate.</summary>
        void WanderStep(ref Enemy e, NavGrid nav)
        {
            int2 c = e.Cell;
            bool far = e.Raid == 0 && math.distance(new float2(c), new float2(e.Home)) > _threats.leashRadius;
            int start = Rng.NextInt(NavGrid.DirectionCount);
            int2 fallback = c;
            for (int k = 0; k < NavGrid.DirectionCount; k++)
            {
                int2 d = NavGrid.Direction((start + k) % NavGrid.DirectionCount);
                if (!nav.CanMove(c, d) || IsClosedGate(c + d)) continue;
                if (far && math.distancesq(new float2(c + d), new float2(e.Home)) >= math.distancesq(new float2(c), new float2(e.Home)))
                {
                    if (fallback.Equals(c)) fallback = c + d;
                    continue;
                }
                StartStep(ref e, c + d);
                return;
            }
            if (!fallback.Equals(c)) StartStep(ref e, fallback);
        }

        void Escape(ref Enemy e, NavGrid nav)
        {
            int2 c = e.Cell;
            for (int i = 0; i < NavGrid.DirectionCount; i++)
            {
                int2 d = NavGrid.Direction(i);
                if (!nav.CanMove(c, d) || IsClosedGate(c + d)) continue;
                StartStep(ref e, c + d);
                return;
            }
            int2 up = new int2(0, nav.FluidAt(c).Kind == FluidKind.Gas ? -1 : 1);
            if (nav.CanSwim(c, up)) StartStep(ref e, c + up);
        }

        bool IsClosedGate(int2 c)
        {
            int id = _sim.Buildings.At(c);
            return id >= 0 && _sim.Buildings.Get(id).IsClosedGate;
        }

        static void StartStep(ref Enemy e, int2 next)
        {
            int2 d = next - e.Cell;
            e.From = e.Cell;
            e.Cell = next;
            e.Progress = 0f;
            e.StepLength = d.x != 0 && d.y != 0 ? 1.4142f : 1f;
            e.Heading = d;
        }

        bool FindAdjacentAnt(int2 c, out int ant)
        {
            var world = _sim.World;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 n = c + new int2(dx, dy);
                if (!world.InBounds(n.x, n.y)) continue;
                if (_antsByCell.TryGetFirstValue(world.Index(n.x, n.y), out ant, out _)) return true;
            }
            ant = -1;
            return false;
        }

        bool AntWithin(int2 c, float radius)
        {
            var world = _sim.World;
            int r = (int)math.ceil(radius);
            float r2 = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = c.x + dx, y = c.y + dy;
                if (!world.InBounds(x, y)) continue;
                if (_antsByCell.ContainsKey(world.Index(x, y))) return true;
            }
            return false;
        }

        void Bite(int antIndex, int enemyDef, float damage)
        {
            var ants = _sim.Ants.Ants;
            var a = ants[antIndex];
            if (a.IsDead) return;
            a.Health -= a.Possessed ? damage * _possession.damageTaken : damage;
            a.SinceHurt = 0f;
            if (a.Health <= 0f)
            {
                a.State = AntState.Dead;
                a.Cause = DeathCause.Killed;
                a.Killer = (short)enemyDef;
            }
            ants[antIndex] = a;
        }

        /// <summary>The possessed ant bites enemy <paramref name="index"/>.</summary>
        internal void Strike(int index, float damage)
        {
            if (index < 0 || index >= Enemies.Count) return;
            var e = Enemies[index];
            Hurt(ref e, damage);
            Enemies[index] = e;
            if (e.IsDead) RemoveDead();
        }

        /// <summary>Every ant next to an enemy bites it, by its caste (soldiers much harder than workers).</summary>
        void AntsBite(float dt)
        {
            var ants = _sim.Ants.Ants;
            var world = _sim.World;
            var castes = _content.CasteSim;
            float mult = _sim.Colony.Mods.Mult(StatKind.AntAttack) * dt;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (e.IsDead) continue;
                float damage = 0f;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int2 n = e.Cell + new int2(dx, dy);
                    if (!world.InBounds(n.x, n.y)) continue;
                    if (!_antsByCell.TryGetFirstValue(world.Index(n.x, n.y), out int ant, out var it)) continue;
                    do
                    {
                        var a = ants[ant];
                        if (!a.IsDead) damage += castes[(int)a.Caste].Attack * mult;
                    } while (_antsByCell.TryGetNextValue(out ant, ref it));
                }
                if (damage <= 0f) continue;
                Hurt(ref e, damage);
                Enemies[i] = e;
            }
        }

        void RemoveDead()
        {
            var colony = _sim.Colony;
            var world = _sim.World;
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                var e = Enemies[i];
                if (!e.IsDead) continue;
                Enemies.RemoveAt(i);
                Changed = true;
                if (e.Escaped)
                {
                    _sim.Events.Publish(new EnemyEscapedEvent(e.Def, e.Cell, e.Eaten));
                    continue;
                }
                var data = _content.Enemies[e.Def];
                var land = world.FloorBelow(e.Cell);
                for (int k = 0; k < data.DropItems.Length; k++)
                {
                    colony.Piles.Add(land, data.DropItems[k], data.DropCounts[k]);
                    _sim.Events.Publish(new ItemDroppedEvent(land, data.DropItems[k]));
                }
                if (data.Def.knowledge > 0f) colony.Inventory.AddKnowledge(data.Def.knowledge, studied: false);
                colony.Stats.EnemiesKilled++;
                _sim.Events.Publish(new EnemyKilledEvent(e.Def, e.Cell));
            }

            if (RaidActive == 0) return;
            foreach (var e in Enemies)
                if (e.Raid == RaidActive) return;
            // Every raider of the raid is gone: repelled if none got away with food.
            bool repelled = RaidFoodLost < 0.5f;
            if (repelled)
            {
                colony.Stats.RaidsRepelled++;
                colony.Inventory.AddKnowledge(_threats.raidKnowledge, studied: false);
            }
            _sim.Events.Publish(new RaidEvent(repelled ? RaidPhase.Repelled : RaidPhase.Ended, RaidActive, RaidSpawn, RaidSize, RaidFoodLost));
            RaidActive = 0;
        }

        /// <summary>Enemies inside cells that just became solid climb out (and take the cave-in damage).</summary>
        internal void LiftOutOfSolid(HashSet<int> solidified, float damage)
        {
            var world = _sim.World;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                if (!solidified.Contains(world.Index(e.Cell.x, e.Cell.y))) continue;
                var c = world.OpenAbove(e.Cell);
                e.Cell = e.From = c;
                e.Progress = 0f;
                e.Pos = e.PrevPos = new float2(c) + 0.5f;
                Hurt(ref e, damage);
                Enemies[i] = e;
            }
        }

        // ---------------------------------------------------------------- flow field sources

        public void AddEnemySources(NativeList<int2> sources)
        {
            foreach (var e in Enemies) sources.Add(e.Cell);
        }

        public void Dispose()
        {
            if (_antsByCell.IsCreated) _antsByCell.Dispose();
        }
    }
}
