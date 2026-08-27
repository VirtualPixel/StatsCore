# Changelog

## Unreleased

- Career rows belonging to a stat nobody defined this session are kept instead of dropped. Running once without a mod that owns some career stats used to erase its history on the next save; the same went for daily buckets when a stat never called `TrackDaily()`.
- Daily buckets are the machine's local calendar day, not UTC. `GetToday` read UTC while `GetOnDay` read the raw date components off whatever you handed it, so the two disagreed between local midnight and UTC midnight. Buckets written by 0.1.0 can sit one day off; everything from here is consistent.
- Slot keys are sanitized. Deaths-by-enemy, damage-by-enemy and map visits key on a display name a content mod can set to anything, and `| = ; ,` in one of those split a save line or a snapshot frame in the wrong place. Those characters fold to `_` now.
- Fixed a NullReferenceException every frame between the chainloader and the first level: the driver called `SemiFunc.RunIsLevel()`, which dereferences `RunManager.instance` unguarded, before it existed.
- StatsNet attaches to `PunManager` even when its `Awake` already ran before the chainloader reached us. It used to leave sync silently dead for the whole session.
- Host migration: the new master pushes a snapshot so the room agrees on the values it is now authoritative for, and pulls the old host had open are dropped rather than firing a half-filled callback.
- A client can no longer answer the same `RequestFromClients` pull more than once and complete it before the rest of the room replies; a client that leaves mid-pull stops being waited on.
- A networked write naming a career stat is refused. Career never rides the wire, so nothing legitimate sends one, and without the check a peer running a patched build could write straight into another player's permanent record.
- Career files carry a version header and the atomic swap keeps the version it replaced as `.bak`, which is read back if the live file has gone unreadable. Files written by 0.1.0 still load.
- Mirrors on synced stats are no longer sampled on clients, where the write was dropped and logged every 3 seconds for the whole session.

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
