using System;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Ability buttons at the end of the toolbar: hotkey, name, and the time left to recharge or run.</summary>
    public sealed class AbilityBar
    {
        readonly ContentDB _content;
        readonly InputAction[] _actions;
        readonly string[] _keys;
        readonly Button[] _buttons;
        readonly string[] _texts;

        public event Action<int> Clicked;

        public AbilityBar(VisualElement container, ContentDB content, InputAction[] actions)
        {
            _content = content;
            _actions = actions;
            int n = content.Abilities.Length;
            _keys = new string[n];
            _buttons = new Button[n];
            _texts = new string[n];
            for (int i = 0; i < n; i++)
            {
                int index = i;
                var def = content.Abilities[i];
                var button = new Button(() => Clicked?.Invoke(index)) { focusable = false };
                button.AddToClassList("tool");
                button.AddToClassList("tool--ability");
                button.style.borderLeftColor = def.color;
                container.Add(button);
                _buttons[i] = button;
            }
        }

        /// <summary>Re-reads the hotkeys after the bindings changed.</summary>
        public void RefreshKeys()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                string key = i < _actions.Length ? GameInput.KeyName(_actions[i]) : "";
                _keys[i] = string.IsNullOrEmpty(key) ? "" : $"<color=#a89070>{key}</color> ";
                _texts[i] = null;
            }
        }

        public void Refresh(Simulation sim, int targeting)
        {
            var abilities = sim.Abilities;
            for (int i = 0; i < _buttons.Length; i++)
            {
                var button = _buttons[i];
                bool unlocked = sim.Colony.AbilityUnlocked[i];
                button.EnableInClassList("hidden", !unlocked);
                if (!unlocked) continue;
                string key = _keys[i] ?? "";
                string name = _content.Abilities[i].DisplayName;
                float running = abilities.Remaining[i], cooldown = abilities.Cooldown[i];
                string text = running > 0f ? $"{key}{name}  {Mathf.CeilToInt(running)}s"
                    : cooldown > 0f ? $"{key}{name}  {HudText.Clock(cooldown)}"
                    : key + name;
                if (text != _texts[i]) button.text = _texts[i] = text;
                button.EnableInClassList("tool--running", running > 0f);
                button.EnableInClassList("tool--cooling", running <= 0f && cooldown > 0f);
                button.EnableInClassList("tool--targeting", targeting == i);
            }
        }
    }
}
