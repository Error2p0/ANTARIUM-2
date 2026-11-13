using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Milestone 11 batch 3: the Vein Instinct switch, shooting stars caught by the player, buildings switched on and off,
    /// and the giant worm event (debug trigger).
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Lets Vein Instinct order ore next to dug-out cells (<paramref name="on"/>) or stops it (Mine tool row).</summary>
        public void EnqueueAutoMark(bool on) => _pending.Add(new Command(CommandKind.AutoMark, 0, default, default, 0, set: on));

        /// <summary>The player clicked a shooting star at <paramref name="cellPos"/> (cell space; the stars are drawn by the view).</summary>
        public void EnqueueShootingStar(float2 cellPos) => _pending.Add(new Command(CommandKind.StarCaught, cellPos, 0f, 0f, true));

        /// <summary>Switches the building covering <paramref name="cell"/> on or off (configure panel).</summary>
        public void EnqueueSwitch(int2 cell, bool on) => _pending.Add(new Command(CommandKind.Switch, 0, cell, default, 0, set: on));

        /// <summary>Debug: a giant worm starts burrowing near the tunnels now.</summary>
        public void EnqueueTriggerWorm() => _pending.Add(new Command(CommandKind.TriggerWorm, 0, default, default, 0));

        void CatchShootingStar(float2 cellPos)
        {
            var stats = Colony.Stats;
            stats.StarsCaught++;
            Events.Publish(new ShootingStarCaughtEvent(cellPos, stats.StarsCaught));
        }
    }
}
