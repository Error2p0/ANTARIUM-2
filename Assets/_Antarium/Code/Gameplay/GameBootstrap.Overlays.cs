using Antarium.Presentation;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>What is drawn over the world: nothing, the flow overlay, the light overlay or the ore overlay (10.7). One at a time.</summary>
    public enum OverlayMode { None, Flow, Light, Ores }

    public sealed partial class GameBootstrap
    {
        OverlayMode _overlayMode;
        OreSurvey _oreSurvey;
        float _surveyTimer;

        /// <summary>The flow overlay is on (belts, machines and idle ants are tinted by the building and colony views).</summary>
        bool _overlay => _overlayMode == OverlayMode.Flow;

        /// <summary>Turns <paramref name="mode"/> on, or off if it is already on.</summary>
        void ToggleOverlay(OverlayMode mode) => SetOverlay(_overlayMode == mode ? OverlayMode.None : mode);

        void ToggleOverlay() => ToggleOverlay(OverlayMode.Flow);

        /// <summary>The toolbar's overlay button: off, flow, light, ores, off.</summary>
        void CycleOverlay() => SetOverlay(_overlayMode == OverlayMode.Ores ? OverlayMode.None : _overlayMode + 1);

        void SetOverlay(OverlayMode mode)
        {
            _overlayMode = mode;
            _terrain?.SetOverlay(mode switch
            {
                OverlayMode.Light => TerrainView.Overlay.Light,
                OverlayMode.Ores => TerrainView.Overlay.Ores,
                _ => TerrainView.Overlay.None,
            });
            _surveyTimer = 0f;
            UpdateOverlay(0f);
        }

        /// <summary>Keys for the overlays, and the legend (the ore counts are refreshed about once a second).</summary>
        void UpdateOverlay(float dt)
        {
            if (_sim == null || _hud == null) return;
            if (_overlayMode == OverlayMode.Ores)
            {
                _oreSurvey ??= new OreSurvey(_content);
                _surveyTimer -= dt;
                if (_surveyTimer <= 0f)
                {
                    _surveyTimer = config.render.oreSurveySeconds;
                    _oreSurvey.Count(_sim.World);
                }
            }
            _hud.SetOverlay(_overlayMode, _oreSurvey);
        }

        void OverlayKeys()
        {
            if (_input.Overlay.WasPressedThisFrame()) ToggleOverlay(OverlayMode.Flow);
            if (_input.LightOverlay.WasPressedThisFrame()) ToggleOverlay(OverlayMode.Light);
            if (_input.OreOverlay.WasPressedThisFrame()) ToggleOverlay(OverlayMode.Ores);
        }
    }
}
