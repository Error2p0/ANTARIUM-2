using System;
using Antarium.Content;
using Antarium.Sim;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Checks the achievements about once a second, unlocks reached ones on the platform and pushes progress stats.
    /// Unlocks belong to the player, not to a colony: a new colony or a loaded save never takes them back.
    /// </summary>
    public sealed class AchievementTracker
    {
        const float CheckSeconds = 1f;
        const float FlushSeconds = 60f;

        readonly ContentDB _content;
        readonly IPlatformServices _platform;
        readonly bool[] _unlocked;
        float _timer, _flushTimer = FlushSeconds;

        public event Action<AchievementData> Unlocked;
        public int UnlockedCount { get; private set; }
        public int Count => _unlocked.Length;

        public AchievementTracker(ContentDB content, IPlatformServices platform)
        {
            _content = content;
            _platform = platform;
            _unlocked = new bool[content.Achievements.Length];
            Refresh();
        }

        /// <summary>Reads the unlocked state from the platform again (after erasing progress).</summary>
        public void Refresh()
        {
            UnlockedCount = 0;
            for (int i = 0; i < _unlocked.Length; i++)
            {
                _unlocked[i] = _platform.IsAchievementUnlocked(_content.Achievements[i].Def.apiName);
                if (_unlocked[i]) UnlockedCount++;
            }
        }

        public bool IsUnlocked(int achievement) => _unlocked[achievement];

        /// <param name="force">Check now instead of waiting for the next second.</param>
        public void Update(float dt, Simulation sim, bool force = false)
        {
            _flushTimer -= dt;
            _timer -= dt;
            if (_timer > 0f && !force) return;
            _timer = CheckSeconds;

            bool any = false;
            for (int i = 0; i < _unlocked.Length; i++)
            {
                var data = _content.Achievements[i];
                long progress = AchievementProgress.Measure(sim, data);
                if (!string.IsNullOrEmpty(data.Def.statName))
                    _platform.SetStat(data.Def.statName, (int)Math.Min(progress, int.MaxValue));
                if (_unlocked[i] || progress < data.Def.amount) continue;
                _unlocked[i] = true;
                UnlockedCount++;
                _platform.UnlockAchievement(data.Def.apiName);
                any = true;
                Unlocked?.Invoke(data);
            }
            if (any || _flushTimer <= 0f)
            {
                _platform.Flush();
                _flushTimer = FlushSeconds;
            }
        }
    }
}
