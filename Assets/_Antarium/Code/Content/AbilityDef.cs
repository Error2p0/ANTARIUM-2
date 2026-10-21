using UnityEngine;

namespace Antarium.Content
{
    public enum AbilityKind : byte
    {
        /// <summary>Workers move, dig and build faster for <see cref="AbilityDef.duration"/>.</summary>
        Frenzy,
        /// <summary>Reveals every ore cell within <see cref="AbilityDef.radius"/> of the cursor through the fog.</summary>
        SeismicSense,
        /// <summary>The queen lays <see cref="AbilityDef.count"/> eggs at once.</summary>
        RoyalDecree,
    }

    /// <summary>An active ability the player triggers by hand (cast at the cursor), with a cooldown.</summary>
    [CreateAssetMenu(menuName = "Antarium/Ability", fileName = "Ability")]
    public sealed class AbilityDef : ContentDef
    {
        public AbilityKind kind;
        [TextArea] public string description;
        [Tooltip("Position on the ability bar (lower first). The first three get the Z / X / C hotkeys.")]
        public int order;

        [Min(1f)] public float cooldown = 120f;
        [Tooltip("Food paid on use.")]
        [Min(0f)] public float foodCost;

        [Header("Effect")]
        [Tooltip("Seconds the effect lasts (Frenzy).")]
        [Min(0f)] public float duration = 20f;
        [Tooltip("Frenzy: extra work speed (0.75 = +75%).")]
        [Min(0f)] public float magnitude = 0.75f;
        [Tooltip("Seismic Sense: radius in cells.")]
        [Min(1f)] public float radius = 20f;
        [Tooltip("Royal Decree: eggs laid.")]
        [Min(1)] public int count = 4;

        [Header("Presentation")]
        public Color color = Color.white;
    }
}
