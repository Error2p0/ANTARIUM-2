using System.Collections;
using Antarium.Content;
using Antarium.Sim;
using Unity.Collections;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Milestone 11 batch 6 review stills (<c>CaptureMenu.Run("trees", false)</c>), on a brand-new colony (seed 62, which has every
    /// surface structure): the hills around the nest, an apple tree with apples under it, an oak and a pine, every structure,
    /// a tree's big roots (revealed for the shot), and the Elder Tree's cavern (revealed) with a golden apple and its hover text.
    /// Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotTrees()
        {
            FreshColony(62u);
            Hud(true);
            yield return Frames(10);
            var world = _sim.World;
            int nest = _sim.Colony.QueenCell.x;
            float Ground(int x) => world.SurfaceY[math.clamp(x, 0, world.Width - 1)] + 1f;

            Cam(new float2(nest + 0.5f, Ground(nest) + 6f), 1);
            yield return Frames(20);
            yield return Still("trees_hills_wide");
            Cam(new float2(nest + 0.5f, Ground(nest) + 4f), 2);
            yield return Frames(10);
            yield return Still("trees_hills");

            // The nearest tree of each kind; apples under the apple tree.
            foreach (byte kind in new[] { SurfacePlants.AppleTree, SurfacePlants.Oak, SurfacePlants.Pine })
            {
                int tree = -1;
                foreach (var p in _sim.Surface.Plants)
                    if (p.Kind == kind && (tree < 0 || math.abs(p.X - nest) < math.abs(tree - nest))) tree = p.X;
                if (tree < 0) continue;
                if (kind == SurfacePlants.AppleTree && _content.TreeForage.Length > 0)
                    foreach (int dx in new[] { -2, 1, 3 })
                        _sim.Colony.Piles.Add(new int2(tree + dx, world.SurfaceY[tree + dx] + 1), _content.TreeForage[0], 1);
                Cam(new float2(tree + 0.5f, Ground(tree) + 4f), 4);
                yield return Frames(10);
                yield return Still(kind == SurfacePlants.AppleTree ? "trees_apple" : kind == SurfacePlants.Oak ? "trees_oak" : "trees_pine");
                if (kind != SurfacePlants.AppleTree) continue;
                // Its big roots, revealed for the shot.
                for (int y = world.SurfaceY[tree] - 70; y <= world.SurfaceY[tree]; y += 6)
                    for (int x = tree - 30; x <= tree + 30; x += 6)
                        Reveal.Around(world, new int2(x, y), 5);
                world.MarkAll(ChunkFlags.TerrainUpload);
                Cam(new float2(tree + 0.5f, Ground(tree) - 22f), 2);
                yield return Frames(20);
                yield return Still("trees_roots");
            }

            // Every structure, nearest first.
            foreach (var f in _sim.Surface.Features)
            {
                Cam(new float2(f.Mid + 0.5f, Ground(f.Mid) - (f.Kind == SurfaceFeatureKind.Anthill ? 3f : 0f)), 4);
                yield return Frames(10);
                yield return Still("trees_" + f.Kind.ToString().ToLowerInvariant() + "_" + f.X0);
            }

            // The Elder Tree: its cavern revealed (as when the colony breaks in), then a golden apple and its hover text.
            if (!RevealElderTree(out var site)) yield break;
            Cam((float2)site.Center + new float2(0.5f, 2f), 2);
            yield return Seconds(2f);
            yield return Still("trees_elder");
            Cam((float2)site.Center + new float2(0.5f, 2f), 1);
            yield return Frames(10);
            yield return Still("trees_elder_wide");
            foreach (var g in _sim.Wild.Growths)
            {
                if (g.Item != _content.ElderFruit || !g.Grown) continue;
                Cam((float2)g.Cell + 0.5f, 5);
                Pointer((float2)g.Cell + 0.5f, true);
                yield return Frames(10);
                yield return Still("trees_elder_fruit");
                Pointer(new float2(-50f, -50f), false);
                break;
            }
        }
    }
}
