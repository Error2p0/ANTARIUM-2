using System;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Controls and help (F1): the current key for every action and a few rules of thumb.</summary>
    public sealed class HelpDialog
    {
        static readonly (string Key, string Text)[] Fixed =
        {
            ("Mouse wheel", "Zoom in and out at the cursor"),
            ("Drag (Build tool)", "Lay a line of belts or lifts; the drag direction sets the flow"),
            ("Right drag", "Erase orders, zones, scents or buildings"),
        };

        static readonly string[] Tips =
        {
            "The contracts on the right always show a next goal.",
            "Paint Mine orders (2) and Storage (3); workers dig and haul on their own.",
            "Hover anything in the world for details. O shows how items flow.",
            "The colony keeps working while the game is closed (at half pace, up to 8 hours).",
        };

        readonly GameInput _input;
        readonly VisualElement _list;
        int _version = -1;

        public VisualElement Root { get; }
        public event Action Closed;

        public HelpDialog(GameInput input)
        {
            _input = input;
            Root = Dialogs.Box("Controls and help");
            Root.AddToClassList("settings");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("scroll");
            Root.Add(scroll);
            _list = new VisualElement();
            scroll.Add(_list);
            Dialogs.Button(Root, "Close (Esc)", () => Closed?.Invoke());
        }

        /// <summary>Rebuilds the list when the key bindings changed.</summary>
        public void Refresh()
        {
            if (_version == _input.BindingsVersion) return;
            _version = _input.BindingsVersion;
            _list.Clear();
            string group = null;
            foreach (var r in _input.Rebindables)
            {
                if (r.Group != group)
                {
                    group = r.Group;
                    Dialogs.Text(_list, group, "help__heading");
                }
                AddRow(GameInput.KeyName(r.Action, r.Binding), r.Name);
            }
            Dialogs.Text(_list, "Mouse", "help__heading");
            foreach (var (key, text) in Fixed) AddRow(key, text);
            Dialogs.Text(_list, "Tips", "help__heading");
            foreach (var tip in Tips) Dialogs.Text(_list, tip, "help__text");
            Dialogs.Text(_list, "Change keys in Settings > Controls.", "dialog__note");
        }

        void AddRow(string key, string text)
        {
            var row = new VisualElement();
            row.AddToClassList("help__row");
            Dialogs.Text(row, key, "help__key");
            Dialogs.Text(row, text, "help__text");
            _list.Add(row);
        }
    }
}
