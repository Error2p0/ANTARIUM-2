using System;
using Antarium.Content;

namespace Antarium.Sim
{
    public enum AbilityResult : byte
    {
        Ok,
        Locked,
        Cooling,
        CantAfford,
        /// <summary>Nothing to do there (e.g. no room for eggs).</summary>
        NoEffect,
        Unknown,
    }

    /// <summary>
    /// Cooldowns and running effects of the active abilities. The effects themselves (revealing ore, laying
    /// eggs) are carried out by <see cref="Simulation.UseAbility"/>, which has access to the world.
    /// </summary>
    public sealed class AbilitySystem
    {
        readonly ContentDB _content;
        readonly Colony _colony;

        /// <summary>Seconds until each ability can be used again.</summary>
        public readonly float[] Cooldown;
        /// <summary>Seconds each ability's effect still runs (Frenzy).</summary>
        public readonly float[] Remaining;

        public AbilitySystem(ContentDB content, Colony colony)
        {
            _content = content;
            _colony = colony;
            Cooldown = new float[content.Abilities.Length];
            Remaining = new float[content.Abilities.Length];
        }

        /// <summary>Full cooldown of an ability after research.</summary>
        public float CooldownOf(int index) => _content.Abilities[index].cooldown / _colony.Mods.Mult(StatKind.AbilityRecharge);

        public AbilityResult Check(int index)
        {
            if (index < 0 || index >= Cooldown.Length) return AbilityResult.Unknown;
            if (!_colony.AbilityUnlocked[index]) return AbilityResult.Locked;
            if (Cooldown[index] > 0f) return AbilityResult.Cooling;
            if (_colony.Inventory.Food + 1e-3f < _content.Abilities[index].foodCost) return AbilityResult.CantAfford;
            return AbilityResult.Ok;
        }

        /// <summary>Multiplier on worker speed, digging and building from running abilities.</summary>
        public float WorkFactor
        {
            get
            {
                float f = 1f;
                for (int i = 0; i < Remaining.Length; i++)
                    if (Remaining[i] > 0f && _content.Abilities[i].kind == AbilityKind.Frenzy)
                        f *= 1f + _content.Abilities[i].magnitude;
                return f;
            }
        }

        /// <summary>Starts the cooldown and the effect timer and pays the cost (the caller has checked).</summary>
        internal void Begin(int index)
        {
            var def = _content.Abilities[index];
            Cooldown[index] = CooldownOf(index);
            Remaining[index] = def.kind == AbilityKind.Frenzy ? def.duration : 0f;
            _colony.Inventory.Food = Math.Max(0f, _colony.Inventory.Food - def.foodCost);
        }

        internal void Tick(float dt)
        {
            for (int i = 0; i < Cooldown.Length; i++)
            {
                if (Cooldown[i] > 0f) Cooldown[i] = Math.Max(0f, Cooldown[i] - dt);
                if (Remaining[i] > 0f) Remaining[i] = Math.Max(0f, Remaining[i] - dt);
            }
        }
    }
}
