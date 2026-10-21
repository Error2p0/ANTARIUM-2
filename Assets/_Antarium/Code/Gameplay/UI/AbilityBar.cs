using System;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Ability buttons at the end of the toolbar: hotkey, name, and the time left to recharge or run.</summary>
    public sealed class AbilityBar
    {
        static readonly string[] Keys = { "Z", "X", "C" };

        readonly ContentDB _content;
        readonly Button[] _buttons;
        readonly string[] _texts;

        public event Action<int> Clicked;

        public AbilityBar(VisualElement container, ContentDB content)
        {
            _content = content;
            int n = content.Abilities.Length;
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

        public void Refresh(Simulation sim, int targeting)
        {
            var abilities = sim.Abilities;
            for (int i = 0; i < _buttons.Length; i++)
            {
                var button = _buttons[i];
                bool unlocked = sim.Colony.AbilityUnlocked[i];
                button.EnableInClassList("hidden", !unlocked);
                if (!unlocked) continue;
                string key = i < Keys.Length ? Keys[i] + "  " : "";
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
