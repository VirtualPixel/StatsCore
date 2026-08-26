using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace StatsCore
{
    // After a hooked game method runs: do stat.Add(...). instance/args are the call's receiver and arguments.
    public delegate void StatHit(Stat stat, object? instance, object[] args);

    // When a Mirror samples a vanilla value. All cheap - no per-frame work:
    //   LevelChange / RunReset piggyback transitions StatsDriver already detects; Tick is a slow (~3s) poll.
    public enum SampleWhen { LevelChange, RunReset, Tick }

    // Binds GAME state to a stat. This is the ONE place game-version churn lives: a target is named by
    // (Type, "method"/"field") and resolved at runtime, so a rename is a one-line fix and a removed member
    // just disables that tracker (logged) instead of crashing. Two flavours, both "ride off vanilla":
    //
    //   HOOK   - event-driven, zero overhead, fires when the vanilla method runs:
    //     Trackers.Hook(doors, typeof(PhysGrabObject), "OnGrabbed", (s, inst, args) => s.Add(idFrom(inst), 1));
    //
    //   MIRROR - read a value the game ALREADY keeps, sampled at lifecycle points (no per-frame cost):
    //     Trackers.Mirror(rooms, () => LevelGenerator.Instance.RoomCount);                 // team/global
    //     Trackers.MirrorPlayers(distance, p => (int)p.someVanillaDistanceField);          // per player
    //
    // Each call is a self-contained, deletable object. Host-only effects (it writes synced stats).
    public static class Trackers
    {
        // ============================ hooks ============================

        public sealed class Hooked
        {
            public Stat Stat { get; }
            public MethodBase? Target { get; }
            public bool Enabled { get; private set; }
            internal readonly StatHit OnHit;

            internal Hooked(Stat stat, MethodBase? target, StatHit onHit)
            {
                Stat = stat; Target = target; OnHit = onHit; Enabled = target != null;
            }
            public void Disable() => Enabled = false;

            internal void Fire(object? instance, object[] args)
            {
                if (!Enabled) return;
                try { OnHit(Stat, instance, args); }
                catch (Exception e) { StatsCorePlugin.Logger.LogWarning($"[StatsCore] hook for '{Stat.Key}' threw: {e.Message}"); }
            }
        }

        private static readonly Harmony _harmony = new("Vippy.StatsCore.Trackers");
        private static readonly Dictionary<MethodBase, List<Hooked>> _byMethod = new();

        public static Hooked Hook(Stat stat, Type gameType, string methodName, StatHit onHit, Type[]? argTypes = null)
        {
            MethodBase? target = AccessTools.Method(gameType, methodName, argTypes);
            var hooked = new Hooked(stat, target, onHit);
            if (target == null)
            {
                StatsCorePlugin.Logger.LogWarning($"[StatsCore] {gameType.Name}.{methodName} not found - hook for '{stat.Key}' disabled (game update?).");
                return hooked;
            }
            if (!_byMethod.TryGetValue(target, out var list))
            {
                list = new List<Hooked>();
                _byMethod[target] = list;
                try { _harmony.Patch(target, postfix: new HarmonyMethod(typeof(Trackers), nameof(HookPostfix))); }
                catch (Exception e)
                {
                    StatsCorePlugin.Logger.LogWarning($"[StatsCore] couldn't patch {gameType.Name}.{methodName}: {e.Message}");
                    hooked.Disable();
                    _byMethod.Remove(target);
                    return hooked;
                }
            }
            list.Add(hooked);
            return hooked;
        }

        // Harmony injects the running method + receiver + args; we demux to the hooks on that method.
        private static void HookPostfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (_byMethod.TryGetValue(__originalMethod, out var list))
                for (int i = 0; i < list.Count; i++) list[i].Fire(__instance, __args);
        }

        // ============================ mirrors ============================

        private sealed class MirrorEntry
        {
            public Stat Stat = null!;
            public SampleWhen When;
            public Func<int>? Global;
            public Func<PlayerAvatar, int>? PerPlayer;
        }
        private static readonly List<MirrorEntry> _mirrors = new();

        // Mirror a single vanilla value (rooms unlocked, quota, etc.) into the stat's team slot.
        public static void Mirror(Stat stat, Func<int> read, SampleWhen when = SampleWhen.LevelChange)
            => _mirrors.Add(new MirrorEntry { Stat = stat, Global = read, When = when });

        // Mirror a per-player vanilla value (distance, hp, whatever the game already tracks) for every player.
        public static void MirrorPlayers(Stat stat, Func<PlayerAvatar, int> read, SampleWhen when = SampleWhen.LevelChange)
            => _mirrors.Add(new MirrorEntry { Stat = stat, PerPlayer = read, When = when });

        // Called by StatsDriver at the matching moment.
        internal static void Sample(SampleWhen when)
        {
            // A mirror writes the stat, so on a client a synced mirror would be dropped by Stat.Write and log
            // a line for it - every 3s, forever, on every client with a Tick mirror. Don't sample those at all.
            bool canWriteSynced = Stats.IsHost;
            for (int i = 0; i < _mirrors.Count; i++)
            {
                var m = _mirrors[i];
                if (m.When != when) continue;
                if (!canWriteSynced && m.Stat.Lifetime != StatLifetime.Career && !m.Stat.IsClientAuthoritative) continue;
                try
                {
                    if (m.Global != null) m.Stat.SetTeam(m.Global());
                    else if (m.PerPlayer != null)
                        foreach (var p in SemiFunc.PlayerGetAll())
                            if (p != null) m.Stat.Set(SemiFunc.PlayerGetSteamID(p), m.PerPlayer(p));
                }
                catch (Exception e) { StatsCorePlugin.Logger.LogWarning($"[StatsCore] mirror for '{m.Stat.Key}' threw: {e.Message}"); }
            }
        }

        internal static bool HasTickMirrors()
        {
            for (int i = 0; i < _mirrors.Count; i++) if (_mirrors[i].When == SampleWhen.Tick) return true;
            return false;
        }
    }
}
