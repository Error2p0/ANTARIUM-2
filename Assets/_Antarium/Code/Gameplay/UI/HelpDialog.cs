using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Help (F1). <b>Guide</b>: the help topics by category on the left, the chosen page on the right; topics the colony
    /// hasn't reached yet are listed as "???", topics revealed this session are marked new. <b>Controls</b>: the current
    /// key for every action, the mouse and a few tips. Restart tutorial starts the first-colony tutorial again.
    /// </summary>
    public sealed class HelpDialog
    {
        static readonly string[] Pages = { "Guide", "Controls" };

        static readonly (string Key, string Text)[] Fixed =
        {
            ("Mouse wheel", "Zoom in and out at the cursor"),
            ("Drag (Build tool)", "Lay a line of belts or lifts; the drag direction sets the flow"),
            ("Right drag", "Erase orders, zones, scents or buildings"),
        };

        static string[] Tips() => new[]
        {
            "The contracts on the right always show a next goal.",
            $"Paint Mine orders ({Keys.Mine}) and Storage ({Keys.Storage}); workers dig and haul on their own.",
            $"Hover anything in the world or on the HUD for details. {Keys.Flow} shows how items flow, {Keys.Light} the light, {Keys.Ores} the ores you know of.",
            "The colony keeps working while the game is closed (at half pace, up to 8 hours).",
        };

        readonly GameInput _input;
        readonly ArtLibrary _art;
        readonly VisualElement _pageIcon;
        readonly Button[] _tabs = new Button[Pages.Length];
        readonly VisualElement _guide, _controls, _topicList, _controlsList;
        readonly Label _pageTitle, _pageBody;
        readonly ScrollView _pageScroll;
        readonly List<Button> _topicButtons = new();
        readonly HashSet<HelpTopicDef> _new = new();
        HelpLibrary _library;
        Simulation _sim;
        HelpTopicDef _shown;
        int _pageIndex, _controlsVersion = -1, _pageVersion = -1;

        public VisualElement Root { get; }
        public event Action Closed;
        /// <summary>Restart tutorial was clicked.</summary>
        public event Action RestartTutorial;

        public HelpDialog(GameInput input, ArtLibrary art = null)
        {
            _input = input;
            _art = art;
            Root = Dialogs.Box("Help");
            Root.AddToClassList("help");
            var tabs = new VisualElement();
            tabs.AddToClassList("tabs");
            for (int i = 0; i < Pages.Length; i++)
            {
                int index = i;
                var tab = Dialogs.Button(tabs, Pages[i], () => ShowPage(index));
                tab.AddToClassList("tab");
                _tabs[i] = tab;
            }
            Root.Add(tabs);

            _guide = new VisualElement();
            _guide.AddToClassList("help__guide");
            var listScroll = new ScrollView(ScrollViewMode.Vertical);
            listScroll.AddToClassList("help__list");
            _topicList = new VisualElement();
            listScroll.Add(_topicList);
            _guide.Add(listScroll);
            var page = new VisualElement();
            page.AddToClassList("help__page");
            var titleRow = new VisualElement();
            titleRow.AddToClassList("help__page-header");
            _pageIcon = new VisualElement { pickingMode = PickingMode.Ignore };
            _pageIcon.AddToClassList("help__page-icon");
            titleRow.Add(_pageIcon);
            _pageTitle = Dialogs.Text(titleRow, "", "help__page-title");
            page.Add(titleRow);
            _pageScroll = new ScrollView(ScrollViewMode.Vertical);
            _pageScroll.AddToClassList("help__page-scroll");
            _pageBody = new Label();
            _pageBody.AddToClassList("help__page-text");
            _pageScroll.Add(_pageBody);
            page.Add(_pageScroll);
            _guide.Add(page);
            Root.Add(_guide);

            _controls = new VisualElement();
            _controls.AddToClassList("help__controls");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("scroll");
            _controlsList = new VisualElement();
            scroll.Add(_controlsList);
            _controls.Add(scroll);
            Root.Add(_controls);

            var buttons = new VisualElement();
            buttons.AddToClassList("row-buttons");
            Root.Add(buttons);
            var restart = Dialogs.Button(buttons, "Restart tutorial", () => RestartTutorial?.Invoke());
            restart.AddToClassList("help__restart");
            var close = Dialogs.Button(buttons, "Close", () => Closed?.Invoke());
            Keys.Bind(() => close.text = Keys.With("Close", Keys.Menu));
            ShowPage(0);
        }

        /// <summary>The topics to show (set once by the bootstrap).</summary>
        public HelpLibrary Library
        {
            get => _library;
            set
            {
                _library = value;
                _shown = null;
            }
        }

        /// <summary>A topic was revealed while playing: it is marked new until read.</summary>
        public void MarkNew(HelpTopicDef topic)
        {
            if (topic != null) _new.Add(topic);
        }

        /// <summary>Opens on <paramref name="topic"/> (Guide), or where it was left; <paramref name="sim"/> decides what is revealed.</summary>
        public void Open(Simulation sim, HelpTopicDef topic = null)
        {
            _sim = sim;
            if (topic != null)
            {
                _shown = topic;
                _pageIndex = 0;
            }
            if (_shown == null && _library != null && _library.Topics.Length > 0) _shown = _library.Topics[0];
            ShowPage(_pageIndex);
        }

        /// <summary>Rebuilds texts that name keys when the key bindings changed.</summary>
        public void Refresh()
        {
            if (_pageIndex == 1)
            {
                if (_controlsVersion != _input.BindingsVersion) BuildControls();
            }
            else if (_pageVersion != _input.BindingsVersion) ShowTopic(_shown);
        }

        void ShowPage(int index)
        {
            _pageIndex = index;
            for (int i = 0; i < _tabs.Length; i++) _tabs[i].EnableInClassList("tab--active", i == index);
            _guide.EnableInClassList("hidden", index != 0);
            _controls.EnableInClassList("hidden", index != 1);
            if (index == 0)
            {
                BuildTopicList();
                ShowTopic(_shown);
            }
            else BuildControls();
        }

        // ---------------------------------------------------------------- guide

        void BuildTopicList()
        {
            _topicList.Clear();
            _topicButtons.Clear();
            if (_library == null) return;
            string category = null;
            foreach (var topic in _library.Topics)
            {
                if (topic.category != category)
                {
                    category = topic.category;
                    Dialogs.Text(_topicList, category, "help__heading");
                }
                var t = topic;
                bool revealed = HelpLibrary.Revealed(topic, _sim);
                var button = new Button(() => ShowTopic(t)) { focusable = false, userData = topic };
                button.AddToClassList("help__topic");
                button.text = revealed ? topic.DisplayName : "???";
                if (revealed) HudController.AddButtonIcon(button, _art, topic.icon);
                button.EnableInClassList("help__topic--locked", !revealed);
                button.EnableInClassList("help__topic--new", revealed && _new.Contains(topic));
                _topicList.Add(button);
                _topicButtons.Add(button);
            }
        }

        void ShowTopic(HelpTopicDef topic)
        {
            _pageVersion = _input != null ? _input.BindingsVersion : 0;
            _shown = topic;
            foreach (var b in _topicButtons)
            {
                bool active = b.userData as HelpTopicDef == topic;
                b.EnableInClassList("help__topic--active", active);
                if (active && _new.Remove(topic)) b.RemoveFromClassList("help__topic--new");
            }
            if (topic == null)
            {
                _pageTitle.text = "Help";
                _pageBody.text = "No help topics found. Run Antarium > Setup > Create Default Content.";
                return;
            }
            bool revealed = HelpLibrary.Revealed(topic, _sim);
            SetPageIcon(revealed ? topic.icon : "lore_fragment_locked");
            if (!revealed)
            {
                _pageTitle.text = "???";
                _pageBody.text = HelpLibrary.LockedHint(topic);
            }
            else
            {
                _pageTitle.text = topic.DisplayName;
                _pageBody.text = HelpText.Expand(topic.body);
            }
            _pageScroll.scrollOffset = UnityEngine.Vector2.zero;
        }

        void SetPageIcon(string icon)
        {
            UnityEngine.Texture2D texture = null;
            bool has = _art != null && !string.IsNullOrEmpty(icon) && _art.TryGet("UI/Icons/" + icon, out texture);
            _pageIcon.EnableInClassList("hidden", !has);
            _pageIcon.style.backgroundImage = has ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
        }

        // ---------------------------------------------------------------- controls

        void BuildControls()
        {
            _controlsVersion = _input.BindingsVersion;
            _controlsList.Clear();
            string group = null;
            foreach (var r in _input.Rebindables)
            {
                if (r.Group != group)
                {
                    group = r.Group;
                    Dialogs.Text(_controlsList, group, "help__heading");
                }
                AddRow(GameInput.KeyName(r.Action, r.Binding), r.Name);
            }
            Dialogs.Text(_controlsList, "Mouse", "help__heading");
            foreach (var (key, text) in Fixed) AddRow(key, text);
            Dialogs.Text(_controlsList, "Tips", "help__heading");
            foreach (var tip in Tips()) Dialogs.Text(_controlsList, tip, "help__text");
            Dialogs.Text(_controlsList, "Change keys in Settings > Controls.", "dialog__note");
        }

        void AddRow(string key, string text)
        {
            var row = new VisualElement();
            row.AddToClassList("help__row");
            Dialogs.Text(row, key, "help__key");
            Dialogs.Text(row, text, "help__text");
            _controlsList.Add(row);
        }
    }
}
