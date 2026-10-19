using System.Collections.Generic;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    public struct Egg
    {
        public int2 Cell;
        public float Age;
    }

    /// <summary>Queen, brood and colony-wide resources.</summary>
    public sealed class Colony
    {
        readonly ColonyConfig _config;

        public readonly Inventory Inventory;
        /// <summary>Stat modifiers from research.</summary>
        public readonly Modifiers Mods = new();
        public readonly ColonyStats Stats;
        public readonly ProductionMeter Meter;
        /// <summary>Per building definition / ability: usable (not locked behind research).</summary>
        public readonly bool[] BuildingUnlocked;
        public readonly bool[] AbilityUnlocked;
        public readonly ItemPiles Piles;
        public readonly List<Egg> Eggs = new();
        /// <summary>Cells of the royal chamber where its share of the stock is shown, in fill order.</summary>
        public readonly List<int2> RoyalDisplayCells = new();
        int2 _queenCell;
        public float EggTimer;
        public float ForageTimer;
        public bool Starving;

        /// <summary>World indices of cells designated for mining / painted as storage.</summary>
        public readonly HashSet<int> Designated = new();
        public readonly HashSet<int> StorageCells = new();

        public Colony(ContentDB content, ColonyConfig config)
        {
            _config = config;
            Stats = new ColonyStats(content.Items.Length, content.Buildings.Length);
            Meter = new ProductionMeter(content.Items.Length);
            Inventory = new Inventory(content, config, Mods, Stats, Meter);
            Piles = new ItemPiles(content.Items.Length);
            BuildingUnlocked = new bool[content.Buildings.Length];
            for (int i = 0; i < BuildingUnlocked.Length; i++) BuildingUnlocked[i] = !content.BuildingStartsLocked[i];
            AbilityUnlocked = new bool[content.Abilities.Length];
            for (int i = 0; i < AbilityUnlocked.Length; i++) AbilityUnlocked[i] = !content.AbilityStartsLocked[i];
        }

        /// <summary>Seconds between eggs while fed.</summary>
        public float EggInterval => _config.eggInterval / Mods.Mult(StatKind.LayingSpeed);
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

        /// <summary>Food eaten per minute by the workers and the queen.</summary>
        public float UpkeepPerMinute(int workers) => workers * _config.workerUpkeep + _config.queenUpkeep;

        /// <summary>True inside the royal chamber or on a storage zone cell: items delivered here go into the stock.</summary>
        public bool IsStockCell(int2 c, int worldIndex) => IsRoyalCell(c) || StorageCells.Contains(worldIndex);

        /// <summary>Within the royal chamber (a square of <c>royalRadius</c> around the queen).</summary>
        public bool IsRoyalCell(int2 c)
        {
            int2 d = math.abs(c - QueenCell);
            return d.x <= _config.royalRadius && d.y <= _config.royalRadius;
        }

        /// <summary>Food upkeep. Starvation slows the colony and stops egg laying; it is never instant death.</summary>
        public void UpdateUpkeep(int workers, float dt)
        {
            float perMinute = UpkeepPerMinute(workers);
            Starving = !Inventory.ConsumeFood(perMinute / 60f * dt) || Inventory.Food <= 0f;
        }

        /// <summary>Lays an egg when fed and there is room. Returns true if one was laid.</summary>
        public bool TryLayEgg(float dt, int2 eggCell)
        {
            EggTimer += dt;
            if (Starving || EggTimer < EggInterval) return false;
            if (Eggs.Count >= MaxEggs || Inventory.Food < _config.eggFoodCost) return false;
            Inventory.ConsumeFood(_config.eggFoodCost);
            Eggs.Add(new Egg { Cell = eggCell });
            EggTimer = 0f;
            return true;
        }

        /// <summary>Lays an egg right away, free and regardless of the egg limit (Royal Decree).</summary>
        public void LayEggNow(int2 eggCell) => Eggs.Add(new Egg { Cell = eggCell });

        /// <summary>Ages eggs and returns hatched ones into <paramref name="hatched"/>.</summary>
        public void UpdateEggs(float dt, List<int2> hatched)
        {
            for (int i = Eggs.Count - 1; i >= 0; i--)
            {
                var egg = Eggs[i];
                egg.Age += dt;
                if (egg.Age >= _config.hatchTime)
                {
                    hatched.Add(egg.Cell);
                    Eggs.RemoveAt(i);
                }
                else Eggs[i] = egg;
            }
        }
    }
}
