using System;
using System.Globalization;

namespace StatsCore
{
    // Turns a stat's plain integer into the text the stats screen shows, following its StatFormat hint.
    internal static class StatFormatter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Value(long value, StatFormat format) => format switch
        {
            StatFormat.Time => Duration(value),
            StatFormat.Distance => Distance(value),
            StatFormat.Money => (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("N0", Inv),
            StatFormat.Percent => value.ToString("N0", Inv) + "%",
            _ => value.ToString("N0", Inv),
        };

        // Seconds as "45s", "12m 05s", "3h 07m" or "2d 5h".
        public static string Duration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600) return (seconds / 60) + "m " + (seconds % 60).ToString("00", Inv) + "s";
            if (seconds < 86400) return (seconds / 3600) + "h " + ((seconds % 3600) / 60).ToString("00", Inv) + "m";
            return (seconds / 86400) + "d " + ((seconds % 86400) / 3600) + "h";
        }

        // Meters as "850 m", "12.4 km" or "340 km".
        public static string Distance(long meters)
        {
            if (meters < 1000) return meters.ToString("N0", Inv) + " m";
            double km = meters / 1000.0;
            return (km < 100 ? km.ToString("0.0", Inv) : km.ToString("N0", Inv)) + " km";
        }

        // A stat key's last segment, spaced and capitalized, for stats nobody called Describe on:
        // "vippy.pourdecisions.drinksTaken" becomes "Drinks Taken".
        public static string Fallback(string key)
        {
            string last = key.Substring(key.LastIndexOf('.') + 1);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < last.Length; i++)
            {
                char c = last[i];
                if (c == '_' || c == '-') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(last[i - 1]) && last[i - 1] != '_') sb.Append(' ');
                sb.Append(i == 0 || sb[sb.Length - 1] == ' ' ? char.ToUpperInvariant(c) : c);
            }
            return sb.Length == 0 ? key : sb.ToString();
        }

        // Steam IDs are 17 digits. Anything else a stat is keyed by (an enemy or level name) is a breakdown.
        public static bool LooksLikeSteamId(string key)
        {
            if (key.Length < 15) return false;
            foreach (char c in key)
                if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
