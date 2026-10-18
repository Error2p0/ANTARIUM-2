using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Binds the Hud.uxml layout: resource bar, alerts and tool buttons. Text refreshes a few times per
    /// second to avoid per-frame garbage.
    /// </summary>
    public sealed class HudController
    {
        const float RefreshInterval = 0.2f;

        readonly UIDocument _document;
        readonly ContentDB _content;
        readonly VisualElement _resources;
        readonly Label _alert;
        readonly Button[] _toolButtons = new Button[3];
        readonly Label _food, _ants, _eggs;
        readonly VisualElement[] _itemEntries;
        readonly Label[] _itemLabels;
        float _timer;

        public Label DebugLabel { get; }
        public event Action<ToolMode> ToolClicked;

        public HudController(UIDocument document, ContentDB content)
        {
            _document = document;
            _content = content;
            if (document == null || document.rootVisualElement == null) return;

            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _resources = root.Q<VisualElement>("resources");
            _alert = root.Q<Label>("alert");
            DebugLabel = root.Q<Label>("debug");
            var toolbar = root.Q<VisualElement>("toolbar");
            if (_resources == null || toolbar == null)
            {
                Debug.LogError("HUD layout is missing elements; assign Hud.uxml to the UIDocument.");
                return;
            }

            _food = AddEntry(new Color(0.55f, 0.85f, 0.35f), out _);
            _ants = AddEntry(new Color(0.65f, 0.3f, 0.2f), out _);
            _eggs = AddEntry(new Color(0.94f, 0.9f, 0.8f), out _);
            _itemEntries = new VisualElement[content.Items.Length];
            _itemLabels = new Label[content.Items.Length];
            for (int i = 0; i < content.Items.Length; i++)
            {
                if (content.Items[i].IsFood) continue;
                _itemLabels[i] = AddEntry(content.Items[i].color, out _itemEntries[i]);
                _itemEntries[i].AddToClassList("hidden");
            }

            AddTool(toolbar, ToolMode.Dig, "1  Dig");
            AddTool(toolbar, ToolMode.Mine, "2  Mine");
            AddTool(toolbar, ToolMode.Storage, "3  Storage");
            SetActiveTool(ToolMode.Dig);
        }

        Label AddEntry(Color swatchColor, out VisualElement entry)
        {
            entry = new VisualElement();
            entry.AddToClassList("resource");
            var swatch = new VisualElement();
            swatch.AddToClassList("resource__swatch");
            swatch.style.backgroundColor = swatchColor;
            var label = new Label();
            label.AddToClassList("resource__label");
            entry.Add(swatch);
            entry.Add(label);
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

        public void SetActiveTool(ToolMode mode)
        {
            for (int i = 0; i < _toolButtons.Length; i++)
                _toolButtons[i]?.EnableInClassList("tool--active", i == (int)mode);
        }

        /// <summary>True when the pointer is over an interactive HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel == null) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPos) != null;
        }

        public void Update(float dt, Simulation sim)
        {
            if (_resources == null) return;
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = RefreshInterval;

            var colony = sim.Colony;
            var inv = colony.Inventory;
            _food.text = $"Food {NumberFormat.Format(inv.Food)}/{NumberFormat.Format(inv.FoodCapacity)}";
            _ants.text = $"Ants {NumberFormat.Format(sim.Ants.Count)}";
            _eggs.text = $"Eggs {colony.Eggs.Count}";

            for (int i = 0; i < _itemLabels.Length; i++)
            {
                if (_itemLabels[i] == null) continue;
                int count = inv.Items[i];
                bool show = count > 0 || inv.InTransit[i] > 0;
                _itemEntries[i].EnableInClassList("hidden", !show);
                if (!show) continue;
                _itemLabels[i].text = $"{_content.Items[i].DisplayName} {NumberFormat.Format(count)}/{NumberFormat.Format(inv.ItemCapacity)}";
                _itemEntries[i].EnableInClassList("resource--full", count >= inv.ItemCapacity);
            }

            bool starving = colony.Starving;
            _alert.EnableInClassList("hidden", !starving);
            if (starving) _alert.text = "The colony is starving: work is slowed and the queen stopped laying.";
        }
    }
}
