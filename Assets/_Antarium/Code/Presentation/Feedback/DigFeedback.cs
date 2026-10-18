using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>Material-coloured debris particles and screen-shake impulses from dig events.</summary>
    public sealed class DigFeedback : IDisposable
    {
        readonly FeedbackConfig _config;
        readonly WorldGrid _world;
        readonly LightSystem _light;
        readonly float _ambient;
        readonly Color[][] _palettes;
        readonly ParticleSystem _particles;
        Unity.Mathematics.Random _rng = new(0xD16u);
        int _budget;

        public DigFeedback(ContentDB content, FeedbackConfig config, float ambient, WorldGrid world, LightSystem light,
            Material particleMaterial, Transform parent)
        {
            _config = config;
            _world = world;
            _light = light;
            _ambient = ambient;

            _palettes = new Color[content.Materials.Length][];
            for (int i = 0; i < _palettes.Length; i++)
            {
                var m = content.Materials[i];
                _palettes[i] = new[] { m.baseColor, m.baseColor, m.shadowColor, m.highlightColor, m.accentColor };
            }

            var go = new GameObject("DigDebris");
            go.transform.SetParent(parent, false);
            _particles = go.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.3f;
            main.maxParticles = 6000;
            main.startSpeed = 0f;
            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 50;
            renderer.maxParticleSize = 10f;
            _particles.Play();
        }

        /// <summary>Spawns debris for this frame's dig events. Returns camera trauma to add.</summary>
        public float Consume(EventBus events)
        {
            _budget = _config.maxParticlesPerFrame;
            // Breaks first: they matter most for feel when the budget runs short.
            foreach (var e in events.Read<CellDamagedEvent>())
                if (e.Broken) Emit(e);
            foreach (var e in events.Read<CellDamagedEvent>())
                if (!e.Broken) Emit(e);

            float trauma = 0f;
            foreach (var e in events.Read<DigResolvedEvent>())
                trauma += math.min(_config.maxShakePerHit, e.CellsBroken * _config.shakePerBreak);
            return trauma;
        }

        void Emit(in CellDamagedEvent e)
        {
            int count = math.min(e.Broken ? _config.particlesPerBreak : _config.particlesPerDamage, _budget);
            if (count <= 0) return;
            _budget -= count;

            float3 lit = math.saturate(_light.Sample(_world, e.Cell.x, e.Cell.y) + _ambient);
            var palette = _palettes[e.Material];
            float2 center = WorldGrid.CellCenterToWorld(e.Cell);
            float pixel = 1f / WorldConstants.PixelsPerUnit;

            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Color c = palette[_rng.NextInt(palette.Length)];
                p.startColor = new Color(c.r * lit.x, c.g * lit.y, c.b * lit.z, 1f);
                p.position = new Vector3(center.x + _rng.NextFloat(-0.25f, 0.25f), center.y + _rng.NextFloat(-0.25f, 0.25f), 0f);
                float2 dir = math.normalize(new float2(_rng.NextFloat(-1f, 1f), _rng.NextFloat(-0.2f, 1f)) + 1e-4f);
                float2 v = dir * _config.particleSpeed * _rng.NextFloat(0.4f, 1f);
                p.velocity = new Vector3(v.x, v.y, 0f);
                p.startSize = _rng.NextFloat() < 0.7f ? pixel : pixel * 2f;
                p.startLifetime = _config.particleLifetime * _rng.NextFloat(0.6f, 1.2f);
                _particles.Emit(p, 1);
            }
        }

        public void Dispose()
        {
            if (_particles != null) UnityEngine.Object.Destroy(_particles.gameObject);
        }
    }
}
