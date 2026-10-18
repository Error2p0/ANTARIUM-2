using Antarium.Core;
using NUnit.Framework;

namespace Antarium.Tests
{
    public class CoreTests
    {
        struct Ping { public int Value; }
        struct Pong { }

        [Test]
        public void EventBus_ReadsPublishedEventsUntilFlush()
        {
            var bus = new EventBus();
            bus.Publish(new Ping { Value = 1 });
            bus.Publish(new Ping { Value = 2 });

            var read = bus.Read<Ping>();
            Assert.AreEqual(2, read.Length);
            Assert.AreEqual(2, read[1].Value);
            Assert.AreEqual(0, bus.Read<Pong>().Length);

            bus.Flush();
            Assert.AreEqual(0, bus.Read<Ping>().Length);
        }

        [Test]
        public void EventBus_GrowsBeyondInitialCapacity()
        {
            var bus = new EventBus();
            for (int i = 0; i < 100; i++) bus.Publish(new Ping { Value = i });
            Assert.AreEqual(100, bus.Read<Ping>().Length);
            Assert.AreEqual(99, bus.Read<Ping>()[99].Value);
        }

        [Test]
        public void SimClock_RunsWholeTicksAndKeepsRemainder()
        {
            var clock = new SimClock(20, 5);
            Assert.AreEqual(0, clock.Advance(0.04));
            Assert.AreEqual(1, clock.Advance(0.02));
            Assert.AreEqual(2, clock.Advance(0.1));
            Assert.AreEqual(3, clock.Tick);
        }

        [Test]
        public void SimClock_CapsTicksPerFrame()
        {
            var clock = new SimClock(20, 5);
            Assert.AreEqual(5, clock.Advance(10.0));
            Assert.Less(clock.Alpha, 1f);
        }
    }
}
