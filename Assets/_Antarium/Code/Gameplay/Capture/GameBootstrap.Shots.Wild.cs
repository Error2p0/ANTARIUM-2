using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Milestone 11 batch 4 review stills (<c>CaptureMenu.Run("wild", false)</c>), on a brand-new colony (seed 1337): a lake and a
    /// river on the surface, a berry bush with berries and a toadstool under it, a cave of glowing mushrooms (and its hover
    /// text), a cave of plain mushrooms, and the underground light off and on. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotWild()
        {
            FreshColony(1337u);
            Hud(true);
            yield return Frames(10);
            var world = _sim.World;
            int nest = _sim.Colony.QueenCell.x;

            // A lake and a river: the nearest of each.
            foreach (bool river in new[] { false, true })
            {
                int best = -1, dist = int.MaxValue;
                for (int i = 0; i < _sim.Surface.Water.Count; i++)
                {
                    var b = _sim.Surface.Water[i];
                    int d = math.abs((b.X0 + b.X1) / 2 - nest);
                    if (b.River == river && d < dist) { dist = d; best = i; }
                }
                if (best < 0) continue;
                var body = _sim.Surface.Water[best];
                int mid = (body.X0 + body.X1) / 2;
                Cam(new float2(mid + 0.5f, world.SurfaceY[body.X0 - 1] + 3f), 3);
                yield return Frames(20);
                yield return Still(river ? "wild_river" : "wild_lake");
            }

            // The berry bush nearest the nest with berries under it, and a toadstool beside it.
            int bush = -1;
            foreach (var p in _sim.Surface.Plants)
                if (p.Kind == SurfacePlants.BerryBush && (bush < 0 || math.abs(p.X - nest) < math.abs(bush - nest))) bush = p.X;
            if (bush >= 0 && _content.BushForage.Length > 0)
            {
                foreach (int dx in new[] { -1, 1 })
                    _sim.Colony.Piles.Add(new int2(bush + dx, world.SurfaceY[bush + dx] + 1), _content.BushForage[0], 1);
                if (_content.SurfaceMushrooms.Length > 0)
                    _sim.Colony.Piles.Add(new int2(bush + 4, world.SurfaceY[bush + 4] + 1), _content.SurfaceMushrooms[0], 1);
                Cam(new float2(bush + 1.5f, world.SurfaceY[bush] + 3f), 5);
                yield return Frames(10);
                yield return Still("wild_berries");
            }

            // Caves: the glowing mushrooms nearest the nest, then plain ones (revealed for the shot).
            foreach (bool glow in new[] { true, false })
            {
                int best = -1;
                float dist = float.MaxValue;
                var growths = _sim.Wild.Growths;
                for (int i = 0; i < growths.Count; i++)
                {
                    bool lit = math.cmax(_content.GrowthLight[growths[i].Item]) > 0f;
                    float d = math.distance(new float2(growths[i].Cell), Queen);
                    if (lit == glow && d < dist) { dist = d; best = i; }
                }
                if (best < 0) continue;
                var cell = growths[best].Cell;
                Reveal.Around(world, cell, 16);
                world.MarkAll(ChunkFlags.TerrainUpload);
                Cam((float2)cell + 0.5f, 3);
                yield return Frames(20);
                yield return Still(glow ? "wild_glow_cave" : "wild_mushroom_cave");
                if (glow)
                {
                    Pointer((float2)cell + 0.5f, true);
                    yield return Frames(6);
                    yield return Still("wild_glow_hover");
                    Pointer(new float2(-50f, -50f), false); // off the world: no hover text in the next stills
                    continue;
                }
                // The underground light: off (the plain ambient), then on.
                _ambientOverride = config.render.ambient;
                yield return Frames(4);
                yield return Still("wild_underground_off");
                _ambientOverride = -1f;
                yield return Seconds(2f);
                yield return Still("wild_underground_on");
            }
        }
    }
}
