using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The light glow ants carry (10.4, GDD 3.2). Only the glow ants nearest the middle of the view get a real
    /// <see cref="Light2D"/> (pooled, at most <see cref="GlowConfig.maxLights"/>); every glow ant also draws its unlit halo
    /// sprite (<see cref="Presentation.ColonyView"/>), so the ones without a light still read as lanterns. Rock blocks
    /// the light: around the active lights, solid cells next to open ones are merged into horizontal runs, and each run
    /// becomes a pooled <see cref="ShadowCaster2D"/> rectangle (the caster's default unit square, sized by its transform).
    /// </summary>
    public sealed class GlowLights : IDisposable
    {
        readonly GlowConfig _config;
        readonly ContentDB _content;
        readonly Transform _root;
        readonly List<Light2D> _lights = new();
        readonly List<ShadowCaster2D> _casters = new();
        /// <summary>Per caste: light radius in cells (0 = none).</summary>
        readonly float[] _radius = new float[Castes.Count];
        readonly bool _anyGlow;

        int[] _candidates = new int[64];
        float[] _distances = new float[64];
        readonly List<int4> _boxes = new();
        readonly HashSet<int> _seen = new();
        float _casterTimer;
        int _activeLights, _activeCasters;

        /// <summary>Glow ants with a real light right now (debug / captures).</summary>
        public int ActiveLights => _activeLights;
        public int ActiveCasters => _activeCasters;

        public GlowLights(GlowConfig config, ContentDB content, Transform parent)
        {
            _config = config;
            _content = content;
            _root = new GameObject("GlowLights").transform;
            _root.SetParent(parent, false);
            for (int c = 0; c < Castes.Count; c++)
            {
                _radius[c] = math.max(0f, content.CasteDefs[c].lightRadius);
                _anyGlow |= _radius[c] > 0f;
            }
        }

        /// <summary>Places the lights on the glow ants nearest the view centre; refreshes the shadow casters now and then.</summary>
        public void Update(Simulation sim, float alpha, float2 viewCenter, float2 halfView, float dt, bool terrainChanged)
        {
            if (!_anyGlow || _config.maxLights <= 0)
            {
                SetActiveLights(0);
                SetActiveCasters(0);
                return;
            }
            var ants = sim.Ants.Ants;
            int n = 0;
            float maxRadius = 0f;
            for (int i = 0; i < ants.Length; i++)
            {
                var a = ants[i];
                float r = _radius[(int)a.Caste];
                if (r <= 0f || a.IsDead) continue;
                float2 p = math.lerp(a.PrevPos, a.Pos, alpha) * WorldConstants.CellSize;
                float2 d = math.abs(p - viewCenter) - halfView;
                float margin = r * WorldConstants.CellSize;
                if (d.x > margin || d.y > margin) continue; // its light can't reach the view
                if (n == _candidates.Length)
                {
                    Array.Resize(ref _candidates, n * 2);
                    Array.Resize(ref _distances, n * 2);
                }
                _candidates[n] = i;
                _distances[n] = math.lengthsq(p - viewCenter);
                n++;
                maxRadius = math.max(maxRadius, r);
            }
            if (n > _config.maxLights) Array.Sort(_distances, _candidates, 0, n);
            int count = math.min(n, _config.maxLights);
            SetActiveLights(count);

            _boxes.Clear();
            for (int k = 0; k < count; k++)
            {
                var a = ants[_candidates[k]];
                float r = _radius[(int)a.Caste];
                float2 p = math.lerp(a.PrevPos, a.Pos, alpha) * WorldConstants.CellSize;
                var light = _lights[k];
                light.transform.position = new Vector3(p.x, p.y, 0f);
                float outer = r * WorldConstants.CellSize;
                if (!Mathf.Approximately(light.pointLightOuterRadius, outer))
                {
                    light.pointLightOuterRadius = outer;
                    light.pointLightInnerRadius = outer * _config.innerShare;
                }
                light.color = _config.color;
                light.intensity = _config.intensity;
                light.shadowsEnabled = _config.shadows;
                light.shadowIntensity = _config.shadowIntensity;
                int2 c = (int2)math.floor(math.lerp(a.PrevPos, a.Pos, alpha));
                int reach = (int)math.ceil(r) + 2; // a little past the light: the casters stay valid until the next refresh
                _boxes.Add(new int4(c - reach, c + reach));
            }

            _casterTimer -= dt;
            if (!_config.shadows || count == 0)
            {
                SetActiveCasters(0);
                return;
            }
            if (_casterTimer > 0f && !terrainChanged) return;
            _casterTimer = _config.casterRefresh;
            BuildCasters(sim.World);
        }

        /// <summary>Solid cells next to open ones inside the light boxes, merged into horizontal runs: one caster per run.</summary>
        void BuildCasters(WorldGrid world)
        {
            _seen.Clear();
            int used = 0;
            foreach (var box in _boxes)
            {
                int x0 = math.max(0, box.x), y0 = math.max(0, box.y);
                int x1 = math.min(world.Width - 1, box.z), y1 = math.min(world.Height - 1, box.w);
                for (int y = y0; y <= y1 && used < _config.maxCasters; y++)
                {
                    int x = x0;
                    while (x <= x1 && used < _config.maxCasters)
                    {
                        if (!IsEdge(world, x, y) || !_seen.Add(world.Index(x, y)))
                        {
                            x++;
                            continue;
                        }
                        int start = x++;
                        while (x <= x1 && IsEdge(world, x, y) && _seen.Add(world.Index(x, y))) x++;
                        PlaceCaster(used++, start, y, x - start);
                    }
                }
            }
            SetActiveCasters(used);
        }

        /// <summary>A solid cell that touches an open one (only those can throw a shadow into a tunnel).</summary>
        static bool IsEdge(WorldGrid world, int x, int y)
        {
            if (world[x, y].IsOpen) return false;
            return Open(world, x + 1, y) || Open(world, x - 1, y) || Open(world, x, y + 1) || Open(world, x, y - 1);
        }

        static bool Open(WorldGrid world, int x, int y) => world.InBounds(x, y) && world[x, y].IsOpen;

        void PlaceCaster(int index, int x, int y, int length)
        {
            while (_casters.Count <= index) _casters.Add(CreateCaster());
            var t = _casters[index].transform;
            float cell = WorldConstants.CellSize;
            t.position = new Vector3((x + length * 0.5f) * cell, (y + 0.5f) * cell, 0f);
            t.localScale = new Vector3(length * cell, cell, 1f);
        }

        ShadowCaster2D CreateCaster()
        {
            // Added at the origin with unit scale, its default shape is the unit square around its pivot; the
            // transform then sizes it, so the shape never has to be edited.
            var go = new GameObject("RockShadow");
            go.transform.SetParent(_root, false);
            var caster = go.AddComponent<ShadowCaster2D>();
            caster.castingOption = ShadowCaster2D.ShadowCastingOptions.CastShadow;
            go.SetActive(false);
            return caster;
        }

        Light2D CreateLight()
        {
            var go = new GameObject("GlowLight");
            go.transform.SetParent(_root, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = _config.color;
            light.intensity = _config.intensity;
            light.falloffIntensity = 0.6f;
            go.SetActive(false);
            return light;
        }

        void SetActiveLights(int count)
        {
            while (_lights.Count < count) _lights.Add(CreateLight());
            for (int i = 0; i < _lights.Count; i++)
            {
                bool on = i < count;
                if (_lights[i].gameObject.activeSelf != on) _lights[i].gameObject.SetActive(on);
            }
            _activeLights = count;
        }

        void SetActiveCasters(int count)
        {
            for (int i = 0; i < _casters.Count; i++)
            {
                bool on = i < count;
                if (_casters[i].gameObject.activeSelf != on) _casters[i].gameObject.SetActive(on);
            }
            _activeCasters = count;
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
