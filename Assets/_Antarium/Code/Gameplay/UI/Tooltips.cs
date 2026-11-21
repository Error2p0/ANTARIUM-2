using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Hover help for HUD elements (10.7): while the pointer is over an element with a tip, its text (and icon) is shown in
    /// the info panel. Tips are functions so costs, counts and key names stay current; the shown text is rebuilt a few
    /// times a second. Nested elements with tips work like a stack: leaving the inner one shows the outer one again.
    /// </summary>
    public sealed class Tooltips
    {
        const float RefreshInterval = 0.25f;

        readonly struct Entry
        {
            public readonly VisualElement Element;
            public readonly Func<string> Text;
            public readonly string Icon;
            public Entry(VisualElement element, Func<string> text, string icon) { Element = element; Text = text; Icon = icon; }
        }

        readonly List<Entry> _hovered = new();
        float _timer;

        /// <summary>The tip of the element under the pointer, or null.</summary>
        public string Text { get; private set; }

        /// <summary>The art name of the icon (<c>UI/Icons/...</c>) shown with <see cref="Text"/>, or null.</summary>
        public string Icon { get; private set; }

        public void Add(VisualElement element, string text, string icon = null) => Add(element, () => text, icon);

        public void Add(VisualElement element, Func<string> text, string icon = null)
        {
            if (element == null || text == null) return;
            var entry = new Entry(element, text, icon);
            element.RegisterCallback<PointerEnterEvent>(_ =>
            {
                Remove(element);
                _hovered.Add(entry);
                Show();
            });
            element.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                Remove(element);
                Show();
            });
        }

        /// <summary>Rebuilds the text a few times a second, and drops tips of elements that were hidden under the pointer.</summary>
        public void Update(float dt)
        {
            if (_hovered.Count == 0) return;
            bool changed = false;
            for (int i = _hovered.Count - 1; i >= 0; i--)
                if (!IsShown(_hovered[i].Element))
                {
                    _hovered.RemoveAt(i);
                    changed = true;
                }
            _timer -= dt;
            if (!changed && _timer > 0f) return;
            Show();
        }

        /// <summary>Shows the tip of <paramref name="element"/> as if the pointer had entered it (capture shots).</summary>
        public void Hover(VisualElement element)
        {
            _hovered.Clear();
            if (element == null) return;
            using var e = PointerEnterEvent.GetPooled();
            e.target = element;
            element.SendEvent(e);
        }

        public void Clear()
        {
            _hovered.Clear();
            Text = null;
            Icon = null;
        }

        void Show()
        {
            _timer = RefreshInterval;
            if (_hovered.Count == 0)
            {
                Text = null;
                Icon = null;
                return;
            }
            var top = _hovered[_hovered.Count - 1];
            string text = top.Text();
            Text = string.IsNullOrEmpty(text) ? null : text;
            Icon = Text != null ? top.Icon : null;
        }

        void Remove(VisualElement element)
        {
            for (int i = _hovered.Count - 1; i >= 0; i--)
                if (_hovered[i].Element == element) _hovered.RemoveAt(i);
        }

        /// <summary>In a panel and neither it nor a parent is hidden (the HUD hides with the <c>hidden</c> class).</summary>
        static bool IsShown(VisualElement element)
        {
            if (element.panel == null) return false;
            for (var e = element; e != null; e = e.parent)
                if (e.ClassListContains("hidden") || e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }
    }
}
