using System;
using System.Collections.Generic;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>The contract board on the right: goal, progress bar, reward and a Claim button per contract.</summary>
    public sealed class ContractsPanel
    {
        sealed class Card
        {
            public VisualElement Root, Fill;
            public Label Title, Goal, Note, Reward;
            public Button Claim;
            public int Def = -1;
            public long Progress = -1, Target = -1;
        }

        readonly ContentDB _content;
        readonly VisualElement _panel;
        readonly List<Card> _cards = new();
        readonly StringBuilder _sb = new();

        public event Action<int> ClaimClicked;

        public ContractsPanel(VisualElement container, ContentDB content)
        {
            _content = content;
            _panel = new VisualElement();
            _panel.AddToClassList("panel");
            var title = new Label("Contracts");
            title.AddToClassList("panel__title");
            _panel.Add(title);
            container.Add(_panel);
        }

        Card AddCard(int slot)
        {
            var card = new Card { Root = new VisualElement() };
            card.Root.AddToClassList("contract");
            card.Title = AddLabel(card.Root, "contract__title");
            card.Note = AddLabel(card.Root, "contract__text");
            card.Goal = AddLabel(card.Root, "contract__text");
            var bar = new VisualElement();
            bar.AddToClassList("bar");
            card.Fill = new VisualElement();
            card.Fill.AddToClassList("bar__fill");
            bar.Add(card.Fill);
            card.Root.Add(bar);
            card.Reward = AddLabel(card.Root, "contract__reward");
            card.Claim = new Button(() => ClaimClicked?.Invoke(slot)) { text = "Claim reward", focusable = false };
            card.Claim.AddToClassList("claim");
            card.Root.Add(card.Claim);
            _panel.Add(card.Root);
            return card;
        }

        static Label AddLabel(VisualElement parent, string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            parent.Add(label);
            return label;
        }

        public void Refresh(Simulation sim)
        {
            var active = sim.Contracts.Active;
            while (_cards.Count < active.Count) _cards.Add(AddCard(_cards.Count));
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                bool shown = i < active.Count;
                card.Root.EnableInClassList("hidden", !shown);
                if (!shown) continue;
                var c = active[i];
                if (card.Def == c.Def && card.Progress == c.Progress && card.Target == c.Target) continue;
                card.Def = c.Def;
                card.Progress = c.Progress;
                card.Target = c.Target;

                var data = _content.Contracts[c.Def];
                var def = data.Def;
                card.Title.text = def.introOrder > 0 ? $"{def.DisplayName}  ({def.introOrder}/{IntroCount()})" : def.DisplayName;
                card.Note.text = def.description ?? string.Empty;
                card.Note.EnableInClassList("hidden", string.IsNullOrEmpty(def.description));
                card.Goal.text = Goal(data, c);
                card.Fill.style.width = Length.Percent(c.Target > 0 ? Math.Min(100f, 100f * c.Progress / c.Target) : 100f);
                _sb.Clear().Append("Reward: ");
                HudText.AppendReward(_sb, _content, data.RewardItems, data.RewardCounts, c.RewardScale, def.rewardFood * c.RewardScale, def.rewardKnowledge * c.RewardScale);
                card.Reward.text = _sb.ToString();
                card.Root.EnableInClassList("contract--done", c.IsDone);
                card.Claim.EnableInClassList("hidden", !c.IsDone);
            }
        }

        int IntroCount()
        {
            int n = 0;
            foreach (var c in _content.Contracts)
                if (c.Def.introOrder > 0) n++;
            return n;
        }

        string Goal(ContractData data, ActiveContract c)
        {
            string progress = $"{NumberFormat.Format(Math.Min(c.Progress, c.Target))}/{NumberFormat.Format(c.Target)}";
            string item = data.Item != ContentDB.NoItem ? _content.Items[data.Item].DisplayName : "";
            return data.Def.kind switch
            {
                ContractKind.Deliver => $"Deliver {item}: {progress}",
                ContractKind.Craft => $"Make {item}: {progress}",
                ContractKind.ReachDepth => $"Deepest dig: {progress}",
                ContractKind.DigDeeper => $"Dig deeper: {progress} cells",
                ContractKind.HatchAnts => $"Hatch workers: {progress}",
                ContractKind.Build => $"Build {_content.Buildings[data.Building].Def.DisplayName}: {progress}",
                ContractKind.DigCells => $"Dig out cells: {progress}",
                _ => progress,
            };
        }
    }
}
