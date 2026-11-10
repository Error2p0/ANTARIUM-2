using Antarium.Content;
using Antarium.Core;

namespace Antarium.Sim
{
    /// <summary>
    /// The ruins' story (10.5): lore fragments the colony finds by studying relics and tablets in the Archive. A fragment is
    /// found for good once enough of its item has been studied in this colony (<see cref="ColonyStats.Studied"/>); the Codex
    /// shows them and the ancient research tier asks for a number of them.
    /// </summary>
    public sealed class LoreSystem
    {
        readonly ContentDB _content;
        readonly Colony _colony;
        readonly EventBus _events;

        /// <summary>Per lore fragment (index into <see cref="ContentDB.Lore"/>).</summary>
        public readonly bool[] Found;
        public int FoundCount { get; private set; }
        public int Total => Found.Length;

        public LoreSystem(ContentDB content, Colony colony, EventBus events)
        {
            _content = content;
            _colony = colony;
            _events = events;
            Found = new bool[content.Lore.Length];
        }

        /// <summary>Finds every fragment whose study goal is reached (cheap: a dozen comparisons).</summary>
        public void Update()
        {
            var studied = _colony.Stats.Studied;
            var lore = _content.Lore;
            for (int i = 0; i < lore.Length; i++)
            {
                if (Found[i] || studied[lore[i].Item] < lore[i].Count) continue;
                Found[i] = true;
                FoundCount++;
                _events.Publish(new LoreFoundEvent(i, FoundCount, Found.Length));
            }
        }

        /// <summary>Marks a fragment found without an event (loading a save).</summary>
        internal void SetFound(int index)
        {
            if (index < 0 || index >= Found.Length || Found[index]) return;
            Found[index] = true;
            FoundCount++;
        }

        /// <summary>How many of the fragment's item are still to be studied (0 when found).</summary>
        public long Remaining(int index)
        {
            if (Found[index]) return 0;
            var data = _content.Lore[index];
            return System.Math.Max(0, data.Count - _colony.Stats.Studied[data.Item]);
        }
    }
}
