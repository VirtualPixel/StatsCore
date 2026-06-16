# StatsCore

A stat-tracking library for R.E.P.O. mods. Define a stat once, then read and write it through a typed handle. StatsCore handles the parts every scorekeeping mod gets wrong: multiplayer sync, lifetime resets that follow the game's real scene flow, on-disk persistence, and querying history by day. It also ships a built-in pack of player stats any mod can read without writing a single tracker.

StatsCore is a library. On its own it quietly records the built-in pack; its real job is to be a dependency for other mods. A drinking-game mod, an end-of-run scoreboard, a "reward the unluckiest Semibot" mod: all of them just call into StatsCore instead of reinventing the bookkeeping.

A screen to browse your stats in-game is coming in a later release. This one is the engine underneath it.

## Install

Drop `StatsCore.dll` into `BepInEx/plugins`. Depends only on BepInEx. If you use a mod manager, add it as a dependency of your mod and the manager handles the rest.

## Quick start

```csharp
using StatsCore;

// Define once (e.g. in your plugin's Awake). Key convention: "author.modname.stat".
var drinks = Stats.Define("vippy.pourdecisions.drinks", StatScope.PerPlayer, StatLifetime.Career)
                  .Describe("Drinks", "Sips taken this career.", category: "Party")
                  .TrackDaily();

// Write. For Career this always lands locally; for synced lifetimes only the host's write lands.
drinks.Add(steamID, 1);

// Read.
int total = drinks.Get(steamID);
int today = drinks.GetToday(steamID);
int thisWeek = drinks.GetLastDays(steamID, 7);
```

Another mod can read your stat by its key without referencing your assembly:

```csharp
int n = Stats.Get("vippy.pourdecisions.drinks")?.Get(steamID) ?? 0;
```

## Scopes and lifetimes

A stat has a **scope** (who it's tracked for) and a **lifetime** (when it resets and whether it persists).

| `StatScope` | meaning |
|---|---|
| `PerPlayer` | one value per steamID |
| `Team` | a single shared value (use `GetTeam` / `SetTeam` / `AddTeam`) |

| `StatLifetime` | resets | synced? | persists? |
|---|---|---|---|
| `Level` | every scene / level transition | yes (host-authoritative) | no |
| `Run` | when a new run starts (back at the lobby menu) | yes (host-authoritative) | no |
| `Session` | never until the game closes | yes (host-authoritative) | no |
| `Career` | never | no (local to each machine) | yes, to disk |

The Run reset correctly distinguishes the truck "lobby" between levels (Run stats survive) from the real lobby menu that starts a fresh run (Run stats clear). That distinction is the thing most mods get wrong.

## The sync model

Synced stats (Level, Run, Session) flow one of two ways. Career stats stay local to each machine.

### Host-authoritative (default)

The host owns the value. Call `Add`/`Set` from host-gated code; it replicates to every client running StatsCore, and late joiners receive a full snapshot the moment they enter the room. A write on a client is dropped with a debug log, so gate your writes:

```csharp
if (Stats.IsHost) myStat.Add(steamID, 1);   // true on the host, or in singleplayer/offline
```

### Client-authoritative (push)

When the *client* is the one that knows the value, mark the stat `ClientAuthoritative()`. Each client owns its own player's slot and pushes changes to everyone, the host included. No host gate, and no client can write another player's slot (spoofed writes are rejected on receipt). This is the clean way to feed a host-side aggregate from per-client counters:

```csharp
// every client, defined the same way
var sips = Stats.Define("vippy.pourdecisions.drinks", StatScope.PerPlayer, StatLifetime.Session)
                .ClientAuthoritative();
sips.Add(localSteamID, 1);          // your own slot; lands on every machine
int crewTotal = sips.Sum();         // the host (and everyone) sees the whole crew
```

### Host-pull (request)

To pull a value that isn't synced continuously, including **Career / local-only** stats, the host can ask every client for its local value of a key. Each client answers for its own player; the host collects the replies (its own included) and gets a `steamID -> value` map when all reply or the timeout elapses:

```csharp
// host only; off-host it answers immediately with just the local value
Stats.RequestFromClients(GameStats.K_Deaths, byPlayer =>
{
    foreach (var kv in byPlayer) Log($"{kv.Key}: {kv.Value} career deaths");
}, timeoutSeconds: 3f);
```

This is how an end-of-run scoreboard gathers everyone's career numbers without each client having to publish them all the time.

### Career is local

Career stats are **local** to each machine. Every player keeps their own record on disk; nothing about Career touches the network except when the host explicitly pulls it. The built-in hooks fire on all clients, but each machine only records its own local Semibot, which keeps a clean per-player history.

## Persistence and querying by day

Career stats are written to `BepInEx/config/StatsCore/career.txt` (debounced, atomic write-then-swap, with first/last timestamps). You get the totals back automatically the moment you `Define` a Career stat.

Opt a Career stat into per-day history with `.TrackDaily()`. Each change is bucketed by UTC calendar day, persisted to `career-daily.txt`, and queryable:

```csharp
stat.GetToday(steamID);                      // gained today
stat.GetOnDay(steamID, someDate);            // gained on one date
stat.GetRange(steamID, fromDate, toDate);    // gained across a date range (inclusive)
stat.GetLastDays(steamID, 7);                // gained over the last 7 days, today included
stat.DailyHistory(steamID);                  // every day on record, oldest first
```

Buckets store the *change* on each day, so a range sum always reconciles to the running total. Daily history is opt-in because it grows the save file; leave it off for stats you'll only ever read as a lifetime total.

## Timestamps and milestones

Every stat stamps when each player's value was first recorded and last changed:

```csharp
DateTime? first = stat.FirstRecorded(steamID);
DateTime? last  = stat.LastUpdated(steamID);
```

Fire a one-shot callback the first time a value crosses a threshold (fires immediately if a freshly-hydrated Career stat is already past it):

```csharp
stat.OnReach(steamID, 100, () => GiveReward(steamID));
```

React to any change with `stat.Changed` (one stat) or `Stats.AnyChanged` (every stat, a firehose handy for an overlay).

## Trackers: binding a stat to game state

You usually don't write your own loop. A `Tracker` binds a stat to a vanilla value in one place, so a game update is a one-line fix and a removed member just disables that tracker (logged) instead of crashing.

```csharp
// HOOK - event-driven, zero per-frame cost, fires when a vanilla method runs.
Trackers.Hook(doors, typeof(PhysGrabObject), "GrabStarted",
    (s, inst, args) => s.Add(SteamIdOf(inst), 1), new[] { typeof(PhysGrabber) });

// MIRROR - read a value the game already keeps, sampled at lifecycle points (no per-frame cost).
Trackers.Mirror(haul, () => StatsManager.instance.GetRunStatTotalHaul(), SampleWhen.Tick);
Trackers.MirrorPlayers(health, p => p.playerHealth.health);
```

`SampleWhen` is `LevelChange`, `RunReset`, or `Tick` (a slow ~3s poll that only runs if some mirror asks for it).

## Built-in GameStats pack

Defined at boot, filled from vanilla events, free to read by any mod. Each is per-player and Career (persisted, local) unless noted. Read by handle (`GameStats.Deaths`) or by stable key (`GameStats.K_Deaths`).

| Category | Stats |
|---|---|
| Misfortune | Deaths (+ per-enemy breakdown), Knockdowns, Spewer Faces, Damage Taken (+ per-enemy breakdown), Hits Taken |
| Recovery | Revives, Health Healed |
| Glory | Crown Wins, Enemies Killed |
| Mobility | Distance Traveled / Walking / Sprinting (metres), Objects Grabbed |
| Mischief | Friendly Fire, Teammates Killed, Betrayal Taken |
| Expeditions | Map Visits (keyed by level name) |
| Expedition (live, synced team mirrors) | Run Level, Run Currency, Total Haul |

The per-enemy breakdowns are keyed by enemy display name rather than steamID, so `GameStats.DamageTakenByEnemy.Get("Loom")` tells you exactly how hard a given enemy has clapped you over your career. Distance is stored in metres; a display can convert to whatever unit it likes (the `StatFormat.Distance` hint marks it).

```csharp
int crowns = GameStats.CrownWins.Get(steamID);
int milesByLoom = GameStats.DamageTakenByEnemy.Get("Loom");
int ranToday = GameStats.DistanceSprinting.GetToday(steamID);
```

## A note on enemy names

Anything keyed by enemy name resolves through the game's own `enemyName`, so it always matches whatever the current build calls a given enemy, including ones added after this library shipped. Community nicknames (like "Loom") only match if that's the in-game name; otherwise read the name the game uses.
