using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StatsCore
{
    // The on-disk career format, with no IO and no Unity in it so it can be exercised directly.
    //
    // career.txt        "statKey|slot=value;firstUtc;lastUtc"     one line per slot
    // career-daily.txt  "statKey|slot|dayIndex=amount;day=amount" one line per slot
    //
    // Both files open with a version comment. Any line the parser doesn't understand is skipped rather than
    // aborting the load, so one mangled line costs one slot instead of the whole history. Files written
    // before versioning have no header and still parse; the header line is skipped as a comment.
    internal static class CareerFile
    {
        internal const int Version = 1;
        internal const string TotalsHeader = "# statscore career v1";
        internal const string DailyHeader = "# statscore career-daily v1";

        // Characters that frame a line here or a snapshot frame on the wire. Slot keys are author-supplied
        // (an enemy or level display name, sometimes from another mod's content pack), so they get folded to
        // '_' on the way in rather than trusted.
        private static readonly char[] Reserved = { '|', '=', ';', ',', ':', '\n', '\r', '\t' };

        internal static string SanitizeSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot) || slot.IndexOfAny(Reserved) < 0) return slot;
            var sb = new StringBuilder(slot.Length);
            foreach (char c in slot) sb.Append(Array.IndexOf(Reserved, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ---- totals ----

        internal static Dictionary<string, Dictionary<string, CareerEntry>> ParseTotals(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, Dictionary<string, CareerEntry>>();
            foreach (string line in lines)
            {
                if (IsSkippable(line)) continue;
                int bar = line.IndexOf('|');
                int eq = line.LastIndexOf('=');
                if (bar <= 0 || eq <= bar + 1) continue;

                string key = line.Substring(0, bar);
                string slot = line.Substring(bar + 1, eq - bar - 1);
                string[] parts = line.Substring(eq + 1).Split(';');

                var entry = new CareerEntry();
                if (!TryInt(parts[0], out entry.Value)) continue;
                if (parts.Length > 1) TryLong(parts[1], out entry.FirstUtc);
                if (parts.Length > 2) TryLong(parts[2], out entry.LastUtc);

                if (!result.TryGetValue(key, out var slots)) { slots = new Dictionary<string, CareerEntry>(); result[key] = slots; }
                slots[slot] = entry;
            }
            return result;
        }

        // `live` is what the defined stats hold right now; `saved` is what was read from disk at startup.
        // A key in both is written from `live` (it was hydrated from `saved`, then played with). A key only
        // in `saved` belongs to a mod that isn't loaded this session and is copied through untouched, so a
        // single evening without that mod doesn't erase its history.
        internal static string SerializeTotals(
            IReadOnlyDictionary<string, Dictionary<string, CareerEntry>> live,
            IReadOnlyDictionary<string, Dictionary<string, CareerEntry>> saved,
            IEnumerable<string>? merged = null)
        {
            var sb = new StringBuilder(TotalsHeader).Append('\n');
            if (merged != null)
                foreach (string token in merged) sb.Append(MergedPrefix).Append(token).Append('\n');
            foreach (var stat in live)
                foreach (var slot in stat.Value) AppendTotal(sb, stat.Key, slot.Key, slot.Value);
            foreach (var stat in saved)
            {
                if (live.ContainsKey(stat.Key)) continue;
                foreach (var slot in stat.Value) AppendTotal(sb, stat.Key, slot.Key, slot.Value);
            }
            return sb.ToString();
        }

        // ---- merging another profile's file in ----

        private const string MergedPrefix = "# merged ";

        // Which per-profile files have already been folded into this file. The marker travels inside the same
        // file as the totals it produced, so a crash can never leave one without the other.
        internal static HashSet<string> ParseMerged(IEnumerable<string> lines)
        {
            var found = new HashSet<string>();
            foreach (string line in lines)
                if (line.StartsWith(MergedPrefix, StringComparison.Ordinal) && line.Length > MergedPrefix.Length)
                    found.Add(line.Substring(MergedPrefix.Length).Trim());
            return found;
        }

        // Adds `source` into `target`: values are summed, the first-recorded time is the earlier of the two
        // and the last-changed time the later. A zero time means unknown, so it never wins.
        internal static void MergeTotals(
            Dictionary<string, Dictionary<string, CareerEntry>> target,
            IReadOnlyDictionary<string, Dictionary<string, CareerEntry>> source)
        {
            foreach (var stat in source)
            {
                if (!target.TryGetValue(stat.Key, out var slots)) { slots = new Dictionary<string, CareerEntry>(); target[stat.Key] = slots; }
                foreach (var slot in stat.Value)
                {
                    if (!slots.TryGetValue(slot.Key, out var have)) { slots[slot.Key] = slot.Value; continue; }
                    long sum = (long)have.Value + slot.Value.Value;
                    have.Value = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, sum));
                    have.FirstUtc = Earlier(have.FirstUtc, slot.Value.FirstUtc);
                    have.LastUtc = Math.Max(have.LastUtc, slot.Value.LastUtc);
                    slots[slot.Key] = have;
                }
            }
        }

        internal static void MergeDaily(
            Dictionary<string, Dictionary<string, Dictionary<int, int>>> target,
            IReadOnlyDictionary<string, Dictionary<string, Dictionary<int, int>>> source)
        {
            foreach (var stat in source)
            {
                if (!target.TryGetValue(stat.Key, out var slots)) { slots = new Dictionary<string, Dictionary<int, int>>(); target[stat.Key] = slots; }
                foreach (var slot in stat.Value)
                {
                    if (!slots.TryGetValue(slot.Key, out var days)) { slots[slot.Key] = new Dictionary<int, int>(slot.Value); continue; }
                    foreach (var day in slot.Value)
                        days[day.Key] = days.TryGetValue(day.Key, out int existing) ? existing + day.Value : day.Value;
                }
            }
        }

        private static long Earlier(long a, long b) => a == 0 ? b : b == 0 ? a : Math.Min(a, b);

        private static void AppendTotal(StringBuilder sb, string key, string slot, CareerEntry entry) =>
            sb.Append(key).Append('|').Append(SanitizeSlot(slot)).Append('=')
              .Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(entry.FirstUtc.ToString(CultureInfo.InvariantCulture)).Append(';')
              .Append(entry.LastUtc.ToString(CultureInfo.InvariantCulture)).Append('\n');

        // ---- daily ----

        internal static Dictionary<string, Dictionary<string, Dictionary<int, int>>> ParseDaily(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>();
            foreach (string line in lines)
            {
                if (IsSkippable(line)) continue;
                int first = line.IndexOf('|');
                int second = first < 0 ? -1 : line.IndexOf('|', first + 1);
                if (first <= 0 || second <= first) continue;

                string key = line.Substring(0, first);
                string slot = line.Substring(first + 1, second - first - 1);

                var byDay = new Dictionary<int, int>();
                foreach (string pair in line.Substring(second + 1).Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    if (TryInt(pair.Substring(0, eq), out int day) && TryInt(pair.Substring(eq + 1), out int amount))
                        byDay[day] = amount;
                }
                if (byDay.Count == 0) continue;

                if (!result.TryGetValue(key, out var slots)) { slots = new Dictionary<string, Dictionary<int, int>>(); result[key] = slots; }
                slots[slot] = byDay;
            }
            return result;
        }

        // Same live-wins-then-copy-the-rest rule as SerializeTotals. A stat that is defined but never called
        // TrackDaily() counts as absent from `live`, so its buckets on disk survive rather than being dropped
        // by a session that only read its running total.
        internal static string SerializeDaily(
            IReadOnlyDictionary<string, Dictionary<string, Dictionary<int, int>>> live,
            IReadOnlyDictionary<string, Dictionary<string, Dictionary<int, int>>> saved)
        {
            var sb = new StringBuilder(DailyHeader).Append('\n');
            foreach (var stat in live)
                foreach (var slot in stat.Value) AppendDaily(sb, stat.Key, slot.Key, slot.Value);
            foreach (var stat in saved)
            {
                if (live.ContainsKey(stat.Key)) continue;
                foreach (var slot in stat.Value) AppendDaily(sb, stat.Key, slot.Key, slot.Value);
            }
            return sb.ToString();
        }

        private static void AppendDaily(StringBuilder sb, string key, string slot, IEnumerable<KeyValuePair<int, int>> byDay)
        {
            var line = new StringBuilder();
            bool any = false;
            foreach (var day in byDay)
            {
                if (any) line.Append(';');
                line.Append(day.Key.ToString(CultureInfo.InvariantCulture)).Append('=')
                    .Append(day.Value.ToString(CultureInfo.InvariantCulture));
                any = true;
            }
            if (!any) return;
            sb.Append(key).Append('|').Append(SanitizeSlot(slot)).Append('|').Append(line).Append('\n');
        }

        private static bool IsSkippable(string line) =>
            string.IsNullOrEmpty(line) || line[0] == '#';

        private static bool TryInt(string s, out int value) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static bool TryLong(string s, out long value) =>
            long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
