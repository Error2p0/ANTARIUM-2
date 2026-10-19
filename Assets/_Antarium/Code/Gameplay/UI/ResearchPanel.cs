using System;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The research tree (T): one column per tier, nodes coloured by state (done, affordable, waiting for
    /// resources, locked). Hovering a node shows what it does and needs; clicking buys it.
    /// </summary>
    public sealed class ResearchPanel
    {
        readonly ContentDB _content;
        readonly Button[] _nodes;
        readonly Label[] _costs;
        readonly Label _knowledge, _detail;
        readonly StringBuilder _sb = new();
        int _hovered = -1;

        public VisualElement Root { get; }
        public event Action<int> ResearchClicked;
        public event Action CloseClicked;

        public ResearchPanel(ContentDB content)
        {
            _content = content;
            Root = new VisualElement();
            Root.AddToClassList("panel");
            Root.AddToClassList("research");

            var header = new VisualElement();
            header.AddToClassList("research__header");
            var title = new Label("Research");
            title.AddToClassList("dialog__title");
            header.Add(title);
            _knowledge = new Label();
            _knowledge.AddToClassList("research__knowledge");
            header.Add(_knowledge);
            var close = new Button(() => CloseClicked?.Invoke()) { text = "Close (T)", focusable = false };
            close.AddToClassList("small-button");
            header.Add(close);
            Root.Add(header);

            var research = content.Research;
            int columns = 0, rows = 0;
            foreach (var r in research)
            {
                columns = Math.Max(columns, r.Def.treePosition.x + 1);
                rows = Math.Max(rows, r.Def.treePosition.y + 1);
            }
            var columnsRow = new VisualElement();
            columnsRow.AddToClassList("research__columns");
            Root.Add(columnsRow);
            var cells = new VisualElement[columns, rows];
            _nodes = new Button[research.Length];
            _costs = new Label[research.Length];
            for (int i = 0; i < research.Length; i++)
            {
                int index = i;
                var def = research[i].Def;
                var node = new Button(() => ResearchClicked?.Invoke(index)) { focusable = false };
                node.AddToClassList("node");
                var name = new Label(def.DisplayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("node__name");
                node.Add(name);
                var cost = new Label { pickingMode = PickingMode.Ignore };
                cost.AddToClassList("node__cost");
                node.Add(cost);
                node.RegisterCallback<PointerEnterEvent>(_ => _hovered = index);
                node.RegisterCallback<PointerLeaveEvent>(_ => { if (_hovered == index) _hovered = -1; });
                _nodes[i] = node;
                _costs[i] = cost;
                var p = def.treePosition;
                if (p.x >= 0 && p.y >= 0) cells[p.x, p.y] = node;
            }
            for (int x = 0; x < columns; x++)
            {
                var column = new VisualElement();
                column.AddToClassList("research__column");
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
            var inv = sim.Colony.Inventory;
            var research = sim.Research;
            _knowledge.text = $"Knowledge {NumberFormat.Format(inv.Knowledge)}";
            for (int i = 0; i < _nodes.Length; i++)
            {
                bool done = research.Done[i];
                bool available = !done && research.IsAvailable(i);
                bool affordable = available && research.CanAfford(i);
                var node = _nodes[i];
                node.EnableInClassList("node--done", done);
                node.EnableInClassList("node--available", available);
                node.EnableInClassList("node--affordable", affordable);
                var data = _content.Research[i];
                _sb.Clear();
                if (done) _sb.Append("Researched");
                else HudText.AppendCost(_sb, _content, data.Def.knowledgeCost, data.CostItems, data.CostCounts, data.Def.foodCost);
                string cost = _sb.ToString();
                if (_costs[i].text != cost) _costs[i].text = cost;
            }
            _detail.text = Detail(sim);
        }

        string Detail(Simulation sim)
        {
            if (_hovered < 0) return "Hover over a node to see what it does. Knowledge comes from the Archive (studying finds), contracts and events.";
            var data = _content.Research[_hovered];
            var def = data.Def;
            _sb.Clear().Append(def.DisplayName).Append(": ").Append(def.description);
            foreach (var e in data.Stats)
            {
                _sb.Append("\n  ").Append(e.value >= 0f ? "+" : "");
                if (HudText.IsPercent(e.stat)) _sb.Append(Math.Round(e.value * 100f)).Append('%');
                else _sb.Append(e.value);
                _sb.Append(' ').Append(HudText.StatName(e.stat));
            }
            foreach (int b in data.UnlockBuildings) _sb.Append("\n  Unlocks building: ").Append(_content.Buildings[b].Def.DisplayName);
            foreach (int a in data.UnlockAbilities) _sb.Append("\n  Unlocks ability: ").Append(_content.Abilities[a].DisplayName);

            var research = sim.Research;
            if (research.Done[_hovered]) return _sb.Append("\nAlready researched.").ToString();
            bool first = true;
            foreach (int p in data.Prerequisites)
            {
                if (research.Done[p]) continue;
                _sb.Append(first ? "\nNeeds first: " : ", ").Append(_content.Research[p].Def.DisplayName);
                first = false;
            }
            _sb.Append("\nCost: ");
            HudText.AppendCost(_sb, _content, def.knowledgeCost, data.CostItems, data.CostCounts, def.foodCost);
            if (first) _sb.Append(research.CanAfford(_hovered) ? "  -  click to research" : "  -  not enough yet");
            return _sb.ToString();
        }
    }
}
