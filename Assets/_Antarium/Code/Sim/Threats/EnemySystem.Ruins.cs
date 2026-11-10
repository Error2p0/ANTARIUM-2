using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Threats of the deep endgame (10.5). <b>Rival ants</b> come in raids like termites, but in the royal chamber they steal
    /// items from the stock (the kind the colony has most of, up to what one can carry) and carry them home; a thief that dies
    /// drops its loot. <b>Mole crickets</b> start in soft ground some way from the nest and tunnel toward the queen, around rock
    /// too hard for them and through soft walls; their tunnel shows through the fog as they dig. Once they break into the
    /// colony's tunnels they hunt ants like cave creatures.
    /// </summary>
    public sealed partial class EnemySystem
    {
        static readonly int2[] Cardinal4 = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        // ---------------------------------------------------------------- thieves

        /// <summary>A thief in the royal chamber grabs one item every <see cref="EnemyDef.stealSeconds"/>; full (or nothing left), it leaves.</summary>
        void Steal(ref Enemy e, EnemyDef def, float dt)
        {
            var inv = _sim.Colony.Inventory;
            if (e.LootCount >= def.carryItems)
            {
                e.State = EnemyState.Leave;
                return;
            }
            ushort item = e.LootCount > 0 ? e.Loot : MostStocked(inv);
            if (item == ContentDB.NoItem || inv.Items[item] <= 0)
            {
                // Nothing (more) to take: off it goes, with whatever it has.
                if (e.LootCount > 0 || item == ContentDB.NoItem) e.State = EnemyState.Leave;
                else e.Loot = ContentDB.NoItem;
                return;
            }
            e.Timer += dt;
            if (e.Timer < def.stealSeconds) return;
            e.Timer = 0f;
            if (!inv.Take(item, 1)) return;
            e.Loot = item;
            e.LootCount++;
            _sim.Events.Publish(new ItemStolenEvent(e.Cell, item));
            if (e.LootCount >= def.carryItems) e.State = EnemyState.Leave;
        }

        /// <summary>The non-food item the stock holds most of, or <see cref="ContentDB.NoItem"/>.</summary>
        ushort MostStocked(Inventory inv)
        {
            ushort best = ContentDB.NoItem;
            int most = 0;
            for (int i = 0; i < inv.Items.Length; i++)
            {
                if (_content.Items[i].IsFood || inv.Items[i] <= most) continue;
                most = inv.Items[i];
                best = (ushort)i;
            }
            return best;
        }

        // ---------------------------------------------------------------- diggers

        void UpdateDiggers(float dt)
        {
            if (_content.Diggers.Length == 0) return;
            DiggerTimer -= dt;
            if (DiggerTimer > 0f) return;
            DiggerTimer = Rng.NextFloat(_threats.moleIntervalMin, math.max(_threats.moleIntervalMin, _threats.moleIntervalMax));
            if (_sim.PlayTime < _threats.moleFirstSeconds || _sim.Ants.Count < _threats.moleMinAnts)
            {
                DiggerTimer = 60f; // look again in a minute
                return;
            }
            TriggerDigger();
        }

        /// <summary>A digger starts tunnelling toward the nest now (debug, tests, the timer). False if no soft ground was found.</summary>
        public bool TriggerDigger()
        {
            if (_content.Diggers.Length == 0) return false;
            int def = _content.Diggers[Rng.NextInt(_content.Diggers.Length)];
            if (!FindDiggerSpawn(_content.Enemies[def].Def, out var cell)) return false;
            _sim.DigByCreature(cell);
            Spawn(def, cell, EnemyState.Burrow);
            _sim.Events.Publish(new DiggerEvent(DiggerPhase.Appeared, def, cell));
            return true;
        }

        /// <summary>Soft, solid ground underground, <c>moleDistance</c> cells from the queen and away from the colony's tunnels.</summary>
        bool FindDiggerSpawn(EnemyDef def, out int2 cell)
        {
            var world = _sim.World;
            var queen = _sim.Colony.QueenCell;
            int lo = math.max(4, _threats.moleDistance.x), hi = math.max(lo, _threats.moleDistance.y);
            for (int attempt = 0; attempt < 48; attempt++)
            {
                float angle = Rng.NextFloat(-0.6f, 0.6f) + (Rng.NextBool() ? 0f : math.PI); // mostly from the side
                float dist = Rng.NextFloat(lo, hi);
                int2 c = queen + new int2((int)math.round(math.cos(angle) * dist), (int)math.round(math.sin(angle) * dist * 0.6f));
                if (!world.InBounds(c.x, c.y) || world.Depth(c.x, c.y) < 4) continue;
                if (!CanDig(world[c.x, c.y], def) || TouchesColony(c)) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        bool CanDig(Cell cell, EnemyDef def)
        {
            if (cell.IsOpen) return false;
            var mat = _content.MaterialSim[cell.Material];
            return !mat.IsIndestructible && !mat.IsAir && mat.Hardness <= def.maxDigHardness;
        }

        /// <summary>The cell or one next to it is open ground the colony has explored (its tunnels).</summary>
        bool TouchesColony(int2 c)
        {
            var world = _sim.World;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (!world.InBounds(x, y)) continue;
                var cell = world[x, y];
                if (cell.IsOpen && (cell.Flags & CellFlags.Explored) != 0) return true;
            }
            return false;
        }

        /// <summary>
        /// One decision of a burrowing digger: break into the colony when next to its tunnels, else step (or dig) to the
        /// neighbour that brings it closest to the queen, with a little wobble so tunnels wind. It never steps straight back,
        /// skips rock too hard for it and gives up when it stops getting closer for too long.
        /// </summary>
        void Burrow(ref Enemy e, EnemyDef def, float dt)
        {
            int2 c = e.Cell;
            var world = _sim.World;
            if (TouchesColony(c))
            {
                e.State = EnemyState.Hunt;
                e.Home = _sim.Colony.QueenCell;
                e.Attacking = false;
                e.Timer = 0f;
                _sim.ExploreTunnel(c);
                _sim.Events.Publish(new DiggerEvent(DiggerPhase.BrokeIn, e.Def, c));
                return;
            }
            if (e.Held > 0f) return;

            float2 target = new float2(_sim.Colony.QueenCell);
            float here = math.distance(new float2(c), target);
            int2 dig = c + e.Heading;
            bool keepDigging = e.Timer > 0f && world.InBounds(dig.x, dig.y) && CanDig(world[dig.x, dig.y], def);
            int2 best = keepDigging ? dig : c;
            if (!keepDigging)
            {
                float bestScore = float.MaxValue;
                for (int k = 0; k < Cardinal4.Length; k++)
                {
                    int2 n = c + Cardinal4[k];
                    if (n.Equals(e.Trail) || !world.InBounds(n.x, n.y) || world.Depth(n.x, n.y) < 2) continue;
                    var cell = world[n.x, n.y];
                    if (cell.IsOpen ? IsClosedGate(n) : !CanDig(cell, def)) continue;
                    float score = math.distance(new float2(n), target) + Rng.NextFloat(0.9f);
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = n;
                }
            }
            bool boxedIn = best.Equals(c);
            bool closer = !boxedIn && math.distance(new float2(best), target) < here;
            e.Stuck = closer ? math.max(0f, e.Stuck - dt) : e.Stuck + dt;
            if (e.Stuck >= _threats.moleGiveUpSeconds)
            {
                e.Escaped = true;
                _sim.Events.Publish(new DiggerEvent(DiggerPhase.GaveUp, e.Def, c));
                return;
            }
            if (boxedIn)
            {
                e.Trail = c; // nothing but the way back: allow it next time
                return;
            }

            e.Heading = best - c;
            if (world[best.x, best.y].IsOpen)
            {
                e.Attacking = false;
                e.Timer = 0f;
                e.Trail = c;
                StartStep(ref e, best);
                return;
            }
            e.Attacking = true;
            float hardness = math.max(0.01f, _content.MaterialSim[world[best.x, best.y].Material].Hardness);
            e.Timer += def.digPower * dt / hardness;
            if (e.Timer < 1f) return;
            e.Timer = 0f;
            _sim.DigByCreature(best);
            e.Trail = c;
            StartStep(ref e, best);
        }
    }
}
