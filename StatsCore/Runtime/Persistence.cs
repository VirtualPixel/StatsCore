using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace StatsCore
{
    // Career stats persisted to this machine's disk (never the game save). Two flat files -
    // career.txt for totals, career-daily.txt for per-day history - in the format CareerFile describes.
    // They live in the game's own per-user data folder, which every mod-manager profile shares, so a
    // career is one record however many profiles you play from. Loaded once; a Career stat hydrates from
    // it the moment it's Defined (consumers Define at their own Awake, which may be after we load).
    //
    // What was read from disk is kept for the life of the session. A save re-emits every row whose stat
    // isn't defined right now, so running the game once without a mod that owns some career stats doesn't
    // wipe their history: uninstall PourDecisions for an evening, put it back, the numbers are still there.
    internal static class Persistence
    {
        internal static bool Dirty { get; private set; }
        internal static void MarkDirty() => Dirty = true;

        private static bool _loaded;
        // statKey -> (slot -> value + timestamps), as read from disk.
        private static readonly Dictionary<string, Dictionary<string, CareerEntry>> _saved = new();
        // statKey -> (slot -> (dayIndex -> amount)), the per-day history read from disk.
        private static readonly Dictionary<string, Dictionary<string, Dictionary<int, int>>> _savedDaily = new();

        // Versions before 1.0.0 kept a separate career inside each profile's config folder. Those are folded
        // into the shared one once, then set aside.
        private static readonly HashSet<string> _merged = new();
        private static readonly List<string> _retire = new();

        private static string Dir => Path.Combine(Application.persistentDataPath, "StatsCore");
        private static string LegacyDir => Path.Combine(BepInEx.Paths.ConfigPath, "StatsCore");
        private static string FilePath => Path.Combine(Dir, "career.txt");
        private static string DailyFilePath => Path.Combine(Dir, "career-daily.txt");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Load(FilePath, lines =>
            {
                foreach (var kv in CareerFile.ParseTotals(lines)) _saved[kv.Key] = kv.Value;
                _merged.UnionWith(CareerFile.ParseMerged(lines));
            }, "career file");
            Load(DailyFilePath, lines => { foreach (var kv in CareerFile.ParseDaily(lines)) _savedDaily[kv.Key] = kv.Value; }, "career daily file");
            MergeLegacy();
        }

        // Fold this profile's old per-profile career into the shared one. Totals add up, so each file may only
        // be merged once: a marker is written into the shared file with the totals it produced, and the old
        // file is renamed once that save has landed.
        private static void MergeLegacy()
        {
            string totalsPath = Path.Combine(LegacyDir, "career.txt");
            string dailyPath = Path.Combine(LegacyDir, "career-daily.txt");
            bool hasTotals = File.Exists(totalsPath), hasDaily = File.Exists(dailyPath);
            if (!hasTotals && !hasDaily) return;

            string token = Token(totalsPath, dailyPath);
            if (!_merged.Contains(token))
            {
                try
                {
                    string[] totals = hasTotals ? File.ReadAllLines(totalsPath) : Array.Empty<string>();
                    string[] daily = hasDaily ? File.ReadAllLines(dailyPath) : Array.Empty<string>();
                    CareerFile.MergeTotals(_saved, CareerFile.ParseTotals(totals));
                    CareerFile.MergeDaily(_savedDaily, CareerFile.ParseDaily(daily));
                }
                catch (Exception e)
                {
                    StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't read this profile's older career file: {e.Message}");
                    return;
                }
                _merged.Add(token);
                MarkDirty();
                StatsCorePlugin.Logger.LogInfo("[StatsCore] folded this profile's career history into the shared one");
            }
            if (hasTotals) _retire.Add(totalsPath);
            if (hasDaily) _retire.Add(dailyPath);
        }

        private static string Token(string totalsPath, string dailyPath)
        {
            string raw = Path.GetFullPath(LegacyDir) + "|" + Describe(totalsPath) + "|" + Describe(dailyPath);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        private static string Describe(string path)
        {
            if (!File.Exists(path)) return "none";
            var info = new FileInfo(path);
            return info.Length + ":" + info.LastWriteTimeUtc.Ticks;
        }

        private static void RetireLegacy()
        {
            foreach (string path in _retire)
            {
                try
                {
                    string target = path + ".migrated";
                    if (File.Exists(target)) target = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".migrated";
                    File.Move(path, target);
                }
                catch (Exception e)
                {
                    StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't set aside {Path.GetFileName(path)}: {e.Message}");
                }
            }
            _retire.Clear();
        }

        // Read a file, falling back to the previous good copy if the current one is gone or unreadable. Save
        // keeps that copy as a side effect of the atomic swap, so recovery costs one session's writes at worst.
        private static void Load(string path, Action<string[]> parse, string what)
        {
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    parse(File.ReadAllLines(candidate));
                    if (candidate != path)
                        StatsCorePlugin.Logger.LogWarning($"[StatsCore] {what} was missing or unreadable - recovered from {Path.GetFileName(candidate)}");
                    return;
                }
                catch (Exception e)
                {
                    StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't read {what} ({Path.GetFileName(candidate)}): {e.Message}");
                }
            }
        }

        // Fill a freshly-defined Career stat with whatever was on disk.
        internal static void Hydrate(Stat stat)
        {
            EnsureLoaded();
            if (_saved.TryGetValue(stat.Key, out var slots)) stat.HydrateCareer(slots);
        }

        // Fill a stat's per-day history from disk. Called when a stat opts into TrackDaily() (which may be
        // after Define, so this is separate from Hydrate). The totals come from Hydrate; these are the buckets.
        internal static void HydrateDaily(Stat stat)
        {
            EnsureLoaded();
            if (!_savedDaily.TryGetValue(stat.Key, out var slots)) return;
            foreach (var kv in slots)
                stat.HydrateDailyEntry(kv.Key, new Dictionary<int, int>(kv.Value));
        }

        internal static void Save()
        {
            if (!Dirty) return;
            EnsureLoaded();
            try
            {
                Directory.CreateDirectory(Dir);

                var liveTotals = new Dictionary<string, Dictionary<string, CareerEntry>>();
                var liveDaily = new Dictionary<string, Dictionary<string, Dictionary<int, int>>>();

                foreach (var stat in Stats.All)
                {
                    if (stat.Lifetime != StatLifetime.Career) continue;

                    var slots = new Dictionary<string, CareerEntry>();
                    foreach (var kv in stat.Raw)
                        slots[kv.Key] = new CareerEntry { Value = kv.Value, FirstUtc = stat.FirstUtc(kv.Key), LastUtc = stat.LastUtc(kv.Key) };
                    liveTotals[stat.Key] = slots;

                    if (!stat.TracksDaily) continue;
                    var days = new Dictionary<string, Dictionary<int, int>>();
                    foreach (var perSlot in stat.RawDaily) days[perSlot.Key] = perSlot.Value;
                    liveDaily[stat.Key] = days;
                }

                WriteAtomic(FilePath, CareerFile.SerializeTotals(liveTotals, _saved, _merged));
                WriteAtomic(DailyFilePath, CareerFile.SerializeDaily(liveDaily, _savedDaily));
                Dirty = false;   // only once both files are down; a failed write retries on the next flush
                RetireLegacy();
            }
            catch (Exception e)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't write career file: {e.Message}");
            }
        }

        // Write-then-swap so a crash mid-write can't leave a truncated file behind, keeping the version it
        // replaced as .bak for Load to fall back on. Replace is atomic on the same volume.
        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
    }
}
