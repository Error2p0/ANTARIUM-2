using Antarium.Content;
using Antarium.Core;
using Antarium.Presentation;
using Unity.Mathematics;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The first-colony tutorial and the help (F1): starting, resuming and ending the tutorial (its state lives in the
    /// settings, not the colony save), what the player did that its goals count, and "new in the help" messages when the
    /// colony reveals a help topic.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>A colony played longer than this (seconds) doesn't start the tutorial by itself (a returning player).</summary>
        const double TutorialMaxStartAge = 300;
        const float HelpCheckSeconds = 1f;

        HelpLibrary _helpLibrary;
        Tutorial _tutorial;
        readonly TutorialProbe _probe = new();
        bool[] _helpRevealed;
        float _helpCheck;
        float2 _lastCameraPos;
        int _lastZoom;
        /// <summary>The camera was moved by the game (Go, leaving the title): that isn't the player looking around.</summary>
        bool _cameraJumped = true;

        void SetUpTutorialAndHelp()
        {
            _helpLibrary = new HelpLibrary(content.helpTopics);
            _tutorial = new Tutorial(content.tutorialSteps);
            if (_hud.Help != null)
            {
                _hud.Help.Library = _helpLibrary;
                _hud.Help.RestartTutorial += RestartTutorial;
            }
            _tutorial.StepStarted += index =>
            {
                if (Capturing) return;
                _settings.tutorialStep = _tutorial.Steps[index].Id;
                _settingsStore.Save(_settings);
            };
            _tutorial.StepCompleted += _ => _audio?.Play(SoundCue.ContractClaim);
            _tutorial.Finished += completed =>
            {
                _settings.tutorialDone = true;
                _settings.tutorialStep = "";
                if (!Capturing) _settingsStore.Save(_settings);
                _hud.SetHighlight(TutorialHighlight.None);
                if (!completed) _hud.ShowToast($"Tutorial ended. You can start it again from the help ({Keys.Help}).");
            };
            var card = _hud.TutorialCard;
            if (card == null) return;
            card.ContinueClicked += _tutorial.Continue;
            card.SkipClicked += _tutorial.Skip;
            card.EndClicked += _tutorial.End;
            card.MoreHelpClicked += topic => OpenHelp(fromMenu: false, topic);
        }

        /// <summary>
        /// Carries on with the step saved in the settings, or starts the tutorial for a new player: settings that never
        /// finished it, a young colony of the first generation.
        /// </summary>
        void MaybeStartTutorial()
        {
            if (Capturing || _tutorial == null || _tutorial.Count == 0 || _tutorial.Active || _sim == null) return;
            int resume = _tutorial.IndexOf(_settings.tutorialStep);
            if (resume >= 0)
            {
                _tutorial.Start(resume);
                return;
            }
            if (_settings.tutorialDone || _sim.Legacy.Flights > 0 || _sim.PlayTime > TutorialMaxStartAge) return;
            _tutorial.Start(0);
        }

        /// <summary>Help → Restart tutorial: from the first step, whatever the colony.</summary>
        void RestartTutorial()
        {
            if (_tutorial == null || _tutorial.Count == 0) return;
            _settings.tutorialDone = false;
            _tutorial.Start(0);
            _backToMenu = false;
            _hud.HideModal();
        }

        /// <summary>Counts how far the player panned and zoomed (the tutorial's first goal).</summary>
        void TrackCamera(bool title)
        {
            var position = _camera.Position;
            int zoom = _camera.Zoom;
            if (!title && !Capturing && !_cameraJumped)
            {
                _probe.CameraTravel += math.length(position - _lastCameraPos) / WorldConstants.CellSize;
                if (zoom != _lastZoom) _probe.Zooms++;
            }
            _cameraJumped = false;
            _lastCameraPos = position;
            _lastZoom = zoom;
        }

        void UpdateTutorial(float dt, bool title, bool paused)
        {
            if (_tutorial == null) return;
            bool running = !title && !Capturing && _flight == null;
            if (running && !paused) _tutorial.Update(_sim, _probe, dt);
            _hud.TutorialCard?.Update(_tutorial, dt, _input.BindingsVersion);
            _hud.SetHighlight(running && _tutorial.Active && !_tutorial.StepDone ? _tutorial.Step.highlight : TutorialHighlight.None);
            if (!title && !Capturing) CheckNewHelp(dt);
        }

        /// <summary>Which help topics the colony has revealed when a session begins (no message for those).</summary>
        void SnapshotHelp()
        {
            if (_helpLibrary == null || _sim == null) return;
            var topics = _helpLibrary.Topics;
            if (_helpRevealed == null || _helpRevealed.Length != topics.Length) _helpRevealed = new bool[topics.Length];
            for (int i = 0; i < topics.Length; i++) _helpRevealed[i] = HelpLibrary.Revealed(topics[i], _sim);
            _helpCheck = HelpCheckSeconds;
        }

        /// <summary>Once a second: a topic the colony just revealed gets a message (when no other message is showing).</summary>
        void CheckNewHelp(float dt)
        {
            if (_helpLibrary == null || _helpRevealed == null || _sim == null) return;
            _helpCheck -= dt;
            if (_helpCheck > 0f || _hud.ToastShowing) return;
            _helpCheck = HelpCheckSeconds;
            HelpTopicDef first = null;
            var topics = _helpLibrary.Topics;
            for (int i = 0; i < topics.Length; i++)
            {
                if (_helpRevealed[i] || !HelpLibrary.Revealed(topics[i], _sim)) continue;
                _helpRevealed[i] = true;
                _hud.Help?.MarkNew(topics[i]);
                first ??= topics[i];
            }
            if (first != null) _hud.ShowToast($"New in the help: {first.DisplayName}. Press {Keys.Help} to read it.");
        }
    }
}
