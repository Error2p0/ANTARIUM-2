using System;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The Queen panel (H or click the queen, 10.4): how she lays (and a button to stop her, 11), the brood, the colony's
    /// upkeep and skills, a shortcut to the caste mix, and her evolution tree. Evolutions belong to this colony and are paid with rare finds from the
    /// stock; hover a node for its details, click to buy its next level.
    /// </summary>
    public sealed class QueenPanel
    {
        const int MaxPips = 5;
        /// <summary><see cref="_hovered"/> while the pointer is over the laying button.</summary>
        const int HoverLaying = -2;

        readonly ContentDB _content;
        readonly Button[] _nodes;
        readonly Label[] _costs;
        readonly VisualElement[][] _pips;
        readonly int[] _shownLevel;
        readonly Label _stats, _detail, _stock;
        readonly StringBuilder _sb = new();
        readonly StringBuilder _cost = new();
        /// <summary>Every item some evolution costs, in first-use order (shown in the header).</summary>
        readonly System.Collections.Generic.List<ushort> _costItems = new();
        readonly Sprite _pipEmpty, _pipFull;
        readonly Button _castes;
        readonly Button _laying;
        bool _layingShownPaused;
        int _hovered = -1;

        public VisualElement Root { get; }
        public event Action<int> EvolveClicked;
        /// <summary>The player wants the queen to lay (true) or to stop (false).</summary>
        public event Action<bool> LayingToggled;
        public event Action CastesClicked;
        public event Action CloseClicked;

        public QueenPanel(ContentDB content, ArtLibrary art)
        {
            _content = content;
            Root = new VisualElement();
            Root.AddToClassList("panel");
            Root.AddToClassList("research");
            Root.AddToClassList("queen");

            var header = new VisualElement();
            header.AddToClassList("research__header");
            var title = new Label("The Queen");
            title.AddToClassList("dialog__title");
            header.Add(title);
            _stock = new Label();
            _stock.AddToClassList("research__knowledge");
            header.Add(_stock);
            var close = new Button(() => CloseClicked?.Invoke()) { focusable = false };
            Keys.Bind(() => close.text = Keys.With("Close", Keys.Queen));
            close.AddToClassList("small-button");
            header.Add(close);
            Root.Add(header);

            if (art != null && art.TryGet("UI/Icons/level_pips", out var pips) && pips.width >= 10)
            {
                int w = pips.width / 2;
                _pipEmpty = Sprite.Create(pips, new Rect(0, 0, w, pips.height), Vector2.zero, 1f);
                _pipFull = Sprite.Create(pips, new Rect(w, 0, w, pips.height), Vector2.zero, 1f);
            }

            var body = new VisualElement();
            body.AddToClassList("queen__body");
            Root.Add(body);

            var left = new VisualElement();
            left.AddToClassList("queen__stats");
            _stats = new Label();
            _stats.AddToClassList("queen__text");
            left.Add(_stats);
            _laying = new Button(() => LayingToggled?.Invoke(_layingShownPaused)) { text = "Stop laying", focusable = false };
            _laying.AddToClassList("small-button");
            _laying.AddToClassList("queen__castes");
            _laying.RegisterCallback<PointerEnterEvent>(_ => _hovered = HoverLaying);
            _laying.RegisterCallback<PointerLeaveEvent>(_ => { if (_hovered == HoverLaying) _hovered = -1; });
            left.Add(_laying);
            _castes = new Button(() => CastesClicked?.Invoke()) { text = "Caste mix (K)", focusable = false };
            _castes.AddToClassList("small-button");
            _castes.AddToClassList("queen__castes");
            left.Add(_castes);
            body.Add(left);

            var evolutions = content.Evolutions;
            foreach (var e in evolutions)
            foreach (var items in e.CostItems)
            foreach (ushort item in items)
                if (!_costItems.Contains(item)) _costItems.Add(item);
            int columns = 0, rows = 0;
            foreach (var e in evolutions)
            {
                columns = Math.Max(columns, e.Def.treePosition.x + 1);
                rows = Math.Max(rows, e.Def.treePosition.y + 1);
            }
            var tree = new VisualElement();
            tree.AddToClassList("research__columns");
            tree.AddToClassList("queen__tree");
            body.Add(tree);
            var cells = new VisualElement[columns, rows];
            _nodes = new Button[evolutions.Length];
            _costs = new Label[evolutions.Length];
            _pips = new VisualElement[evolutions.Length][];
            _shownLevel = new int[evolutions.Length];
            for (int i = 0; i < evolutions.Length; i++)
            {
                int index = i;
                var data = evolutions[i];
                var node = new Button(() => EvolveClicked?.Invoke(index)) { focusable = false };
                node.AddToClassList("node");
                node.AddToClassList("evolution");
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("evolution__top");
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                if (HudController.SetIcon(icon, art, data.Def.icon, "evolution__icon")) top.Add(icon);
                var name = new Label(data.Def.DisplayName) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("node__name");
                top.Add(name);
                var pipRow = new VisualElement { pickingMode = PickingMode.Ignore };
                pipRow.AddToClassList("evolution__pips");
                int levels = Math.Min(MaxPips, data.MaxLevel);
                _pips[i] = new VisualElement[levels];
                for (int l = 0; l < levels; l++)
                {
                    var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                    pip.AddToClassList("evolution__pip");
                    pipRow.Add(pip);
                    _pips[i][l] = pip;
                }
                top.Add(pipRow);
                node.Add(top);
                var cost = new Label { pickingMode = PickingMode.Ignore };
                cost.AddToClassList("node__cost");
                node.Add(cost);
                node.RegisterCallback<PointerEnterEvent>(_ => _hovered = index);
                node.RegisterCallback<PointerLeaveEvent>(_ => { if (_hovered == index) _hovered = -1; });
                _nodes[i] = node;
                _costs[i] = cost;
                _shownLevel[i] = -1;
                var p = data.Def.treePosition;
                if (p.x >= 0 && p.y >= 0) cells[p.x, p.y] = node;
            }
            for (int x = 0; x < columns; x++)
            {
                var column = new VisualElement();
                column.AddToClassList("research__column");
                column.AddToClassList("queen__column");
                for (int y = 0; y < rows; y++)
                {
                    if (cells[x, y] != null) column.Add(cells[x, y]);
                    else
                    {
                        var spacer = new VisualElement();
                        spacer.AddToClassList("node-spacer");
                        spacer.AddToClassList("evolution");
                        column.Add(spacer);
                    }
                }
                tree.Add(column);
            }

            _detail = new Label();
            _detail.AddToClassList("research__detail");
            Root.Add(_detail);
        }

        public void SetCastesKey(string key) => HudController.SetButtonText(_castes, string.IsNullOrEmpty(key) ? "Caste mix" : $"Caste mix ({key})");

        public void Refresh(Simulation sim)
        {
            var evo = sim.Evolutions;
            var inv = sim.Colony.Inventory;
            _stock.text = StockLine(sim);
            for (int i = 0; i < _nodes.Length; i++)
            {
                var data = _content.Evolutions[i];
                int level = evo.Levels[i];
                bool done = level >= data.MaxLevel;
                bool available = !done && evo.IsAvailable(i);
                bool affordable = available && evo.CanAfford(i);
                var node = _nodes[i];
                node.EnableInClassList("node--done", done);
                node.EnableInClassList("node--owned", !done && level > 0);
                node.EnableInClassList("node--available", available);
                node.EnableInClassList("node--affordable", affordable);
                string cost = done ? "All levels" : CostText(data, level, inv);
                if (_costs[i].text != cost) _costs[i].text = cost;
                if (_shownLevel[i] == level) continue;
                _shownLevel[i] = level;
                for (int l = 0; l < _pips[i].Length; l++)
                {
                    var sprite = l < level ? _pipFull : _pipEmpty;
                    if (sprite != null) _pips[i][l].style.backgroundImage = new StyleBackground(sprite);
                    _pips[i][l].EnableInClassList("evolution__pip--full", l < level);
                }
            }
            bool paused = sim.Colony.LayingPaused;
            if (paused != _layingShownPaused || _laying.text.Length == 0)
            {
                _layingShownPaused = paused;
                _laying.text = paused ? "Resume laying" : "Stop laying";
                _laying.EnableInClassList("toggle--on", paused);
            }
            _stats.text = StatsText(sim);
            _detail.text = Detail(sim);
        }

        /// <summary>The rare finds evolutions cost, as the stock holds them.</summary>
        string StockLine(Simulation sim)
        {
            _sb.Clear();
            var inv = sim.Colony.Inventory;
            foreach (ushort item in _costItems)
            {
                if (_sb.Length > 0) _sb.Append("   ");
                _sb.Append(_content.Items[item].DisplayName).Append(' ').Append(NumberFormat.Format(inv.Items[item]));
            }
            return _sb.ToString();
        }

        /// <summary>"4 Honeydew, 2 Amber, 40 Food" for the level after <paramref name="level"/>; items the stock lacks are marked.</summary>
        string CostText(EvolutionData data, int level, Inventory inv)
        {
            _cost.Clear();
            var items = data.CostItems[level];
            var counts = data.CostCounts[level];
            for (int k = 0; k < items.Length; k++)
            {
                if (_cost.Length > 0) _cost.Append(", ");
                _cost.Append(counts[k]).Append(' ').Append(_content.Items[items[k]].DisplayName);
                if (inv.Items[items[k]] < counts[k]) _cost.Append(" (").Append(inv.Items[items[k]]).Append(')');
            }
            if (data.FoodCost[level] > 0f)
            {
                if (_cost.Length > 0) _cost.Append(", ");
                _cost.Append(NumberFormat.Format(data.FoodCost[level])).Append(" Food");
            }
            return _cost.Length > 0 ? _cost.ToString() : "Free";
        }

        string StatsText(Simulation sim)
        {
            var colony = sim.Colony;
            _sb.Clear();
            if (colony.LayingPaused) _sb.Append("Not laying: you told her to rest.");
            else if (colony.Starving) _sb.Append("Starving: she stopped laying.");
            else if (colony.QueenDisturbed) _sb.Append("Raiders in her chamber: she stopped laying.");
            else _sb.Append("Lays an egg every ").Append(colony.EggInterval.ToString("0.0")).Append(" s");
            if (colony.QueenFed) _sb.Append("\nEating honeydew: lays ").Append(Mathf.RoundToInt(colony.FedLayingBonus * 100f)).Append("% faster, eats no food");
            int nursery = 0;
            foreach (var e in colony.Eggs) if (e.InNursery) nursery++;
            _sb.Append("\nEggs by her: ").Append(colony.LooseEggs).Append('/').Append(colony.MaxEggs);
            if (nursery > 0) _sb.Append(",  in nurseries: ").Append(nursery);
            _sb.Append("\nFood upkeep: ").Append(colony.UpkeepPerMinute(sim.AliveByCaste).ToString("0.0")).Append(" a minute");

            int skilled = 0, masters = 0, strong = 0, swift = 0, lucky = 0, lazy = 0;
            foreach (var a in sim.Ants.Ants)
            {
                int level = sim.AntLevel(a);
                if (a.Skill != Jobs.NoSkill && level >= 2) skilled++;
                if (level >= 5) masters++;
                switch (a.Trait)
                {
                    case AntTrait.Strong: strong++; break;
                    case AntTrait.Swift: swift++; break;
                    case AntTrait.Lucky: lucky++; break;
                    case AntTrait.Lazy: lazy++; break;
                }
            }
            _sb.Append("\nAnts: ").Append(NumberFormat.Format(sim.Ants.Count)).Append(",  skilled ").Append(skilled).Append(",  masters ").Append(masters);
            _sb.Append("\nTraits: ").Append(strong).Append(" strong, ").Append(swift).Append(" swift, ").Append(lucky).Append(" lucky, ").Append(lazy).Append(" lazy");
            return _sb.ToString();
        }

        string Detail(Simulation sim)
        {
            var evo = sim.Evolutions;
            if (_hovered == HoverLaying)
                return sim.Colony.LayingPaused
                    ? "The queen is resting: she lays no eggs until you let her again. Royal Decree still lays its eggs."
                    : "Stop the queen laying eggs, for instance when the colony has as many ants as it can feed. Click again to let her lay.";
            if (_hovered < 0)
                return "Evolutions belong to this colony: pay them with rare finds from the stock (honeydew, amber, fossils, star fragments, relics). " +
                       "Each column needs a level of the one to its left.";
            var data = _content.Evolutions[_hovered];
            _sb.Clear().Append(data.Def.DisplayName).Append(": ").Append(data.Def.description);
            foreach (var e in data.Effects)
            {
                _sb.Append("\n  ");
                switch (e.kind)
                {
                    case EvolutionEffectKind.Stat:
                        _sb.Append(e.value >= 0f ? "+" : "");
                        if (HudText.IsPercent(e.stat)) _sb.Append(Mathf.RoundToInt(e.value * 100f)).Append('%');
                        else _sb.Append(e.value);
                        _sb.Append(' ').Append(HudText.StatName(e.stat)).Append(" per level");
                        break;
                    case EvolutionEffectKind.UnlockCaste:
                        _sb.Append("Level ").Append(Math.Max(1, e.level)).Append(": the queen lays ")
                            .Append(_content.Caste(e.caste).DisplayName.ToLowerInvariant()).Append('s');
                        break;
                    case EvolutionEffectKind.UnlockAbility:
                        _sb.Append("Level ").Append(Math.Max(1, e.level)).Append(": unlocks ").Append(e.ability != null ? e.ability.DisplayName : "an ability");
                        break;
                }
            }
            int level = evo.Levels[_hovered];
            if (level >= data.MaxLevel) return _sb.Append("\nAll ").Append(data.MaxLevel).Append(data.MaxLevel == 1 ? " level" : " levels").Append(" evolved.").ToString();
            bool ready = true;
            foreach (int p in data.Prerequisites)
            {
                if (evo.Levels[p] > 0) continue;
                _sb.Append(ready ? "\nNeeds first: " : ", ").Append(_content.Evolutions[p].Def.DisplayName);
                ready = false;
            }
            _sb.Append("\nLevel ").Append(level + 1).Append('/').Append(data.MaxLevel).Append(": ").Append(CostText(data, level, sim.Colony.Inventory));
            if (ready) _sb.Append(evo.CanAfford(_hovered) ? "  -  click to evolve" : "  -  not enough in the stock yet");
            return _sb.ToString();
        }
    }
}
