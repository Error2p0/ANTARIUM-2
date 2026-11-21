using System;
using Antarium.Content;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// The tutorial card at the top left: step number, title, what to do (keys as bound now), how far the goal is, and
    /// Continue / Skip step, More help (the step's help page) and End tutorial (a second click confirms).
    /// </summary>
    public sealed class TutorialPanel
    {
        const float ConfirmSeconds = 3f;

        readonly VisualElement _root;
        readonly Label _count, _title, _body, _progress;
        readonly Button _primary, _more, _end;
        readonly Func<BuildingDef, string> _costText;
        Tutorial _tutorial;
        int _version = -1, _keys = -1;
        float _confirm;

        public event Action ContinueClicked, SkipClicked, EndClicked;
        public event Action<HelpTopicDef> MoreHelpClicked;

        public VisualElement Root => _root;
        public bool Visible => _root != null && !_root.ClassListContains("hidden");

        /// <param name="costText">The cost of a building as the palette writes it (for <c>{Cost}</c>).</param>
        public TutorialPanel(VisualElement root, Func<BuildingDef, string> costText)
        {
            _root = root;
            _costText = costText;
            if (root == null) return;
            root.AddToClassList("hidden");
            var header = new VisualElement();
            header.AddToClassList("tutorial__header");
            _count = new Label { pickingMode = PickingMode.Ignore };
            _count.AddToClassList("panel__title");
            _count.AddToClassList("tutorial__count");
            header.Add(_count);
            _end = new Button(OnEnd) { focusable = false };
            _end.AddToClassList("small-button");
            _end.AddToClassList("tutorial__end");
            header.Add(_end);
            root.Add(header);
            _title = Text("tutorial__title");
            _body = Text("tutorial__text");
            _progress = Text("tutorial__progress");
            var row = new VisualElement();
            row.AddToClassList("tutorial__buttons");
            _primary = new Button(OnPrimary) { focusable = false };
            _primary.AddToClassList("claim");
            _primary.AddToClassList("tutorial__button");
            row.Add(_primary);
            _more = new Button(() =>
            {
                var topic = _tutorial?.Step?.helpTopic;
                if (topic != null) MoreHelpClicked?.Invoke(topic);
            }) { text = "More help", focusable = false };
            _more.AddToClassList("small-button");
            _more.AddToClassList("tutorial__more");
            row.Add(_more);
            root.Add(row);
            SetEndText();
        }

        Label Text(string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            _root.Add(label);
            return label;
        }

        /// <summary>Shows <paramref name="tutorial"/>'s current step (rebuilt only when it or the key bindings change).</summary>
        public void Update(Tutorial tutorial, float dt, int bindingsVersion)
        {
            if (_root == null) return;
            _tutorial = tutorial;
            bool show = tutorial != null && tutorial.Active;
            _root.EnableInClassList("hidden", !show);
            if (_confirm > 0f)
            {
                _confirm -= dt;
                if (_confirm <= 0f) SetEndText();
            }
            if (!show)
            {
                _version = -1;
                return;
            }
            if (tutorial.Version == _version && bindingsVersion == _keys) return;
            bool newStep = _title.userData as TutorialStepDef != tutorial.Step || bindingsVersion != _keys;
            _version = tutorial.Version;
            _keys = bindingsVersion;
            var step = tutorial.Step;
            if (newStep)
            {
                _title.userData = step;
                _count.text = $"Tutorial {tutorial.Index + 1}/{tutorial.Count}";
                _title.text = step.DisplayName;
                _body.text = HelpText.Expand(step.body, name => name == "Cost" && step.building != null ? _costText?.Invoke(step.building) : null);
                _more.EnableInClassList("hidden", step.helpTopic == null);
            }
            string progress = tutorial.ProgressText();
            _progress.EnableInClassList("hidden", string.IsNullOrEmpty(progress));
            _progress.text = progress ?? "";
            _root.EnableInClassList("tutorial--done", tutorial.StepDone);
            bool last = tutorial.Index == tutorial.Count - 1;
            _primary.text = tutorial.Target == 0 ? (last ? "Finish" : "Continue") : tutorial.StepDone ? "Next" : "Skip step";
            _primary.EnableInClassList("tutorial__button--skip", tutorial.Target > 0 && !tutorial.StepDone);
        }

        void OnPrimary()
        {
            if (_tutorial == null || !_tutorial.Active) return;
            if (_tutorial.Target == 0 || _tutorial.StepDone) ContinueClicked?.Invoke();
            else SkipClicked?.Invoke();
        }

        void OnEnd()
        {
            if (_confirm > 0f)
            {
                _confirm = 0f;
                SetEndText();
                EndClicked?.Invoke();
                return;
            }
            _confirm = ConfirmSeconds;
            _end.text = "Click again to end";
            _end.AddToClassList("tutorial__end--warn");
        }

        void SetEndText()
        {
            if (_end == null) return;
            _end.text = "End tutorial";
            _end.RemoveFromClassList("tutorial__end--warn");
        }
    }
}
