using Antarium.Content;

namespace Antarium.Sim
{
    /// <summary>Measures achievement progress from a colony's lifetime stats and its legacy.</summary>
    public static class AchievementProgress
    {
        public static long Measure(Simulation sim, AchievementData data)
        {
            var stats = sim.Colony.Stats;
            var legacy = sim.Legacy;
            switch (data.Def.kind)
            {
                case AchievementKind.ReachDepth: return stats.MaxDepth;
                case AchievementKind.DigCells: return stats.CellsDug;
                case AchievementKind.HatchAnts: return stats.AntsHatched;
                case AchievementKind.HatchCaste: return stats.HatchedCaste[(int)data.Def.caste];
                case AchievementKind.AntsAlive: return sim.Ants.Count;
                case AchievementKind.Build: return stats.Built[data.Building];
                case AchievementKind.Deliver: return stats.Delivered[data.Item];
                case AchievementKind.Craft: return stats.Crafted[data.Item];
                case AchievementKind.KillEnemies: return stats.EnemiesKilled;
                case AchievementKind.RepelRaids: return stats.RaidsRepelled;
                case AchievementKind.Contracts: return stats.ContractsCompleted;
                case AchievementKind.Research: return sim.Research.DoneCount;
                case AchievementKind.KnowledgeEarned: return (long)stats.KnowledgeEarned;
                case AchievementKind.PrincessesRaised: return stats.PrincessesRaised;
                case AchievementKind.Flights: return legacy.Flights;
                case AchievementKind.EventsWon: return stats.EventsWon;
                case AchievementKind.TraitLevels:
                {
                    long levels = 0;
                    foreach (int l in legacy.Levels) levels += l;
                    return levels;
                }
                case AchievementKind.MemoryEarned: return legacy.MemoryEarned;
                default: return 0;
            }
        }

        public static bool IsReached(Simulation sim, AchievementData data) => Measure(sim, data) >= data.Def.amount;
    }
}
