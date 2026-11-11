using System.Collections;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// 10.7 review stills (<c>CaptureMenu.Run("presentation", false)</c>): the light overlay by the nest and at a deep outpost,
    /// the ore overlay with its legend, the flow overlay with its legend; tooltips on a palette building, an item in the resource
    /// bar, an ability and a caste row; the info panel's icon over an ore cell and over an ant; the achievements dialog. Not store media.
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

            // Tooltips: a building in the palette, an item in the resource bar, an ability, a caste row.
            Cam(q + new float2(10f, 2f), 3);
            _tools.SetMode(ToolMode.Build);
            yield return Seconds(0.3f);
            _hud.Tips.Hover(_hud.BuildingButton(BuildingIndex("crusher")));
            yield return Seconds(0.5f);
            yield return Still("presentation_tip_building");
            _tools.SetMode(ToolMode.Dig);
            ushort iron = ItemIndex("iron_ore");
            _hud.Tips.Hover(_hud.ItemEntry(iron));
            yield return Seconds(0.5f);
            yield return Still("presentation_tip_item");
            _hud.Tips.Hover(_hud.Abilities.Button(1));
            yield return Seconds(0.5f);
            yield return Still("presentation_tip_ability");
            if (!_hud.Castes.Visible) _hud.Castes.Toggle();
            _hud.Tips.Hover(_hud.Castes.Root.Query<VisualElement>(className: "caste").AtIndex(1));
            yield return Seconds(0.5f);
            yield return Still("presentation_tip_caste");
            _hud.Castes.Hide();
            _hud.Tips.Clear();

            // The info panel's icon: an iron ore cell, then an ant.
            if (FindMaterial("iron_ore", (int2)deep, 30, out int2 ore))
            {
                Cam((float2)ore + 0.5f, 4);
                Pointer((float2)ore + 0.5f, true);
                yield return Seconds(0.6f);
                yield return Still("presentation_hover_ore");
            }
            var ants = _sim.Ants.Ants;
            if (ants.Length > 0)
            {
                Cam(ants[0].Pos, 5);
                Pointer(ants[0].Pos, true);
                yield return Seconds(0.3f);
                Pointer(_sim.Ants.Ants[0].Pos, true);
                yield return Seconds(0.3f);
                yield return Still("presentation_hover_ant");
            }
            Pointer(q, false);

            // The achievements dialog with the achievements' own icons.
            _hud.ShowModal(_hud.AchievementsPanel.Root);
            yield return Seconds(0.5f);
            yield return Still("presentation_achievements");
            _hud.HideModal();
        }

        ushort ItemIndex(string id)
        {
            for (ushort i = 0; i < _content.Items.Length; i++)
                if (_content.Items[i].Id == id) return i;
            return 0;
        }

        /// <summary>The nearest revealed solid cell of material <paramref name="id"/> within <paramref name="radius"/> of <paramref name="around"/>.</summary>
        bool FindMaterial(string id, int2 around, int radius, out int2 found)
        {
            found = around;
            if (!_content.TryGetMaterialIndex(id, out var material)) return false;
            int best = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int2 c = around + new int2(dx, dy);
                if (!_sim.World.InBounds(c.x, c.y)) continue;
                var cell = _sim.World[c.x, c.y];
                if (cell.Material != material || cell.IsOpen || !cell.IsRevealed || dx * dx + dy * dy >= best) continue;
                best = dx * dx + dy * dy;
                found = c;
            }
            return best != int.MaxValue;
        }
    }
}
