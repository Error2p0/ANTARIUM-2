using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public struct Egg
    {
        public int2 Cell;
        public float Age;
        public AntCaste Caste;
        /// <summary>Nursery building the egg lies in, or -1 (next to the queen, or put down somewhere).</summary>
        public int Nursery;
        /// <summary>Place in its nursery (eggs share floor cells, two to a cell).</summary>
        public int Slot;

        public bool InNursery => Nursery >= 0;
    }

    /// <summary>Queen, brood and colony-wide resources.</summary>
    public sealed class Colony
    {
        readonly ContentDB _content;
        readonly ColonyConfig _config;
        readonly CasteConfig _castes;

        public readonly Inventory Inventory;
        /// <summary>Stat modifiers from research.</summary>
        public readonly Modifiers Mods = new();
        public readonly ColonyStats Stats;
        public readonly ProductionMeter Meter;
        /// <summary>Per building definition / ability: usable (not locked behind research).</summary>
        public readonly bool[] BuildingUnlocked;
        public readonly bool[] AbilityUnlocked;
        /// <summary>Per <see cref="AntCaste"/>: the queen may lay it (authored and not locked behind research).</summary>
        public readonly bool[] CasteUnlocked = new bool[Castes.Count];
        /// <summary>Per <see cref="AntCaste"/>: share of the colony (ants and eggs) the queen keeps of it. Workers fill the rest.</summary>
        public readonly float[] CasteShare = new float[Castes.Count];
        public readonly ItemPiles Piles;
        public readonly List<Egg> Eggs = new();
        /// <summary>Cells of the royal chamber where its share of the stock is shown, in fill order.</summary>
        public readonly List<int2> RoyalDisplayCells = new();
        int2 _queenCell;
        public float EggTimer;
        public float ForageTimer;
        public bool Starving;
        /// <summary>Raiders are in the royal chamber: the queen does not lay.</summary>
        public bool QueenDisturbed;
        /// <summary>Princesses raised in princess chambers, waiting for the Nuptial Flight.</summary>
        public int Princesses;
        /// <summary>Princesses that count toward a flight; princess chambers stop raising more at this many.</summary>
        public int PrincessCap = 5;

        /// <summary>World indices of cells designated for mining / painted as storage.</summary>
        public readonly HashSet<int> Designated = new();
        public readonly HashSet<int> StorageCells = new();

        public Colony(ContentDB content, ColonyConfig config, CasteConfig castes = null)
        {
            _content = content;
            _config = config;
            _castes = castes ?? new CasteConfig();
            Stats = new ColonyStats(content.Items.Length, content.Buildings.Length);
            Meter = new ProductionMeter(content.Items.Length);
            Inventory = new Inventory(content, config, Mods, Stats, Meter);
            Piles = new ItemPiles(content.Items.Length);
            BuildingUnlocked = new bool[content.Buildings.Length];
            for (int i = 0; i < BuildingUnlocked.Length; i++) BuildingUnlocked[i] = !content.BuildingStartsLocked[i];
            AbilityUnlocked = new bool[content.Abilities.Length];
            for (int i = 0; i < AbilityUnlocked.Length; i++) AbilityUnlocked[i] = !content.AbilityStartsLocked[i];
            for (int c = 0; c < Castes.Count; c++)
                if (content.CasteDefined[c] && !content.CasteStartsLocked[c]) UnlockCaste((AntCaste)c);
        }

        /// <summary>Seconds between eggs while fed.</summary>
        public float EggInterval => _config.eggInterval / Mods.Mult(StatKind.LayingSpeed);
        /// <summary>Eggs that may wait next to the queen at once (eggs in nurseries don't count).</summary>
        public int MaxEggs => _config.maxEggs + (int)Mods.Flat(StatKind.MaxEggs);

        public int2 QueenCell
        {
            get => _queenCell;
            set
            {
                _queenCell = value;
                Inventory.SetRoyalCell(value);
            }
        }

        public float SpeedFactor => Starving ? _config.starvingSpeedFactor : 1f;

        /// <summary>Eggs waiting outside a nursery (they count toward <see cref="MaxEggs"/>).</summary>
        public int LooseEggs
        {
            get
            {
                int n = 0;
                foreach (var e in Eggs)
                    if (!e.InNursery) n++;
                return n;
            }
        }

        /// <summary>Food eaten per minute by the ants (<paramref name="alive"/> per caste) and the queen.</summary>
        public float UpkeepPerMinute(IReadOnlyList<int> alive)
        {
            float total = _config.queenUpkeep;
            for (int c = 0; c < alive.Count && c < Castes.Count; c++) total += alive[c] * _content.CasteDefs[c].upkeep;
            return total;
        }

        public float EggCost(AntCaste caste) => _content.Caste(caste).eggFoodCost;

        public bool SoldiersUnlocked => CasteUnlocked[(int)AntCaste.Soldier];
        public float ShareStep => _castes.shareStep;

        /// <summary>Research unlocked a caste: its share starts at the caste's default (within the total limit).</summary>
        public void UnlockCaste(AntCaste caste)
        {
            int c = (int)caste;
            if (CasteUnlocked[c] || !_content.CasteDefined[c]) return;
            CasteUnlocked[c] = true;
            if (caste != AntCaste.Worker) SetCasteShare(caste, _content.Caste(caste).defaultShare);
        }

        /// <summary>
        /// Sets a specialist caste's share: snapped to the step, at most <c>maxShare</c>, and lowered so all specialists
        /// together stay within <c>maxTotalShare</c>. Workers have no share of their own.
        /// </summary>
        public void SetCasteShare(AntCaste caste, float share)
        {
            int c = (int)caste;
            if (caste == AntCaste.Worker || !CasteUnlocked[c]) return;
            float step = math.max(0.01f, _castes.shareStep);
            float others = SpecialistShare - CasteShare[c];
            float max = math.min(_castes.maxShare, math.max(0f, _castes.maxTotalShare - others));
            share = math.clamp(math.round(share / step) * step, 0f, max);
            CasteShare[c] = math.floor(share / step + 1e-3f) * step; // keep it on the step after clamping
        }

        /// <summary>Sum of the specialist shares (workers get the rest).</summary>
        public float SpecialistShare
        {
            get
            {
                float s = 0f;
                for (int c = 1; c < Castes.Count; c++) s += CasteShare[c];
                return s;
            }
        }

        /// <summary>True inside the royal chamber or on a storage zone cell: items delivered here go into the stock.</summary>
        public bool IsStockCell(int2 c, int worldIndex) => IsRoyalCell(c) || StorageCells.Contains(worldIndex);

        /// <summary>Within the royal chamber (a square of <c>royalRadius</c> around the queen).</summary>
        public bool IsRoyalCell(int2 c)
        {
            int2 d = math.abs(c - QueenCell);
            return d.x <= _config.royalRadius && d.y <= _config.royalRadius;
        }

        /// <summary>Food upkeep. Starvation slows the colony and stops egg laying; it is never instant death.</summary>
        public void UpdateUpkeep(int[] alive, float dt)
        {
            float perMinute = UpkeepPerMinute(alive);
            Starving = !Inventory.ConsumeFood(perMinute / 60f * dt) || Inventory.Food <= 0f;
        }

        /// <summary>
        /// Lays an egg when fed, undisturbed and there is room next to the queen. Its caste is the one furthest below
        /// its share (see <see cref="NextCaste"/>). Returns true if one was laid.
        /// </summary>
        public bool TryLayEgg(float dt, int2 eggCell, int[] alive)
        {
            EggTimer += dt;
            if (Starving || QueenDisturbed || EggTimer < EggInterval) return false;
            if (LooseEggs >= MaxEggs) return false;
            var caste = NextCaste(alive);
            float cost = EggCost(caste);
            if (Inventory.Food < cost) return false;
            Inventory.ConsumeFood(cost);
            Eggs.Add(new Egg { Cell = eggCell, Caste = caste, Nursery = -1 });
            EggTimer = 0f;
            return true;
        }

        /// <summary>
        /// Caste of the next egg: the unlocked specialist furthest below its share of ants and eggs (by at least half
        /// an ant), else a worker. <paramref name="alive"/> = ants per caste, plus eggs being carried.
        /// </summary>
        public AntCaste NextCaste(int[] alive)
        {
            int total = 1 + Eggs.Count;
            for (int c = 0; c < alive.Length; c++) total += alive[c];
            var best = AntCaste.Worker;
            float bestDeficit = 0.5f - 1e-4f;
            foreach (var caste in _content.CasteOrder)
            {
                int c = (int)caste;
                if (caste == AntCaste.Worker || !CasteUnlocked[c] || CasteShare[c] <= 0f) continue;
                int have = c < alive.Length ? alive[c] : 0;
                foreach (var egg in Eggs)
                    if (egg.Caste == caste) have++;
                float deficit = CasteShare[c] * total - have;
                if (deficit <= bestDeficit) continue;
                bestDeficit = deficit;
                best = caste;
            }
            return best;
        }

        /// <summary>Lays a worker egg right away, free and regardless of the egg limit (Royal Decree).</summary>
        public void LayEggNow(int2 eggCell) => Eggs.Add(new Egg { Cell = eggCell, Nursery = -1 });

        /// <summary>
        /// Ages eggs and returns hatched ones into <paramref name="hatched"/>. Eggs in a nursery age faster by its
        /// bonus (<paramref name="nurseryBonus"/> per nursery building id, or null).
        /// </summary>
        public void UpdateEggs(float dt, List<Egg> hatched, System.Func<int, float> nurseryBonus = null)
        {
            for (int i = Eggs.Count - 1; i >= 0; i--)
            {
                var egg = Eggs[i];
                float speed = egg.InNursery && nurseryBonus != null ? 1f + nurseryBonus(egg.Nursery) : 1f;
                egg.Age += dt * speed;
                if (egg.Age >= _config.hatchTime)
                {
                    hatched.Add(egg);
                    Eggs.RemoveAt(i);
                }
                else Eggs[i] = egg;
            }
        }

        public float HatchTime => _config.hatchTime;
    }
}
