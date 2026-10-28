using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Settings: Audio, Display, Gameplay and Controls pages. Every change applies at once (<see cref="Changed"/>) and
    /// is saved when the dialog closes. Options are "&lt; value &gt;" steppers, which suit the pixel skin and a gamepad.
    /// </summary>
    public sealed class SettingsDialog
    {
        const float ConfirmSeconds = 3f;
        static readonly string[] Pages = { "Audio", "Display", "Gameplay", "Controls" };
        static readonly int[] FrameCaps = { 30, 60, 120, 144, 165, 240, 0 };
        static readonly int[] Autosaves = { 0, 1, 2, 5, 10 };

        readonly GameInput _input;
        readonly VisualElement _page;
        readonly Button[] _tabs = new Button[Pages.Length];
        readonly List<Action> _refreshers = new();
        GameSettings _settings;
        int _pageIndex;
        Button _erase;
        float _confirm;
        InputActionRebindingExtensions.RebindingOperation _rebind;
        int _rebindEndFrame = -1;

        public VisualElement Root { get; }
        /// <summary>A value changed: apply it.</summary>
        public event Action<GameSettings> Changed;
        /// <summary>Close was pressed: save.</summary>
        public event Action Closed;
        /// <summary>The player confirmed erasing every colony, the bloodline and the achievements.</summary>
        public event Action EraseProgress;

        /// <summary>True while waiting for a key, and on the frame after (so that key does nothing else).</summary>
        public bool Rebinding => _rebind != null || Time.frameCount <= _rebindEndFrame;

        public SettingsDialog(GameInput input)
        {
            _input = input;
            Root = Dialogs.Box("Settings");
            Root.AddToClassList("settings");
            var tabs = new VisualElement();
            tabs.AddToClassList("tabs");
            for (int i = 0; i < Pages.Length; i++)
            {
                int index = i;
                var tab = Dialogs.Button(tabs, Pages[i], () => ShowPage(index));
                tab.AddToClassList("tab");
                _tabs[i] = tab;
            }
            Root.Add(tabs);
            _page = new VisualElement();
            _page.AddToClassList("settings__page");
            Root.Add(_page);
            Dialogs.Button(Root, "Close (Esc)", () => Closed?.Invoke());
        }

        /// <summary>Shows the dialog for <paramref name="settings"/> (edited in place).</summary>
        public void Open(GameSettings settings)
        {
            _settings = settings;
            _confirm = 0f;
            ShowPage(_pageIndex);
        }

        public void Update(float dt)
        {
            if (_confirm > 0f)
            {
                _confirm -= dt;
                if (_confirm <= 0f && _erase != null) SetEraseText();
            }
        }

        void ShowPage(int index)
        {
            CancelRebind();
            _pageIndex = index;
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].EnableInClassList("tab--active", i == index);
            _page.Clear();
            _refreshers.Clear();
            _erase = null;
            if (_settings == null) return;
            switch (index)
            {
                case 0: BuildAudio(); break;
                case 1: BuildDisplay(); break;
                case 2: BuildGameplay(); break;
                default: BuildControls(); break;
            }
        }

        void Apply()
        {
            foreach (var r in _refreshers) r();
            Changed?.Invoke(_settings);
        }

        // ---------------------------------------------------------------- pages

        void BuildAudio()
        {
            Volume("Master volume", () => _settings.masterVolume, v => _settings.masterVolume = v);
            Volume("Music", () => _settings.musicVolume, v => _settings.musicVolume = v);
            Volume("Effects", () => _settings.sfxVolume, v => _settings.sfxVolume = v);
            Volume("Ambience", () => _settings.ambienceVolume, v => _settings.ambienceVolume = v);
            Volume("Interface", () => _settings.uiVolume, v => _settings.uiVolume = v);
            Toggle("Mute when in the background", () => _settings.muteInBackground, v => _settings.muteInBackground = v);
        }

        void BuildDisplay()
        {
            string[] modes = { "Borderless fullscreen", "Exclusive fullscreen", "Windowed" };
            FullScreenMode[] modeValues = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };
            Option("Window", modes, () => Math.Max(0, Array.IndexOf(modeValues, _settings.windowMode)), i => _settings.windowMode = modeValues[i]);

            var sizes = new List<Vector2Int> { Vector2Int.zero };
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!sizes.Contains(size)) sizes.Add(size);
            }
            var labels = new string[sizes.Count];
            labels[0] = "Native";
            for (int i = 1; i < sizes.Count; i++) labels[i] = $"{sizes[i].x} x {sizes[i].y}";
            Option("Resolution", labels, () => Math.Max(0, sizes.IndexOf(new Vector2Int(_settings.width, _settings.height))), i =>
            {
                _settings.width = sizes[i].x;
                _settings.height = sizes[i].y;
            });
            Toggle("V-sync", () => _settings.vsync, v => _settings.vsync = v);
            var caps = Array.ConvertAll(FrameCaps, c => c == 0 ? "Unlimited" : $"{c} fps");
            Option("Frame limit (without v-sync)", caps, () => Math.Max(0, Array.IndexOf(FrameCaps, _settings.frameCap)), i => _settings.frameCap = FrameCaps[i]);
            string[] scales = { "Auto", "1x", "2x", "3x", "4x" };
            Option("Interface size", scales, () => _settings.uiScale, i => _settings.uiScale = i);
        }

        void BuildGameplay()
        {
            string[] shakes = { "Off", "25%", "50%", "75%", "100%" };
            Option("Screen shake", shakes, () => Mathf.RoundToInt(_settings.screenShake * 4f), i => _settings.screenShake = i / 4f);
            Toggle("Pan at the screen edge", () => _settings.edgePan, v => _settings.edgePan = v);
            Toggle("Number pops", () => _settings.numberPops, v => _settings.numberPops = v);
            Option("Big numbers", new[] { "Short (1.23K)", "Scientific (1.23e3)" }, () => _settings.scientificNumbers ? 1 : 0, i => _settings.scientificNumbers = i == 1);
            var autosaves = Array.ConvertAll(Autosaves, m => m == 0 ? "Only when quitting" : $"Every {m} min");
            Option("Autosave", autosaves, () => Math.Max(0, Array.IndexOf(Autosaves, _settings.autosaveMinutes)), i => _settings.autosaveMinutes = Autosaves[i]);

            Dialogs.Text(_page, "Erasing deletes the colony save, its backup, the Genetic Memory bloodline and the achievements kept on this computer. Settings stay.", "dialog__note");
            _erase = Dialogs.Button(_page, "", () =>
            {
                if (_confirm > 0f)
                {
                    _confirm = 0f;
                    SetEraseText();
                    EraseProgress?.Invoke();
                }
                else
                {
                    _confirm = ConfirmSeconds;
                    SetEraseText();
                }
            });
            SetEraseText();
        }

        void SetEraseText()
        {
            bool confirming = _confirm > 0f;
            _erase.text = confirming ? "Click again: erase everything?" : "Erase all progress";
            _erase.EnableInClassList("menu-button--warn", true);
        }

        void BuildControls()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("scroll");
            _page.Add(scroll);
            string group = null;
            foreach (var r in _input.Rebindables)
            {
                if (r.Group != group)
                {
                    group = r.Group;
                    Dialogs.Text(scroll, group, "help__heading");
                }
                var row = Row(scroll, r.Name);
                var rebind = r;
                Button key = null;
                key = new Button(() => StartRebind(rebind, key)) { focusable = false };
                key.AddToClassList("key-button");
                row.Add(key);
                _refreshers.Add(() => key.text = GameInput.KeyName(rebind.Action, rebind.Binding));
            }
            var buttons = new VisualElement();
            buttons.AddToClassList("row-buttons");
            Dialogs.Button(buttons, "Reset to defaults", () =>
            {
                CancelRebind();
                _input.ResetOverrides();
                _settings.bindingOverrides = "";
                Apply();
            });
            _page.Add(buttons);
            Dialogs.Text(_page, "Click a key, then press the new key or mouse button. Esc cancels. The gamepad keeps its own layout.", "dialog__note");
            foreach (var r in _refreshers) r();
        }

        void StartRebind(GameInput.Rebindable r, Button button)
        {
            CancelRebind();
            button.text = "Press a key...";
            button.AddToClassList("key-button--waiting");
            r.Action.Disable();
            _rebind = r.Action.PerformInteractiveRebinding(r.Binding)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Gamepad>")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => FinishRebind(r, button, changed: true))
                .OnCancel(_ => FinishRebind(r, button, changed: false))
                .Start();
        }

        void FinishRebind(GameInput.Rebindable r, Button button, bool changed)
        {
            _rebind?.Dispose();
            _rebind = null;
            _rebindEndFrame = Time.frameCount + 1;
            r.Action.Enable();
            button.RemoveFromClassList("key-button--waiting");
            if (changed)
            {
                _input.BindingsChanged();
                _settings.bindingOverrides = _input.SaveOverrides();
            }
            Apply();
        }

        public void CancelRebind() => _rebind?.Cancel();

        // ---------------------------------------------------------------- rows

        VisualElement Row(VisualElement parent, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("option");
            Dialogs.Text(row, label, "option__label");
            parent.Add(row);
            return row;
        }

        void Option(string label, string[] values, Func<int> get, Action<int> set)
        {
            var row = Row(_page, label);
            var value = new Label();
            value.AddToClassList("option__value");
            void Step(int delta)
            {
                set((get() + delta + values.Length) % values.Length);
                Apply();
            }
            var left = new Button(() => Step(-1)) { text = "<", focusable = false };
            left.AddToClassList("option__button");
            var right = new Button(() => Step(1)) { text = ">", focusable = false };
            right.AddToClassList("option__button");
            row.Add(left);
            row.Add(value);
            row.Add(right);
            _refreshers.Add(() => value.text = values[Mathf.Clamp(get(), 0, values.Length - 1)]);
            value.text = values[Mathf.Clamp(get(), 0, values.Length - 1)];
        }

        void Toggle(string label, Func<bool> get, Action<bool> set) =>
            Option(label, new[] { "Off", "On" }, () => get() ? 1 : 0, i => set(i == 1));

        void Volume(string label, Func<float> get, Action<float> set)
        {
            var values = new string[11];
            for (int i = 0; i <= 10; i++) values[i] = i == 0 ? "Off" : $"{i * 10}%";
            Option(label, values, () => Mathf.RoundToInt(get() * 10f), i => set(i / 10f));
        }
    }
}
