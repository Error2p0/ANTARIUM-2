using System;
using System.Collections.Generic;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Cards for random events on the left: what happened, the time left to act, and a Go button that moves
    /// the camera there. Finished events stay a few seconds with their outcome.
    /// </summary>
    public sealed class EventFeed
    {
        const float InstantLinger = 30f;
        const float EndedLinger = 8f;

        sealed class Card
        {
            public int Id, Def;
            public int2 Cell;
            public bool Ended;
            public float Linger;
            public VisualElement Root;
            public Label Title, Body, Timer;
        }

        readonly ContentDB _content;
        readonly VisualElement _container;
        readonly List<Card> _cards = new();
        readonly StringBuilder _sb = new();

        /// <summary>Event id and the cell it started at.</summary>
        public event Action<int, int2> GoClicked;

        public EventFeed(VisualElement container, ContentDB content)
        {
            _container = container;
            _content = content;
        }

        /// <summary>Drops all cards, then adds one for every event still waiting in <paramref name="sim"/> (a loaded save).</summary>
        public void Reset(Simulation sim)
        {
            foreach (var c in _cards) c.Root.RemoveFromHierarchy();
            _cards.Clear();
            foreach (var e in sim.RandomEvents.Active)
                AddCard(new RandomEventEvent(e.Def, e.Id, RandomEventPhase.Started, e.Cell), _content.Events[e.Def].Def);
        }

        /// <summary>Adds cards for new events and marks finished ones. Returns a toast for the newest change, or null.</summary>
        public string Consume(EventBus events)
        {
            string toast = null;
            foreach (var e in events.Read<RandomEventEvent>())
            {
                var def = _content.Events[e.Event].Def;
                if (e.Phase == RandomEventPhase.Started)
                {
                    AddCard(e, def);
                    toast = def.DisplayName + "!";
                    continue;
                }
                var card = Find(e.Id);
                bool won = e.Phase == RandomEventPhase.Won;
                string outcome = Outcome(e.Event, won);
                toast = outcome;
                if (card == null) continue;
                card.Ended = true;
                card.Linger = EndedLinger;
                card.Body.text = outcome;
                card.Timer.text = won ? "Done" : "Missed";
                card.Root.EnableInClassList(won ? "event--won" : "event--lost", true);
            }
            return toast;
        }

        string Outcome(int index, bool won)
        {
            var data = _content.Events[index];
            var def = data.Def;
            switch (def.kind)
            {
                case RandomEventKind.GoldenBeetle:
                    if (!won) return "The golden beetle got away.";
                    _sb.Clear().Append("Caught the golden beetle: ");
                    HudText.AppendReward(_sb, _content, data.RewardItems, data.RewardCounts, 1f, def.rewardFood, def.rewardKnowledge);
                    return _sb.ToString();
                case RandomEventKind.TrappedAnts:
                    return won ? $"Rescued {def.count} ants! They join the colony." : "The trapped ants were lost.";
                default:
                    return won ? def.DisplayName + ": done." : def.DisplayName + ": missed.";
            }
        }

        void AddCard(in RandomEventEvent e, RandomEventDef def)
        {
            var card = new Card { Id = e.Id, Def = e.Event, Cell = e.Cell, Root = new VisualElement() };
            card.Root.AddToClassList("event");
            card.Title = Label(card.Root, "contract__title", def.DisplayName);
            card.Body = Label(card.Root, "contract__text", def.description);
            var row = new VisualElement();
            row.AddToClassList("event__row");
            card.Timer = Label(row, "event__timer", "");
            var go = new Button(() => GoClicked?.Invoke(card.Id, card.Cell)) { text = "Go", focusable = false };
            go.AddToClassList("small-button");
            row.Add(go);
            var close = new Button(() => Remove(card)) { text = "x", focusable = false };
            close.AddToClassList("small-button");
            row.Add(close);
            card.Root.Add(row);
            card.Linger = def.duration > 0f ? 0f : InstantLinger;
            _container.Add(card.Root);
            _cards.Add(card);
        }

        static Label Label(VisualElement parent, string cls, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            parent.Add(label);
            return label;
        }

        Card Find(int id)
        {
            foreach (var c in _cards)
                if (c.Id == id) return c;
            return null;
        }

        void Remove(Card card)
        {
            card.Root.RemoveFromHierarchy();
            _cards.Remove(card);
        }

        public void Update(float dt, Simulation sim)
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var card = _cards[i];
                if (!card.Ended && sim.RandomEvents.TryGetActive(card.Id, out var active))
                {
                    card.Timer.text = active.TimeLeft > 0f ? HudText.Duration(active.TimeLeft) + " left" : "";
                    continue;
                }
                // Instant events (meteorite, vein) and finished ones linger for a while.
                if (card.Linger > 0f)
                {
                    if (!card.Ended && card.Timer.text.Length == 0) card.Timer.text = "Take a look!";
                    card.Linger -= dt;
                    if (card.Linger > 0f) continue;
                }
                Remove(card);
            }
        }

        /// <summary>Where the event is now (a beetle moves), for the camera.</summary>
        public static int2 CurrentCell(Simulation sim, int id, int2 fallback)
        {
            foreach (var c in sim.RandomEvents.Critters)
                if (c.EventId == id) return c.Cell;
            return sim.RandomEvents.TryGetActive(id, out var e) ? e.Cell : fallback;
        }
    }
}
