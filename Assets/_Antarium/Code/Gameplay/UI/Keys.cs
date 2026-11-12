using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The keys currently bound to the game's actions, for hints and messages (10.7: no fixed letters in texts). Before
    /// <see cref="Use"/> (tests, or a HUD without input) the default keys are returned. Labels that name a key register
    /// with <see cref="Bind"/> and are rewritten whenever the bindings change.
    /// </summary>
    public static class Keys
    {
        static GameInput _input;
        static int _version = -1;
        static readonly List<Action> Bound = new();

        public static void Use(GameInput input)
        {
            _input = input;
            Bound.Clear();
            _version = -1;
        }

        /// <summary>Runs <paramref name="apply"/> now and again every time the key bindings change.</summary>
        public static void Bind(Action apply)
        {
            apply();
            Bound.Add(apply);
        }

        /// <summary>Re-applies the bound labels if the bindings changed since the last call.</summary>
        public static void Refresh()
        {
            int version = _input != null ? _input.BindingsVersion : 0;
            if (version == _version) return;
            _version = version;
            foreach (var apply in Bound) apply();
        }

        /// <summary>The key bound to <paramref name="action"/>; <paramref name="fallback"/> without input, "unbound" with no key.</summary>
        public static string Of(InputAction action, string fallback)
        {
            if (_input == null || action == null) return fallback;
            string key = GameInput.KeyName(action);
            return string.IsNullOrEmpty(key) ? "unbound" : key;
        }

        public static string Dig => Of(_input?.ToolDig, "1");
        public static string Mine => Of(_input?.ToolMine, "2");
        public static string Storage => Of(_input?.ToolStorage, "3");
        public static string Build => Of(_input?.ToolBuild, "4");
        public static string Scent => Of(_input?.ToolPheromone, "5");
        public static string Possess => Of(_input?.Possess, "F");
        public static string Interact => Of(_input?.Interact, "Space");
        public static string Configure => Of(_input?.Configure, "I");
        public static string Rotate => Of(_input?.Rotate, "R");
        public static string Next => Of(_input?.NextBuilding, "E");
        public static string Previous => Of(_input?.PrevBuilding, "Q");
        public static string Brush => Of(_input?.BrushSize, "Tab");
        public static string Flow => Of(_input?.Overlay, "O");
        public static string Light => Of(_input?.LightOverlay, "J");
        public static string Ores => Of(_input?.OreOverlay, "M");
        public static string Research => Of(_input?.Research, "T");
        public static string Castes => Of(_input?.Castes, "K");
        public static string Genetics => Of(_input?.Genetics, "G");
        public static string Flight => Of(_input?.Flight, "N");
        public static string Queen => Of(_input?.Queen, "H");
        public static string Priorities => Of(_input?.Priorities, "P");
        public static string Codex => Of(_input?.Codex, "L");
        public static string Menu => Of(_input?.Menu, "Esc");
        public static string Save => Of(_input?.QuickSave, "F5");
        public static string Load => Of(_input?.QuickLoad, "F9");
        public static string Help => Of(_input?.Help, "F1");

        /// <summary>The key of ability <paramref name="index"/> in bar order (Z, X, C, V, B by default).</summary>
        public static string Ability(int index)
        {
            const string defaults = "ZXCVB";
            string fallback = index >= 0 && index < defaults.Length ? defaults[index].ToString() : "";
            var actions = _input?.Abilities;
            return actions != null && index >= 0 && index < actions.Length ? Of(actions[index], fallback) : fallback;
        }

        /// <summary>"<paramref name="name"/> (<paramref name="key"/>)", or just the name if the key is unbound.</summary>
        public static string With(string name, string key) => key == "unbound" || string.IsNullOrEmpty(key) ? name : $"{name} ({key})";
    }
}
