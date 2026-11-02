using System;
using System.Collections.Generic;
using System.IO;
using Antarium.Gameplay;
using NUnit.Framework;

namespace Antarium.Tests
{
    /// <summary>Milestone 9: the store platform (merge with Steam, presence) and moving the old save folder.</summary>
    public class ReleaseTests
    {
        /// <summary>A store that answers from dictionaries; <see cref="Ready"/> false = stats still loading.</summary>
        sealed class FakeStore : IStoreClient
        {
            public readonly Dictionary<string, bool> Achievements = new();
            public readonly Dictionary<string, int> Stats = new();
            public readonly Dictionary<string, string> Presence = new();
            public bool Ready = true;
            public int Stores, PresenceCalls;
            public bool Disposed;

            public string Name => "Fake";
            public event Action<bool> OverlayChanged;
            public void Overlay(bool open) => OverlayChanged?.Invoke(open);
            public void RunCallbacks() { }

            public bool TryGetAchievement(string apiName, out bool unlocked)
            {
                unlocked = false;
                return Ready && Achievements.TryGetValue(apiName, out unlocked);
            }

            public bool SetAchievement(string apiName)
            {
                if (!Achievements.ContainsKey(apiName)) return false;
                Achievements[apiName] = true;
                return true;
            }

            public bool TryGetStat(string apiName, out int value)
            {
                value = 0;
                return Ready && Stats.TryGetValue(apiName, out value);
            }

            public bool SetStat(string apiName, int value)
            {
                if (!Stats.ContainsKey(apiName)) return false;
                Stats[apiName] = value;
                return true;
            }

            public bool StoreStats()
            {
                Stores++;
                return true;
            }

            public void SetPresence(string key, string value)
            {
                Presence[key] = value;
                PresenceCalls++;
            }

            public void Dispose() => Disposed = true;
        }

        string _dir;

        [SetUp]
        public void MakeDir()
        {
            _dir = Path.Combine(Path.GetTempPath(), "antarium-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void RemoveDir()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static readonly string[] Achs = { "ACH_A", "ACH_B", "ACH_C", "ACH_MISSING" };
        static readonly string[] Stats = { "STAT_DEPTH", "STAT_FLIGHTS" };

        FakeStore NewStore()
        {
            var s = new FakeStore();
            s.Achievements["ACH_A"] = false;
            s.Achievements["ACH_B"] = true;
            s.Achievements["ACH_C"] = false;
            s.Stats["STAT_DEPTH"] = 50;
            s.Stats["STAT_FLIGHTS"] = 0;
            return s;
        }

        [Test]
        public void StorePlatform_MergesLocalAndStoreBothWays()
        {
            using (var local = new LocalPlatform(_dir))
            {
                local.UnlockAchievement("ACH_A"); // earned before the first Steam launch
                local.SetStat("STAT_DEPTH", 20);
                local.SetStat("STAT_FLIGHTS", 3);
            }
            var store = NewStore();
            var p = new StorePlatform(store, new LocalPlatform(_dir), Achs, Stats);
            p.Tick(1f);

            Assert.IsTrue(p.Synced);
            Assert.IsTrue(store.Achievements["ACH_A"], "local unlock reaches the store");
            Assert.IsTrue(p.IsAchievementUnlocked("ACH_B"), "store unlock reaches the game");
            Assert.IsFalse(p.IsAchievementUnlocked("ACH_C"));
            Assert.AreEqual(50, store.Stats["STAT_DEPTH"], "the store's higher stat stays");
            Assert.AreEqual(3, store.Stats["STAT_FLIGHTS"], "the higher local stat is pushed");
            CollectionAssert.AreEqual(new[] { "ACH_MISSING" }, p.Unknown, "achievements not set up on the store are reported");
            Assert.AreEqual(1, store.Stores, "stored once after the merge");
            p.Dispose();
            Assert.IsTrue(store.Disposed);

            var again = new LocalPlatform(_dir);
            Assert.IsTrue(again.IsAchievementUnlocked("ACH_B"), "the merge is written to the profile");
            Assert.AreEqual(50, again.GetStat("STAT_DEPTH"));
        }

        [Test]
        public void StorePlatform_WaitsForTheStoreBeforeMerging()
        {
            var store = NewStore();
            store.Ready = false;
            var p = new StorePlatform(store, new LocalPlatform(_dir), Achs, Stats);
            p.Tick(1f);
            Assert.IsFalse(p.Synced, "nothing answered: retry");

            p.SetStat("STAT_DEPTH", 10);
            Assert.AreEqual(50, store.Stats["STAT_DEPTH"], "no stat is pushed before the merge (the store may be higher)");
            p.UnlockAchievement("ACH_C");
            Assert.IsTrue(store.Achievements["ACH_C"], "unlocks go straight to the store");

            store.Ready = true;
            p.Tick(0.1f);
            Assert.IsFalse(p.Synced, "retries once a second");
            p.Tick(1f);
            Assert.IsTrue(p.Synced);
            Assert.AreEqual(50, store.Stats["STAT_DEPTH"]);

            p.SetStat("STAT_DEPTH", 60);
            p.SetStat("STAT_DEPTH", 55);
            Assert.AreEqual(60, store.Stats["STAT_DEPTH"], "after the merge stats go to the store, highest value kept");
            int stores = store.Stores;
            p.Flush();
            Assert.AreEqual(stores + 1, store.Stores);
            p.Flush();
            Assert.AreEqual(stores + 1, store.Stores, "nothing new, nothing stored");
        }

        [Test]
        public void StorePlatform_GivesUpWaitingAndResetsOnlyLocally()
        {
            var store = NewStore();
            store.Ready = false;
            var p = new StorePlatform(store, new LocalPlatform(_dir), Achs, Stats);
            for (int i = 0; i < 10; i++) p.Tick(1f);
            Assert.IsTrue(p.Synced, "after 10 tries the merge runs with whatever answered");
            Assert.AreEqual(Achs.Length + Stats.Length, p.Unknown.Count);

            store.Ready = true;
            p.ResetProgress();
            Assert.IsFalse(p.Synced, "erasing progress merges again");
            p.Tick(1f);
            Assert.IsTrue(p.IsAchievementUnlocked("ACH_B"), "the store keeps its achievements");
        }

        [Test]
        public void StorePlatform_SetsRichPresenceOnlyWhenItChanges()
        {
            var store = NewStore();
            var p = new StorePlatform(store, new LocalPlatform(_dir), Achs, Stats);
            p.SetPresence(new Presence(PresenceState.Title));
            Assert.AreEqual(StorePlatform.TitleToken, store.Presence[StorePlatform.DisplayKey]);

            p.SetPresence(new Presence(PresenceState.Colony, 1, 12));
            Assert.AreEqual(StorePlatform.FirstColonyToken, store.Presence[StorePlatform.DisplayKey]);
            p.SetPresence(new Presence(PresenceState.Colony, 3, 420));
            Assert.AreEqual(StorePlatform.ColonyToken, store.Presence[StorePlatform.DisplayKey]);
            Assert.AreEqual("3", store.Presence[StorePlatform.GenerationKey]);
            Assert.AreEqual("420", store.Presence[StorePlatform.DepthKey]);

            int calls = store.PresenceCalls;
            p.SetPresence(new Presence(PresenceState.Colony, 3, 420));
            Assert.AreEqual(calls, store.PresenceCalls, "same presence, no calls");

            bool? open = null;
            p.OverlayChanged += o => open = o;
            store.Overlay(true);
            Assert.AreEqual(true, open, "the overlay event is passed through");
        }

        [Test]
        public void MoveOldFolder_CopiesSavesOnceFromTheOldCompany()
        {
            string old = Path.Combine(_dir, "DefaultCompany", "ANTARIUM 2");
            string now = Path.Combine(_dir, "Elevate Studios", "ANTARIUM 2");
            Directory.CreateDirectory(Path.Combine(old, "Saves"));
            File.WriteAllText(Path.Combine(old, "Saves", "colony.sav"), "save");
            File.WriteAllText(Path.Combine(old, "Saves", "colony.bak"), "backup");
            File.WriteAllText(Path.Combine(old, "settings.json"), "{}");

            Assert.IsTrue(PlatformServices.MoveOldFolder(now, "DefaultCompany"));
            Assert.AreEqual("save", File.ReadAllText(Path.Combine(now, "Saves", "colony.sav")));
            Assert.IsTrue(File.Exists(Path.Combine(now, "Saves", "colony.bak")));
            Assert.IsTrue(File.Exists(Path.Combine(now, "settings.json")));
            Assert.IsTrue(File.Exists(Path.Combine(old, "Saves", "colony.sav")), "the old folder is kept");

            File.WriteAllText(Path.Combine(old, "Saves", "colony.sav"), "older");
            Assert.IsFalse(PlatformServices.MoveOldFolder(now, "DefaultCompany"), "only once: the new folder has data");
            Assert.AreEqual("save", File.ReadAllText(Path.Combine(now, "Saves", "colony.sav")));

            Assert.IsFalse(PlatformServices.MoveOldFolder(old, "DefaultCompany"), "nothing to do when still on the old name");
            Assert.IsFalse(PlatformServices.MoveOldFolder(Path.Combine(_dir, "Other", "ANTARIUM 2"), "Nobody"), "no old folder");
        }
    }
}
