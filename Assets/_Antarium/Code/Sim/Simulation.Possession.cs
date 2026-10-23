using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public enum PossessionPhase : byte
    {
        /// <summary>The player took control of an ant.</summary>
        Started,
        /// <summary>The player let go.</summary>
        Released,
        /// <summary>The possessed ant died.</summary>
        Died,
        /// <summary>A possess click found no ant.</summary>
        Missed,
    }

    /// <summary>
    /// Ant possession: the player takes control of one ant. It walks where the player pushes it (same crawling rules
    /// as every ant), bites the rock or enemies within reach, picks up and delivers items. It is stronger than its
    /// caste (faster, digs like the Dig tool, takes less damage) and takes no AI decisions until released.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>How close (cells) a possess click must be to an ant.</summary>
        const float PossessPickRadius = 1.5f;

        /// <summary>Index of the possessed ant, or -1.</summary>
        public int PossessedAnt => Ants.PossessedIndex();

        void Possess(float2 cellPos)
        {
            int best = -1;
            float bestDist = PossessPickRadius;
            var ants = Ants.Ants;
            for (int i = 0; i < ants.Length; i++)
            {
                if (ants[i].IsDead) continue;
                float d = math.distance(ants[i].Pos, cellPos);
                if (d > bestDist) continue;
                bestDist = d;
                best = i;
            }
            if (best < 0)
            {
                Events.Publish(new PossessionEvent(PossessionPhase.Missed, AntCaste.Worker, (int2)math.floor(cellPos)));
                return;
            }
            Release(PossessionPhase.Released, announce: false);
            var a = ants[best];
            a.Possessed = true;
            if (a.State != AntState.Falling && a.State != AntState.Swimming) a.State = AntState.Idle;
            ants[best] = a;
            Events.Publish(new PossessionEvent(PossessionPhase.Started, a.Caste, a.Cell));
        }

        void Release(PossessionPhase phase, bool announce = true)
        {
            int i = Ants.PossessedIndex();
            if (i < 0) return;
            var a = Ants.Ants[i];
            a.Possessed = false;
            Ants.Ants[i] = a;
            PossessInput = int2.zero;
            if (announce) Events.Publish(new PossessionEvent(phase, a.Caste, a.Cell));
        }

        /// <summary>A bite toward <paramref name="target"/>, clamped to the ant's reach: an enemy there takes damage, else the rock is dug.</summary>
        void PossessAct(float2 target, float combo)
        {
            int i = Ants.PossessedIndex();
            if (i < 0) return;
            var a = Ants.Ants[i];
            var cfg = Config.possession;
            float2 delta = target - a.Pos;
            float dist = math.length(delta);
            if (dist > cfg.reach) target = a.Pos + delta / dist * cfg.reach;
            if (dist > 1e-3f)
                a.Heading = math.abs(delta.x) >= math.abs(delta.y) ? new int2((int)math.sign(delta.x), 0) : new int2(0, (int)math.sign(delta.y));
            a.ActTimer = 0.25f;
            Ants.Ants[i] = a;

            int enemy = Enemies.EnemyAt(target, 1.2f);
            if (enemy >= 0)
            {
                Enemies.Strike(enemy, cfg.biteDamage * Colony.Mods.Mult(StatKind.AntAttack));
                return;
            }
            ResolveDig(new DigCommand(target, Config.dig.brushRadius * 0.6f, cfg.digPower * combo));
        }

        /// <summary>Carrying: deliver here (machine, stock, belt) or put it down. Empty-handed: pick up a pile at or next to the ant.</summary>
        void PossessInteract()
        {
            int i = Ants.PossessedIndex();
            if (i < 0) return;
            var a = Ants.Ants[i];
            if (a.CarryingEgg)
            {
                int id = Buildings.At(a.Cell);
                if (id >= 0 && NurseryRoom(id) > 0)
                {
                    var b = Buildings.Get(id);
                    int slot = 0;
                    while (SlotTaken(id, slot)) slot++;
                    Colony.Eggs.Add(new Egg { Cell = NurserySlotCell(b, slot), Age = a.EggAge, Caste = a.EggCaste, Nursery = id, Slot = slot });
                    a.CarryingEgg = false;
                    _broodChanged = true;
                }
                else PutEggDown(ref a, a.Cell);
            }
            else if (a.IsCarrying)
            {
                if (DeliverCarried(ref a, a.Cell) == 0) DropCarried(ref a, a.Cell);
            }
            else
            {
                for (int k = -1; k < NavGrid.DirectionCount; k++)
                {
                    int2 c = k < 0 ? a.Cell : a.Cell + NavGrid.Direction(k);
                    if (!Colony.Piles.TryTakeAny(c, out ushort item)) continue;
                    a.Carry = item;
                    a.CarryCount = 1;
                    a.Dest = CarryDest.Undecided;
                    Colony.Inventory.BeginTransit(item);
                    break;
                }
            }
            a.ActTimer = 0.25f;
            Ants.Ants[i] = a;
        }
    }
}
