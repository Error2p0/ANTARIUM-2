using System;
using System.Collections.Generic;
using System.Text;

namespace Antarium.Gameplay
{
    /// <summary>
    /// Fills the key placeholders of help and tutorial texts (<c>{Mine}</c>, <c>{Help}</c>, <c>{Pan}</c>, <c>{Ability1}</c>...) with
    /// the keys currently bound, highlighted. Unknown placeholders are left as they are, unless the caller resolves them.
    /// </summary>
    public static class HelpText
    {
        public const string KeyColor = "#ffd28c";

        static readonly Dictionary<string, Func<string>> KeysByName = new()
        {
            ["Dig"] = () => Keys.Dig,
            ["Mine"] = () => Keys.Mine,
            ["Storage"] = () => Keys.Storage,
            ["Build"] = () => Keys.Build,
            ["Scent"] = () => Keys.Scent,
            ["Possess"] = () => Keys.Possess,
            ["Interact"] = () => Keys.Interact,
            ["Configure"] = () => Keys.Configure,
            ["Rotate"] = () => Keys.Rotate,
            ["Next"] = () => Keys.Next,
            ["Previous"] = () => Keys.Previous,
            ["Brush"] = () => Keys.Brush,
            ["Flow"] = () => Keys.Flow,
            ["Light"] = () => Keys.Light,
            ["Ores"] = () => Keys.Ores,
            ["Research"] = () => Keys.Research,
            ["Castes"] = () => Keys.Castes,
            ["Genetics"] = () => Keys.Genetics,
            ["Flight"] = () => Keys.Flight,
            ["Queen"] = () => Keys.Queen,
            ["Priorities"] = () => Keys.Priorities,
            ["Codex"] = () => Keys.Codex,
            ["Menu"] = () => Keys.Menu,
            ["Save"] = () => Keys.Save,
            ["Load"] = () => Keys.Load,
            ["Help"] = () => Keys.Help,
            ["Pan"] = () => Keys.Pan,
            ["Ability1"] = () => Keys.Ability(0),
            ["Ability2"] = () => Keys.Ability(1),
            ["Ability3"] = () => Keys.Ability(2),
            ["Ability4"] = () => Keys.Ability(3),
            ["Ability5"] = () => Keys.Ability(4),
        };

        /// <summary>True if <paramref name="name"/> (without braces) names a key.</summary>
        public static bool IsKey(string name) => name != null && KeysByName.ContainsKey(name);

        /// <summary>
        /// <paramref name="text"/> with every <c>{Key}</c> replaced by the bound key in <see cref="KeyColor"/>. Other placeholders
        /// go to <paramref name="resolve"/> (null or a null result keeps them as written).
        /// </summary>
        public static string Expand(string text, Func<string, string> resolve = null)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 64);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
                if (open < 0 || close < 0)
                {
                    sb.Append(text, i, text.Length - i);
                    break;
                }
                sb.Append(text, i, open - i);
                string name = text.Substring(open + 1, close - open - 1);
                if (KeysByName.TryGetValue(name, out var key))
                    sb.Append("<color=").Append(KeyColor).Append('>').Append(key()).Append("</color>");
                else sb.Append(resolve?.Invoke(name) ?? text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>The placeholder names in <paramref name="text"/> (for content checks).</summary>
        public static List<string> Placeholders(string text)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(text)) return names;
            int i = 0;
            while (true)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) break;
                int close = text.IndexOf('}', open + 1);
                if (close < 0) break;
                names.Add(text.Substring(open + 1, close - open - 1));
                i = close + 1;
            }
            return names;
        }
    }
}
