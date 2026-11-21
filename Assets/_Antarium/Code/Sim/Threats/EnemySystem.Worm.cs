using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// The giant earthworm of the random event (Milestone 11). It starts in soft ground near the colony's tunnels and
    /// meanders through it (going on straight most of the time, turning now and then, drawn back when it strays past
    /// <c>wormLeash</c>), digging a one-cell tunnel that becomes part of the colony's known tunnels as it goes. It crosses
    /// the colony's tunnels but never natural caves, fluids, buildings, the royal chamber or rock too hard for it; boxed in,
    /// it backs out the other way. Its body (<see cref="WormBody"/>) follows the head cell by cell: ants next to any part
    /// of it bite it, soldiers hunt it, and the Dig tool hurts it. It never hunts; it only thrashes at ants touching its head.
    /// At most one worm at a time.
    /// </summary>
    public sealed partial class EnemySystem
    {
        /// <summary>
        /// The worm's body, nearest the head first: <c>wormSegments</c> cells, then one more (where the tail comes from,
        /// for drawing it in between). Empty without a worm. Saved.
        /// </summary>
        internal readonly List<int2> WormBody = new();
        readonly HashSet<int> _wormBiters = new();

        public IReadOnlyList<int2> WormCells => WormBody;

        int WormSegments => math.max(1, _threats.wormSegments);

        /// <summary>Index of the worm in <see cref="Enemies"/>, or -1.</summary>
        public int WormIndex()
        {
            for (int i = 0; i < Enemies.Count; i++)
                if (_content.Enemies[Enemies[i].Def].Def.behavior == EnemyBehavior.Worm) return i;
            return -1;
        }

        /// <summary>
        /// The worm of random event <paramref name="eventId"/> starts burrowing (enemy <paramref name="def"/>). False if a worm
        /// is already about or no soft ground was found near the tunnels.
        /// </summary>
        internal bool TriggerWorm(int def, int eventId, out int2 cell)
        {
            cell = default;
            if (def < 0 || def >= _content.Enemies.Length || WormIndex() >= 0) return false;
            if (!FindWormSpawn(_content.Enemies[def].Def, out cell)) return false;
            _sim.DigByCreature(cell);
            MarkWormTunnel(cell);
            Spawn(def, cell, EnemyState.Tunnel);
            var e = Enemies[Enemies.Count - 1];
            e.Event = eventId;
            e.Heading = Cardinal4[Rng.NextInt(Cardinal4.Length)];
            Enemies[Enemies.Count - 1] = e;
            WormBody.Clear();
            for (int k = 0; k <= WormSegments; k++) WormBody.Add(cell);
            return true;
        }

        /// <summary>The worm of event <paramref name="eventId"/> burrows away (its time is up): it is removed without loot.</summary>
        internal void RemoveWorm(int eventId)
        {
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                if (Enemies[i].Event != eventId) continue;
                var cell = Enemies[i].Cell;
                Enemies.RemoveAt(i);
                WormBody.Clear();
                Changed = true;
                _sim.Events.Publish(new WormEvent(WormPhase.Left, cell));
            }
        }

        /// <summary>The worm's head or a body cell lies within <paramref name="radius"/> of <paramref name="cellPos"/> (cell space).</summary>
        int WormAt(float2 cellPos, float radius)
        {
            int w = WormIndex();
            if (w < 0) return -1;
            if (math.distance(Enemies[w].Pos, cellPos) <= radius) return w;
            for (int k = 0; k < WormSegments && k < WormBody.Count; k++)
                if (math.distance(new float2(WormBody[k]) + 0.5f, cellPos) <= radius) return w;
            return -1;
        }

        /// <summary>
        /// A Dig tool hit at <paramref name="cellPos"/> (brush <paramref name="radius"/>, dig <paramref name="power"/>) hurts the
        /// worm if it touches its body. True if it did.
        /// </summary>
        internal bool HitWorm(float2 cellPos, float radius, float power)
        {
            int w = WormAt(cellPos, radius + 0.7f);
            if (w < 0) return false;
            var e = Enemies[w];
            Hurt(ref e, power * _threats.wormDigDamage);
            Enemies[w] = e;
            return true;
        }

        /// <summary>Soft solid ground <c>wormDistance</c> cells from a random ant underground, clear of the tunnels.</summary>
        bool FindWormSpawn(EnemyDef def, out int2 cell)
        {
            var world = _sim.World;
            var ants = _sim.Ants.Ants;
            int lo = math.max(2, _threats.wormDistance.x), hi = math.max(lo, _threats.wormDistance.y);
            int minDepth = math.max(1, _threats.wormMinDepth) + 1;
            for (int attempt = 0; attempt < 64 && ants.Length > 0; attempt++)
            {
                var a = ants[Rng.NextInt(ants.Length)];
                if (a.IsDead || world.Depth(a.Cell.x, a.Cell.y) < minDepth) continue;
                float angle = Rng.NextFloat(math.PI * 2f);
                float dist = Rng.NextFloat(lo, hi);
                int2 c = a.Cell + new int2((int)math.round(math.cos(angle) * dist), (int)math.round(math.sin(angle) * dist * 0.7f));
                if (!world.InBounds(c.x, c.y) || world.Depth(c.x, c.y) < minDepth) continue;
                if (!CanDig(world[c.x, c.y], def) || NearColony(c, _threats.wormClearance)) continue;
                cell = c;
                return true;
            }
            cell = default;
            return false;
        }

        /// <summary>
        /// One decision of the worm: thrash at an ant touching its head, then keep digging the cell ahead, or pick the next
        /// cell (and step into it if it is open). Boxed in, it backs out: its tail becomes its head.
        /// </summary>
        void Wriggle(ref Enemy e, EnemyDef def, float dt)
        {
            int2 c = e.Cell;
            if (FindAdjacentAnt(c, out int ant))
            {
                e.Attacking = true;
                if (e.AttackTimer <= 0f)
                {
                    e.AttackTimer = def.attackInterval;
                    Bite(ant, e.Def, def.damage);
                }
            }
            else e.Attacking = false;
            if (e.Held > 0f) return;

            var world = _sim.World;
            int2 target = c;
            if (e.Timer > 0f)
            {
                target = c + e.Heading;
                if (!WormCanEnter(target, def))
                {
                    e.Timer = 0f;
                    target = c;
                }
            }
            if (target.Equals(c)) target = ChooseWormStep(e, def);
            if (target.Equals(c))
            {
                ReverseWorm(ref e);
                return;
            }
            e.Heading = target - c;
            if (world[target.x, target.y].IsOpen)
            {
                e.Timer = 0f;
                StepWorm(ref e, target);
                return;
            }
            float hardness = math.max(0.01f, _content.MaterialSim[world[target.x, target.y].Material].Hardness);
            e.Timer += def.digPower * dt / hardness;
            if (e.Timer < 1f) return;
            e.Timer = 0f;
            _sim.DigByCreature(target);
            MarkWormTunnel(target);
            _sim.Events.Publish(new WormEvent(WormPhase.Dug, target));
            StepWorm(ref e, target);
        }

        /// <summary>Straight on most of the time, else a random turn; past the leash it turns back toward where it started.</summary>
        int2 ChooseWormStep(in Enemy e, EnemyDef def)
        {
            int2 c = e.Cell;
            float2 home = new float2(e.Home);
            float here = math.distance(new float2(c), home);
            bool far = here > _threats.wormLeash;
            int2 best = c;
            float bestScore = float.MaxValue;
            for (int k = 0; k < Cardinal4.Length; k++)
            {
                int2 d = Cardinal4[k];
                if (d.Equals(-e.Heading)) continue;
                int2 n = c + d;
                if (!WormCanEnter(n, def)) continue;
                float score = Rng.NextFloat();
                if (d.Equals(e.Heading)) score -= 1f - _threats.wormTurnChance;
                if (d.y != 0) score += 0.15f; // worms wander along more than up and down
                if (far) score += (math.distance(new float2(n), home) - here) * 2f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = n;
            }
            return best;
        }

        /// <summary>
        /// Soft ground it can dig (and no building stands on it), or the colony's own (explored) tunnels without deep fluid,
        /// lava, buildings or the royal chamber. Never its own body, natural caves or above <c>wormMinDepth</c>.
        /// </summary>
        bool WormCanEnter(int2 n, EnemyDef def)
        {
            var world = _sim.World;
            if (!world.InBounds(n.x, n.y) || world.Depth(n.x, n.y) < math.max(1, _threats.wormMinDepth)) return false;
            for (int k = 0; k < WormSegments && k < WormBody.Count; k++)
                if (WormBody[k].Equals(n)) return false;
            var cell = world[n.x, n.y];
            if (_sim.Buildings.At(n) >= 0) return false;
            if (!cell.IsOpen)
            {
                int above = _sim.Buildings.At(n + new int2(0, 1));
                return CanDig(cell, def) && above < 0;
            }
            if ((cell.Flags & CellFlags.Explored) == 0 || _sim.Colony.IsRoyalCell(n)) return false;
            var f = world.Fluid[world.Index(n.x, n.y)];
            if (f.Level == 0) return true;
            if (f.Kind == FluidKind.Lava) return false;
            return f.Kind.IsGas() || f.Level < FluidCell.Full / 2;
        }

        /// <summary>The head moves on: the body follows one cell.</summary>
        void StepWorm(ref Enemy e, int2 next)
        {
            WormBody.Insert(0, e.Cell);
            while (WormBody.Count > WormSegments + 1) WormBody.RemoveAt(WormBody.Count - 1);
            e.Trail = e.Cell;
            StartStep(ref e, next);
        }

        /// <summary>Boxed in: the worm backs out, its last segment becoming the head.</summary>
        void ReverseWorm(ref Enemy e)
        {
            int n = math.min(WormSegments, WormBody.Count);
            if (n == 0) return;
            var tail = WormBody[n - 1];
            if (tail.Equals(e.Cell)) return; // still coiled up where it started: nothing to back into
            var cells = new List<int2>(n + 1) { e.Cell };
            for (int k = 0; k < n; k++) cells.Add(WormBody[k]);
            WormBody.Clear();
            for (int k = cells.Count - 2; k >= 0; k--) WormBody.Add(cells[k]);
            WormBody.Add(cells[0]);
            int2 head = cells[cells.Count - 1];
            int2 behind = cells[cells.Count - 2];
            e.Cell = e.From = head;
            e.Progress = 0f;
            e.Timer = 0f;
            e.Pos = e.PrevPos = new float2(head) + 0.5f;
            e.Heading = head.Equals(behind) ? Cardinal4[Rng.NextInt(Cardinal4.Length)] : head - behind;
            e.Trail = behind;
            Changed = true;
        }

        /// <summary>Its tunnel joins the colony's known tunnels (ants may use it; breaking into it is no new cave).</summary>
        void MarkWormTunnel(int2 c)
        {
            var world = _sim.World;
            int i = world.Index(c.x, c.y);
            var cell = world.Cells[i];
            cell.Flags |= CellFlags.Explored | CellFlags.Revealed;
            world.Cells[i] = cell;
            world.MarkChanged(c.x, c.y, ChunkFlags.TerrainUpload);
        }

        /// <summary>Ants next to any body cell of the worm bite it too (each ant once).</summary>
        float WormBites(in Enemy worm, float mult)
        {
            var ants = _sim.Ants.Ants;
            var world = _sim.World;
            var castes = _content.CasteSim;
            _wormBiters.Clear();
            float damage = 0f;
            for (int k = -1; k < WormSegments && k < WormBody.Count; k++)
            {
                int2 at = k < 0 ? worm.Cell : WormBody[k];
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int2 n = at + new int2(dx, dy);
                    if (!world.InBounds(n.x, n.y)) continue;
                    if (!_antsByCell.TryGetFirstValue(world.Index(n.x, n.y), out int ant, out var it)) continue;
                    do
                    {
                        var a = ants[ant];
                        if (!a.IsDead && _wormBiters.Add(ant)) damage += castes[(int)a.Caste].Attack * mult;
                    } while (_antsByCell.TryGetNextValue(out ant, ref it));
                }
            }
            return damage;
        }
    }
}
