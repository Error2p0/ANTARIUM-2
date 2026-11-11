using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>What the slot picker shows for one save slot (10.6).</summary>
    public struct SlotView
    {
        public bool Empty;
        /// <summary>The colony behind the title screen is this slot's.</summary>
        public bool Current;
        /// <summary>First line: generation, ants, depth. Second: play time, when it was saved.</summary>
        public string Summary, Detail;
    }

    /// <summary>
    /// The title screen over the paused colony: Continue (the saved colony, with offline progress), New colony (keeps the
    /// bloodline), Save slots (10.6: three slots, each its own bloodline: play, delete, or start a new bloodline in an empty one),
    /// Settings, Achievements, Help, Quit. Starting a new colony over a save and deleting a slot ask for a second click.
    /// </summary>
    public sealed class TitleScreen
    {
        const float ConfirmSeconds = 3f;

        readonly Button _continue, _newGame;
        readonly Label _status;
        readonly VisualElement _menu, _slots;
        readonly SlotCard[] _cards;
        float _confirm;
        bool _hasSave;

        sealed class SlotCard
        {
            public VisualElement Root;
            public Label Name, Summary, Detail;
            public Button Play, Delete;
            public float Confirm;
            public bool Empty;
        }

        public VisualElement Root { get; }
        public event Action Continue, NewColony, Settings, Achievements, Help, Quit;
        /// <summary>Play the slot (load it; the current slot = continue).</summary>
        public event Action<int> PlaySlot;
        /// <summary>Start a new bloodline in an empty slot.</summary>
        public event Action<int> NewBloodline;
        /// <summary>Delete the slot's save and backups (confirmed).</summary>
        public event Action<int> DeleteSlot;

        /// <param name="art">For the crest above the logo (<c>UI/title_emblem</c>); none without it.</param>
        public TitleScreen(Antarium.Content.ArtLibrary art = null, int slots = SaveStore.SlotCount)
        {
            Root = new VisualElement();
            Root.AddToClassList("title");
            if (art != null && art.TryGet("UI/title_emblem", out var emblem))
            {
                var crest = new VisualElement { pickingMode = PickingMode.Ignore };
                crest.AddToClassList("title__emblem");
                crest.style.backgroundImage = new StyleBackground(emblem);
                Root.Add(crest);
            }
            var logo = new Label("Antarium");
            logo.AddToClassList("title__logo");
            Root.Add(logo);
            Dialogs.Text(Root, "Dig deep. Build the colony. Raise a dynasty.", "title__tagline");

            _menu = new VisualElement();
            _menu.AddToClassList("panel");
            _menu.AddToClassList("title__menu");
            Root.Add(_menu);
            _continue = Dialogs.Button(_menu, "Continue", () => Continue?.Invoke());
            _status = Dialogs.Text(_menu, "", "dialog__note");
            _newGame = Dialogs.Button(_menu, "New colony", () =>
            {
                if (!_hasSave || _confirm > 0f)
                {
                    _confirm = 0f;
                    NewColony?.Invoke();
                }
                else _confirm = ConfirmSeconds;
            });
            Dialogs.Button(_menu, "Save slots", () => ShowSlots(true));
            Dialogs.Button(_menu, "Settings", () => Settings?.Invoke());
            Dialogs.Button(_menu, "Achievements", () => Achievements?.Invoke());
            Dialogs.Button(_menu, "Controls and help", () => Help?.Invoke());
            Dialogs.Button(_menu, "Quit", () => Quit?.Invoke());

            _slots = new VisualElement();
            _slots.AddToClassList("panel");
            _slots.AddToClassList("title__slots");
            _slots.AddToClassList("hidden");
            Root.Add(_slots);
            Dialogs.Text(_slots, "Save slots", "dialog__title");
            Dialogs.Text(_slots, "Each slot holds its own bloodline: a colony and the Genetic Memory of every flight before it. " +
                                 "Achievements are shared.", "dialog__note");
            _cards = new SlotCard[slots];
            for (int i = 0; i < slots; i++) _cards[i] = BuildCard(i);
            Dialogs.Button(_slots, "Back", () => ShowSlots(false));

            var version = new Label($"v{Application.version}");
            version.AddToClassList("title__version");
            Root.Add(version);
        }

        SlotCard BuildCard(int slot)
        {
            var card = new SlotCard { Root = new VisualElement() };
            card.Root.AddToClassList("slot");
            _slots.Add(card.Root);
            card.Name = Dialogs.Text(card.Root, $"Slot {slot + 1}", "slot__name");
            card.Summary = Dialogs.Text(card.Root, "", "slot__line");
            card.Detail = Dialogs.Text(card.Root, "", "slot__line");
            var buttons = new VisualElement();
            buttons.AddToClassList("slot__buttons");
            card.Root.Add(buttons);
            card.Play = new Button(() =>
            {
                if (card.Empty) NewBloodline?.Invoke(slot);
                else PlaySlot?.Invoke(slot);
            }) { focusable = false };
            card.Play.AddToClassList("small-button");
            card.Play.AddToClassList("slot__button");
            buttons.Add(card.Play);
            card.Delete = new Button(() =>
            {
                if (card.Confirm > 0f)
                {
                    card.Confirm = 0f;
                    DeleteSlot?.Invoke(slot);
                }
                else card.Confirm = ConfirmSeconds;
            }) { text = "Delete", focusable = false };
            card.Delete.AddToClassList("small-button");
            card.Delete.AddToClassList("slot__button");
            buttons.Add(card.Delete);
            return card;
        }

        /// <summary>Shows the slot picker instead of the menu (or back).</summary>
        public void ShowSlots(bool show)
        {
            _slots.EnableInClassList("hidden", !show);
            _menu.EnableInClassList("hidden", show);
        }

        public bool SlotsShowing => !_slots.ClassListContains("hidden");

        /// <param name="saveStatus">One line about the saved colony, or null when there is no save.</param>
        public void SetSave(string saveStatus)
        {
            _hasSave = saveStatus != null;
            _continue.EnableInClassList("hidden", !_hasSave);
            _status.EnableInClassList("hidden", !_hasSave);
            _status.text = saveStatus ?? "";
            _confirm = 0f;
        }

        public void SetSlots(SlotView[] slots)
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                var view = i < slots.Length ? slots[i] : new SlotView { Empty = true };
                card.Empty = view.Empty;
                card.Confirm = 0f;
                card.Name.text = view.Current ? $"Slot {i + 1}  -  playing" : $"Slot {i + 1}";
                card.Summary.text = view.Empty ? "Empty" : view.Summary;
                card.Detail.text = view.Empty ? "Start a new bloodline here, with no Genetic Memory." : view.Detail;
                card.Play.text = view.Empty ? "New bloodline" : view.Current ? "Continue" : "Play";
                card.Delete.EnableInClassList("hidden", view.Empty);
                card.Root.EnableInClassList("slot--current", view.Current);
                card.Root.EnableInClassList("slot--empty", view.Empty);
            }
        }

        public void Update(float dt)
        {
            if (_confirm > 0f) _confirm -= dt;
            bool confirming = _confirm > 0f;
            _newGame.text = confirming ? "Click again: abandon the saved colony?" : "New colony";
            _newGame.EnableInClassList("menu-button--warn", confirming);
            foreach (var card in _cards)
            {
                if (card.Confirm > 0f) card.Confirm -= dt;
                bool warn = card.Confirm > 0f;
                card.Delete.text = warn ? "Click again: delete for good?" : "Delete";
                card.Delete.EnableInClassList("menu-button--warn", warn);
            }
        }
    }
}
