using System;
using System.Threading;

namespace Antarium.Core
{
    /// <summary>
    /// Pull-based message bus for struct events. Producers publish during the frame; consumers
    /// read everything published since the last <see cref="Flush"/>. The owner of the frame loop
    /// calls Flush once all consumers have run. Allocation-free after warm-up.
    /// </summary>
    public sealed class EventBus
    {
        interface IChannel
        {
            void Clear();
        }

        sealed class Channel<T> : IChannel where T : struct
        {
            public T[] Items = new T[16];
            public int Count;

            public void Add(in T item)
            {
                if (Count == Items.Length)
                    Array.Resize(ref Items, Count * 2);
                Items[Count++] = item;
            }

            public void Clear() => Count = 0;
        }

        static int s_TypeCounter = -1;

        static class TypeSlot<T> where T : struct
        {
            public static readonly int Index = Interlocked.Increment(ref s_TypeCounter);
        }

        IChannel[] _channels = new IChannel[16];

        public void Publish<T>(in T item) where T : struct => GetChannel<T>().Add(item);

        public ReadOnlySpan<T> Read<T>() where T : struct
        {
            var channel = GetChannel<T>();
            return new ReadOnlySpan<T>(channel.Items, 0, channel.Count);
        }

        public void Flush()
        {
            for (int i = 0; i < _channels.Length; i++)
                _channels[i]?.Clear();
        }

        Channel<T> GetChannel<T>() where T : struct
        {
            int index = TypeSlot<T>.Index;
            if (index >= _channels.Length)
                Array.Resize(ref _channels, Math.Max(index + 1, _channels.Length * 2));
            return (Channel<T>)(_channels[index] ??= new Channel<T>());
        }
    }
}
