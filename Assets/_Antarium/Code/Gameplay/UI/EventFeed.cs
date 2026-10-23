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
    /// Cards on the left: random events (what happened, the time left to act), raids (warning countdown,
    /// raiders left, outcome) and creatures found in a newly opened cave. A Go button moves the camera there.
    /// Finished cards stay a few seconds with their outcome.
    /// </summary>
    public sealed class EventFeed
    {
        const float InstantLinger = 30f;
        const float EndedLinger = 8f;

        enum CardKind : byte { RandomEvent, Raid, Creatures }

        sealed class Card
        {
            public CardKind Kind;
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
        Simulation _sim;
        int _nextCreatureCard = 1;

        /// <summary>Where to move the camera (the event's current place).</summary>
        public event Action<int2> GoClicked;

        public EventFeed(VisualElement container, ContentDB content)
        {
            _container = container;
            _content = content;
        }

        /// <summary>Drops all cards, then adds one for every event still waiting in <paramref name="sim"/> (a loaded save).</summary>
        public void Reset(Simulation sim)
        {
            _sim = sim;
            foreach (var c in _cards) c.Root.RemoveFromHierarchy();
            _cards.Clear();
            foreach (var e in sim.RandomEvents.Active)
                AddRandomCard(new RandomEventEvent(e.Def, e.Id, RandomEventPhase.Started, e.Cell), _content.Events[e.Def].Def);
            var threats = sim.Enemies;
            if (threats.RaidIncoming) AddRaidCard(new RaidEvent(RaidPhase.Warning, threats.RaidsStarted + 1, threats.RaidCell, threats.NextRaidSize(), 0f));
            else if (threats.CountRaiders() > 0) AddRaidCard(new RaidEvent(RaidPhase.Started, threats.ActiveRaid, threats.RaidCell, threats.CountRaiders(), 0f));
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
                    AddRandomCard(e, def);
                    toast = def.DisplayName + "!";
                    continue;
                }
                var card = Find(CardKind.RandomEvent, e.Id);
                bool won = e.Phase == RandomEventPhase.Won;
                string outcome = Outcome(e.Event, won);
                toast = outcome;
                if (card != null) End(card, outcome, won ? "Done" : "Missed", won);
            }
            foreach (var e in events.Read<RaidEvent>())
                toast = OnRaid(e);
            foreach (var e in events.Read<CreaturesFoundEvent>())
                toast = OnCreatures(e);
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

        string RaiderName(int count)
        {
            var raiders = _content.Raiders;
            string name = raiders.Length > 0 ? _content.Enemies[raiders[0]].Def.DisplayName : "raider";
            return HudText.Plural(count, name);
        }

        string OnRaid(in RaidEvent e)
        {
            var card = Find(CardKind.Raid, e.Id);
            switch (e.Phase)
            {
                case RaidPhase.Warning:
                    card ??= AddRaidCard(e);
                    return $"Raid incoming! {RaiderName(e.Size)} will arrive from the {Side(e.Cell)}.";
                case RaidPhase.Started:
                    card ??= AddRaidCard(e);
                    card.Title.text = "Raid!";
                    card.Body.text = $"{RaiderName(e.Size)} are heading for the royal chamber. Fight them off before they eat your food.";
                    card.Cell = e.Cell;
                    return "The raiders are here!";
                case RaidPhase.Repelled:
                {
                    string text = "Raid repelled! The raiders are gone, the food is safe.";
                    if (card != null) End(card, text, "Repelled", true);
                    return text;
                }
                default:
                {
                    string text = $"The raid is over. Raiders got away with {NumberFormat.Format(e.FoodLost)} food.";
                    if (card != null) End(card, text, "Over", false);
                    return text;
                }
            }
        }

        string OnCreatures(in CreaturesFoundEvent e)
        {
            var def = _content.Enemies[e.Def].Def;
            string who = HudText.Plural(e.Count, def.DisplayName);
            var card = NewCard(CardKind.Creatures, _nextCreatureCard++, e.Def, e.Cell, def.DisplayName + "!",
                $"{char.ToUpperInvariant(who[0])}{who.Substring(1)} {(e.Count == 1 ? "lives" : "live")} in the cave you broke into. {def.description}");
            card.Linger = InstantLinger;
            card.Timer.text = "Beware!";
            return $"Something stirs in the cave: {HudText.Plural(e.Count, def.DisplayName)}.";
        }

        string Side(int2 cell) => _sim != null && cell.x < _sim.Colony.QueenCell.x ? "left" : "right";

        void AddRandomCard(in RandomEventEvent e, RandomEventDef def)
        {
            var card = NewCard(CardKind.RandomEvent, e.Id, e.Event, e.Cell, def.DisplayName, def.description);
            card.Linger = def.duration > 0f ? 0f : InstantLinger;
        }

        Card AddRaidCard(in RaidEvent e)
        {
            var card = NewCard(CardKind.Raid, e.Id, -1, e.Cell, "Raid incoming!",
                $"{RaiderName(e.Size)} are coming from the {Side(e.Cell)}. Gates, traps and soldiers help.");
            card.Root.AddToClassList("event--danger");
            return card;
        }

        Card NewCard(CardKind kind, int id, int def, int2 cell, string title, string body)
        {
            var card = new Card { Kind = kind, Id = id, Def = def, Cell = cell, Root = new VisualElement() };
            card.Root.AddToClassList("event");
            card.Title = Label(card.Root, "contract__title", title);
            card.Body = Label(card.Root, "contract__text", body);
            var row = new VisualElement();
            row.AddToClassList("event__row");
            card.Timer = Label(row, "event__timer", "");
            var go = new Button(() => GoClicked?.Invoke(CurrentCell(card))) { text = "Go", focusable = false };
            go.AddToClassList("small-button");
            row.Add(go);
            var close = new Button(() => Remove(card)) { text = "x", focusable = false };
            close.AddToClassList("small-button");
            row.Add(close);
            card.Root.Add(row);
            _container.Add(card.Root);
            _cards.Add(card);
            return card;
        }

        void End(Card card, string text, string timer, bool good)
        {
            card.Ended = true;
            card.Linger = EndedLinger;
            card.Body.text = text;
            card.Timer.text = timer;
            card.Root.EnableInClassList(good ? "event--won" : "event--lost", true);
        }

        static Label Label(VisualElement parent, string cls, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            parent.Add(label);
            return label;
        }

        Card Find(CardKind kind, int id)
        {
            foreach (var c in _cards)
                if (c.Kind == kind && c.Id == id) return c;
            return null;
        }

        void Remove(Card card)
        {
            card.Root.RemoveFromHierarchy();
            _cards.Remove(card);
        }

        public void Update(float dt, Simulation sim)
        {
            _sim = sim;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var card = _cards[i];
                if (!card.Ended)
                {
                    if (card.Kind == CardKind.RandomEvent && sim.RandomEvents.TryGetActive(card.Id, out var active))
                    {
                        card.Timer.text = active.TimeLeft > 0f ? HudText.Duration(active.TimeLeft) + " left" : "";
                        continue;
                    }
                    if (card.Kind == CardKind.Raid)
                    {
                        var threats = sim.Enemies;
                        if (threats.RaidIncoming) card.Timer.text = "Arrive in " + HudText.Duration(threats.SecondsToRaid);
                        else card.Timer.text = $"{threats.CountRaiders()} left";
                        continue;
                    }
                }
                // Instant events, creature sightings and finished cards linger for a while.
                if (card.Linger > 0f)
                {
                    if (!card.Ended && card.Timer.text.Length == 0) card.Timer.text = "Take a look!";
                    card.Linger -= dt;
                    if (card.Linger > 0f) continue;
                }
                Remove(card);
            }
        }

        /// <summary>Where the card's event is now (a beetle or raiders move), for the camera.</summary>
        int2 CurrentCell(Card card)
        {
            if (_sim == null) return card.Cell;
            switch (card.Kind)
            {
                case CardKind.RandomEvent:
                    foreach (var c in _sim.RandomEvents.Critters)
                        if (c.EventId == card.Id) return c.Cell;
                    return _sim.RandomEvents.TryGetActive(card.Id, out var e) ? e.Cell : card.Cell;
                case CardKind.Raid:
                    foreach (var enemy in _sim.Enemies.Enemies)
                        if (enemy.Raid == card.Id) return enemy.Cell;
                    return card.Cell;
                default:
                    return card.Cell;
            }
        }
    }
}
