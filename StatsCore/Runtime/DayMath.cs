using System;

namespace StatsCore
{
    // Calendar-day arithmetic for the per-day history. Days are indexed as whole days since 1970-01-01 in
    // the MACHINE'S LOCAL time, not UTC: a career stat is local to one PC and one player, and "how much did
    // I do today" has to mean their today. UTC buckets rolled over mid-evening for anyone west of Greenwich,
    // which is exactly when REPO gets played.
    //
    // Only the calendar date is ever used, so DST shifts and leap seconds can't move a bucket.
    internal static class DayMath
    {
        internal static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        // Index of the local calendar day a DateTime falls on. A UTC value is converted first; anything else
        // (Local or Unspecified) is read as a local date, so both DateTime.Today and DateTime.UtcNow work.
        internal static int IndexOf(DateTime when)
        {
            if (when.Kind == DateTimeKind.Utc) when = when.ToLocalTime();
            return (when.Date - Epoch).Days;
        }

        internal static int Today() => IndexOf(DateTime.Now);

        internal static DateTime DateOf(int dayIndex) => Epoch.AddDays(dayIndex);
    }
}
