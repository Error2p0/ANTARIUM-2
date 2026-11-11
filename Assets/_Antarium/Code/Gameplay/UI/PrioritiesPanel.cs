using System;
using Antarium.Content;
using Antarium.Sim;
using UnityEngine.UIElements;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Job priorities (P, 10.4): one row per job with its priority 1..5 and - / +. A higher priority makes ants pick that
    /// job over others nearby; each caste still only takes the jobs it does.
    /// </summary>
    public sealed class PrioritiesPanel
    {
        static readonly string[] Hints =
        {
            "mining orders",
            "items to storage",
            "blueprints",
            "inputs to machines",
            "engineers at machines",
            "eggs to nurseries",
        };

        readonly VisualElement _root;
        readonly Label[] _values = new Label[Jobs.Count];
        readonly int[] _shown = new int[Jobs.Count];

        /// <summary>A - / + button: job and -1 / +1.</summary>
        public event Action<JobKind, int> Step;

        static readonly string[] Tips =
        {
            "Dig: workers, miners and fire ants dig out mining orders.",
            "Haul: workers, haulers and nurses carry loose items and piles to storage.",
            "Build: workers and builders build blueprints (buildings, walls, repairs).",
            "Supply: workers, haulers and engineers fetch the inputs machines need.",
            "Tend: engineers stay by a machine and speed it up.",
            "Nurse: nurses carry eggs to a Nursery, where they hatch faster.",
        };

        public PrioritiesPanel(VisualElement root, ArtLibrary art, Tooltips tips = null)
        {
            _root = root;
            var title = new Label();
            Keys.Bind(() => title.text = Keys.With("Job priorities", Keys.Priorities));
            title.AddToClassList("panel__title");
            root.Add(title);
            for (int j = 0; j < Jobs.Count; j++)
            {
                var job = (JobKind)j;
                var row = new VisualElement();
                row.AddToClassList("caste");
                row.AddToClassList("priority");
                int index = j;
                tips?.Add(row, () => Tips[index] + "\n- / +: its priority, 1 (last) to 5 (first). At 3 it is weighed as usual.",
                    "priority_" + job.ToString().ToLowerInvariant());
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                if (!HudController.SetIcon(icon, art, "priority_" + job.ToString().ToLowerInvariant(), "priority__icon"))
                    icon.AddToClassList("caste__swatch");
                var name = new Label(HudText.JobName(job)) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("caste__name");
                var hint = new Label(Hints[j]) { pickingMode = PickingMode.Ignore };
                hint.AddToClassList("priority__hint");
                _values[j] = new Label { pickingMode = PickingMode.Ignore };
                _values[j].AddToClassList("priority__value");
                var less = new Button(() => Step?.Invoke(job, -1)) { text = "-", focusable = false };
                less.AddToClassList("small-button");
                var more = new Button(() => Step?.Invoke(job, 1)) { text = "+", focusable = false };
                more.AddToClassList("small-button");
                row.Add(icon);
                row.Add(name);
                row.Add(hint);
                row.Add(_values[j]);
                row.Add(less);
                row.Add(more);
                root.Add(row);
            }
            var note = new Label("Higher: ants pick that job over others nearby. Each caste still only does its own jobs.")
                { pickingMode = PickingMode.Ignore };
            note.AddToClassList("caste__hint");
            root.Add(note);
        }

        public VisualElement Root => _root;
        public bool Visible => !_root.ClassListContains("hidden");
        public void Toggle() => _root.EnableInClassList("hidden", Visible);
        public void Hide() => _root.AddToClassList("hidden");

        public void Refresh(Simulation sim)
        {
            if (!Visible) return;
            var p = sim.Colony.JobPriority;
            for (int j = 0; j < Jobs.Count; j++)
            {
                if (_shown[j] == p[j]) continue;
                _shown[j] = p[j];
                _values[j].text = $"{p[j]} {HudText.PriorityName(p[j])}";
                _values[j].EnableInClassList("priority__value--high", p[j] > Simulation.NormalPriority);
                _values[j].EnableInClassList("priority__value--low", p[j] < Simulation.NormalPriority);
            }
        }
    }
}
