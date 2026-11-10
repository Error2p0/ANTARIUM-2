using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>What the configure panel is set up for.</summary>
    public enum ConfigTarget
    {
        None,
        /// <summary>A painted storage zone (every storage cell connected to the one picked).</summary>
        Storage,
        Chest,
        Unloader,
        Filter,
        Emitter,
        Machine,
        /// <summary>The colony stock: how much of each item is kept for building.</summary>
        Stock,
    }

    /// <summary>
    /// The configure panel (I over a building or storage, or a click on an item in the resource bar): which items a
    /// storage zone or chest takes, the item an unloader or filter handles, an emitter's scent, the recipe a machine
    /// runs, and how much of each item the colony keeps for building.
    /// </summary>
    public sealed class ConfigurePanel
    {
        const int ReserveStepSize = 5;

        readonly ContentDB _content;
        readonly ArtLibrary _art;
        readonly VisualElement _root;
        readonly Label _title, _hint;
        readonly VisualElement _list;
        readonly List<(Button Button, int Value)> _options = new();
        readonly List<(Label Label, ushort Item)> _stockLabels = new();
        readonly ulong _allItems;

        public ConfigTarget Target { get; private set; }
        /// <summary>The cell the panel was opened on (storage, chest, building).</summary>
        public int2 Cell { get; private set; }
        int _building = -1;
        int _defShown = -1;

        /// <summary>A storage zone or chest at the cell now takes the items in the mask (all bits = anything).</summary>
        public event Action<int2, ulong> StorageFilterChanged;
        /// <summary>Unloader / filter item (<see cref="ContentDB.NoItem"/> = any) or emitter pheromone at the cell.</summary>
        public event Action<int2, ushort> ItemPicked;
        /// <summary>Machine at the cell runs only this recipe (-1 = any).</summary>
        public event Action<int2, int> RecipePicked;
        /// <summary>Change the reserve of an item by this many.</summary>
        public event Action<ushort, int> ReserveStep;

        public ConfigurePanel(VisualElement root, ContentDB content, ArtLibrary art = null)
        {
            _art = art;
            _root = root;
            _content = content;
            for (int i = 0; i < content.Items.Length && i < 64; i++) _allItems |= 1ul << i;

            var header = new VisualElement();
            header.AddToClassList("configure__header");
            _title = new Label();
            _title.AddToClassList("panel__title");
            _title.AddToClassList("configure__title");
            var close = new Button(Hide) { text = "x", focusable = false };
            close.AddToClassList("small-button");
            header.Add(_title);
            header.Add(close);
            root.Add(header);
            _list = new VisualElement();
            _list.AddToClassList("configure__list");
            root.Add(_list);
            _hint = new Label { pickingMode = PickingMode.Ignore };
            _hint.AddToClassList("configure__hint");
            root.Add(_hint);
        }

        public VisualElement Root => _root;
        public bool Visible => !_root.ClassListContains("hidden");

        public void Hide()
        {
            _root.AddToClassList("hidden");
            Target = ConfigTarget.None;
        }

        /// <summary>What there is to configure at <paramref name="cell"/> (a building first, then storage).</summary>
        public static ConfigTarget TargetAt(Simulation sim, int2 cell, out int building)
        {
            building = sim.Buildings.At(cell);
            if (building >= 0)
            {
                var b = sim.Buildings.Get(building);
                switch (b.Kind)
                {
                    case BuildingKind.Chest: return b.IsActive ? ConfigTarget.Chest : ConfigTarget.None;
                    case BuildingKind.Unloader: return ConfigTarget.Unloader;
                    case BuildingKind.Filter: return ConfigTarget.Filter;
                    case BuildingKind.Emitter: return ConfigTarget.Emitter;
                    case BuildingKind.Processor:
                        return b.IsActive && sim.Content.Buildings[b.Def].Recipes.Length > 1 ? ConfigTarget.Machine : ConfigTarget.None;
                }
            }
            building = -1;
            if (!sim.World.InBounds(cell.x, cell.y)) return ConfigTarget.None;
            return sim.Colony.StorageCells.Contains(sim.World.Index(cell.x, cell.y)) ? ConfigTarget.Storage : ConfigTarget.None;
        }

        /// <summary>Opens the panel for what is at <paramref name="cell"/>. False (and nothing changes) if there is nothing to set there.</summary>
        public bool Open(Simulation sim, int2 cell)
        {
            var target = TargetAt(sim, cell, out int building);
            if (target == ConfigTarget.None) return false;
            Target = target;
            Cell = cell;
            _building = building;
            _defShown = building >= 0 ? sim.Buildings.Get(building).Def : -1;
            Build(sim);
            _root.RemoveFromClassList("hidden");
            Refresh(sim);
            return true;
        }

        /// <summary>Opens the stock view: the reserve kept for building, per item.</summary>
        public void OpenStock(Simulation sim)
        {
            Target = ConfigTarget.Stock;
            _building = -1;
            Build(sim);
            _root.RemoveFromClassList("hidden");
            Refresh(sim);
        }

        void Build(Simulation sim)
        {
            _list.Clear();
            _options.Clear();
            _stockLabels.Clear();
            _list.EnableInClassList("configure__list--grid", Target is ConfigTarget.Storage or ConfigTarget.Chest or ConfigTarget.Unloader or ConfigTarget.Filter);
            switch (Target)
            {
                case ConfigTarget.Storage:
                case ConfigTarget.Chest:
                {
                    _title.text = Target == ConfigTarget.Chest ? "Chest takes" : "Storage zone takes";
                    var row = new VisualElement();
                    row.AddToClassList("configure__row");
                    var all = new Button(() => StorageFilterChanged?.Invoke(Cell, ulong.MaxValue)) { text = "All", focusable = false };
                    var none = new Button(() => StorageFilterChanged?.Invoke(Cell, 0)) { text = "None", focusable = false };
                    all.AddToClassList("small-button");
                    none.AddToClassList("small-button");
                    row.Add(all);
                    row.Add(none);
                    _list.Add(row);
                    for (ushort i = 0; i < _content.Items.Length && i < 64; i++)
                    {
                        if (_content.Items[i].IsFood) continue;
                        ushort item = i;
                        AddOption(_content.Items[i].DisplayName, _content.Items[i].color, i, () => ToggleItem(sim, item), "item_" + _content.Items[i].Id);
                    }
                    _hint.text = Target == ConfigTarget.Chest
                        ? "Ants and belts only bring the items you choose. What is already inside stays."
                        : "Every storage cell connected to this one only takes the items you choose.";
                    break;
                }
                case ConfigTarget.Unloader:
                case ConfigTarget.Filter:
                {
                    bool unloader = Target == ConfigTarget.Unloader;
                    _title.text = unloader ? "Unloader takes out" : "Filter sends ahead";
                    if (unloader) AddOption("Any item", new Color(0.8f, 0.8f, 0.8f), ContentDB.NoItem, () => ItemPicked?.Invoke(Cell, ContentDB.NoItem));
                    for (ushort i = 0; i < _content.Items.Length; i++)
                    {
                        if (unloader && _content.Items[i].IsFood) continue;
                        ushort item = i;
                        AddOption(_content.Items[i].DisplayName, _content.Items[i].color, i, () => ItemPicked?.Invoke(Cell, item), "item_" + _content.Items[i].Id);
                    }
                    _hint.text = unloader
                        ? "It takes from the storage, chest or royal chamber behind it (opposite its arrow). R turns new ones."
                        : "The chosen item leaves in the arrow direction; everything else goes to the other sides.";
                    break;
                }
                case ConfigTarget.Emitter:
                    _title.text = "Emitter scent";
                    for (int k = 0; k < Pheromones.Count; k++)
                    {
                        var kind = (PheromoneKind)k;
                        if (!ToolController.ScentAvailable(sim, kind)) continue;
                        var colors = sim.Config.render.pheromoneColors;
                        var color = colors != null && k < colors.Length ? colors[k] : Color.white;
                        AddOption(HudController.ScentName(kind), color, k, () => ItemPicked?.Invoke(Cell, (ushort)kind));
                    }
                    _hint.text = "Painted scent of this kind around the emitter never fades.";
                    break;
                case ConfigTarget.Machine:
                {
                    var data = sim.Content.Buildings[_defShown];
                    _title.text = data.Def.DisplayName + " runs";
                    AddOption("Any recipe it can", new Color(0.8f, 0.8f, 0.8f), -1, () => RecipePicked?.Invoke(Cell, -1));
                    for (int r = 0; r < data.Recipes.Length; r++)
                    {
                        int choice = r;
                        AddOption(RecipeText(sim.Content.Recipes[data.Recipes[r]]), _content.Buildings[_defShown].Def.color, r,
                            () => RecipePicked?.Invoke(Cell, choice));
                    }
                    _hint.text = "With one recipe chosen the machine only takes in what that recipe needs.";
                    break;
                }
                case ConfigTarget.Stock:
                    _title.text = "Keep for building";
                    for (ushort i = 0; i < _content.Items.Length; i++)
                    {
                        if (_content.Items[i].IsFood) continue;
                        ushort item = i;
                        var row = new VisualElement();
                        row.AddToClassList("configure__row");
                        var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                        if (!HudController.SetIcon(swatch, _art, "item_" + _content.Items[i].Id, "resource__icon"))
                        {
                            swatch.AddToClassList("caste__swatch");
                            swatch.style.backgroundColor = _content.Items[i].color;
                        }
                        var name = new Label(_content.Items[i].DisplayName) { pickingMode = PickingMode.Ignore };
                        name.AddToClassList("configure__name");
                        var value = new Label { pickingMode = PickingMode.Ignore };
                        value.AddToClassList("configure__value");
                        var less = new Button(() => ReserveStep?.Invoke(item, -ReserveStepSize)) { text = "-", focusable = false };
                        var more = new Button(() => ReserveStep?.Invoke(item, ReserveStepSize)) { text = "+", focusable = false };
                        less.AddToClassList("small-button");
                        more.AddToClassList("small-button");
                        row.Add(swatch);
                        row.Add(name);
                        row.Add(value);
                        row.Add(less);
                        row.Add(more);
                        _list.Add(row);
                        _stockLabels.Add((value, item));
                    }
                    _hint.text = "Machines and unloaders leave this many of each item in storage, so you can still build.";
                    break;
            }
        }

        string RecipeText(RecipeData recipe)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < recipe.InputItems.Length; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(recipe.InputCounts[i]).Append(' ').Append(_content.Items[recipe.InputItems[i]].DisplayName);
            }
            sb.Append(" -> ");
            if (recipe.OutputItems.Length == 0 && recipe.Knowledge > 0f) sb.Append(NumberFormat.Format(recipe.Knowledge)).Append(" knowledge");
            else if (recipe.Princesses > 0) sb.Append(recipe.Princesses).Append(" princess");
            for (int i = 0; i < recipe.OutputItems.Length; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(recipe.OutputCounts[i]).Append(' ').Append(_content.Items[recipe.OutputItems[i]].DisplayName);
            }
            return sb.ToString();
        }

        /// <param name="icon">Icon name (<c>UI/Icons/</c>); without one the option shows the colour swatch.</param>
        void AddOption(string text, Color swatchColor, int value, Action clicked, string icon = null)
        {
            var button = new Button(clicked) { focusable = false };
            button.AddToClassList("building");
            button.AddToClassList("configure__option");
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            if (!HudController.SetIcon(swatch, _art, icon, "configure__icon"))
            {
                swatch.AddToClassList("building__swatch");
                swatch.style.backgroundColor = swatchColor;
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("building__name");
            label.AddToClassList("configure__option-name");
            button.Add(swatch);
            button.Add(label);
            _list.Add(button);
            _options.Add((button, value));
        }

        void ToggleItem(Simulation sim, ushort item)
        {
            var container = Container(sim);
            if (container == null) return;
            ulong mask = (container.Allow & _allItems) ^ (1ul << item);
            StorageFilterChanged?.Invoke(Cell, mask == _allItems ? ulong.MaxValue : mask);
        }

        StockContainer Container(Simulation sim) =>
            sim.World.InBounds(Cell.x, Cell.y) ? sim.Colony.Inventory.ContainerAt(sim.World.Index(Cell.x, Cell.y)) : null;

        /// <summary>Marks the current choices; closes the panel when what it was opened on is gone.</summary>
        public void Refresh(Simulation sim)
        {
            if (!Visible) return;
            if (Target != ConfigTarget.Stock)
            {
                var now = TargetAt(sim, Cell, out int building);
                if (now != Target || building != _building || (building >= 0 && sim.Buildings.Get(building).Def != _defShown))
                {
                    Hide();
                    return;
                }
            }
            switch (Target)
            {
                case ConfigTarget.Storage:
                case ConfigTarget.Chest:
                {
                    var c = Container(sim);
                    ulong allow = c?.Allow ?? ulong.MaxValue;
                    foreach (var (button, value) in _options) button.EnableInClassList("building--active", (allow & (1ul << value)) != 0);
                    if (Target == ConfigTarget.Chest && c != null) _title.text = $"Chest takes ({c.Total}/{c.Capacity})";
                    else if (Target == ConfigTarget.Storage) _title.text = $"Storage zone takes ({sim.ZoneCells(Cell).Count} cells)";
                    break;
                }
                case ConfigTarget.Unloader:
                case ConfigTarget.Filter:
                case ConfigTarget.Emitter:
                {
                    int current = sim.Buildings.Get(_building).Filter;
                    foreach (var (button, value) in _options) button.EnableInClassList("building--active", value == current);
                    break;
                }
                case ConfigTarget.Machine:
                {
                    var m = sim.Buildings.MachineOf(_building);
                    int current = m?.Choice ?? -1;
                    foreach (var (button, value) in _options) button.EnableInClassList("building--active", value == current);
                    break;
                }
                case ConfigTarget.Stock:
                {
                    var inv = sim.Colony.Inventory;
                    foreach (var (label, item) in _stockLabels)
                    {
                        string text = $"{NumberFormat.Format(inv.Items[item])}  keep {inv.Reserve[item]}";
                        if (label.text != text) label.text = text;
                    }
                    break;
                }
            }
        }
    }
}
