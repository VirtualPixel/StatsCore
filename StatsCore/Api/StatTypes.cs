namespace StatsCore
{
    // Who a stat is tracked for.
    public enum StatScope
    {
        PerPlayer,   // a value per steamID
        Team,        // a single shared value (stored under the team key)
    }

    // When a stat resets / whether it persists. Synced lifetimes are host-authoritative and replicated to
    // every client (and late joiners). Career is LOCAL to each machine and saved to disk across sessions.
    public enum StatLifetime
    {
        Level,    // cleared on every level / scene transition          (synced)
        Run,      // cleared when a new run starts (back in lobby menu)  (synced)
        Session,  // never auto-cleared; lives until the game closes     (synced)
        Career,   // never cleared; persisted to disk, local to this PC  (not synced)
    }

    // One player's persisted record for a stat: the value plus when it was first recorded and last changed
    // (Unix seconds, 0 = unknown). Used by the career file and HydrateCareer.
    internal struct CareerEntry
    {
        public int Value;
        public long FirstUtc;
        public long LastUtc;
    }

    // A display hint for stats-page / evaluation UIs - how a value should be read. The number stored is
    // always a plain int; Format only tells a renderer how to present it (e.g. seconds -> "2m 03s").
    public enum StatFormat
    {
        Count,     // a tally (doors touched, kills, drinks)
        Time,      // seconds
        Distance,  // meters
        Money,     // currency
        Percent,   // 0..100
        Raw,       // show the number as-is
    }
}
