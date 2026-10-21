using System;
using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>What happened while the game was closed (shown in the welcome-back summary).</summary>
    public sealed class OfflineReport
    {
        /// <summary>Real time away.</summary>
        public double AwaySeconds;
        /// <summary>Time actually credited (away time, capped).</summary>
        public double CreditedSeconds;
        public float Efficiency;
        /// <summary>Per item: net change of the stock.</summary>
        public readonly int[] Items;
        /// <summary>Items produced that did not fit into storage.</summary>
        public int ItemsLost;
        public float Food;
        public float Knowledge;
        public int Hatched;
        /// <summary>Food ran out at some point.</summary>
        public bool Starved;

        public OfflineReport(int items) => Items = new int[items];

        public bool WasCapped => AwaySeconds > CreditedSeconds + 1.0;
    }

    /// <summary>
    /// Offline progress: instead of running the full ant simulation, replays the colony's recent production
    /// rates (<see cref="ProductionMeter"/>) for the time away, in one-minute steps, up to the offline cap.
    /// The colony runs at the offline efficiency as a whole: it earns and eats at that pace. Items only arrive
    /// while storage has room; the queen keeps laying while there is food to spare, and the eggs hatch.
    /// </summary>
    public static class OfflineProgress
    {
        const float StepSeconds = 60f;

        public static OfflineReport Apply(Simulation sim, double awaySeconds)
        {
            var content = sim.Content;
            var colony = sim.Colony;
            var inv = colony.Inventory;
            var mods = colony.Mods;
            var cfg = sim.Config.save;
            int n = content.Items.Length;
            var report = new OfflineReport(n)
            {
                AwaySeconds = Math.Max(0.0, awaySeconds),
                Efficiency = math.saturate(cfg.offlineEfficiency + mods.Flat(StatKind.OfflineEfficiency)),
            };
            double cap = Math.Max(0f, cfg.offlineMaxHours + mods.Flat(StatKind.OfflineHours)) * 3600.0;
            report.CreditedSeconds = Math.Min(report.AwaySeconds, cap);
            if (report.CreditedSeconds <= 0.0) return report;

            var meter = colony.Meter;
            float eff = report.Efficiency;
            var rates = new float[n];
            for (int i = 0; i < n; i++)
                rates[i] = content.Items[i].IsFood ? 0f : meter.ItemPerMinute((ushort)i) * eff;
            float foodRate = meter.FoodPerMinute * eff;
            float knowledgeRate = meter.KnowledgePerMinute * eff;
            var carry = new float[n];
            float food0 = inv.Food, knowledge0 = inv.Knowledge;
            float eggCost = sim.Config.colony.eggFoodCost;

            // Eggs that were waiting have hatched by now (each into its own caste).
            int waiting = colony.Eggs.Count;
            foreach (var egg in colony.Eggs) sim.SpawnAnts(1, egg.Caste);
            colony.Eggs.Clear();
            report.Hatched += waiting;
            int soldiers = sim.Ants.CountCaste(AntCaste.Soldier);

            float eggs = 0f;
            double left = report.CreditedSeconds;
            while (left > 0.0)
            {
                float step = (float)Math.Min(StepSeconds, left);
                left -= step;
                float minutes = step / 60f;

                for (int i = 0; i < n; i++)
                {
                    if (rates[i] == 0f) continue;
                    carry[i] += rates[i] * minutes;
                    int whole = (int)carry[i];
                    if (whole == 0) continue;
                    carry[i] -= whole;
                    ushort item = (ushort)i;
                    if (whole > 0)
                    {
                        int missing = inv.Add(item, whole, colony.QueenCell);
                        int stored = whole - missing;
                        report.Items[i] += stored;
                        report.ItemsLost += missing;
                        colony.Stats.Delivered[i] += stored;
                    }
                    else
                    {
                        int take = Math.Min(-whole, inv.Items[i]);
                        if (take > 0 && inv.Take(item, take)) report.Items[i] -= take;
                    }
                }

                float upkeepPerMinute = colony.UpkeepPerMinute(sim.Ants.Count - soldiers, soldiers) * eff;
                inv.Food = math.clamp(inv.Food + (foodRate - upkeepPerMinute) * minutes, 0f, inv.FoodCapacity);
                if (inv.Food <= 0f) report.Starved = true;
                inv.AddKnowledge(knowledgeRate * minutes, studied: false);

                // The queen lays at the offline efficiency while fed; eggs hatch within the step.
                eggs += step / colony.EggInterval * eff;
                float reserve = upkeepPerMinute * cfg.offlineFoodReserveMinutes;
                while (eggs >= 1f)
                {
                    // Soldiers keep their share of the colony, as while playing.
                    var caste = colony.NextCaste(sim.Ants.Count, soldiers);
                    float cost = caste == AntCaste.Soldier ? sim.Config.combat.soldierEggFoodCost : eggCost;
                    if (inv.Food - cost < reserve) break;
                    inv.Food -= cost;
                    eggs -= 1f;
                    sim.SpawnAnts(1, caste);
                    if (caste == AntCaste.Soldier) soldiers++;
                    report.Hatched++;
                }
                eggs = Math.Min(eggs, 1f); // no saving up while hungry
            }

            colony.Stats.AntsHatched += report.Hatched;
            colony.Starving = inv.Food <= 0f;
            report.Food = inv.Food - food0;
            report.Knowledge = inv.Knowledge - knowledge0;
            sim.Abilities.Tick((float)report.CreditedSeconds);
            sim.RandomEvents.SkipTime((float)report.AwaySeconds);
            inv.AcceptanceChanged = true;
            return report;
        }
    }
}
