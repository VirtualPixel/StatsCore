# Changelog

## 0.2.0

First release.

- Define a stat once (`Stats.Define("author.mod.stat", scope, lifetime)`), read and write it through the handle. Keys validate at Define; reserved framing characters throw immediately.
- Level/Run/Session lifetimes sync: the host's writes replicate to every client running StatsCore, late joiners get a full snapshot on entry, and off-host writes drop with a debug log (gate on `Stats.IsHost`).
- Career lifetime persists locally to `BepInEx/config/StatsCore/career.txt` with debounced, atomic writes. First/last timestamps ride along.
- Lifetime resets follow the game's actual scene transitions, including the truck-lobby vs lobby-menu distinction that usually eats run stats.
- Built-in GameStats pack (deaths, revives, knockdowns, crown wins, map visits and friends) any mod can read without defining anything.
