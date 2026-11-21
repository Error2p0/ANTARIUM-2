using System;
using System.Collections.Generic;
using Antarium.Content;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>
    /// What the player did that the simulation doesn't record, counted since the game started (the tutorial measures from
    /// where each step began): camera travel, zooms, cells dug with the Dig tool, panels opened.
    /// </summary>
    public sealed class TutorialProbe
    {
        /// <summary>Cells the view was panned (jumps such as Go or leaving the title don't count).</summary>
        public float CameraTravel;
        public int Zooms;
        /// <summary>Cells dug out with the Dig tool.</summary>
        public int PlayerDug;
        public int QueenOpened, HelpOpened;
    }

    /// <summary>
    /// The first-colony tutorial: one step at a time, each finished by doing what it asks (a goal checked against the
    /// colony and <see cref="TutorialProbe"/>), or by Continue on a message step. A finished step shows as done for
    /// <see cref="DoneSeconds"/>, then the next one starts. Steps can be skipped and the tutorial ended at any time.
    /// </summary>
    public sealed class Tutorial
    {
        public const float DoneSeconds = 1.6f;
        const float CheckInterval = 0.25f;

        readonly TutorialStepDef[] _steps;
        float _doneTimer, _checkTimer;
        bool _baselined;
        float _travel0;
        int _zooms0, _dug0, _queen0, _help0, _peakOrders;
        long _delivered0;

        /// <summary>The current step, or -1 when the tutorial isn't running.</summary>
        public int Index { get; private set; } = -1;
        public bool Active => Index >= 0 && Index < _steps.Length;
        public int Count => _steps.Length;
        public TutorialStepDef Step => Active ? _steps[Index] : null;
        public IReadOnlyList<TutorialStepDef> Steps => _steps;
        /// <summary>The current step's goal is reached; the next step starts in a moment.</summary>
        public bool StepDone { get; private set; }
        /// <summary>How far the current goal is (0..<see cref="Target"/>); <see cref="Target"/> 0 = a message step.</summary>
        public int Progress { get; private set; }
        public int Target { get; private set; }
        /// <summary>Changes whenever what the panel shows changes (step, progress, done).</summary>
        public int Version { get; private set; }

        /// <summary>A step began (index).</summary>
        public event Action<int> StepStarted;
        /// <summary>A step's goal was reached (index).</summary>
        public event Action<int> StepCompleted;
        /// <summary>The tutorial ran out of steps (true) or was ended by the player (false).</summary>
        public event Action<bool> Finished;

        public Tutorial(IEnumerable<TutorialStepDef> steps)
        {
            var list = new List<TutorialStepDef>();
            if (steps != null)
                foreach (var s in steps)
                    if (s != null) list.Add(s);
            _steps = list.ToArray();
            var keys = new int[_steps.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = _steps[i].order * 4096 + i;
            Array.Sort(keys, _steps);
        }

        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _steps.Length; i++)
                if (_steps[i].Id == id) return i;
            return -1;
        }

        /// <summary>Starts (or restarts) at step <paramref name="index"/>.</summary>
        public void Start(int index)
        {
            if (index < 0 || index >= _steps.Length) return;
            Begin(index);
        }

        /// <summary>The colony changed (a new colony or a load): goals measured from the step's start begin again.</summary>
        public void Rebase() => _baselined = false;

        /// <summary>Continue on a message step, or on a finished one (no need to wait).</summary>
        public void Continue()
        {
            if (!Active) return;
            if (Target == 0 || StepDone) Advance();
        }

        public void Skip()
        {
            if (Active) Advance();
        }

        /// <summary>The player ended the tutorial.</summary>
        public void End()
        {
            if (!Active) return;
            Index = -1;
            StepDone = false;
            Version++;
            Finished?.Invoke(false);
        }

        public void Update(Simulation sim, TutorialProbe probe, float dt)
        {
            if (!Active || sim == null || probe == null) return;
            if (StepDone)
            {
                _doneTimer -= dt;
                if (_doneTimer <= 0f) Advance();
                return;
            }
            if (!_baselined)
            {
                TakeBaseline(sim, probe);
                _checkTimer = 0f;
            }
            _checkTimer -= dt;
            if (_checkTimer > 0f) return;
            _checkTimer = CheckInterval;
            Evaluate(sim, probe);
        }

        /// <summary>Checks the goal now (tests; <see cref="Update"/> does it a few times a second).</summary>
        public void Evaluate(Simulation sim, TutorialProbe probe)
        {
            if (!Active || StepDone) return;
            if (!_baselined) TakeBaseline(sim, probe);
            var step = _steps[Index];
            int target = TargetOf(step), progress = Math.Min(Measure(step, sim, probe), target);
            if (progress != Progress || target != Target)
            {
                Progress = progress;
                Target = target;
                Version++;
            }
            if (target > 0 && progress >= target)
            {
                StepDone = true;
                _doneTimer = DoneSeconds;
                Version++;
                StepCompleted?.Invoke(Index);
            }
        }

        /// <summary>The goal's size: 0 for message steps, 2 for Look around (pan and zoom), 1 for "do it once" goals.</summary>
        public static int TargetOf(TutorialStepDef step) => step.goal switch
        {
            TutorialGoal.None => 0,
            TutorialGoal.LookAround => 2,
            TutorialGoal.OpenQueen or TutorialGoal.OpenHelp => 1,
            _ => Math.Max(1, step.amount),
        };

        /// <summary>"Cells dug: 3 / 6" and the like for the panel; null for goals without a count.</summary>
        public string ProgressText()
        {
            var step = Step;
            if (step == null) return null;
            if (StepDone) return "Done!";
            string Count(string what) => $"{what}: {Progress} / {Target}";
            return step.goal switch
            {
                TutorialGoal.LookAround => $"Pan the view: {(_panDone ? "done" : "not yet")}. Zoom: {(_zoomDone ? "done" : "not yet")}.",
                TutorialGoal.DigYourself => Count("Cells dug"),
                TutorialGoal.MiningOrders => Count("Cells ordered"),
                TutorialGoal.StorageZone => Count("Storage cells"),
                TutorialGoal.ItemsStored => Count("Items brought in"),
                TutorialGoal.ClaimContract => Count("Contracts claimed"),
                TutorialGoal.Research => Count("Research done"),
                TutorialGoal.PlaceBuilding => Count(step.building != null ? step.building.DisplayName + " placed" : "Buildings placed"),
                _ => null,
            };
        }

        bool _panDone, _zoomDone;

        int Measure(TutorialStepDef step, Simulation sim, TutorialProbe probe)
        {
            var colony = sim.Colony;
            switch (step.goal)
            {
                case TutorialGoal.LookAround:
                    _panDone = probe.CameraTravel - _travel0 >= Math.Max(1, step.amount);
                    _zoomDone = probe.Zooms > _zooms0;
                    return (_panDone ? 1 : 0) + (_zoomDone ? 1 : 0);
                case TutorialGoal.DigYourself: return probe.PlayerDug - _dug0;
                case TutorialGoal.MiningOrders:
                    // Workers dig orders away as they go, so the most there were at once counts.
                    _peakOrders = Math.Max(_peakOrders, colony.Designated.Count);
                    return _peakOrders;
                case TutorialGoal.StorageZone: return colony.StorageCells.Count;
                case TutorialGoal.ItemsStored: return (int)Math.Min(int.MaxValue, Math.Max(0L, Delivered(colony.Stats) - _delivered0));
                case TutorialGoal.OpenQueen: return probe.QueenOpened - _queen0;
                case TutorialGoal.OpenHelp: return probe.HelpOpened - _help0;
                case TutorialGoal.ClaimContract: return colony.Stats.ContractsCompleted;
                case TutorialGoal.Research: return sim.Research.DoneCount;
                case TutorialGoal.PlaceBuilding: return Placed(step.building, sim);
                default: return 0;
            }
        }

        /// <summary>Buildings of <paramref name="def"/> standing or planned (any building when null).</summary>
        static int Placed(BuildingDef def, Simulation sim)
        {
            int index = -1;
            if (def != null && !sim.Content.TryGetBuildingIndex(def.Id, out index)) return 0;
            int count = 0;
            var all = sim.Buildings.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Alive && (index < 0 || all[i].Def == index)) count++;
            return count;
        }

        static long Delivered(ColonyStats stats)
        {
            long sum = 0;
            foreach (long n in stats.Delivered) sum += n;
            return sum;
        }

        void TakeBaseline(Simulation sim, TutorialProbe probe)
        {
            _baselined = true;
            _travel0 = probe.CameraTravel;
            _zooms0 = probe.Zooms;
            _dug0 = probe.PlayerDug;
            _queen0 = probe.QueenOpened;
            _help0 = probe.HelpOpened;
            _delivered0 = Delivered(sim.Colony.Stats);
            _peakOrders = 0;
        }

        void Begin(int index)
        {
            Index = index;
            StepDone = false;
            _baselined = false;
            _panDone = _zoomDone = false;
            Progress = 0;
            Target = TargetOf(_steps[index]);
            Version++;
            StepStarted?.Invoke(index);
        }

        void Advance()
        {
            int next = Index + 1;
            if (next >= _steps.Length)
            {
                Index = -1;
                StepDone = false;
                Version++;
                Finished?.Invoke(true);
                return;
            }
            Begin(next);
        }
    }
}
