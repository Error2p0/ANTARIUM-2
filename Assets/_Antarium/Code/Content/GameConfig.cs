using System;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>Tuning values for systems. Content (materials, biomes...) lives in <see cref="ContentDatabase"/>.</summary>
    [CreateAssetMenu(menuName = "Antarium/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        public SimConfig sim = new();
        public DigConfig dig = new();
        public LightConfig light = new();
        public RenderConfig render = new();
        public CameraConfig camera = new();
        public FeedbackConfig feedback = new();
    }

    [Serializable]
    public sealed class SimConfig
    {
        [Min(1)] public int ticksPerSecond = 20;
        [Min(1)] public int maxTicksPerFrame = 5;
    }

    [Serializable]
    public sealed class DigConfig
    {
        [Tooltip("Hardness units removed from a cell per hit at the brush centre.")]
        [Min(0f)] public float power = 0.6f;
        [Min(0.5f)] public float brushRadius = 1.6f;
        [Tooltip("Damage multiplier at the brush edge.")]
        [Range(0f, 1f)] public float brushEdgeFactor = 0.35f;
        [Min(0.5f)] public float hitsPerSecond = 10f;

        [Header("Combo")]
        [Min(0.05f)] public float comboWindow = 0.45f;
        [Min(0f)] public float comboPerHit = 0.04f;
        [Min(1f)] public float maxCombo = 2.5f;

        [Header("Reveal")]
        [Min(1)] public int revealRadius = 4;
        [Tooltip("Max cells of a newly breached cave revealed at once.")]
        [Min(1)] public int caveRevealLimit = 20000;
    }

    [Serializable]
    public sealed class LightConfig
    {
        [Range(8, 255)] public int maxLevel = 64;
        [Tooltip("Light lost per open cell. Also sets the max reach: maxLevel / openCost cells.")]
        [Min(1)] public int openCost = 2;
        [Min(1)] public int solidCost = 8;
        [ColorUsage(false)] public Color skyLight = Color.white;
    }

    [Serializable]
    public sealed class RenderConfig
    {
        [Tooltip("Number of brightness steps the grid light is quantised into.")]
        [Range(2, 32)] public int lightBands = 7;
        [Tooltip("Per-pixel jitter on terrain edges. Higher = crumblier.")]
        [Range(0f, 0.5f)] public float edgeNoise = 0.16f;
        [Range(0f, 1f)] public float wallBrightness = 0.42f;
        [Range(0f, 1f)] public float wallOcclusion = 0.55f;
        [Tooltip("Intensity of the global Light2D: the minimum brightness of revealed, unlit areas.")]
        [Range(0f, 1f)] public float ambient = 0.025f;
        public Color skyTop = new(0.36f, 0.62f, 0.86f);
        public Color skyHorizon = new(0.72f, 0.86f, 0.93f);
    }

    [Serializable]
    public sealed class CameraConfig
    {
        [Min(1)] public int minZoom = 1;
        [Min(1)] public int maxZoom = 8;
        [Min(1)] public int defaultZoom = 3;
        [Tooltip("Keyboard pan speed in world units per second at zoom 1.")]
        [Min(1f)] public float panSpeed = 60f;
        [Tooltip("Max shake offset in art pixels at full trauma.")]
        [Min(0f)] public float maxShakePixels = 3f;
        [Min(0f)] public float shakeDecay = 2.5f;
    }

    [Serializable]
    public sealed class FeedbackConfig
    {
        [Min(0)] public int particlesPerDamage = 1;
        [Min(0)] public int particlesPerBreak = 6;
        [Min(0f)] public float particleSpeed = 4f;
        [Min(0f)] public float particleLifetime = 0.6f;
        [Min(0f)] public float shakePerBreak = 0.06f;
        [Min(0f)] public float maxShakePerHit = 0.25f;
    }
}
