using System;
using System.Collections.Generic;

namespace StatsCore
{
    // The library entry point. Define a stat once, then read/write it through the returned handle.
    //
    //   using StatsCore;
    //   var drinks = Stats.Define("vippy.pourdecisions.drinks", StatScope.PerPlayer, StatLifetime.Run);
    //   drinks.Add(steamID, 1);          // host-authoritative for synced lifetimes; local if Career
    //   int n = drinks.Get(steamID);
    //
    // Another mod can READ your stat by its key:  Stats.Get("vippy.pourdecisions.drinks")?.Get(id)
    //
    // Level/Run/Session stats sync: the host's writes replicate to every client running StatsCore,
    // late joiners get a snapshot, and off-host writes are dropped (gate writes on Stats.IsHost).
    // Career stats are LOCAL: each machine keeps its own player's record on disk, nothing networked.
    // Keys must be unique and stable; convention "author.modname.stat".
    public static class Stats
    {
        private static readonly Dictionary<string, Stat> _stats = new();

        // Reserved by the sync wire format and the career file. Validated at Define
        // so a bad key fails the author loudly instead of corrupting frames later.
        private static readonly char[] ReservedKeyChars = { '|', ',', ':', ';', '=', ' ', '\t', '\n', '\r' };

        // True when this machine's writes to synced stats will land (host of a room,
        // or not in multiplayer at all).
        public static bool IsHost => StatsNet.CanWriteSynced;

        public static Stat Define(string key, StatScope scope, StatLifetime lifetime)
        {
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(ReservedKeyChars) >= 0)
                throw new ArgumentException($"stat key '{key}' is empty or contains a reserved character (| , : ; = or whitespace)", nameof(key));
            if (_stats.TryGetValue(key, out var existing))
            {
                // Same handle for everyone; logged so an accidental key collision
                // between two mods leaves a trace.
                StatsCorePlugin.Logger.LogDebug($"[StatsCore] Define('{key}') returned the existing stat (scope/lifetime of the first definition win)");
                return existing;
            }
            var stat = new Stat(key, scope, lifetime);
            _stats[key] = stat;
            if (lifetime == StatLifetime.Career) Persistence.Hydrate(stat);
            return stat;
        }

        public static Stat? Get(string key) => _stats.TryGetValue(key, out var s) ? s : null;

        public static IEnumerable<Stat> All => _stats.Values;

        // Fires whenever ANY defined stat changes: (stat, steamID, newValue). A firehose, handy for a stats
        // overlay or debug readout. For a targeted reaction, prefer a specific Stat.Changed or Stat.OnReach.
        public static event Action<Stat, string, int>? AnyChanged;
        internal static void RaiseAnyChanged(Stat stat, string steamID, int value) => AnyChanged?.Invoke(stat, steamID, value);

        // ---- lifecycle (driven locally by StatsDriver off REPO's scene transitions) ----

        internal static void ResetLifetime(StatLifetime lifetime)
        {
            foreach (var s in _stats.Values)
                if (s.Lifetime == lifetime) s.Clear();
        }
    }
}
