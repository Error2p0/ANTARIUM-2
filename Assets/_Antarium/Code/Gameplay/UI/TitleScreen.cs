using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The title screen over the paused colony: Continue (the saved colony, with offline progress), New colony,
    /// Settings, Achievements, Quit. Starting a new colony over an existing save asks for a second click.
    /// </summary>
    public sealed class TitleScreen
    {
        const float ConfirmSeconds = 3f;

        readonly Button _continue, _newGame;
        readonly Label _status;
        float _confirm;
        bool _hasSave;

        public VisualElement Root { get; }
        public event Action Continue, NewColony, Settings, Achievements, Help, Quit;

        public TitleScreen()
        {
            Root = new VisualElement();
            Root.AddToClassList("title");
            var logo = new Label("Antarium");
            logo.AddToClassList("title__logo");
            Root.Add(logo);
            Dialogs.Text(Root, "Dig deep. Build the colony. Raise a dynasty.", "title__tagline");

            var menu = new VisualElement();
            menu.AddToClassList("panel");
            menu.AddToClassList("title__menu");
            Root.Add(menu);
            _continue = Dialogs.Button(menu, "Continue", () => Continue?.Invoke());
            _status = Dialogs.Text(menu, "", "dialog__note");
            _newGame = Dialogs.Button(menu, "New colony", () =>
            {
                if (!_hasSave || _confirm > 0f)
                {
                    _confirm = 0f;
                    NewColony?.Invoke();
                }
                else _confirm = ConfirmSeconds;
            });
            Dialogs.Button(menu, "Settings", () => Settings?.Invoke());
            Dialogs.Button(menu, "Achievements", () => Achievements?.Invoke());
            Dialogs.Button(menu, "Controls and help", () => Help?.Invoke());
            Dialogs.Button(menu, "Quit", () => Quit?.Invoke());

            var version = new Label($"v{Application.version}");
            version.AddToClassList("title__version");
            Root.Add(version);
        }

        /// <param name="saveStatus">One line about the saved colony, or null when there is no save.</param>
        public void SetSave(string saveStatus)
        {
            _hasSave = saveStatus != null;
            _continue.EnableInClassList("hidden", !_hasSave);
            _status.EnableInClassList("hidden", !_hasSave);
            _status.text = saveStatus ?? "";
            _confirm = 0f;
        }

        public void Update(float dt)
        {
            if (_confirm > 0f) _confirm -= dt;
            bool confirming = _confirm > 0f;
            _newGame.text = confirming ? "Click again: abandon the saved colony?" : "New colony";
            _newGame.EnableInClassList("menu-button--warn", confirming);
        }
    }
}
