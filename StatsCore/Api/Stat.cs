using System;
using System.Collections.Generic;
using System.Linq;

namespace StatsCore
{
    // A single tracked stat - a named bag of per-player (or team) integers. Get works everywhere; writes
    // (Add/Set) are HOST-AUTHORITATIVE for synced lifetimes: call them from host-gated code, and the value
    // replicates to every client automatically. Career stats are local and always writable.
    //
    // You never construct this directly - get one from Stats.Define(...).
    public sealed class Stat
    {
        public const string TeamKey = "_team";

        public string Key { get; }
        public StatScope Scope { get; }
        public StatLifetime Lifetime { get; }

        // Optional display metadata - for stats-page / evaluation UIs that enumerate Stats.All. Purely
        // presentational; the tracked value is unaffected. Set via Describe(); DisplayName defaults to Key.
        public string DisplayName { get; private set; }
        public string Description { get; private set; } = "";
        public string Category { get; private set; } = "";
        public StatFormat Format { get; private set; } = StatFormat.Count;

        // Fires after a value changes (locally or from the network): (steamID, newValue). Subscribe here to
        // trigger off a stat - e.g. reward when Get(id) crosses a threshold.
        public event Action<string, int>? Changed;

        private readonly Dictionary<string, int> _values = new();
        // When each player's value was first recorded / last changed (Unix seconds, 0 = never). Stamped on
        // every change so a permanent stat also answers "when" - first time, most recent time.
        private readonly Dictionary<string, long> _firstUtc = new();
        private readonly Dictionary<string, long> _lastUtc = new();

        internal Stat(string key, StatScope scope, StatLifetime lifetime)
        {
            Key = key;
            Scope = scope;
            Lifetime = lifetime;
            DisplayName = key;
        }

        // Fluent, optional. Returns the same Stat so it chains off Define():
        //   Stats.Define("mod.doors", PerPlayer, Run).Describe("Doors Touched", category: "Curiosities");
        public Stat Describe(string? displayName = null, string? description = null,
                             string? category = null, StatFormat? format = null)
        {
            if (displayName != null) DisplayName = displayName;
            if (description != null) Description = description;
            if (category != null) Category = category;
            if (format != null) Format = format.Value;
            return this;
        }

        // ---- reads ----

        public int Get(string steamID) => _values.TryGetValue(Norm(steamID), out var v) ? v : 0;
        public int GetTeam() => Get(TeamKey);

        // Sum across players (excludes the team slot).
        public int Sum()
        {
            int s = 0;
            foreach (var kv in _values) if (kv.Key != TeamKey) s += kv.Value;
            return s;
        }

        // Heaviest first, team slot excluded. Each entry is (steamID, value).
        public List<KeyValuePair<string, int>> Leaderboard() =>
            _values.Where(kv => kv.Key != TeamKey).OrderByDescending(kv => kv.Value).ToList();

        public IReadOnlyDictionary<string, int> Entries => _values;

        // ---- timestamps (Unix seconds; 0 = never recorded) ----

        public long FirstUtc(string steamID) => _firstUtc.TryGetValue(Norm(steamID), out var t) ? t : 0;
        public long LastUtc(string steamID) => _lastUtc.TryGetValue(Norm(steamID), out var t) ? t : 0;

        public DateTime? FirstRecorded(string steamID)
        {
            long t = FirstUtc(steamID);
            return t == 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime;
        }
        public DateTime? LastUpdated(string steamID)
        {
            long t = LastUtc(steamID);
            return t == 0 ? (DateTime?)null : DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime;
        }

        // ---- writes ----
        // Career writes always land locally. Synced lifetimes (Level/Run/Session) are
        // host-authoritative: on the host (or in singleplayer) the write lands and
        // replicates to everyone; on a client it's dropped with a debug log so the
        // consuming mod author sees why in LogOutput instead of chasing a ghost.

        public void Add(string steamID, int delta) => Write(Norm(steamID), Get(steamID) + delta);
        public void Set(string steamID, int value) => Write(Norm(steamID), value);
        public void AddTeam(int delta) => Write(TeamKey, GetTeam() + delta);
        public void SetTeam(int value) => Write(TeamKey, value);

        private void Write(string steamID, int value)
        {
            if (Lifetime != StatLifetime.Career && !StatsNet.CanWriteSynced)
            {
                StatsCorePlugin.Logger.LogDebug($"[StatsCore] write to synced '{Key}' ignored off-host (gate on Stats.IsHost)");
                return;
            }
            ApplyLocal(steamID, value);
            if (Lifetime != StatLifetime.Career) StatsNet.OnHostWrote(this, steamID, value);
        }

        // Network-applied values land without rebroadcast.
        internal void ApplyFromNetwork(string steamID, int value) => ApplyLocal(Norm(steamID), value);

        // Fire `callback` once, the first time `steamID`'s value reaches `threshold` (>=). If it's already
        // there - e.g. a career stat just hydrated from disk - fires immediately. One-shot: unsubscribes
        // itself, so a milestone reward is a single line and never re-triggers.
        public void OnReach(string steamID, int threshold, Action callback)
        {
            string id = Norm(steamID);
            if (Get(id) >= threshold) { callback(); return; }
            void Handler(string changedId, int newValue)
            {
                if (changedId != id || newValue < threshold) return;
                Changed -= Handler;
                callback();
            }
            Changed += Handler;
        }

        // ---- internal: apply a value (from a write or a career hydrate) ----
        internal void ApplyLocal(string steamID, int value)
        {
            _values[steamID] = value;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!_firstUtc.ContainsKey(steamID)) _firstUtc[steamID] = now;
            _lastUtc[steamID] = now;
            Changed?.Invoke(steamID, value);
            Stats.RaiseAnyChanged(this, steamID, value);
            if (Lifetime == StatLifetime.Career) Persistence.MarkDirty();
        }

        internal void Clear()
        {
            _values.Clear();
            _firstUtc.Clear();
            _lastUtc.Clear();
        }

        internal void HydrateCareer(Dictionary<string, CareerEntry> saved)
        {
            foreach (var kv in saved)
            {
                _values[kv.Key] = kv.Value.Value;
                if (kv.Value.FirstUtc != 0) _firstUtc[kv.Key] = kv.Value.FirstUtc;
                if (kv.Value.LastUtc != 0) _lastUtc[kv.Key] = kv.Value.LastUtc;
            }
        }

        internal IEnumerable<KeyValuePair<string, int>> Raw => _values;

        private static string Norm(string steamID) => string.IsNullOrEmpty(steamID) ? TeamKey : steamID;
    }
}
