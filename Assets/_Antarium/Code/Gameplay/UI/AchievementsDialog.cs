using System;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>Every achievement with its progress in this colony; hidden ones show "???" until unlocked.</summary>
    public sealed class AchievementsDialog
    {
        readonly ContentDB _content;
        readonly Label _summary;
        readonly VisualElement[] _rows;
        readonly Label[] _names, _descs, _progress;

        public VisualElement Root { get; }
        public event Action Closed;

        public AchievementsDialog(ContentDB content)
        {
            _content = content;
            Root = Dialogs.Box("Achievements");
            Root.AddToClassList("settings");
            _summary = Dialogs.Text(Root, "", "dialog__note");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("scroll");
            Root.Add(scroll);
            int n = content.Achievements.Length;
            _rows = new VisualElement[n];
            _names = new Label[n];
            _descs = new Label[n];
            _progress = new Label[n];
            for (int i = 0; i < n; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("achievement");
                var icon = new VisualElement();
                icon.AddToClassList("achievement__icon");
                row.Add(icon);
                var text = new VisualElement();
                text.AddToClassList("achievement__text");
                _names[i] = Dialogs.Text(text, "", "achievement__name");
                _descs[i] = Dialogs.Text(text, "", "achievement__desc");
                row.Add(text);
                _progress[i] = Dialogs.Text(row, "", "achievement__progress");
                scroll.Add(row);
                _rows[i] = row;
            }
            Dialogs.Button(Root, "Close (Esc)", () => Closed?.Invoke());
        }

        /// <param name="sim">The colony progress is measured in; null on the title screen without a colony.</param>
        public void Refresh(AchievementTracker tracker, Simulation sim)
        {
            _summary.text = $"{tracker.UnlockedCount} of {tracker.Count} unlocked. Achievements belong to you, not to a colony: once unlocked they stay.";
            for (int i = 0; i < _rows.Length; i++)
            {
                var data = _content.Achievements[i];
                bool done = tracker.IsUnlocked(i);
                bool secret = data.Def.hidden && !done;
                _rows[i].EnableInClassList("achievement--done", done);
                _names[i].text = secret ? "???" : data.Def.DisplayName;
                _descs[i].text = secret ? "A hidden achievement. Keep exploring." : data.Def.description;
                if (done) _progress[i].text = "Unlocked";
                else if (secret || sim == null) _progress[i].text = "";
                else
                {
                    long have = Math.Min(AchievementProgress.Measure(sim, data), data.Def.amount);
                    _progress[i].text = $"{NumberFormat.Format(have)} / {NumberFormat.Format(data.Def.amount)}";
                }
            }
        }
    }
}
