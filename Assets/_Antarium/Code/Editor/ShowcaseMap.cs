using System.IO;
using Antarium.Content;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Antarium.Editor
{
    /// <summary>
    /// Antarium → Capture → Write Showcase Map: the whole showcase world as one image (one pixel per cell; solid
    /// cells in their material colour, dug cells dark, fluids blue / orange / green, ants white, enemies red, the
    /// queen magenta). For planning the showcase colony and the capture shots. Written to <c>Captures/</c>.
    /// </summary>
    public static class ShowcaseMap
    {
        [MenuItem("Antarium/Capture/Write Showcase Map")]
        static void WriteBuilt() => Write(built: true);

        [MenuItem("Antarium/Capture/Write Showcase Seed Map (untouched world)")]
        static void WriteRaw() => Write(built: false);

        public static string Write(bool built)
        {
            var database = AssetDatabase.LoadAssetAtPath<ContentDatabase>("Assets/_Antarium/Content/ContentDatabase.asset");
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>("Assets/_Antarium/Content/GameConfig.asset");
            var content = ContentDB.Build(database);
            var sim = built ? ShowcaseColony.Build(content, config) : new Simulation(content, config, ShowcaseColony.Seed, new Legacy(content));
            var world = sim.World;
            var tex = new Texture2D(world.Width, world.Height, TextureFormat.RGBA32, false);
            var pixels = new Color32[world.Width * world.Height];
            for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                int i = world.Index(x, y);
                var cell = world.Cells[i];
                var fluid = world.Fluid[i];
                Color c;
                if (cell.IsOpen)
                {
                    c = content.Materials[cell.Material].IsAir ? new Color(0.55f, 0.7f, 0.85f) : new Color(0.08f, 0.06f, 0.05f);
                    if (fluid.Level > 40)
                        c = fluid.Kind == FluidKind.Water ? new Color(0.15f, 0.35f, 0.9f)
                            : fluid.Kind == FluidKind.Lava ? new Color(1f, 0.45f, 0.05f)
                            : new Color(0.4f, 0.75f, 0.2f);
                }
                else
                {
                    var m = content.Materials[cell.Material];
                    c = m.baseColor;
                    if (m.emissionStrength > 0f || m.selfGlow > 0f) c = Color.Lerp(c, m.emissionColor, 0.6f) * 1.3f;
                    if (!cell.IsRevealed) c *= 0.55f;
                }
                pixels[y * world.Width + x] = c;
            }
            foreach (var a in sim.Ants.Ants) Plot(pixels, world.Width, world.Height, a.Cell, Color.white);
            foreach (var e in sim.Enemies.Enemies) Plot(pixels, world.Width, world.Height, e.Cell, Color.red);
            var q = sim.Colony.QueenCell;
            for (int d = -2; d <= 2; d++)
            {
                Plot(pixels, world.Width, world.Height, q + new int2(d, 0), Color.magenta);
                Plot(pixels, world.Width, world.Height, q + new int2(0, d), Color.magenta);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            Directory.CreateDirectory(CaptureMenu.OutputDirectory);
            string path = Path.Combine(CaptureMenu.OutputDirectory, built ? "showcase_map.png" : "showcase_seed_map.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            foreach (var w in ShowcaseColony.Warnings) Debug.LogWarning("Showcase: " + w);
            File.WriteAllText(Path.Combine(CaptureMenu.OutputDirectory, "showcase_log.txt"),
                $"queen {q} surface {world.SurfaceY[q.x]} shaft x {ShowcaseColony.Last.ShaftX} ants {sim.Ants.Count} enemies {sim.Enemies.Enemies.Count}\n" + string.Join("\n", ShowcaseColony.Warnings));
            Debug.Log($"Showcase map: queen {q}, surface at the queen {world.SurfaceY[q.x]}, {sim.Ants.Count} ants, {sim.Enemies.Enemies.Count} enemies → {path}");
            sim.Dispose();
            content.Dispose();
            return path;
        }

        static void Plot(Color32[] pixels, int w, int h, int2 c, Color color)
        {
            if (c.x < 0 || c.y < 0 || c.x >= w || c.y >= h) return;
            pixels[c.y * w + c.x] = color;
        }
    }
}
