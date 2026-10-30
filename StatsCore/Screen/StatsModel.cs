using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StatsCore
{
    internal enum StatsView { Career, Today, Week, Run }

    internal enum LineKind { Header, Row, Sub, Note }

    internal readonly struct Line
    {
        public readonly LineKind Kind;
        public readonly string Text;
        public readonly string Value;
        public readonly bool Dim;

        public Line(LineKind kind, string text, string value = "", bool dim = false)
        {
            Kind = kind;
            Text = text;
            Value = value;
            Dim = dim;
        }
    }

    // Reads the registered stats and arranges them for the stats screen. No Unity or MenuLib in here.
    internal static class StatsModel
    {
        internal const string AllCategories = "All";
        internal static readonly string[] ViewNames = { "Career", "Today", "Last 7 days", "This run" };

        // The built-in pack's categories in the order they read best; other mods' categories follow A to Z.
        private static readonly string[] CategoryOrder =
            { "Glory", "Mobility", "Expeditions", "Misfortune", "Recovery", "Mischief", "Expedition" };

        private const int BreakdownRows = 6;

        internal static string LocalId()
        {
            string? avatar = PlayerAvatar.instance != null ? PlayerAvatar.instance.steamID : null;
            if (!string.IsNullOrEmpty(avatar)) return avatar!;

            // In the menus there is no avatar. Career stats are local, so the ID most of them hold is yours.
            var counts = new Dictionary<string, int>();
            foreach (Stat stat in Stats.All)
            {
                if (stat.Lifetime != StatLifetime.Career) continue;
                foreach (string key in stat.Entries.Keys)
                    if (StatFormatter.LooksLikeSteamId(key))
                        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            return counts.Count == 0 ? "" : counts.OrderByDescending(c => c.Value).First().Key;
        }

        internal static string[] CategoryNames()
        {
            var found = new HashSet<string>(Stats.All.Select(CategoryOf));
            var ordered = CategoryOrder.Where(found.Contains).ToList();
            ordered.AddRange(found.Except(ordered).OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
            ordered.Insert(0, AllCategories);
            return ordered.ToArray();
        }

        internal static List<Line> Build(StatsView view, string category)
        {
            string id = LocalId();
            var lines = new List<Line>();

            if (view == StatsView.Career && category == AllCategories)
                AddHighlights(lines, id);

            var wanted = Stats.All
                .Where(s => Shown(s, view) && (category == AllCategories || CategoryOf(s) == category))
                .GroupBy(CategoryOf)
                .OrderBy(g => CategoryRank(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var group in wanted)
            {
                var rows = new List<Line>();
                foreach (Stat stat in group)
                    AddStat(rows, stat, view, id);
                if (rows.Count == 0) continue;
                lines.Add(new Line(LineKind.Header, group.Key));
                lines.AddRange(rows);
            }

            if (lines.Count == 0)
                lines.Add(new Line(LineKind.Note, EmptyMessage(view)));
            return lines;
        }

        private static bool Shown(Stat stat, StatsView view) => view switch
        {
            StatsView.Career => stat.Lifetime == StatLifetime.Career,
            StatsView.Today or StatsView.Week => stat.Lifetime == StatLifetime.Career && stat.TracksDaily,
            _ => stat.Lifetime != StatLifetime.Career,
        };

        private static string CategoryOf(Stat stat) =>
            string.IsNullOrWhiteSpace(stat.Category) ? "Other" : stat.Category;

        private static int CategoryRank(string category)
        {
            int i = Array.IndexOf(CategoryOrder, category);
            return i < 0 ? CategoryOrder.Length : i;
        }

        private static string NameOf(Stat stat) =>
            string.IsNullOrWhiteSpace(stat.DisplayName) || stat.DisplayName == stat.Key
                ? StatFormatter.Fallback(stat.Key)
                : stat.DisplayName;

        private static void AddStat(List<Line> rows, Stat stat, StatsView view, string id)
        {
            var named = stat.Entries
                .Where(e => e.Key != Stat.TeamKey && !StatFormatter.LooksLikeSteamId(e.Key) && e.Value > 0)
                .OrderByDescending(e => e.Value)
                .ToList();

            if (named.Count > 0)
            {
                if (view == StatsView.Today || view == StatsView.Week) return;
                rows.Add(new Line(LineKind.Row, NameOf(stat), StatFormatter.Value(named.Sum(e => (long)e.Value), stat.Format)));
                foreach (var entry in named.Take(BreakdownRows))
                    rows.Add(new Line(LineKind.Sub, entry.Key, StatFormatter.Value(entry.Value, stat.Format)));
                return;
            }

            long value = view switch
            {
                StatsView.Today => stat.GetToday(id),
                StatsView.Week => stat.GetLastDays(id, 7),
                _ => stat.Scope == StatScope.Team ? stat.GetTeam() : stat.Get(id),
            };
            if (value == 0 && (view == StatsView.Today || view == StatsView.Week)) return;
            rows.Add(new Line(LineKind.Row, NameOf(stat), StatFormatter.Value(value, stat.Format), value == 0));
        }

        private static void AddHighlights(List<Line> lines, string id)
        {
            long first = 0;
            foreach (Stat stat in Stats.All)
            {
                if (stat.Lifetime != StatLifetime.Career) continue;
                foreach (string slot in stat.Entries.Keys)
                {
                    long t = stat.FirstUtc(slot);
                    if (t > 0 && (first == 0 || t < first)) first = t;
                }
            }

            var top = new List<Line>();
            if (first > 0)
            {
                DateTime since = DateTimeOffset.FromUnixTimeSeconds(first).LocalDateTime;
                top.Add(new Line(LineKind.Row, "Tracked since", since.ToString("d MMM yyyy", CultureInfo.InvariantCulture)));
            }
            AddTopEntry(top, GameStats.K_DeathsByEnemy, "Most deadly");
            AddTopEntry(top, GameStats.K_MapVisits, "Favorite level");
            if (top.Count == 0) return;

            lines.Add(new Line(LineKind.Header, "Highlights"));
            lines.AddRange(top);
        }

        private static void AddTopEntry(List<Line> into, string key, string label)
        {
            Stat? stat = Stats.Get(key);
            if (stat == null) return;
            var best = stat.Entries
                .Where(e => e.Key != Stat.TeamKey && !StatFormatter.LooksLikeSteamId(e.Key) && e.Value > 0)
                .OrderByDescending(e => e.Value)
                .FirstOrDefault();
            if (best.Key != null)
                into.Add(new Line(LineKind.Row, label, best.Key + " (" + StatFormatter.Value(best.Value, StatFormat.Count) + ")"));
        }

        private static string EmptyMessage(StatsView view) => view switch
        {
            StatsView.Today => "Nothing recorded today yet.",
            StatsView.Week => "Nothing recorded this week yet.",
            StatsView.Run => "Nothing tracked for this run yet.",
            _ => "Nothing here yet. Play a round and come back.",
        };
    }
}
