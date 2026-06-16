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

        // When true, every change to this (Career) stat is also bucketed by calendar day so it can be queried
        // per-day. Opt in with TrackDaily(); only meaningful for Career, which is the only persisted lifetime.
        public bool TracksDaily { get; private set; }

        // Ownership of a synced stat. Default is host-authoritative (only the host writes; gate on
        // Stats.IsHost). Opt into ClientAuthoritative() to flip it: each client owns its OWN player's slot and
        // pushes changes to everyone - the host then has every client's value without asking. See StatsNet.
        public bool IsClientAuthoritative { get; private set; }

        // Fires after a value changes (locally or from the network): (steamID, newValue). Subscribe here to
        // trigger off a stat - e.g. reward when Get(id) crosses a threshold.
        public event Action<string, int>? Changed;

        private readonly Dictionary<string, int> _values = new();
        // When each player's value was first recorded / last changed (Unix seconds, 0 = never). Stamped on
        // every change so a permanent stat also answers "when" - first time, most recent time.
        private readonly Dictionary<string, long> _firstUtc = new();
        private readonly Dictionary<string, long> _lastUtc = new();
        // Per-player, per-day deltas: steamID -> (dayEpoch -> amount changed that day). dayEpoch is whole UTC
        // days since the Unix epoch. Only populated when TracksDaily; this is what query-by-day reads.
        private readonly Dictionary<string, Dictionary<int, int>> _daily = new();

        private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

        // Opt this stat into per-day history so it answers GetToday / GetOnDay / GetRange / GetLastDays /
        // DailyHistory. Chains off Define like Describe:
        //   Stats.Define("mod.drinks", PerPlayer, Career).Describe("Drinks").TrackDaily();
        // Career only - daily history rides the persisted record. Calling it triggers a daily hydrate so a
        // stat that opts in after Define still gets yesterday's buckets back from disk.
        public Stat TrackDaily()
        {
            if (Lifetime != StatLifetime.Career)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] TrackDaily() on non-career '{Key}' ignored - daily history needs persistence (use StatLifetime.Career)");
                return this;
            }
            if (!TracksDaily)
            {
                TracksDaily = true;
                Persistence.HydrateDaily(this);
            }
            return this;
        }

        // Make this synced stat client-authoritative instead of host-authoritative: each client writes its
        // OWN player's slot and the value replicates to everyone (the host included). Use it when the client
        // is the one that knows the value - per-client counters a host wants to aggregate live. Chains off
        // Define like Describe:
        //   Stats.Define("vippy.pourdecisions.drinks", PerPlayer, Session).ClientAuthoritative();
        // A client may only write its own slot; spoofing another player's slot is rejected on receipt.
        public Stat ClientAuthoritative()
        {
            if (Lifetime == StatLifetime.Career)
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] ClientAuthoritative() on career '{Key}' ignored - career stats are already local; use Stats.RequestFromClients to pull them");
            else
                IsClientAuthoritative = true;
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

        // ---- query by day (only meaningful when TrackDaily() was called) ----
        // Each bucket is how much the value CHANGED on that calendar day (UTC), so a range sum reconciles to
        // the total. Days are addressed by their date; the time-of-day on a passed DateTime is ignored.

        // Amount gained today (UTC).
        public int GetToday(string steamID) => Bucket(Norm(steamID), DayEpochNow());

        // Amount gained on one specific day.
        public int GetOnDay(string steamID, DateTime dayUtc) => Bucket(Norm(steamID), DayEpoch(dayUtc));

        // Amount gained across [fromUtc, toUtc] inclusive (order-independent).
        public int GetRange(string steamID, DateTime fromUtc, DateTime toUtc)
        {
            if (!_daily.TryGetValue(Norm(steamID), out var byDay)) return 0;
            int a = DayEpoch(fromUtc), b = DayEpoch(toUtc);
            if (a > b) (a, b) = (b, a);
            int sum = 0;
            foreach (var kv in byDay) if (kv.Key >= a && kv.Key <= b) sum += kv.Value;
            return sum;
        }

        // Amount gained over the last `days` days, today included (days=1 == today, 7 == this week so far).
        public int GetLastDays(string steamID, int days)
        {
            if (days < 1 || !_daily.TryGetValue(Norm(steamID), out var byDay)) return 0;
            int today = DayEpochNow(), from = today - (days - 1);
            int sum = 0;
            foreach (var kv in byDay) if (kv.Key >= from && kv.Key <= today) sum += kv.Value;
            return sum;
        }

        // Full per-day history, oldest first: each entry is (that day's UTC date, amount gained that day).
        public List<KeyValuePair<DateTime, int>> DailyHistory(string steamID)
        {
            var history = new List<KeyValuePair<DateTime, int>>();
            if (_daily.TryGetValue(Norm(steamID), out var byDay))
                foreach (var kv in byDay.OrderBy(k => k.Key))
                    history.Add(new KeyValuePair<DateTime, int>(DateOfEpoch(kv.Key), kv.Value));
            return history;
        }

        private int Bucket(string id, int dayEpoch) =>
            _daily.TryGetValue(id, out var byDay) && byDay.TryGetValue(dayEpoch, out var v) ? v : 0;

        private static int DayEpochNow() => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400L);
        private static int DayEpoch(DateTime day)
        {
            var utc = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Utc);
            return (int)(((DateTimeOffset)utc).ToUnixTimeSeconds() / 86400L);
        }
        private static DateTime DateOfEpoch(int dayEpoch) => UnixEpoch.AddDays(dayEpoch);

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
            // Career is local: always writable, never networked.
            if (Lifetime == StatLifetime.Career) { ApplyLocal(steamID, value); return; }

            // Client-authoritative: you may only write your own slot; the change replicates to everyone.
            if (IsClientAuthoritative)
            {
                if (!StatsNet.OwnsSlot(steamID))
                {
                    StatsCorePlugin.Logger.LogDebug($"[StatsCore] client-authoritative write to '{Key}' for a slot you don't own ignored (write your own steamID only)");
                    return;
                }
                ApplyLocal(steamID, value);
                StatsNet.OnOwnerWrote(this, steamID, value);
                return;
            }

            // Host-authoritative (default): only the host's write lands and replicates.
            if (!StatsNet.CanWriteSynced)
            {
                StatsCorePlugin.Logger.LogDebug($"[StatsCore] write to synced '{Key}' ignored off-host (gate on Stats.IsHost)");
                return;
            }
            ApplyLocal(steamID, value);
            StatsNet.OnHostWrote(this, steamID, value);
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
            int old = _values.TryGetValue(steamID, out var ov) ? ov : 0;
            _values[steamID] = value;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!_firstUtc.ContainsKey(steamID)) _firstUtc[steamID] = now;
            _lastUtc[steamID] = now;

            // Bucket the change into today. Hydrates bypass this path (they set _values directly), so loading
            // from disk never double-counts the per-day history.
            if (TracksDaily && Lifetime == StatLifetime.Career && value != old)
            {
                int day = (int)(now / 86400L);
                if (!_daily.TryGetValue(steamID, out var byDay)) { byDay = new Dictionary<int, int>(); _daily[steamID] = byDay; }
                byDay.TryGetValue(day, out var cur);
                byDay[day] = cur + (value - old);
            }

            Changed?.Invoke(steamID, value);
            Stats.RaiseAnyChanged(this, steamID, value);
            if (Lifetime == StatLifetime.Career) Persistence.MarkDirty();
        }

        internal void Clear()
        {
            _values.Clear();
            _firstUtc.Clear();
            _lastUtc.Clear();
            _daily.Clear();
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

        // ---- daily persistence hooks (used by Persistence) ----
        internal IEnumerable<KeyValuePair<string, Dictionary<int, int>>> RawDaily => _daily;
        internal void HydrateDailyEntry(string steamID, Dictionary<int, int> byDay) => _daily[steamID] = byDay;

        private static string Norm(string steamID) => string.IsNullOrEmpty(steamID) ? TeamKey : steamID;
    }
}
