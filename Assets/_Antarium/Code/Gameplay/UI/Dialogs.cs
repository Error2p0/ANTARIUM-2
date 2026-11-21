using System;
using System.Text;
using Antarium.Content;
using Antarium.Core;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Esc menu: resume, save, load, start a new colony or a new bloodline (both confirmed by a second click), settings,
    /// achievements, help, back to the title screen, quit.
    /// </summary>
    public sealed class MenuDialog
    {
        const float ConfirmSeconds = 3f;

        readonly Button _newGame, _newBloodline, _achievements, _wishlist, _fullGame;
        readonly Label _status;
        float _confirm, _confirmBloodline;

        public VisualElement Root { get; }
        public event Action Resume, Save, Load, NewGame, NewBloodline, Settings, Achievements, Help, MainMenu, Quit;
        /// <summary>The demo (GDD 2.16): Wishlist on Steam, and what the full game adds.</summary>
        public event Action Wishlist, FullGame;

        public MenuDialog(float autosaveSeconds)
        {
            Root = Dialogs.Box("Antarium");
            _status = Dialogs.Text(Root, "", "dialog__note");
            var resume = Dialogs.Button(Root, "Resume", () => Resume?.Invoke());
            var save = Dialogs.Button(Root, "Save", () => Save?.Invoke());
            var load = Dialogs.Button(Root, "Load last save", () => Load?.Invoke());
            Keys.Bind(() =>
            {
                resume.text = Keys.With("Resume", Keys.Menu);
                save.text = Keys.With("Save", Keys.Save);
                load.text = Keys.With("Load last save", Keys.Load);
            });
            _newGame = Dialogs.Button(Root, "New colony", () =>
            {
                if (_confirm > 0f)
                {
                    _confirm = 0f;
                    NewGame?.Invoke();
                }
                else _confirm = ConfirmSeconds;
            });
            _newBloodline = Dialogs.Button(Root, "New bloodline", () =>
            {
                if (_confirmBloodline > 0f)
                {
                    _confirmBloodline = 0f;
                    NewBloodline?.Invoke();
                }
                else _confirmBloodline = ConfirmSeconds;
            });
            Dialogs.Button(Root, "Settings", () => Settings?.Invoke());
            _achievements = Dialogs.Button(Root, "Achievements", () => Achievements?.Invoke());
            var help = Dialogs.Button(Root, "Help and controls", () => Help?.Invoke());
            Keys.Bind(() => help.text = Keys.With("Help and controls", Keys.Help));
            _fullGame = Dialogs.Button(Root, "The full game", () => FullGame?.Invoke());
            _fullGame.AddToClassList("hidden");
            _wishlist = Dialogs.Button(Root, "Wishlist on Steam", () => Wishlist?.Invoke());
            _wishlist.AddToClassList("menu-button--wishlist");
            _wishlist.AddToClassList("hidden");
            Dialogs.Button(Root, "Save and return to title", () => MainMenu?.Invoke());
            Dialogs.Button(Root, "Save and quit", () => Quit?.Invoke());
            Dialogs.Text(Root, (autosaveSeconds > 0f ? $"The game saves itself every {HudText.Duration(autosaveSeconds)} and when you quit. "
                                                       : "The game saves when you quit. ")
                               + "The colony keeps working while the game is closed.", "dialog__note");
        }

        /// <summary>The demo: Wishlist on Steam and the full game's features instead of achievements.</summary>
        public void SetDemo(bool demo)
        {
            _wishlist.EnableInClassList("hidden", !demo);
            _fullGame.EnableInClassList("hidden", !demo);
            _achievements.EnableInClassList("hidden", demo);
        }

        public void Update(float dt, string status)
        {
            _status.text = status;
            if (_confirm > 0f) _confirm -= dt;
            bool confirming = _confirm > 0f;
            _newGame.text = confirming ? "Click again: abandon this colony?" : "New colony";
            _newGame.EnableInClassList("menu-button--warn", confirming);
            if (_confirmBloodline > 0f) _confirmBloodline -= dt;
            bool restart = _confirmBloodline > 0f;
            _newBloodline.text = restart ? "Click again: start over without Genetic Memory?" : "New bloodline";
            _newBloodline.EnableInClassList("menu-button--warn", restart);
        }
    }

    /// <summary>"Welcome back" summary of offline progress.</summary>
    public sealed class WelcomeBackDialog
    {
        readonly ContentDB _content;
        readonly Label _body;
        readonly StringBuilder _sb = new();

        public VisualElement Root { get; }
        public event Action Closed;

        public WelcomeBackDialog(ContentDB content)
        {
            _content = content;
            Root = Dialogs.Box("Welcome back!");
            _body = Dialogs.Text(Root, "", "dialog__text");
            Dialogs.Button(Root, "Continue", () => Closed?.Invoke());
        }

        public void Show(OfflineReport r)
        {
            _sb.Clear();
            _sb.Append("You were away ").Append(HudText.Duration(r.AwaySeconds)).Append(". The colony kept working at ")
                .Append(Math.Round(r.Efficiency * 100f)).Append("% of its recent pace");
            if (r.WasCapped) _sb.Append(" for the first ").Append(HudText.Duration(r.CreditedSeconds));
            _sb.Append(".\n");
            int lines = 0;
            for (int i = 0; i < r.Items.Length; i++)
            {
                if (r.Items[i] == 0) continue;
                _sb.Append('\n').Append(r.Items[i] > 0 ? "+" : "").Append(NumberFormat.Format(r.Items[i])).Append(' ').Append(_content.Items[i].DisplayName);
                lines++;
            }
            if (Math.Abs(r.Food) >= 1f) { _sb.Append('\n').Append(r.Food > 0f ? "+" : "-").Append(NumberFormat.Format(Math.Abs(r.Food))).Append(" Food"); lines++; }
            if (r.Knowledge >= 1f) { _sb.Append("\n+").Append(NumberFormat.Format(r.Knowledge)).Append(" Knowledge"); lines++; }
            if (r.Hatched > 0) { _sb.Append("\n+").Append(r.Hatched).Append(r.Hatched == 1 ? " worker hatched" : " workers hatched"); lines++; }
            if (lines == 0) _sb.Append("\nNothing much happened: build up production (belts, machines, foraging) so the colony earns while you're away.");
            if (r.ItemsLost > 0) _sb.Append("\n\nStorage was full: ").Append(NumberFormat.Format(r.ItemsLost)).Append(" items had no room. Paint more storage!");
            if (r.Starved) _sb.Append("\n\nFood ran out at some point.");
            _body.text = _sb.ToString();
        }
    }

    static class Dialogs
    {
        public static VisualElement Box(string title)
        {
            var box = new VisualElement();
            box.AddToClassList("panel");
            box.AddToClassList("dialog");
            Text(box, title, "dialog__title");
            return box;
        }

        public static Label Text(VisualElement parent, string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList(cls);
            parent.Add(label);
            return label;
        }

        public static Button Button(VisualElement parent, string text, Action click)
        {
            var button = new Button(click) { text = text, focusable = false };
            button.AddToClassList("menu-button");
            parent.Add(button);
            return button;
        }
    }
}
