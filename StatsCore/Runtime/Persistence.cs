using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace StatsCore
{
    // Career stats persisted to this machine's disk (never the game save). One flat file under the BepInEx
    // config dir: each line is "statKey|steamID=value". Loaded once; a Career stat hydrates from it the
    // moment it's Defined (consumers Define at their own Awake, which may be after we load).
    internal static class Persistence
    {
        internal static bool Dirty { get; private set; }
        internal static void MarkDirty() => Dirty = true;

        private static bool _loaded;
        // statKey -> (steamID -> value + timestamps), as read from disk.
        private static readonly Dictionary<string, Dictionary<string, CareerEntry>> _saved = new();
        // statKey -> (steamID -> (dayEpoch -> amount)), the per-day history read from disk.
        private static readonly Dictionary<string, Dictionary<string, Dictionary<int, int>>> _savedDaily = new();

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "StatsCore", "career.txt");
        private static string DailyFilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "StatsCore", "career-daily.txt");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    // Format: statKey|steamID=value[;firstUtc;lastUtc]  (timestamps optional, older files omit them)
                    int bar = line.IndexOf('|');
                    int eq = line.LastIndexOf('=');
                    if (bar < 0 || eq < bar) continue;
                    string key = line.Substring(0, bar);
                    string id = line.Substring(bar + 1, eq - bar - 1);
                    string rest = line.Substring(eq + 1);

                    var entry = new CareerEntry();
                    string[] parts = rest.Split(';');
                    if (!int.TryParse(parts[0], out entry.Value)) continue;
                    if (parts.Length > 1) long.TryParse(parts[1], out entry.FirstUtc);
                    if (parts.Length > 2) long.TryParse(parts[2], out entry.LastUtc);

                    if (!_saved.TryGetValue(key, out var map)) { map = new Dictionary<string, CareerEntry>(); _saved[key] = map; }
                    map[id] = entry;
                }
            }
            catch (Exception e)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't read career file: {e.Message}");
            }

            try
            {
                if (File.Exists(DailyFilePath))
                foreach (var line in File.ReadAllLines(DailyFilePath))
                {
                    // Format: statKey|steamID|dayEpoch=amount;dayEpoch=amount;...
                    int b1 = line.IndexOf('|');
                    int b2 = b1 < 0 ? -1 : line.IndexOf('|', b1 + 1);
                    if (b1 < 0 || b2 < 0) continue;
                    string key = line.Substring(0, b1);
                    string id = line.Substring(b1 + 1, b2 - b1 - 1);

                    var byDay = new Dictionary<int, int>();
                    foreach (string pair in line.Substring(b2 + 1).Split(';'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq <= 0) continue;
                        if (int.TryParse(pair.Substring(0, eq), out int day) &&
                            int.TryParse(pair.Substring(eq + 1), out int amount))
                            byDay[day] = amount;
                    }
                    if (byDay.Count == 0) continue;
                    if (!_savedDaily.TryGetValue(key, out var map)) { map = new Dictionary<string, Dictionary<int, int>>(); _savedDaily[key] = map; }
                    map[id] = byDay;
                }
            }
            catch (Exception e)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't read career daily file: {e.Message}");
            }
        }

        // Fill a freshly-defined Career stat with whatever was on disk.
        internal static void Hydrate(Stat stat)
        {
            EnsureLoaded();
            if (_saved.TryGetValue(stat.Key, out var map)) stat.HydrateCareer(map);
        }

        // Fill a stat's per-day history from disk. Called when a stat opts into TrackDaily() (which may be
        // after Define, so this is separate from Hydrate). The totals come from Hydrate; these are the buckets.
        internal static void HydrateDaily(Stat stat)
        {
            EnsureLoaded();
            if (!_savedDaily.TryGetValue(stat.Key, out var map)) return;
            foreach (var kv in map)
                stat.HydrateDailyEntry(kv.Key, new Dictionary<int, int>(kv.Value));
        }

        internal static void Save()
        {
            if (!Dirty) return;
            Dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                var sb = new StringBuilder();
                var daily = new StringBuilder();
                foreach (var stat in Stats.All)
                {
                    if (stat.Lifetime != StatLifetime.Career) continue;
                    foreach (var kv in stat.Raw)
                        sb.Append(stat.Key).Append('|').Append(kv.Key).Append('=').Append(kv.Value)
                          .Append(';').Append(stat.FirstUtc(kv.Key)).Append(';').Append(stat.LastUtc(kv.Key)).Append('\n');

                    if (!stat.TracksDaily) continue;
                    foreach (var perPlayer in stat.RawDaily)
                    {
                        if (perPlayer.Value.Count == 0) continue;
                        daily.Append(stat.Key).Append('|').Append(perPlayer.Key).Append('|');
                        bool any = false;
                        foreach (var day in perPlayer.Value)
                        {
                            if (any) daily.Append(';');
                            daily.Append(day.Key).Append('=').Append(day.Value);
                            any = true;
                        }
                        daily.Append('\n');
                    }
                }

                WriteAtomic(FilePath, sb.ToString());
                WriteAtomic(DailyFilePath, daily.ToString());
            }
            catch (Exception e)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't write career file: {e.Message}");
            }
        }

        // Write-then-swap so a crash mid-write can't leave a truncated file behind. Replace is atomic on the
        // same volume.
        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
