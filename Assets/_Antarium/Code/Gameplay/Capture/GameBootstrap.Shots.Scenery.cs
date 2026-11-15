using System;
using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Milestone 11 batch 5 review stills (<c>CaptureMenu.Run("scenery", false)</c>), on a brand-new colony (seed 1337): the
    /// surface background (mountains, hills, trees and clouds) by day, at sunset, at night and in the rain, and from higher up;
    /// then water poured into a shaft (a falling stream into a pool) and down a sloping tunnel. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotScenery()
        {
            FreshColony(1337u);
            Hud(false);
            yield return Frames(10);
            var world = _sim.World;
            int nest = _sim.Colony.QueenCell.x;
            float ground = world.SurfaceY[nest];

            // The background at four times of day / weather, then zoomed out and from higher up.
            (string Name, double Time, bool Rain)[] times =
            {
                ("scenery_day", 0.5, false), ("scenery_sunset", 0.82, false), ("scenery_night", 0.0, false), ("scenery_rain", 0.45, true),
            };
            Cam(new float2(nest + 12.5f, ground + 12f), 3);
            foreach (var t in times)
            {
                _sim.Surface.Clock = Math.Floor(_sim.Surface.Clock) + 1.0 + t.Time;
                if (t.Rain) _sim.StartWeather(WeatherKind.Rain);
                yield return Seconds(t.Rain ? 5f : 0.5f);
                yield return Still(t.Name);
            }
            _sim.StartWeather(WeatherKind.Clear);
            _sim.Surface.Clock = Math.Floor(_sim.Surface.Clock) + 1.0 + 0.5;
            yield return Seconds(5f);
            Cam(new float2(nest + 40.5f, ground + 20f), 2);
            yield return Frames(4);
            yield return Still("scenery_wide");
            Cam(new float2(nest + 40.5f, ground + 34f), 3);
            yield return Frames(4);
            yield return Still("scenery_high");

            // Water: a shaft with a pool under it, and a sloping tunnel running down beside it.
            int x = nest - 26, top = (int)ground - 12;
            _sim.CarveRectNow(new int2(x - 1, top - 14), new int2(x, top));
            _sim.CarveEllipseNow(new float2(x + 0.5f, top - 17f), new float2(6f, 3.5f));
            _sim.CarveLineNow(new float2(x - 3f, top + 0.5f), new float2(x - 15f, top - 10f), 1.1f);
            _sim.RevealNow(new int2(x - 6, top - 8), 22);
            _sim.ClearPilesNow();
            world.MarkAll(ChunkFlags.TerrainUpload);
            Cam(new float2(x - 3f, top - 8f), 4);
            for (int i = 0; i < 360; i++)
            {
                _sim.Fluids.AddWater(new int2(x, top), 22);
                _sim.Fluids.AddWater(new int2(x - 3, top), 14);
                yield return null;
                if (i == 120) yield return Still("water_falling_early");
            }
            yield return Still("water_falling");
            yield return Frames(3);
            yield return Still("water_falling_b");
        }
    }
}
