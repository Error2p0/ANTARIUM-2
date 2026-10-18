using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Sim
{
    /// <summary>
    /// Owns the simulation state and runs it on a fixed tick. Input arrives as commands that are applied
    /// at the start of the next tick; results leave as events on <see cref="Events"/>.
    /// </summary>
    public sealed class Simulation : IDisposable
    {
        public readonly ContentDB Content;
        public readonly GameConfig Config;
        public readonly uint Seed;
        public readonly WorldGrid World;
        public readonly LightSystem Light;
        public readonly EventBus Events = new();
        public readonly SimClock Clock;

        readonly List<DigCommand> _pendingDigs = new();
        NativeList<int2> _broken;
        NativeList<int2> _breachStarts;

        public Simulation(ContentDB content, GameConfig config, uint seed)
        {
            Content = content;
            Config = config;
            Seed = seed;
            Clock = new SimClock(config.sim.ticksPerSecond, config.sim.maxTicksPerFrame);

            World = WorldGenerator.Create(content, seed);
            Reveal.Initial(World, content.MaterialSim, config.dig.revealRadius);
            Light = new LightSystem(World, config.light);
            Light.RecomputeAll(World, content.MaterialSim);

            _broken = new NativeList<int2>(64, Allocator.Persistent);
            _breachStarts = new NativeList<int2>(16, Allocator.Persistent);
        }

        public void Enqueue(in DigCommand command) => _pendingDigs.Add(command);

        /// <summary>Advances the clock and runs the due ticks. Returns the number of ticks run.</summary>
        public int Advance(double deltaTime)
        {
            int ticks = Clock.Advance(deltaTime);
            for (int i = 0; i < ticks; i++)
                Tick();
            return ticks;
        }

        public void Tick()
        {
            for (int i = 0; i < _pendingDigs.Count; i++)
                ResolveDig(_pendingDigs[i]);
            _pendingDigs.Clear();
        }

        /// <summary>Refreshes derived data (light) after ticks. Call once per frame.</summary>
        public void UpdateDerived()
        {
            Light.UpdateDirty(World, Content.MaterialSim);
        }

        void ResolveDig(in DigCommand cmd)
        {
            var dig = Config.dig;
            _broken.Clear();
            _breachStarts.Clear();

            int damaged = Digging.Apply(World, Content.MaterialSim, cmd, dig.brushEdgeFactor, Events, _broken);

            for (int i = 0; i < _broken.Length; i++)
            {
                int2 c = _broken[i];
                Reveal.Around(World, c, dig.revealRadius);
                AddBreach(c + new int2(1, 0));
                AddBreach(c + new int2(-1, 0));
                AddBreach(c + new int2(0, 1));
                AddBreach(c + new int2(0, -1));
            }

            int discovered = 0;
            if (_breachStarts.Length > 0)
                discovered = Reveal.Flood(World, _breachStarts.AsArray(), dig.revealRadius, dig.caveRevealLimit);

            if (damaged > 0)
                Events.Publish(new DigResolvedEvent(cmd.CellPos, damaged, _broken.Length, discovered));
        }

        void AddBreach(int2 c)
        {
            if (Reveal.IsUnexploredOpen(World, c.x, c.y))
                _breachStarts.Add(c);
        }

        public void Dispose()
        {
            World.Dispose();
            Light.Dispose();
            if (_broken.IsCreated) _broken.Dispose();
            if (_breachStarts.IsCreated) _breachStarts.Dispose();
        }
    }
}
