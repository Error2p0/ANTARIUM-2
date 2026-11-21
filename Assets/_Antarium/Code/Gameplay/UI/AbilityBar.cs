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

        public AbilityBar(VisualElement container, ContentDB content, ArtLibrary art, InputAction[] actions, Tooltips tips = null)
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
                HudController.AddButtonIcon(button, art, "ability_" + def.Id);
                container.Add(button);
                _buttons[i] = button;
                tips?.Add(button, () => Tip(index), "ability_" + def.Id);
            }
        }

        string Tip(int index)
        {
            var def = _content.Abilities[index];
            var sb = new System.Text.StringBuilder(def.DisplayName);
            string key = Keys.Ability(index);
            if (key != "unbound" && key != "") sb.Append(" (").Append(key).Append(')');
            if (!string.IsNullOrEmpty(def.description)) sb.Append(": ").Append(def.description);
            sb.Append("\nRecharges in ").Append(HudText.Duration(def.cooldown));
            if (def.foodCost > 0f) sb.Append(", costs ").Append(Antarium.Core.NumberFormat.Format(def.foodCost)).Append(" food");
            sb.Append('.');
            if (def.kind is AbilityKind.SeismicSense or AbilityKind.PheromoneBurst) sb.Append(" Click it, then click where to use it.");
            return sb.ToString();
        }

        /// <summary>The button of ability <paramref name="index"/> (capture shots).</summary>
        public Button Button(int index) => index >= 0 && index < _buttons.Length ? _buttons[index] : null;

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
                if (text != _texts[i]) HudController.SetButtonText(button, _texts[i] = text);
                button.EnableInClassList("tool--running", running > 0f);
                button.EnableInClassList("tool--cooling", running <= 0f && cooldown > 0f);
                button.EnableInClassList("tool--targeting", targeting == i);
            }
        }
    }
}
