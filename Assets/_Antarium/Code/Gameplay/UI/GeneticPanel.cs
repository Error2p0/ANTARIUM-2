using System;
using System.Text;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The Genetic Memory tree (G): traits bought with Genetic Memory from Nuptial Flights, kept in every colony after.
    /// One column per tier; nodes show their level and the cost of the next one. Hover for details, click to buy.
    /// </summary>
    public sealed class GeneticPanel
    {
        readonly ContentDB _content;
        readonly Button[] _nodes;
        readonly Label[] _levels;
        readonly Label _memory, _detail;
        readonly StringBuilder _sb = new();
        int _hovered = -1;

        public VisualElement Root { get; }
        public event Action<int> BuyClicked;
        public event Action CloseClicked;

        public GeneticPanel(ContentDB content)
        {
            _content = content;
            Root = new VisualElement();
            Root.AddToClassList("panel");
            Root.AddToClassList("research");
            Root.AddToClassList("genetics");

            var header = new VisualElement();
            header.AddToClassList("research__header");
            var title = new Label("Genetic Memory");
            title.AddToClassList("dialog__title");
            header.Add(title);
            _memory = new Label();
            _memory.AddToClassList("research__knowledge");
            header.Add(_memory);
            var close = new Button(() => CloseClicked?.Invoke()) { text = "Close (G)", focusable = false };
            close.AddToClassList("small-button");
            header.Add(close);
            Root.Add(header);

            var traits = content.Genetics;
            int columns = 0, rows = 0;
            foreach (var g in traits)
            {
                columns = Math.Max(columns, g.Def.treePosition.x + 1);
                rows = Math.Max(rows, g.Def.treePosition.y + 1);
            }
            var columnsRow = new VisualElement();
            columnsRow.AddToClassList("research__columns");
            Root.Add(columnsRow);
            var cells = new VisualElement[columns, rows];
            _nodes = new Button[traits.Length];
            _levels = new Label[traits.Length];
            for (int i = 0; i < traits.Length; i++)
            {
                int index = i;
                var def = traits[i].Def;
                var node = new Button(() => BuyClicked?.Invoke(index)) { focusable = false };
                node.AddToClassList("node");
                var name = new Label(def.DisplayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("node__name");
                node.Add(name);
                var level = new Label { pickingMode = PickingMode.Ignore };
                level.AddToClassList("node__cost");
                node.Add(level);
                node.RegisterCallback<PointerEnterEvent>(_ => _hovered = index);
                node.RegisterCallback<PointerLeaveEvent>(_ => { if (_hovered == index) _hovered = -1; });
                _nodes[i] = node;
                _levels[i] = level;
                var p = def.treePosition;
                if (p.x >= 0 && p.y >= 0) cells[p.x, p.y] = node;
            }
            for (int x = 0; x < columns; x++)
            {
                var column = new VisualElement();
                column.AddToClassList("research__column");
                column.AddToClassList("genetics__column");
                var tier = new Label($"Tier {x + 1}");
                tier.AddToClassList("research__tier");
                column.Add(tier);
                for (int y = 0; y < rows; y++)
                {
                    if (cells[x, y] != null) column.Add(cells[x, y]);
                    else
                    {
                        var spacer = new VisualElement();
                        spacer.AddToClassList("node-spacer");
                        column.Add(spacer);
                    }
                }
                columnsRow.Add(column);
            }

            _detail = new Label();
            _detail.AddToClassList("research__detail");
            Root.Add(_detail);
        }

        public void Refresh(Simulation sim)
        {
            var legacy = sim.Legacy;
            _memory.text = $"{legacy.Memory} Genetic Memory   ({legacy.Flights} {(legacy.Flights == 1 ? "flight" : "flights")})";
            for (int i = 0; i < _nodes.Length; i++)
            {
                int level = legacy.Levels[i], max = _content.Genetics[i].MaxLevel;
                bool done = level >= max;
                bool available = !done && legacy.IsAvailable(i);
                bool affordable = available && legacy.Memory >= legacy.Cost(i);
                var node = _nodes[i];
                node.EnableInClassList("node--done", done);
                node.EnableInClassList("node--owned", !done && level > 0);
                node.EnableInClassList("node--available", available);
                node.EnableInClassList("node--affordable", affordable);
                _sb.Clear();
                if (max > 1) _sb.Append("Level ").Append(level).Append('/').Append(max);
                if (!done)
                {
                    if (_sb.Length > 0) _sb.Append("  -  ");
                    _sb.Append(legacy.Cost(i)).Append(" memory");
                }
                else if (max == 1) _sb.Append("Owned");
                string text = _sb.ToString();
                if (_levels[i].text != text) _levels[i].text = text;
            }
            _detail.text = Detail(sim);
        }

        string Detail(Simulation sim)
        {
            var legacy = sim.Legacy;
            if (_hovered < 0)
                return legacy.Awakened
                    ? "Traits are kept forever, in every colony after a Nuptial Flight. Colony bonuses work right away; starting bonuses from the next colony on."
                    : "Genetic Memory comes from Nuptial Flights (N): raise princesses in a Princess Chamber and send them off to found a new colony.";
            var data = _content.Genetics[_hovered];
            var def = data.Def;
            _sb.Clear().Append(def.DisplayName).Append(": ").Append(def.description);
            for (int k = 0; k < data.Effects.Length; k++) AppendEffect(data.Effects[k], data.Targets[k]);

            int level = legacy.Levels[_hovered];
            if (level >= data.MaxLevel) return _sb.Append(data.MaxLevel > 1 ? $"\nAll {data.MaxLevel} levels bought." : "\nAlready yours.").ToString();
            bool first = true;
            foreach (int p in data.Prerequisites)
            {
                if (legacy.Levels[p] > 0) continue;
                _sb.Append(first ? "\nNeeds first: " : ", ").Append(_content.Genetics[p].Def.DisplayName);
                first = false;
            }
            _sb.Append("\nNext level: ").Append(legacy.Cost(_hovered)).Append(" Genetic Memory");
            if (first) _sb.Append(legacy.Memory >= legacy.Cost(_hovered) ? "  -  click to buy" : "  -  not enough memory yet");
            return _sb.ToString();
        }

        void AppendEffect(in GeneticEffect e, int target)
        {
            _sb.Append("\n  ");
            switch (e.kind)
            {
                case GeneticEffectKind.Stat:
                    _sb.Append(e.value >= 0f ? "+" : "");
                    if (HudText.IsPercent(e.stat)) _sb.Append(Math.Round(e.value * 100f)).Append('%');
                    else _sb.Append(e.value);
                    _sb.Append(' ').Append(HudText.StatName(e.stat)).Append(" per level");
                    break;
                case GeneticEffectKind.StartWorkers: _sb.Append('+').Append(e.value).Append(" starting workers per level"); break;
                case GeneticEffectKind.StartFood: _sb.Append('+').Append(e.value).Append(" starting food per level"); break;
                case GeneticEffectKind.StartKnowledge: _sb.Append('+').Append(e.value).Append(" starting knowledge per level"); break;
                case GeneticEffectKind.StartItems:
                    _sb.Append('+').Append(e.count).Append(' ').Append(_content.Items[target].DisplayName).Append(" at the start per level");
                    break;
                case GeneticEffectKind.InheritResearch: _sb.Append("Every colony starts with ").Append(_content.Research[target].Def.DisplayName).Append(" researched"); break;
                case GeneticEffectKind.ModifierSlots: _sb.Append('+').Append(e.value).Append(" world modifier slot per level"); break;
                case GeneticEffectKind.UnlockModifier: _sb.Append("Unlocks world modifier: ").Append(_content.Modifiers[target].DisplayName); break;
                case GeneticEffectKind.MaxPrincesses: _sb.Append('+').Append(e.value).Append(" princess counted per level"); break;
                case GeneticEffectKind.RevealRadius: _sb.Append("New colonies see ").Append(e.value).Append(" cells around the nest per level"); break;
                case GeneticEffectKind.VeinInstinct: _sb.Append("Ore next to dug-out cells is ordered for mining automatically"); break;
            }
        }
    }
}
