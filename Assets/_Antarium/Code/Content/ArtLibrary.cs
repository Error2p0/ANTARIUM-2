using System;
using System.Collections.Generic;
using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// The PNG art in <c>Assets/_Antarium/Art/</c>, by name (the path under that folder without the extension, e.g.
    /// <c>Colony/ant_worker</c>). Filled by <i>Antarium → Art → Import Overrides</i>. The sprite atlases, the HUD and
    /// the terrain look their pieces up here; anything missing falls back to the art generated in code.
    /// See <c>docs/ART_STYLE.md</c> for the names and sizes.
    /// </summary>
    [CreateAssetMenu(menuName = "Antarium/Art Library", fileName = "ArtLibrary")]
    public sealed class ArtLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string name;
            public Texture2D texture;
        }

        public List<Entry> entries = new();

        Dictionary<string, Texture2D> _byName;

        public bool TryGet(string name, out Texture2D texture)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, Texture2D>(entries.Count);
                foreach (var e in entries)
                    if (e.texture != null && !string.IsNullOrEmpty(e.name)) _byName[e.name] = e.texture;
            }
            return _byName.TryGetValue(name, out texture);
        }

        public Texture2D Get(string name) => TryGet(name, out var texture) ? texture : null;

        /// <summary>Call after changing <see cref="entries"/>.</summary>
        public void Rebuild() => _byName = null;

        void OnEnable() => _byName = null;
    }
}
