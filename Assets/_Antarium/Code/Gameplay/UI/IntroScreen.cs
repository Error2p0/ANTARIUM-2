using System;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The opening intro when the game is launched: the studio logo on black, then the game logo with the story's first
    /// line, then the black lifts off the title screen (which is already up behind it). Any key, click or gamepad button
    /// skips to the lift. The logos are the PNGs in <c>Assets/_Antarium/Icon</c>, set in <c>Hud.uss</c>
    /// (<c>.intro__studio</c>, <c>.intro__game</c>). Times in seconds from the start.
    /// </summary>
    public sealed class IntroScreen
    {
        const float StudioIn = 0.4f, StudioFade = 0.7f, StudioOut = 2.6f;
        const float GameIn = 3.4f, GameFade = 0.8f, LineIn = 4.3f, LineFade = 0.7f;
        const float Lift = 6.6f, LiftSeconds = 1.1f;
        /// <summary>A skip during the logos goes straight to a short lift.</summary>
        const float SkipLiftSeconds = 0.35f;
        const float MaxStep = 1f / 20f;

        public const string StoryLine = "Long ago, the Old Colony filled these depths. Its Engine fell silent.\nNow a new queen begins to dig.";

        readonly VisualElement _studio, _game;
        readonly Label _line;
        float _time, _liftStart = Lift, _liftSeconds = LiftSeconds;

        public VisualElement Root { get; }
        public bool Playing { get; private set; }
        /// <summary>The intro has lifted off the title screen.</summary>
        public event Action Finished;

        public IntroScreen()
        {
            Root = new VisualElement { pickingMode = PickingMode.Position }; // swallows clicks meant for the title behind it
            Root.AddToClassList("intro");
            Root.AddToClassList("hidden");
            _studio = new VisualElement { pickingMode = PickingMode.Ignore };
            _studio.AddToClassList("intro__studio");
            Root.Add(_studio);
            _game = new VisualElement { pickingMode = PickingMode.Ignore };
            _game.AddToClassList("intro__game");
            Root.Add(_game);
            _line = new Label(StoryLine) { pickingMode = PickingMode.Ignore };
            _line.AddToClassList("intro__line");
            Root.Add(_line);
        }

        public void Play()
        {
            _time = 0f;
            _liftStart = Lift;
            _liftSeconds = LiftSeconds;
            Playing = true;
            Root.RemoveFromClassList("hidden");
            Apply();
        }

        /// <summary>Ends it at once, without the lift (media capture between shots).</summary>
        public void Stop()
        {
            Playing = false;
            Root.AddToClassList("hidden");
        }

        /// <summary>Jumps to the lift (a skip during the lift ends it at once).</summary>
        public void Skip()
        {
            if (!Playing) return;
            if (_time >= _liftStart) _time = _liftStart + _liftSeconds;
            else
            {
                _liftStart = _time;
                _liftSeconds = SkipLiftSeconds;
            }
        }

        public void Update(float dt, bool skip)
        {
            if (!Playing) return;
            if (skip) Skip();
            _time += math.clamp(dt, 0f, MaxStep); // the first frames after loading can hitch: don't skip the logos
            if (_time >= _liftStart + _liftSeconds)
            {
                Playing = false;
                Root.AddToClassList("hidden");
                Finished?.Invoke();
                return;
            }
            Apply();
        }

        void Apply()
        {
            float lift = math.saturate((_time - _liftStart) / _liftSeconds);
            float keep = 1f - Smooth(lift);
            float studio = Ramp(_time, StudioIn, StudioFade) * (1f - Ramp(_time, StudioOut, StudioFade));
            float game = Ramp(_time, GameIn, GameFade);
            _studio.style.opacity = studio * keep;
            _game.style.opacity = game * keep;
            float grow = 0.94f + 0.06f * Smooth(game);
            _game.style.scale = new Scale(new UnityEngine.Vector2(grow, grow));
            _line.style.opacity = Ramp(_time, LineIn, LineFade) * keep;
            Root.style.backgroundColor = new UnityEngine.Color(0.02f, 0.012f, 0.008f, keep);
        }

        static float Ramp(float t, float start, float seconds) => Smooth(math.saturate((t - start) / seconds));
        static float Smooth(float k) => k * k * (3f - 2f * k);
    }
}
