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
        readonly VisualElement[] _icons;
        readonly ArtLibrary _art;
        /// <summary>Per achievement: 0 not drawn yet, 1 locked icon, 2 unlocked icon, 3 hidden (plain square).</summary>
        readonly byte[] _iconShown;

        public VisualElement Root { get; }
        public event Action Closed;

        public AchievementsDialog(ContentDB content, ArtLibrary art = null)
        {
            _content = content;
            _art = art;
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
            _icons = new VisualElement[n];
            _iconShown = new byte[n];
            for (int i = 0; i < n; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("achievement");
                var icon = new VisualElement();
                icon.AddToClassList("achievement__icon");
                row.Add(icon);
                _icons[i] = icon;
                var text = new VisualElement();
                text.AddToClassList("achievement__text");
                _names[i] = Dialogs.Text(text, "", "achievement__name");
                _descs[i] = Dialogs.Text(text, "", "achievement__desc");
                row.Add(text);
                _progress[i] = Dialogs.Text(row, "", "achievement__progress");
                scroll.Add(row);
                _rows[i] = row;
            }
            var close = Dialogs.Button(Root, "Close", () => Closed?.Invoke());
            Keys.Bind(() => close.text = Keys.With("Close", Keys.Menu));
        }

        /// <summary>The achievement's own icon (<c>UI/Achievements/&lt;API name&gt;</c>, grey while locked); hidden ones keep the plain square.</summary>
        void SetIcon(int i, string api, byte state)
        {
            if (_iconShown[i] == state) return;
            _iconShown[i] = state;
            string name = state == 2 ? api : api + "_locked";
            UnityEngine.Texture2D texture = null;
            bool has = state != 3 && _art != null && _art.TryGet("UI/Achievements/" + name, out texture) && texture != null;
            _icons[i].EnableInClassList("achievement__icon--art", has);
            _icons[i].style.backgroundImage = has ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
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
                SetIcon(i, data.Def.apiName, secret ? (byte)3 : done ? (byte)2 : (byte)1);
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
