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

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "StatsCore", "career.txt");

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
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
        }

        // Fill a freshly-defined Career stat with whatever was on disk.
        internal static void Hydrate(Stat stat)
        {
            EnsureLoaded();
            if (_saved.TryGetValue(stat.Key, out var map)) stat.HydrateCareer(map);
        }

        internal static void Save()
        {
            if (!Dirty) return;
            Dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var sb = new StringBuilder();
                foreach (var stat in Stats.All)
                {
                    if (stat.Lifetime != StatLifetime.Career) continue;
                    foreach (var kv in stat.Raw)
                        sb.Append(stat.Key).Append('|').Append(kv.Key).Append('=').Append(kv.Value)
                          .Append(';').Append(stat.FirstUtc(kv.Key)).Append(';').Append(stat.LastUtc(kv.Key)).Append('\n');
                }
                // Write-then-swap so a crash mid-write can't leave a truncated
                // career file behind. Replace is atomic on the same volume.
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't write career file: {e.Message}");
            }
        }
    }
}
