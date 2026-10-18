using UnityEngine;

namespace Antarium.Content
{
    /// <summary>
    /// Base for all authored content. The string <see cref="Id"/> is the stable key used in saves;
    /// runtime code works with dense indices baked by <see cref="ContentDB"/>.
    /// </summary>
    public abstract class ContentDef : ScriptableObject
    {
        [SerializeField, Tooltip("Stable identifier used in save files. Never change it after release.")]
        string id;

        [SerializeField] string displayName;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id))
                id = name.Trim().ToLowerInvariant().Replace(' ', '_');
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        public void EditorSetIdentity(string newId, string newDisplayName)
        {
            id = newId;
            displayName = newDisplayName;
        }
#endif
    }
}
