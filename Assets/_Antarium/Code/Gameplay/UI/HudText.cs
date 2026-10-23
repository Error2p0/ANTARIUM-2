using System.Text;
using Antarium.Content;
using Antarium.Core;

namespace Antarium.Gameplay
{
    /// <summary>Shared wording for the HUD panels.</summary>
    public static class HudText
    {
        /// <summary>"45s", "3m 05s", "2h 13m".</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            long s = (long)System.Math.Ceiling(seconds);
            if (s < 60) return s + "s";
            if (s < 3600) return s % 60 == 0 ? $"{s / 60}m" : $"{s / 60}m {s % 60:00}s";
            return $"{s / 3600}h {s / 60 % 60:00}m";
        }

        /// <summary>"0:45", "2:05" (whole seconds rounded up).</summary>
        public static string Clock(double seconds)
        {
            long s = (long)System.Math.Ceiling(System.Math.Max(0.0, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>"+6 Knowledge, +10 Food, +2 Amber" (items scaled and rounded like the simulation does).</summary>
        public static void AppendReward(StringBuilder sb, ContentDB content, ushort[] items, int[] counts, float scale, float food, float knowledge)
        {
            int start = sb.Length;
            void Sep()
            {
                if (sb.Length > start) sb.Append(", ");
            }
            if (knowledge > 0f) { Sep(); sb.Append('+').Append(NumberFormat.Format(knowledge)).Append(" Knowledge"); }
            if (food > 0f) { Sep(); sb.Append('+').Append(NumberFormat.Format(food)).Append(" Food"); }
            for (int i = 0; i < items.Length; i++)
            {
                Sep();
                int n = System.Math.Max(1, (int)System.Math.Round(counts[i] * scale));
                sb.Append('+').Append(n).Append(' ').Append(content.Items[items[i]].DisplayName);
            }
        }

        /// <summary>"10 Knowledge, 6 Copper Ore, 20 Food".</summary>
        public static void AppendCost(StringBuilder sb, ContentDB content, float knowledge, ushort[] items, int[] counts, float food)
        {
            int start = sb.Length;
            if (knowledge > 0f) sb.Append(NumberFormat.Format(knowledge)).Append(" Knowledge");
            for (int i = 0; i < items.Length; i++)
            {
                if (sb.Length > start) sb.Append(", ");
                sb.Append(counts[i]).Append(' ').Append(content.Items[items[i]].DisplayName);
            }
            if (food > 0f)
            {
                if (sb.Length > start) sb.Append(", ");
                sb.Append(NumberFormat.Format(food)).Append(" Food");
            }
            if (sb.Length == start) sb.Append("Free");
        }

        /// <summary>"a Cave Spider", "an Ant", "3 Cave Spiders".</summary>
        public static string Plural(int count, string name)
        {
            if (count == 1) return ("AEIOUaeiou".IndexOf(name[0]) >= 0 ? "an " : "a ") + name;
            return count + " " + (name.EndsWith("s") ? name : name + "s");
        }

        public static string StatName(StatKind stat) => stat switch
        {
            StatKind.WorkerSpeed => "worker speed",
            StatKind.WorkerDig => "worker digging",
            StatKind.PlayerDig => "your digging",
            StatKind.BuildSpeed => "building speed",
            StatKind.BeltSpeed => "belt speed",
            StatKind.MachineSpeed => "machine speed",
            StatKind.RoyalCapacity => "royal chamber items",
            StatKind.FoodCapacity => "food capacity",
            StatKind.LayingSpeed => "egg laying",
            StatKind.MaxEggs => "waiting eggs",
            StatKind.ForageRate => "forage",
            StatKind.OfflineEfficiency => "offline efficiency",
            StatKind.OfflineHours => "offline hours",
            StatKind.ContractSlots => "contract slots",
            StatKind.KnowledgeGain => "knowledge from studying",
            StatKind.AbilityRecharge => "ability recharge",
            StatKind.AntHealth => "ant hit points",
            StatKind.AntAttack => "ant bite damage",
            StatKind.HazardResistance => "resistance to drowning, lava, gas and cave-ins",
            StatKind.DefenseStrength => "gate hit points and trap damage",
            _ => stat.ToString(),
        };

        /// <summary>True for stats whose value is a fraction shown as a percentage.</summary>
        public static bool IsPercent(StatKind stat) => stat switch
        {
            StatKind.RoyalCapacity or StatKind.FoodCapacity or StatKind.MaxEggs or StatKind.OfflineHours or StatKind.ContractSlots => false,
            _ => true,
        };
    }
}
