using System;
using System.Collections.Generic;

namespace StatsCore
{
    // The library entry point. Define a stat once, then read/write it through the returned handle.
    //
    //   using StatsCore;
    //   var drinks = Stats.Define("vippy.pourdecisions.drinks", StatScope.PerPlayer, StatLifetime.Run);
    //   drinks.Add(steamID, 1);          // local; persisted if Career
    //   int n = drinks.Get(steamID);
    //
    // Another mod can READ your stat by its key:  Stats.Get("vippy.pourdecisions.drinks")?.Get(id)
    //
    // StatsCore is LOCAL ONLY - it never touches the network. Each machine tracks its own player's record
    // (and whatever a consumer feeds it). If you need everyone's data, the consuming mod does that
    // networking itself. Keys must be unique and stable; convention "author.modname.stat".
    public static class Stats
    {
        private static readonly Dictionary<string, Stat> _stats = new();

        public static Stat Define(string key, StatScope scope, StatLifetime lifetime)
        {
            if (_stats.TryGetValue(key, out var existing)) return existing;
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
