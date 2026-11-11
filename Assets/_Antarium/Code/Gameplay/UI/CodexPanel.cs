using System;
using System.Text;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The Codex (L, 10.5): the story of the lost colony, one fragment per relic milestone or tablet studied in the Archive.
    /// Found fragments can be read; the others say what is still to be studied. The header counts them and names the ancient
    /// research that waits for more.
    /// </summary>
    public sealed class CodexPanel
    {
        readonly ContentDB _content;
        readonly ArtLibrary _art;
        readonly Button[] _entries;
        readonly Label[] _titles;
        readonly VisualElement[] _icons;
        readonly bool[] _shownFound;
        readonly Label _count, _text, _heading;
        readonly StringBuilder _sb = new();
        int _selected = -1;
        bool _dirty = true;

        public VisualElement Root { get; }
        public event Action CloseClicked;

        public CodexPanel(ContentDB content, ArtLibrary art)
        {
            _content = content;
            _art = art;
            Root = new VisualElement();
            Root.AddToClassList("panel");
            Root.AddToClassList("research");
            Root.AddToClassList("codex");

            var header = new VisualElement();
            header.AddToClassList("research__header");
            var title = new Label("Codex");
            title.AddToClassList("dialog__title");
            header.Add(title);
            _count = new Label();
            _count.AddToClassList("research__knowledge");
            header.Add(_count);
            var close = new Button(() => CloseClicked?.Invoke()) { focusable = false };
            Keys.Bind(() => close.text = Keys.With("Close", Keys.Codex));
            close.AddToClassList("small-button");
            header.Add(close);
            Root.Add(header);

            var body = new VisualElement();
            body.AddToClassList("codex__body");
            Root.Add(body);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("codex__list");
            body.Add(list);
            var page = new VisualElement();
            page.AddToClassList("codex__page");
            _heading = new Label();
            _heading.AddToClassList("codex__heading");
            page.Add(_heading);
            _text = new Label();
            _text.AddToClassList("codex__text");
            page.Add(_text);
            body.Add(page);

            var lore = content.Lore;
            _entries = new Button[lore.Length];
            _titles = new Label[lore.Length];
            _icons = new VisualElement[lore.Length];
            _shownFound = new bool[lore.Length];
            for (int i = 0; i < lore.Length; i++)
            {
                int index = i;
                var entry = new Button(() => Select(index)) { focusable = false };
                entry.AddToClassList("codex__entry");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("codex__icon");
                entry.Add(icon);
                var name = new Label { pickingMode = PickingMode.Ignore };
                name.AddToClassList("codex__name");
                entry.Add(name);
                list.Add(entry);
                _entries[i] = entry;
                _titles[i] = name;
                _icons[i] = icon;
                _shownFound[i] = true; // forces the first refresh to draw it
            }
        }

        /// <summary>Shows <paramref name="lore"/> (a found fragment opens with it, e.g. from a toast).</summary>
        public void Select(int lore)
        {
            _selected = lore;
            _dirty = true;
        }

        public void Refresh(Simulation sim)
        {
            var lore = sim.Lore;
            _count.text = $"Fragments found: {lore.FoundCount} / {lore.Total}";
            for (int i = 0; i < _entries.Length; i++)
            {
                bool found = lore.Found[i];
                _entries[i].EnableInClassList("codex__entry--selected", i == _selected);
                if (_shownFound[i] == found && !_dirty) continue;
                _shownFound[i] = found;
                _titles[i].text = found ? _content.Lore[i].Def.DisplayName : "???";
                _entries[i].EnableInClassList("codex__entry--found", found);
                _icons[i].Clear();
                HudController.SetIcon(_icons[i], _art, found ? "lore_fragment" : "lore_fragment_locked", "codex__icon");
            }
            if (_selected < 0 || _selected >= _entries.Length)
            {
                _heading.text = "The lost colony";
                _text.text = Intro(sim);
            }
            else if (lore.Found[_selected])
            {
                var def = _content.Lore[_selected].Def;
                _heading.text = def.DisplayName;
                _text.text = def.text;
            }
            else
            {
                var data = _content.Lore[_selected];
                long left = lore.Remaining(_selected);
                string item = _content.Items[data.Item].DisplayName.ToLowerInvariant();
                _heading.text = "???";
                _text.text = $"Not found yet. Study {left} more {(left == 1 ? item : Plural(item))} in an Archive to find this fragment " +
                             $"({sim.Colony.Stats.Studied[data.Item]} studied so far).";
            }
            _dirty = false;
        }

        string Intro(Simulation sim)
        {
            _sb.Clear().Append("Deep below, the ruins of an ant colony older than any memory. Relics and tablets taken from them and ")
                .Append("studied in an Archive tell its story, a fragment at a time. Pick a fragment on the left to read it.");
            int needed = int.MaxValue;
            string next = null;
            for (int i = 0; i < _content.Research.Length; i++)
            {
                int lore = _content.Research[i].Def.loreRequired;
                if (lore <= sim.Lore.FoundCount || sim.Research.Done[i] || lore >= needed) continue;
                needed = lore;
                next = _content.Research[i].Def.DisplayName;
            }
            if (next != null) _sb.Append("\n\nThe ancient research needs lore: ").Append(next).Append(" opens at ").Append(needed).Append(" fragments.");
            return _sb.ToString();
        }

        static string Plural(string word) => word.EndsWith("y") ? word.Substring(0, word.Length - 1) + "ies" : word + "s";
    }
}
