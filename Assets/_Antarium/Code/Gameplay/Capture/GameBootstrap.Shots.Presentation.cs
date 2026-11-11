using System.Collections;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// 10.7 review stills (<c>CaptureMenu.Run("presentation", false)</c>): the light overlay by the nest and at a deep outpost,
    /// the ore overlay with its legend, and the flow overlay with its legend. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotPresentation()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;

            Cam(q + new float2(18f, 4f), 3);
            SetOverlay(OverlayMode.Light);
            yield return Seconds(1f);
            yield return Still("presentation_light");
            var deep = _show.Outposts[math.min(2, _show.Outposts.Length - 1)];
            Cam((float2)deep + new float2(0f, 4f), 3);
            yield return Seconds(1f);
            yield return Still("presentation_light_deep");

            // The same view without and with the ore overlay, then deeper where the rarer finds are.
            SetOverlay(OverlayMode.None);
            Cam(q + new float2(10f, -30f), 2);
            yield return Seconds(1f);
            yield return Still("presentation_ores_plain");
            SetOverlay(OverlayMode.Ores);
            yield return Seconds(1.5f);
            yield return Still("presentation_ores");
            Cam((float2)deep + new float2(0f, 4f), 2);
            yield return Seconds(1f);
            yield return Still("presentation_ores_deep");

            SetOverlay(OverlayMode.Flow);
            Cam(q + new float2(18f, 4f), 3);
            yield return Seconds(1f);
            yield return Still("presentation_flow");
            SetOverlay(OverlayMode.None);
        }
    }
}
