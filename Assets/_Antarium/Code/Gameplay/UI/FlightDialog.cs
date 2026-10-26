using System;
using System.Collections.Generic;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The Nuptial Flight screen (N): what a flight would earn and why, the world modifiers to pick for the next
    /// colony, and the button that sends the princesses off (confirmed by a second click).
    /// </summary>
    public sealed class FlightDialog
    {
        const float ConfirmSeconds = 4f;

        readonly ContentDB _content;
        readonly Label _body, _slots, _modifierHint;
        readonly Button[] _modifiers;
        readonly Label[] _modifierText;
        readonly Button _fly;
        readonly List<int> _chosen = new();
        readonly StringBuilder _sb = new();
        /// <summary>Per world modifier: the genetic trait that unlocks it (null if it starts unlocked).</summary>
        readonly string[] _unlockedBy;
        float _confirm;
        bool _canFly;
        int _slotCount;

        public VisualElement Root { get; }
        /// <summary>The player confirmed the flight; the chosen world modifiers for the next colony.</summary>
        public event Action<int[]> FlyClicked;
        public event Action GeneticsClicked;
        public event Action CloseClicked;

        public FlightDialog(ContentDB content)
        {
            _content = content;
            Root = Dialogs.Box("Nuptial Flight");
            Root.AddToClassList("flight");
            _body = Dialogs.Text(Root, "", "dialog__text");

            var title = Dialogs.Text(Root, "World modifiers for the next colony", "flight__heading");
            title.pickingMode = PickingMode.Ignore;
            _slots = Dialogs.Text(Root, "", "dialog__note");
            var list = new VisualElement();
            list.AddToClassList("flight__modifiers");
            Root.Add(list);
            _unlockedBy = new string[content.Modifiers.Length];
            foreach (var g in content.Genetics)
                for (int k = 0; k < g.Effects.Length; k++)
                    if (g.Effects[k].kind == GeneticEffectKind.UnlockModifier) _unlockedBy[g.Targets[k]] = g.Def.DisplayName;
            _modifiers = new Button[content.Modifiers.Length];
            _modifierText = new Label[content.Modifiers.Length];
            for (int i = 0; i < _modifiers.Length; i++)
            {
                int index = i;
                var def = content.Modifiers[i];
                var button = new Button(() => Toggle(index)) { focusable = false };
                button.AddToClassList("modifier");
                var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("modifier__swatch");
                swatch.style.backgroundColor = def.color;
                button.Add(swatch);
                var text = new Label { pickingMode = PickingMode.Ignore };
                text.AddToClassList("modifier__text");
                button.Add(text);
                list.Add(button);
                _modifiers[i] = button;
                _modifierText[i] = text;
            }
            _modifierHint = Dialogs.Text(Root, "", "dialog__note");

            _fly = Dialogs.Button(Root, "Take flight", OnFly);
            Dialogs.Button(Root, "Genetic tree (G)", () => GeneticsClicked?.Invoke());
            Dialogs.Button(Root, "Close (N)", () => CloseClicked?.Invoke());
        }

        /// <summary>Opening the screen: forget an unfinished confirmation and choices that are no longer allowed.</summary>
        public void Open(Simulation sim)
        {
            _confirm = 0f;
            _chosen.RemoveAll(m => !sim.Legacy.ModifierUnlocked(m));
            int slots = sim.Legacy.ModifierSlots(sim.Config.prestige);
            if (_chosen.Count > slots) _chosen.RemoveRange(slots, _chosen.Count - slots);
        }

        void Toggle(int modifier)
        {
            if (_chosen.Remove(modifier)) return;
            if (_slotCount <= 0) return;
            if (_chosen.Count >= _slotCount) _chosen.RemoveAt(0); // the oldest choice makes room
            _chosen.Add(modifier);
        }

        void OnFly()
        {
            if (!_canFly) return;
            if (_confirm > 0f)
            {
                _confirm = 0f;
                FlyClicked?.Invoke(_chosen.ToArray());
            }
            else _confirm = ConfirmSeconds;
        }

        public void Update(float dt, Simulation sim)
        {
            if (_confirm > 0f) _confirm -= dt;
            var legacy = sim.Legacy;
            var cfg = sim.Config.prestige;
            var e = sim.EstimateFlight();
            _canFly = e.CanFly;

            _sb.Clear();
            _sb.Append("The queen sends winged princesses to found a new colony. This colony ends; its Genetic Memory lives on, and every genetic trait you buy with it stays forever.\n\n");
            _sb.Append("Princesses: ").Append(e.Princesses).Append(" (").Append(e.MaxPrincesses).Append(" count; each after the first adds ")
                .Append(Math.Round(cfg.princessBonus * 100f)).Append("%)\n");
            var stats = sim.Colony.Stats;
            _sb.Append("Colony score ").Append(NumberFormat.Format(e.Score)).Append(" = depth ").Append(stats.MaxDepth)
                .Append(" (").Append(NumberFormat.Format(e.DepthScore)).Append(") + knowledge earned ").Append(NumberFormat.Format((float)stats.KnowledgeEarned))
                .Append(" (").Append(NumberFormat.Format(e.KnowledgeScore)).Append(") + research ").Append(sim.Research.DoneCount)
                .Append(" (").Append(NumberFormat.Format(e.ResearchScore)).Append(") + contracts ").Append(stats.ContractsCompleted)
                .Append(" (").Append(NumberFormat.Format(e.ContractScore)).Append(")\n");
            _sb.Append("Genetic Memory: ").Append(e.Base.ToString("0.0")).Append(" x princesses ").Append(e.PrincessFactor.ToString("0.00"));
            if (Math.Abs(e.Gain - 1f) > 1e-3f) _sb.Append(" x gain ").Append(e.Gain.ToString("0.00"));
            _sb.Append(" = ").Append(e.Memory).Append('\n');
            _sb.Append("You have ").Append(legacy.Memory).Append(" unspent, ").Append(legacy.Flights).Append(legacy.Flights == 1 ? " flight so far." : " flights so far.");
            if (legacy.BestDepth > 0) _sb.Append(" Deepest colony: ").Append(legacy.BestDepth).Append('.');
            if (!e.EnoughPrincesses)
                _sb.Append("\n\nRaise at least ").Append(e.MinPrincesses).Append(e.MinPrincesses == 1 ? " princess" : " princesses")
                    .Append(" in a Princess Chamber first (research Nuptial Flight, then feed it fungus and amber).");
            else if (e.Memory < 1) _sb.Append("\n\nThe colony has not achieved enough yet to pass on any memory: dig deeper, study and research more.");
            _body.text = _sb.ToString();

            _slotCount = legacy.ModifierSlots(cfg);
            _chosen.RemoveAll(m => !legacy.ModifierUnlocked(m));
            _slots.text = _slotCount <= 0 ? "No modifier slots yet." : $"Pick up to {_slotCount}: {_chosen.Count} chosen. Harder worlds raise the next flight's Genetic Memory.";
            for (int i = 0; i < _modifiers.Length; i++)
            {
                var def = _content.Modifiers[i];
                bool unlocked = legacy.ModifierUnlocked(i);
                _modifiers[i].EnableInClassList("modifier--locked", !unlocked);
                _modifiers[i].EnableInClassList("modifier--chosen", _chosen.Contains(i));
                _modifiers[i].SetEnabled(unlocked);
                string text = unlocked ? $"{def.DisplayName}: {def.description}" : $"{def.DisplayName}: locked (genetic trait {_unlockedBy[i] ?? "?"})";
                if (_modifierText[i].text != text) _modifierText[i].text = text;
            }
            _modifierHint.text = sim.WorldModifiers.Count > 0 ? "This colony: " + ModifierNames(sim.WorldModifiers) + "." : "";

            bool confirming = _confirm > 0f && _canFly;
            _fly.SetEnabled(_canFly);
            _fly.text = !_canFly ? "Take flight (not ready)" : confirming ? $"Click again: end this colony for {e.Memory} Genetic Memory?" : $"Take flight (+{e.Memory} Genetic Memory)";
            _fly.EnableInClassList("menu-button--warn", confirming);
        }

        string ModifierNames(IReadOnlyList<int> modifiers)
        {
            var sb = new StringBuilder();
            foreach (int m in modifiers)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(_content.Modifiers[m].DisplayName);
            }
            return sb.ToString();
        }
    }
}
