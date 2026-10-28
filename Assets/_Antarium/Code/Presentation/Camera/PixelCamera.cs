using Antarium.Content;
using Antarium.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Antarium.Presentation
{
    /// <summary>
    /// Pixel-perfect orthographic camera: zoom is an integer number of screen pixels per art pixel and the
    /// rendered position snaps to the art-pixel grid, so art pixels never shimmer or change size.
    /// </summary>
    public sealed class PixelCamera
    {
        readonly Camera _camera;
        readonly CameraConfig _config;
        float _trauma;
        Unity.Mathematics.Random _rng = new(0xCA3u);

        /// <summary>Unsnapped focus point in world units.</summary>
        public float2 Position;
        public int Zoom { get; private set; }
        /// <summary>Player setting: camera shake strength (0 = off).</summary>
        public float ShakeScale = 1f;

        /// <summary>Zoom steps between the current zoom and the closest one.</summary>
        public int StepsOut => _config.maxZoom - Zoom;

        /// <summary>Half the visible area in world units.</summary>
        public float2 HalfViewSize => new float2(Screen.width, Screen.height) * 0.5f * UnitsPerScreenPixel;

        public PixelCamera(Camera camera, CameraConfig config)
        {
            _camera = camera;
            _config = config;
            _camera.orthographic = true;
            Zoom = math.clamp(config.defaultZoom, config.minZoom, config.maxZoom);
        }

        float UnitsPerScreenPixel => 1f / (WorldConstants.PixelsPerUnit * Zoom);

        /// <summary>Changes zoom by <paramref name="steps"/>, keeping the world point under <paramref name="anchorScreen"/> fixed.</summary>
        public void ZoomBy(int steps, Vector2 anchorScreen)
        {
            int next = math.clamp(Zoom + steps, _config.minZoom, _config.maxZoom);
            if (next == Zoom) return;
            float2 offset = (float2)anchorScreen - new float2(Screen.width, Screen.height) * 0.5f;
            float2 anchorWorld = Position + offset * UnitsPerScreenPixel;
            Zoom = next;
            Position = anchorWorld - offset * UnitsPerScreenPixel;
        }

        public void PanScreenPixels(Vector2 delta) => Position -= (float2)delta * UnitsPerScreenPixel;

        public void PanWorld(float2 delta) => Position += delta;

        public float PanSpeed => _config.panSpeed / Zoom;

        public void AddTrauma(float amount) => _trauma = math.saturate(_trauma + amount * ShakeScale);

        public float2 ScreenToWorld(Vector2 screen)
        {
            var p = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            return new float2(p.x, p.y);
        }

        /// <summary>Clamps to the world, applies shake and writes the camera transform and size.</summary>
        public void Apply(float deltaTime, float2 worldSize)
        {
            float2 half = new float2(Screen.width, Screen.height) * 0.5f * UnitsPerScreenPixel;
            for (int axis = 0; axis < 2; axis++)
            {
                Position[axis] = worldSize[axis] <= half[axis] * 2f
                    ? worldSize[axis] * 0.5f
                    : math.clamp(Position[axis], half[axis], worldSize[axis] - half[axis]);
            }

            _camera.orthographicSize = Screen.height * 0.5f * UnitsPerScreenPixel;

            const float ppu = WorldConstants.PixelsPerUnit;
            float2 snapped = math.round(Position * ppu) / ppu;

            // Shake in whole art pixels; trauma squared feels better than linear.
            _trauma = math.max(0f, _trauma - _config.shakeDecay * deltaTime);
            float shake = _trauma * _trauma * _config.maxShakePixels;
            if (shake > 0f)
                snapped += math.round(_rng.NextFloat2(-shake, shake)) / ppu;

            // With an odd screen size the centre falls on a pixel centre; shift half a screen pixel.
            if ((Screen.width & 1) == 1) snapped.x += 0.5f * UnitsPerScreenPixel;
            if ((Screen.height & 1) == 1) snapped.y += 0.5f * UnitsPerScreenPixel;

            _camera.transform.position = new Vector3(snapped.x, snapped.y, -10f);
        }
    }
}
