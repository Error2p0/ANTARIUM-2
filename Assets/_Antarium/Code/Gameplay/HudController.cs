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
    /// Binds the Hud.uxml layout: resource bar with income rates, alerts, toolbar, build palette, the hover
    /// info panel and short toasts. Text refreshes a few times per second to avoid per-frame garbage.
    /// </summary>
    public sealed class HudController
    {
        const float RefreshInterval = 0.2f;
        const float ToastSeconds = 2.5f;

        readonly UIDocument _document;
        readonly ContentDB _content;
        readonly VisualElement _resources;
        readonly VisualElement _palette;
        readonly Label _alert, _info, _toast;
        readonly Button[] _toolButtons = new Button[4];
        readonly Button _overlayButton;
        readonly Label _food, _foodRate, _ants, _eggs, _storage;
        readonly VisualElement _storageEntry;
        readonly VisualElement[] _itemEntries;
        readonly Label[] _itemLabels, _itemRates;
        readonly Button[] _buildingButtons;
        readonly StringBuilder _sb = new();
        float _timer;
        float _toastTimer;

        public Label DebugLabel { get; }
        public event Action<ToolMode> ToolClicked;
        public event Action<int> BuildingClicked;
        public event Action OverlayClicked;

        public HudController(UIDocument document, ContentDB content)
        {
            _document = document;
            _content = content;
            if (document == null || document.rootVisualElement == null) return;

            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _resources = root.Q<VisualElement>("resources");
            _alert = root.Q<Label>("alert");
            _info = root.Q<Label>("info");
            _toast = root.Q<Label>("toast");
            _palette = root.Q<VisualElement>("build-palette");
            DebugLabel = root.Q<Label>("debug");
            var toolbar = root.Q<VisualElement>("toolbar");
            if (_resources == null || toolbar == null || _palette == null || _info == null || _toast == null)
            {
                Debug.LogError("HUD layout is missing elements; assign Hud.uxml to the UIDocument.");
                return;
            }

            _food = AddEntry(new Color(0.55f, 0.85f, 0.35f), out _, out _foodRate);
            _ants = AddEntry(new Color(0.65f, 0.3f, 0.2f), out _, out _);
            _eggs = AddEntry(new Color(0.94f, 0.9f, 0.8f), out _, out _);
            _storage = AddEntry(new Color(0.3f, 0.75f, 0.85f), out _storageEntry, out _);
            _itemEntries = new VisualElement[content.Items.Length];
            _itemLabels = new Label[content.Items.Length];
            _itemRates = new Label[content.Items.Length];
            for (int i = 0; i < content.Items.Length; i++)
            {
                if (content.Items[i].IsFood) continue;
                _itemLabels[i] = AddEntry(content.Items[i].color, out _itemEntries[i], out _itemRates[i]);
                _itemEntries[i].AddToClassList("hidden");
            }

            AddTool(toolbar, ToolMode.Dig, "1  Dig");
            AddTool(toolbar, ToolMode.Mine, "2  Mine");
            AddTool(toolbar, ToolMode.Storage, "3  Storage");
            AddTool(toolbar, ToolMode.Build, "4  Build");
            _overlayButton = new Button(() => OverlayClicked?.Invoke()) { text = "O  Flow", focusable = false };
            _overlayButton.AddToClassList("tool");
            _overlayButton.AddToClassList("tool--toggle");
            toolbar.Add(_overlayButton);

            _buildingButtons = new Button[content.Buildings.Length];
            for (int i = 0; i < content.Buildings.Length; i++)
                _buildingButtons[i] = AddBuilding(i);
            SetActiveTool(ToolMode.Dig);
            SetSelectedBuilding(0);
        }

        Label AddEntry(Color swatchColor, out VisualElement entry, out Label rate)
        {
            entry = new VisualElement();
            entry.AddToClassList("resource");
            var swatch = new VisualElement();
            swatch.AddToClassList("resource__swatch");
            swatch.style.backgroundColor = swatchColor;
            var label = new Label();
            label.AddToClassList("resource__label");
            rate = new Label();
            rate.AddToClassList("resource__rate");
            entry.Add(swatch);
            entry.Add(label);
            entry.Add(rate);
            _resources.Add(entry);
            return label;
        }

        void AddTool(VisualElement toolbar, ToolMode mode, string text)
        {
            var button = new Button(() => ToolClicked?.Invoke(mode)) { text = text, focusable = false };
            button.AddToClassList("tool");
            toolbar.Add(button);
            _toolButtons[(int)mode] = button;
        }

        Button AddBuilding(int index)
        {
            var def = _content.Buildings[index].Def;
            var button = new Button(() => BuildingClicked?.Invoke(index)) { focusable = false };
            button.AddToClassList("building");
            var swatch = new VisualElement();
            swatch.AddToClassList("building__swatch");
            swatch.style.backgroundColor = def.color;
            var text = new VisualElement();
            text.AddToClassList("building__text");
            text.pickingMode = PickingMode.Ignore;
            var name = new Label(def.DisplayName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("building__name");
            var cost = new Label(CostText(_content.Buildings[index])) { pickingMode = PickingMode.Ignore };
            cost.AddToClassList("building__cost");
            text.Add(name);
            text.Add(cost);
            swatch.pickingMode = PickingMode.Ignore;
            button.Add(swatch);
            button.Add(text);
            _palette.Add(button);
            return button;
        }

        public string CostText(BuildingData data)
        {
            _sb.Clear();
            for (int i = 0; i < data.CostItems.Length; i++)
            {
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(data.CostCounts[i]).Append(' ').Append(_content.Items[data.CostItems[i]].DisplayName);
            }
            if (data.Def.foodCost > 0f)
            {
                if (_sb.Length > 0) _sb.Append(", ");
                _sb.Append(NumberFormat.Format(data.Def.foodCost)).Append(" Food");
            }
            return _sb.Length > 0 ? _sb.ToString() : "Free";
        }

        public void SetActiveTool(ToolMode mode)
        {
            for (int i = 0; i < _toolButtons.Length; i++)
                _toolButtons[i]?.EnableInClassList("tool--active", i == (int)mode);
            _palette?.EnableInClassList("hidden", mode != ToolMode.Build);
        }

        public void SetSelectedBuilding(int index)
        {
            if (_buildingButtons == null) return;
            for (int i = 0; i < _buildingButtons.Length; i++)
                _buildingButtons[i].EnableInClassList("building--active", i == index);
        }

        public void SetOverlay(bool on) => _overlayButton?.EnableInClassList("tool--on", on);

        /// <summary>Hover / selection details; null hides the panel.</summary>
        public void SetInfo(string text)
        {
            if (_info == null) return;
            _info.EnableInClassList("hidden", string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) _info.text = text;
        }

        public void ShowToast(string text)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toast.RemoveFromClassList("hidden");
            _toastTimer = ToastSeconds;
        }

        /// <summary>True when the pointer is over an interactive HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel == null) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPos) != null;
        }

        /// <summary>Turns refused placements into a toast.</summary>
        public void Consume(EventBus events, Simulation sim)
        {
            var failed = events.Read<PlaceFailedEvent>();
            if (failed.Length > 0) ShowToast(PlaceMessage(failed[failed.Length - 1].Result, failed[failed.Length - 1].Def, sim));
        }

        public string PlaceMessage(PlaceResult result, int def, Simulation sim)
        {
            var data = def >= 0 && def < _content.Buildings.Length ? _content.Buildings[def] : null;
            switch (result)
            {
                case PlaceResult.Hidden: return "You can only build where you can see.";
                case PlaceResult.NotOpen:
                    return data != null && !data.Def.IsTransport ? "Machines need dug-out space underground." : "Belts need dug-out space or the ground surface.";
                case PlaceResult.Occupied: return "Something is already built there.";
                case PlaceResult.NeedsFloor: return "Machines need a flat, solid floor under their whole width.";
                case PlaceResult.OutOfBounds: return "That is outside the world.";
                case PlaceResult.CantAfford:
                {
                    if (data == null) return "Not enough resources.";
                    var inv = sim.Colony.Inventory;
                    for (int i = 0; i < data.CostItems.Length; i++)
                        if (inv.Items[data.CostItems[i]] < data.CostCounts[i])
                            return $"Not enough {_content.Items[data.CostItems[i]].DisplayName} ({inv.Items[data.CostItems[i]]}/{data.CostCounts[i]}).";
                    return "Not enough food.";
                }
                default: return "Can't build there.";
            }
        }

        public void Update(float dt, Simulation sim)
        {
            if (_resources == null) return;
            if (_toastTimer > 0f)
            {
                _toastTimer -= dt;
                if (_toastTimer <= 0f) _toast.AddToClassList("hidden");
            }
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = RefreshInterval;

            var colony = sim.Colony;
            var inv = colony.Inventory;
            _food.text = $"Food {NumberFormat.Format(inv.Food)}/{NumberFormat.Format(inv.FoodCapacity)}";
            SetRate(_foodRate, inv.FoodIncomePerMinute - colony.UpkeepPerMinute(sim.Ants.Count));
            _ants.text = $"Ants {NumberFormat.Format(sim.Ants.Count)}";
            _eggs.text = $"Eggs {colony.Eggs.Count}";
            _storage.text = $"Storage {NumberFormat.Format(inv.Stored)}/{NumberFormat.Format(inv.Capacity)}";
            _storageEntry.EnableInClassList("resource--full", inv.Stored >= inv.Capacity);

            for (int i = 0; i < _itemLabels.Length; i++)
            {
                if (_itemLabels[i] == null) continue;
                int count = inv.Items[i];
                bool show = count > 0 || inv.InTransit[i] > 0;
                _itemEntries[i].EnableInClassList("hidden", !show);
                if (!show) continue;
                _itemLabels[i].text = $"{_content.Items[i].DisplayName} {NumberFormat.Format(count)}";
                SetRate(_itemRates[i], inv.IncomePerMinute[i]);
            }

            for (int i = 0; i < _buildingButtons.Length; i++)
                _buildingButtons[i].EnableInClassList("building--poor", !inv.CanAfford(_content.Buildings[i]));

            bool starving = colony.Starving;
            _alert.EnableInClassList("hidden", !starving);
            if (starving) _alert.text = "The colony is starving: work is slowed and the queen stopped laying.";
        }

        static void SetRate(Label label, float perMinute)
        {
            bool show = Mathf.Abs(perMinute) >= 0.05f;
            label.EnableInClassList("hidden", !show);
            if (!show) return;
            label.EnableInClassList("resource__rate--negative", perMinute < 0f);
            string value = Mathf.Abs(perMinute) < 10f ? Mathf.Abs(perMinute).ToString("0.0") : NumberFormat.Format(Mathf.Abs(perMinute));
            label.text = (perMinute < 0f ? "-" : "+") + value + "/min";
        }
    }
}
