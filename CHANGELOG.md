# Changelog

## 0.1.0

First release.

- Define a stat once (`Stats.Define("author.mod.stat", scope, lifetime)`), then read and write it through the returned handle. Keys validate at Define; reserved framing characters throw immediately.
- Four lifetimes: Level, Run and Session sync across the room; Career persists locally to disk.
- Host-authoritative sync (default): the host's writes replicate to every client running StatsCore, late joiners get a full snapshot on entry, and off-host writes drop with a debug log (gate on `Stats.IsHost`).
- Client-authoritative sync (`ClientAuthoritative()`): each client owns its own player's slot and pushes changes to everyone; receipts reject any client trying to write another player's slot.
- Host-pull (`Stats.RequestFromClients(key, callback)`): the host asks every client for its local value of a key and collects the answers, so a scoreboard can gather everyone's Career/local stats on demand.
- Career stats persist to `BepInEx/config/StatsCore/career.txt` with debounced, atomic writes. First and last timestamps ride along.
- Query by day: opt a Career stat into `TrackDaily()` and read `GetToday`, `GetOnDay`, `GetRange`, `GetLastDays`, and `DailyHistory`. Per-day history persists to `career-daily.txt`.
- Lifetime resets follow the game's actual scene transitions, including the truck-lobby vs lobby-menu distinction that usually eats run stats.
- Trackers bind game state to a stat in one place: `Hook` rides a vanilla method (zero per-frame cost), `Mirror`/`MirrorPlayers` sample a value the game already keeps. A renamed or removed target disables just that one tracker, logged, instead of crashing.
- Built-in GameStats pack any mod can read without defining anything: deaths (with per-enemy breakdown), knockdowns, spewer faces, damage taken (with per-enemy breakdown), hits, revives, health healed, crown wins, enemies killed, map visits, distance walked/sprinting, objects grabbed, friendly fire, and live synced mirrors of the run's level, currency, and haul.
