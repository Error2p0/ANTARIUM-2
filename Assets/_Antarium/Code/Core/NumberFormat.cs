using System;
using System.Globalization;

namespace Antarium.Core
{
    /// <summary>
    /// Incremental-game number formatting: 999, 1.23K, 12.3K, 123K, 1.23M ... and an optional scientific
    /// mode. Values are truncated, never rounded up, so 999,999 shows as 999K rather than 1000K.
    /// </summary>
    public static class NumberFormat
    {
        static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        public static string Format(double value, bool scientific = false)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

            double abs = Math.Abs(value);
            string sign = value < 0 ? "-" : "";
            if (abs < 1000.0)
                return sign + Math.Floor(abs).ToString("0", CultureInfo.InvariantCulture);

            int group = (int)Math.Floor(Math.Log10(abs) / 3.0);
            if (scientific || group >= Suffixes.Length)
            {
                int exponent = (int)Math.Floor(Math.Log10(abs));
                double mantissa = Truncate(abs / Math.Pow(10, exponent), 2);
                return sign + mantissa.ToString("0.00", CultureInfo.InvariantCulture) + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            }

            double scaled = abs / Math.Pow(1000.0, group);
            if (scaled >= 1000.0) // floating point edge, e.g. 999999.9999
            {
                group++;
                scaled /= 1000.0;
            }
            string text = scaled < 10 ? Truncate(scaled, 2).ToString("0.00", CultureInfo.InvariantCulture)
                : scaled < 100 ? Truncate(scaled, 1).ToString("0.0", CultureInfo.InvariantCulture)
                : Math.Floor(scaled).ToString("0", CultureInfo.InvariantCulture);
            return sign + text + Suffixes[group];
        }

        static double Truncate(double value, int decimals)
        {
            double f = Math.Pow(10, decimals);
            return Math.Floor(value * f + 1e-9) / f;
        }
    }
}
