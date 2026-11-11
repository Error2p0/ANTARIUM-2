using System;
using System.Collections;
using System.Collections.Generic;
using Antarium.Sim;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// 10.6 review stills (<c>CaptureMenu.Run("bloodline", false)</c>): the genetic tree with the fifth tier and the Respec button,
    /// the tree after a respec, the Queen's Throne around the queen with its hover text, the flight screen with the biome
    /// variants, the pause menu with New bloodline, the save slot picker, and a world with the three biome variants. Not store media.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        IEnumerator ShotBloodline()
        {
            LoadShowcase();
            Hud(true);
            var q = Queen;

            // A bloodline with a few flights: the first three tiers, the throne and the wild lineages bought.
            var legacy = _sim.Legacy;
            legacy.Flights = 4;
            legacy.MemoryEarned = 140;
            for (int g = 0; g < _content.Genetics.Length; g++)
            {
                var def = _content.Genetics[g].Def;
                if (def.treePosition.x <= 2 || def.Id == "royal_throne" || def.Id == "wild_lineages" || def.Id == "deep_memory") legacy.Levels[g] = 1;
            }
            legacy.Memory = 24;
            int throne = BuildingIndex("queens_throne");
            _sim.GiveResources(40, 300f);
            yield return Seconds(0.3f);
            _hud.ShowModal(_hud.Genetics.Root);
            yield return Seconds(0.5f);
            yield return Still("bloodline_genetics");
            _sim.EnqueueRespec();
            yield return Seconds(0.5f);
            yield return Still("bloodline_respec");
            _hud.HideModal();
            for (int g = 0; g < _content.Genetics.Length; g++)
            {
                var def = _content.Genetics[g].Def;
                if (def.treePosition.x <= 2 || def.Id == "royal_throne" || def.Id == "wild_lineages" || def.Id == "deep_memory") legacy.Levels[g] = 1;
            }
            if (throne >= 0) _sim.Colony.BuildingUnlocked[throne] = true; // the respec locked it again

            // The Queen's Throne around the queen: in the build palette, then built, with its hover text.
            int2 origin = _sim.Colony.QueenCell;
            for (int dx = -3; dx <= 0; dx++)
            {
                var o = _sim.Colony.QueenCell + new int2(dx, 0);
                if (_sim.Buildings.CanPlace(throne, o, default) != PlaceResult.Ok) continue;
                origin = o;
                break;
            }
            Cam(q + new float2(0f, 2f), 6);
            _tools.SetMode(ToolMode.Build);
            yield return Seconds(0.3f);
            yield return Still("bloodline_palette");
            _tools.SetMode(ToolMode.Dig);
            _sim.EnqueueBuild(throne, origin, default);
            yield return Seconds(0.3f);
            _sim.Buildings.CompleteAllBlueprints();
            Pointer((float2)origin + new float2(1.5f, 1f), true);
            yield return Seconds(2f);
            yield return Still("bloodline_throne");
            Pointer(q, false);

            // The flight screen: the biome variants among the world modifiers.
            _sim.Colony.Princesses = 3;
            ToggleFlight();
            yield return Seconds(0.5f);
            yield return Still("bloodline_flight");
            _hud.HideModal();

            // The pause menu (New bloodline) and the slot picker with two filled slots.
            _hud.ShowModal(_hud.Menu.Root);
            yield return Seconds(0.3f);
            yield return Still("bloodline_menu");
            _hud.HideModal();
            _stores[0].Save(SaveGame.Capture(_sim, DateTime.UtcNow.AddHours(-26)));
            legacy.Flights = 0;
            _stores[1].Save(SaveGame.Capture(_sim, DateTime.UtcNow.AddMinutes(-12)));
            _stores[2].Delete();
            _store = _stores[1];
            _freshColony = false;
            ShowTitle();
            _hud.Title.ShowSlots(true);
            yield return Seconds(1f);
            yield return Still("bloodline_slots");
            _hud.Title.ShowSlots(false);
            _hud.SetTitle(false);

            // A new colony on a world with all three biome variants; each variant layer revealed (and lit) for review.
            var variants = new List<int>();
            foreach (var id in new[] { "ember_strata", "drowned_clay", "amethyst_caverns" })
                if (_content.TryGetModifierIndex(id, out int m)) variants.Add(m);
            StartNewColony(42u, legacy.Clone(), variants.ToArray(), announce: false);
            int x = _sim.World.Width / 2 + 40;
            foreach (var (name, biomeId) in new[] { ("clay", "clay"), ("strata", "stone_strata"), ("caverns", "crystal_caverns") })
            {
                int biome = _content.WorldGen.biomes.FindIndex(b => b != null && b.Id == biomeId);
                int depth = 0;
                while (depth < _sim.World.Height && _sim.BiomeAtDepth(depth) < biome) depth++;
                var cell = new int2(x, _sim.World.SurfaceY[x] - depth - 40);
                Reveal.Around(_sim.World, cell, 34);
                if (globalLight != null) globalLight.intensity = 0.8f;
                Cam(cell, 3);
                yield return Seconds(0.5f);
                yield return Still("bloodline_variant_" + name);
            }
            if (globalLight != null) globalLight.intensity = config.render.ambient;
        }
    }
}
