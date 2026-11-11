using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The strip above the toolbar that explains the overlay that is on (10.7): the flow colours, the light bands from
    /// dark to bright, or every find the colony knows of with its icon and how many cells of it are known.
    /// </summary>
    public sealed class OverlayLegend
    {
        readonly VisualElement _root;
        readonly ContentDB _content;
        readonly RenderConfig _render;
        readonly ArtLibrary _art;
        readonly GameInput _input;
        readonly Label _title;
        readonly VisualElement _entries;
        readonly Label _note;
        OverlayMode _shown = (OverlayMode)(-1);
        int _keysVersion = -1;
        ushort[] _finds;
        Label[] _counts;
        VisualElement[] _findEntries;
        int[] _shownCounts;

        const string OresNote = "Cells of each find you have seen, or found with Seismic Sense (shown faintly in the dark).";
        const string NoFindsNote = "No finds seen yet: dig deeper, or use Seismic Sense.";

        public OverlayLegend(VisualElement root, ContentDB content, RenderConfig render, ArtLibrary art, GameInput input)
        {
            _root = root;
            _content = content;
            _render = render;
            _art = art;
            _input = input;
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("legend__title");
            _entries = new VisualElement { pickingMode = PickingMode.Ignore };
            _entries.AddToClassList("legend__entries");
            _note = new Label { pickingMode = PickingMode.Ignore };
            _note.AddToClassList("legend__note");
            root.Add(_title);
            root.Add(_entries);
            root.Add(_note);
            root.AddToClassList("hidden");
        }

        public VisualElement Root => _root;

        public void Show(OverlayMode mode, OreSurvey survey)
        {
            int version = _input != null ? _input.BindingsVersion : 0;
            if (mode != _shown || version != _keysVersion)
            {
                _shown = mode;
                _keysVersion = version;
                Build(mode, survey);
            }
            if (mode == OverlayMode.Ores) RefreshCounts(survey);
        }

        void Build(OverlayMode mode, OreSurvey survey)
        {
            _root.EnableInClassList("hidden", mode == OverlayMode.None);
            _entries.Clear();
            _finds = null;
            switch (mode)
            {
                case OverlayMode.Flow:
                    _title.text = Title("Flow overlay", _input?.Overlay);
                    Swatch(_render.overlayIdleColor, "belt idle");
                    Swatch(_render.overlayFlowColor, "belt busy");
                    Swatch(_render.overlayJamColor, "jammed");
                    Swatch(_render.overlayWorkingColor, "machine working");
                    Swatch(_render.overlayStarvedColor, "machine waiting");
                    Swatch(_render.overlayIdleAntColor, "idle ant");
                    _note.text = "Hover a belt or machine for its exact rates.";
                    break;
                case OverlayMode.Light:
                    _title.text = Title("Light overlay", _input?.LightOverlay);
                    var ramp = _render.lightOverlayRamp;
                    int bands = ramp != null ? Mathf.Min(ramp.Length, 8) : 0;
                    for (int i = 0; i < bands; i++)
                        Swatch(ramp[i], i == 0 ? "dark" : i == bands - 1 ? "bright" : null);
                    _note.text = "Light comes from the sky, glowing rock and crystals, lava, fire and glow ants.";
                    break;
                case OverlayMode.Ores:
                    _title.text = Title("Ore overlay", _input?.OreOverlay);
                    BuildFinds(survey);
                    _note.text = OresNote;
                    break;
            }
        }

        string Title(string name, InputAction action)
        {
            string key = action != null ? GameInput.KeyName(action) : "";
            return string.IsNullOrEmpty(key) ? name : $"{name} ({key})";
        }

        void Swatch(Color color, string text)
        {
            var entry = new VisualElement { pickingMode = PickingMode.Ignore };
            entry.AddToClassList("legend__entry");
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("legend__swatch");
            swatch.style.backgroundColor = color;
            entry.Add(swatch);
            if (!string.IsNullOrEmpty(text))
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("legend__label");
                entry.Add(label);
            }
            _entries.Add(entry);
        }

        void BuildFinds(OreSurvey survey)
        {
            if (survey == null) return;
            _finds = survey.Finds;
            _counts = new Label[_finds.Length];
            _findEntries = new VisualElement[_finds.Length];
            _shownCounts = new int[_finds.Length];
            for (int i = 0; i < _finds.Length; i++)
            {
                var item = _content.Items[_finds[i]];
                var entry = new VisualElement { pickingMode = PickingMode.Ignore };
                entry.AddToClassList("legend__entry");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                if (!HudController.SetIcon(icon, _art, "item_" + item.Id, "legend__icon"))
                {
                    icon.AddToClassList("legend__swatch");
                    icon.style.backgroundColor = item.color;
                }
                // The colour the overlay draws it in, next to its icon.
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("legend__swatch");
                swatch.AddToClassList("legend__swatch--small");
                swatch.style.backgroundColor = item.MapColor;
                var label = new Label(item.DisplayName) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("legend__label");
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("legend__count");
                entry.Add(swatch);
                entry.Add(icon);
                entry.Add(label);
                entry.Add(count);
                _entries.Add(entry);
                _counts[i] = count;
                _findEntries[i] = entry;
                _shownCounts[i] = -1;
            }
        }

        void RefreshCounts(OreSurvey survey)
        {
            if (survey == null) return;
            if (_finds == null)
            {
                BuildFinds(survey);
                if (_finds == null) return;
            }
            int shown = 0;
            for (int i = 0; i < _finds.Length; i++)
            {
                int known = survey.Known(_finds[i]);
                if (known > 0) shown++;
                if (known == _shownCounts[i]) continue;
                _shownCounts[i] = known;
                _findEntries[i].EnableInClassList("hidden", known == 0);
                _counts[i].text = NumberFormat.Format(known);
            }
            string note = shown == 0 ? NoFindsNote : OresNote;
            if (_note.text != note) _note.text = note;
        }
    }
}
