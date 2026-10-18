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
        public readonly ItemPiles Piles;
        public readonly List<Egg> Eggs = new();
        public int2 QueenCell;
        public float EggTimer;
        public float ForageTimer;
        public bool Starving;

        /// <summary>World indices of cells designated for mining / painted as storage.</summary>
        public readonly HashSet<int> Designated = new();
        public readonly HashSet<int> StorageCells = new();

        public Colony(ContentDB content, ColonyConfig config)
        {
            _config = config;
            Inventory = new Inventory(content, config);
            Piles = new ItemPiles(content.Items.Length);
        }

        public float SpeedFactor => Starving ? _config.starvingSpeedFactor : 1f;

        /// <summary>Food upkeep. Starvation slows the colony and stops egg laying; it is never instant death.</summary>
        public void UpdateUpkeep(int workers, float dt)
        {
            float perMinute = workers * _config.workerUpkeep + _config.queenUpkeep;
            Starving = !Inventory.ConsumeFood(perMinute / 60f * dt) || Inventory.Food <= 0f;
        }

        /// <summary>Lays an egg when fed and there is room. Returns true if one was laid.</summary>
        public bool TryLayEgg(float dt, int2 eggCell)
        {
            EggTimer += dt;
            if (Starving || EggTimer < _config.eggInterval) return false;
            if (Eggs.Count >= _config.maxEggs || Inventory.Food < _config.eggFoodCost) return false;
            Inventory.ConsumeFood(_config.eggFoodCost);
            Eggs.Add(new Egg { Cell = eggCell });
            EggTimer = 0f;
            return true;
        }

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
