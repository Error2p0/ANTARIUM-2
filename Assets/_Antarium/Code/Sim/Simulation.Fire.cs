using Antarium.Content;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Fire (10.3): lava and meteorites light flammable cells, burning cells spread, smoke and burn out into ash (which
    /// falls like sand when undermined). Ants next to a fire are burned in the ant job; see <see cref="FireSystem"/>.
    /// </summary>
    public sealed partial class Simulation
    {
        /// <summary>Lights the flammable cell at <paramref name="c"/> (debug, tests, events). False if it can't burn.</summary>
        public bool Ignite(int2 c)
        {
            bool lit = Fire.Ignite(c, FluidStepSeconds);
            AnnounceFires();
            return lit;
        }

        /// <summary>A meteorite hit: flammable cells (roots) around the crater catch fire.</summary>
        internal int IgniteAround(float2 center, float radius)
        {
            int lit = Fire.IgniteAround(center, radius, FluidStepSeconds);
            AnnounceFires();
            return lit;
        }

        float FluidStepSeconds => TickDuration * math.max(1, Config.fluids.ticksPerStep);

        /// <summary>One fire step with the fluid step: lava's ignitions, spreading, smoke, water, and ash.</summary>
        void StepFire(float stepSeconds)
        {
            var ignitions = Fluids.Ignitions;
            for (int i = 0; i < ignitions.Length; i++) Fire.Ignite(ignitions[i], stepSeconds);
            Fire.Step(Fluids, stepSeconds);
            AnnounceFires();
            foreach (var c in Fire.Doused)
                Events.Publish(new FireEvent(FireEventKind.Doused, c, World[c.x, c.y].Material));
            foreach (var c in Fire.BurntOut) BurnOut(c);
        }

        void AnnounceFires()
        {
            foreach (var c in Fire.Started)
                Events.Publish(new FireEvent(FireEventKind.Started, c, World[c.x, c.y].Material));
            Fire.ClearStarted();
        }

        /// <summary>A burnt-out cell is ash now (the fire system turned it); undermined ash falls like sand.</summary>
        void BurnOut(int2 c) => AddUnstable(c);
    }
}
