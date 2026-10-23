using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The caste mix (K): one row per unlocked caste with how many there are (and eggs), its share of the colony and
    /// - / + to change it. Workers fill whatever share the specialists leave.
    /// </summary>
    public sealed class CastesPanel
    {
        readonly ContentDB _content;
        readonly VisualElement _root;
        readonly VisualElement[] _rows = new VisualElement[Castes.Count];
        readonly Label[] _counts = new Label[Castes.Count];
        readonly Label[] _shares = new Label[Castes.Count];
        readonly string[] _texts = new string[Castes.Count * 2];
        readonly Label _hint;
        readonly int[] _eggs = new int[Castes.Count];

        /// <summary>A share button: caste and -1 / +1 step.</summary>
        public event Action<AntCaste, int> ShareStep;

        public CastesPanel(VisualElement root, ContentDB content)
        {
            _root = root;
            _content = content;
            var title = new Label("Castes (K)");
            title.AddToClassList("panel__title");
            root.Add(title);
            foreach (var caste in content.CasteOrder)
            {
                int c = (int)caste;
                var def = content.Caste(caste);
                var row = new VisualElement();
                row.AddToClassList("caste");
                row.tooltip = def.description;
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("caste__swatch");
                swatch.style.backgroundColor = def.color;
                var name = new Label(def.DisplayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("caste__name");
                _counts[c] = new Label { pickingMode = PickingMode.Ignore };
                _counts[c].AddToClassList("caste__count");
                _shares[c] = new Label { pickingMode = PickingMode.Ignore };
                _shares[c].AddToClassList("caste__share");
                row.Add(swatch);
                row.Add(name);
                row.Add(_counts[c]);
                row.Add(_shares[c]);
                if (caste != AntCaste.Worker)
                {
                    var less = new Button(() => ShareStep?.Invoke(caste, -1)) { text = "-", focusable = false };
                    less.AddToClassList("small-button");
                    var more = new Button(() => ShareStep?.Invoke(caste, 1)) { text = "+", focusable = false };
                    more.AddToClassList("small-button");
                    row.Add(less);
                    row.Add(more);
                }
                else _shares[c].AddToClassList("caste__share--rest");
                root.Add(row);
                _rows[c] = row;
            }
            _hint = new Label { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("caste__hint");
            root.Add(_hint);
        }

        public VisualElement Root => _root;
        public bool Visible => !_root.ClassListContains("hidden");

        public void Toggle() => _root.EnableInClassList("hidden", Visible);

        public void Hide() => _root.AddToClassList("hidden");

        /// <summary>True once the queen can lay anything but workers (the panel is worth opening).</summary>
        public static bool HasSpecialists(Simulation sim)
        {
            var unlocked = sim.Colony.CasteUnlocked;
            for (int c = 1; c < unlocked.Length; c++)
                if (unlocked[c]) return true;
            return false;
        }

        public void Refresh(Simulation sim)
        {
            if (!Visible) return;
            var colony = sim.Colony;
            Array.Clear(_eggs, 0, _eggs.Length);
            foreach (var e in colony.Eggs) _eggs[(int)e.Caste]++;
            foreach (var a in sim.Ants.Ants)
                if (a.CarryingEgg) _eggs[(int)a.EggCaste]++;
            var alive = sim.AliveByCaste;
            float rest = 1f - colony.SpecialistShare;
            foreach (var caste in _content.CasteOrder)
            {
                int c = (int)caste;
                bool show = colony.CasteUnlocked[c];
                _rows[c].EnableInClassList("hidden", !show);
                if (!show) continue;
                string count = _eggs[c] > 0 ? $"{NumberFormat.Format(alive[c])} (+{_eggs[c]} eggs)" : NumberFormat.Format(alive[c]);
                string share = caste == AntCaste.Worker ? $"rest {Mathf.RoundToInt(rest * 100f)}%" : $"{Mathf.RoundToInt(colony.CasteShare[c] * 100f)}%";
                Set(_counts[c], c * 2, count);
                Set(_shares[c], c * 2 + 1, share);
            }
            bool nurses = colony.CasteUnlocked[(int)AntCaste.Nurse] && alive[(int)AntCaste.Nurse] > 0 && !HasNursery(sim);
            _hint.text = nurses
                ? "Nurses need a Nursery (Build tool) to carry eggs to."
                : "The queen lays whichever caste is furthest below its share.";
        }

        static bool HasNursery(Simulation sim)
        {
            foreach (var b in sim.Buildings.All)
                if (b.Kind == BuildingKind.Nursery && b.IsActive) return true;
            return false;
        }

        void Set(Label label, int slot, string text)
        {
            if (_texts[slot] == text) return;
            _texts[slot] = text;
            label.text = text;
        }
    }
}
