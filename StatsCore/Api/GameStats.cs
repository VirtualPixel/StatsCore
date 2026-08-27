using UnityEngine;

namespace StatsCore
{
    // The built-in REPO stat pack - a permanent, per-player record any mod can read by key without writing
    // its own trackers. Define once at boot; the values fill themselves from vanilla events and persist to
    // disk across sessions. Want to reward a good player? Read GameStats.CrownWins.Get(steamID) and go.
    //
    //   int crowns = GameStats.CrownWins.Get(steamID);             // typed handle
    //   int deaths = Stats.Get(GameStats.K_Deaths)?.Get(id) ?? 0;  // or by stable key, no reference needed
    //   int today  = GameStats.DistanceTraveled.GetToday(id);      // meters walked/ran today (query by day)
    //
    // Most of these are CAREER stats: persisted, and LOCAL to each machine - every player's file holds their
    // own record (the hooks fire on all clients, but each only writes for its own local avatar). That keeps a
    // clean per-player history without one machine guessing at another's lifetime totals. A handful at the
    // bottom (Run*) are synced team mirrors of the live expedition, shown as how the Mirror feature is used.
    public static class GameStats
    {
        // Stable, public keys - the contract other mods read against. Don't rename these.
        public const string K_Deaths = "statscore.deaths";
        public const string K_DeathsByEnemy = "statscore.deathsByEnemy";       // keyed by ENEMY NAME, not steamID
        public const string K_Knockdowns = "statscore.knockdowns";
        public const string K_Revives = "statscore.revives";
        public const string K_SpewerFaces = "statscore.spewerFaces";
        public const string K_CrownWins = "statscore.crownWins";
        public const string K_MapVisits = "statscore.mapVisits";               // keyed by LEVEL NAME, not steamID
        public const string K_DamageTaken = "statscore.damageTaken";
        public const string K_DamageTakenByEnemy = "statscore.damageTakenByEnemy"; // keyed by ENEMY NAME, not steamID
        public const string K_HitsTaken = "statscore.hitsTaken";
        public const string K_HealthHealed = "statscore.healthHealed";
        public const string K_DistanceTraveled = "statscore.distanceTraveled"; // meters
        public const string K_DistanceWalking = "statscore.distanceWalking";   // meters at walking pace
        public const string K_DistanceSprinting = "statscore.distanceSprinting"; // meters at a sprint
        public const string K_ObjectsGrabbed = "statscore.objectsGrabbed";
        public const string K_EnemiesKilled = "statscore.enemiesKilled";
        public const string K_FriendlyFireDealt = "statscore.friendlyFireDealt";
        public const string K_TeammatesKilled = "statscore.teammatesKilled";
        public const string K_DamageFromTeammates = "statscore.damageFromTeammates";
        public const string K_RunLevel = "statscore.runLevel";                 // synced team mirror
        public const string K_RunCurrency = "statscore.runCurrency";           // synced team mirror
        public const string K_RunTotalHaul = "statscore.runTotalHaul";         // synced team mirror

        public static Stat Deaths { get; private set; } = null!;
        public static Stat DeathsByEnemy { get; private set; } = null!;
        public static Stat Knockdowns { get; private set; } = null!;
        public static Stat Revives { get; private set; } = null!;
        public static Stat SpewerFaces { get; private set; } = null!;
        public static Stat CrownWins { get; private set; } = null!;
        public static Stat MapVisits { get; private set; } = null!;
        public static Stat DamageTaken { get; private set; } = null!;
        public static Stat DamageTakenByEnemy { get; private set; } = null!;
        public static Stat HitsTaken { get; private set; } = null!;
        public static Stat HealthHealed { get; private set; } = null!;
        public static Stat DistanceTraveled { get; private set; } = null!;
        public static Stat DistanceWalking { get; private set; } = null!;
        public static Stat DistanceSprinting { get; private set; } = null!;
        public static Stat ObjectsGrabbed { get; private set; } = null!;
        public static Stat EnemiesKilled { get; private set; } = null!;
        public static Stat FriendlyFireDealt { get; private set; } = null!;
        public static Stat TeammatesKilled { get; private set; } = null!;
        public static Stat DamageFromTeammates { get; private set; } = null!;
        public static Stat RunLevel { get; private set; } = null!;
        public static Stat RunCurrency { get; private set; } = null!;
        public static Stat RunTotalHaul { get; private set; } = null!;

        private static bool _registered;

        // Called once from StatsCorePlugin.Awake. Defines the pack and wires each stat to the vanilla event
        // that feeds it. A target the current game build no longer has just disables that one tracker
        // (logged by Trackers) - the rest keep working.
        internal static void Register()
        {
            if (_registered) return;
            _registered = true;

            DefinePack();
            WireMisfortune();
            WireRecovery();
            WireGlory();
            WireMischief();
            WireRunMirrors();
        }

        private static void DefinePack()
        {
            Deaths = Career(K_Deaths, "Deaths", "Times you've gone down for good.", "Misfortune").TrackDaily();
            DeathsByEnemy = Career(K_DeathsByEnemy, "Deaths by Enemy", "Your deaths, broken down by what killed you (keyed by enemy name).", "Misfortune");
            Knockdowns = Career(K_Knockdowns, "Knockdowns", "Times something put you on the floor.", "Misfortune").TrackDaily();
            SpewerFaces = Career(K_SpewerFaces, "Spewer Faces", "Times a spewer latched onto your face.", "Misfortune");
            DamageTaken = Career(K_DamageTaken, "Damage Taken", "Total damage you've soaked.", "Misfortune", StatFormat.Raw).TrackDaily();
            DamageTakenByEnemy = Career(K_DamageTakenByEnemy, "Damage by Enemy", "Damage dealt to you, broken down by what dealt it (keyed by enemy name). How hard each thing has clapped you.", "Misfortune", StatFormat.Raw);
            HitsTaken = Career(K_HitsTaken, "Hits Taken", "Number of separate times you've been hurt.", "Misfortune").TrackDaily();

            Revives = Career(K_Revives, "Revives", "Times you were brought back.", "Recovery").TrackDaily();
            HealthHealed = Career(K_HealthHealed, "Health Healed", "Total health restored to you.", "Recovery", StatFormat.Raw).TrackDaily();

            CrownWins = Career(K_CrownWins, "Crown Wins", "King-of-the-losers victories.", "Glory");
            EnemiesKilled = Career(K_EnemiesKilled, "Enemies Killed", "Enemies that died to your handiwork (best effort - the game rarely names a killer).", "Glory").TrackDaily();
            MapVisits = Career(K_MapVisits, "Map Visits", "How many times you've dropped into each level (keyed by level name).", "Expeditions");
            DistanceTraveled = Career(K_DistanceTraveled, "Distance Traveled", "Total ground you've covered, in meters.", "Mobility", StatFormat.Distance).TrackDaily();
            DistanceWalking = Career(K_DistanceWalking, "Distance Walking", "Metres covered at a walk.", "Mobility", StatFormat.Distance).TrackDaily();
            DistanceSprinting = Career(K_DistanceSprinting, "Distance Sprinting", "Metres covered at a sprint.", "Mobility", StatFormat.Distance).TrackDaily();
            ObjectsGrabbed = Career(K_ObjectsGrabbed, "Objects Grabbed", "Things you've latched onto with the grab beam.", "Mobility").TrackDaily();

            FriendlyFireDealt = Career(K_FriendlyFireDealt, "Friendly Fire", "Damage you've dealt to your own crew.", "Mischief", StatFormat.Raw).TrackDaily();
            TeammatesKilled = Career(K_TeammatesKilled, "Teammates Killed", "Crewmates you've personally finished off.", "Mischief");
            DamageFromTeammates = Career(K_DamageFromTeammates, "Betrayal Taken", "Damage your own crew has dealt to you.", "Mischief", StatFormat.Raw).TrackDaily();

            RunLevel = Stats.Define(K_RunLevel, StatScope.Team, StatLifetime.Run)
                .Describe("Run Level", "The expedition level the crew is on.", "Expedition", StatFormat.Raw);
            RunCurrency = Stats.Define(K_RunCurrency, StatScope.Team, StatLifetime.Run)
                .Describe("Run Currency", "Money the crew is holding this run.", "Expedition", StatFormat.Money);
            RunTotalHaul = Stats.Define(K_RunTotalHaul, StatScope.Team, StatLifetime.Run)
                .Describe("Total Haul", "Total value the crew has hauled this run.", "Expedition", StatFormat.Money);
        }

        // ---- Misfortune: deaths, knockdowns, damage taken ----
        private static void WireMisfortune()
        {
            // You died - total, plus a per-enemy breakdown (PlayerDeathRPC(int enemyIndex)).
            Trackers.Hook(Deaths, typeof(PlayerAvatar), "PlayerDeathRPC", (s, inst, args) =>
            {
                if (!(inst is PlayerAvatar pa) || !IsLocal(pa)) return;
                s.Add(pa.steamID, 1);
                int idx = (args.Length > 0 && args[0] is int i) ? i : -1;
                DeathsByEnemy.Add(EnemyName(idx), 1);
            });

            // Knocked down by something (not a self-tumble): TumbleSetRPC(isTumbling, playerInput).
            Trackers.Hook(Knockdowns, typeof(PlayerTumble), "TumbleSetRPC", (s, inst, args) =>
            {
                if (args.Length < 2 || !(bool)args[0] || (bool)args[1]) return;
                var pa = (inst as PlayerTumble)?.playerAvatar;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // A spewer (slow-mouth) clamped onto your face: UpdateStateRPC(State) reaching Attached.
            Trackers.Hook(SpewerFaces, typeof(EnemySlowMouth), "UpdateStateRPC", (s, inst, args) =>
            {
                if (args.Length < 1 || (EnemySlowMouth.State)args[0] != EnemySlowMouth.State.Attached) return;
                var pa = (inst as EnemySlowMouth)?.playerTarget;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // You took damage - total, hit count, and a per-enemy breakdown ("how hard has Loom clapped you").
            // Hurt(int damage, bool savingGrace, int enemyIndex, bool hurtByHeal) is the local owner's apply.
            Trackers.Hook(DamageTaken, typeof(PlayerHealth), "Hurt", (s, inst, args) =>
            {
                if (!(inst is PlayerHealth ph)) return;
                var pa = ph.playerAvatar;
                if (pa == null || !IsLocal(pa)) return;
                int dmg = (args.Length > 0 && args[0] is int d) ? d : 0;
                bool hurtByHeal = args.Length > 3 && args[3] is bool hb && hb;
                if (dmg <= 0 || hurtByHeal) return;
                s.Add(pa.steamID, dmg);
                HitsTaken.Add(pa.steamID, 1);
                int idx = (args.Length > 2 && args[2] is int i) ? i : -1;
                DamageTakenByEnemy.Add(EnemyName(idx), dmg);
            }, new[] { typeof(int), typeof(bool), typeof(int), typeof(bool) });
        }

        // ---- Recovery: revives, healing ----
        private static void WireRecovery()
        {
            // Brought back.
            Trackers.Hook(Revives, typeof(PlayerAvatar), "ReviveRPC", (s, inst, args) =>
            {
                if (inst is PlayerAvatar pa && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // Health restored: Heal(int healAmount, bool effect).
            Trackers.Hook(HealthHealed, typeof(PlayerHealth), "Heal", (s, inst, args) =>
            {
                if (!(inst is PlayerHealth ph)) return;
                var pa = ph.playerAvatar;
                if (pa == null || !IsLocal(pa)) return;
                int amt = (args.Length > 0 && args[0] is int a) ? a : 0;
                if (amt > 0) s.Add(pa.steamID, amt);
            }, new[] { typeof(int), typeof(bool) });
        }

        // ---- Glory: crowns, kills, grabbing ----
        private static void WireGlory()
        {
            // You grabbed the crown in the arena: CrownGrabRPC sets winnerPlayer.
            Trackers.Hook(CrownWins, typeof(Arena), "CrownGrabRPC", (s, inst, args) =>
            {
                var pa = (inst as Arena)?.winnerPlayer;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // Same crown, race variant: ArenaRace.SetWinnerRPC sets winnerPlayer at the finish line.
            Trackers.Hook(CrownWins, typeof(ArenaRace), "SetWinnerRPC", (s, inst, args) =>
            {
                var pa = (inst as ArenaRace)?.winnerPlayer;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // An enemy died. The game doesn't track who landed the blow, so credit whoever last hurt it with
            // an object (onObjectHurtPlayer) when that's the local player - a best-effort kill count.
            Trackers.Hook(EnemiesKilled, typeof(EnemyHealth), "DeathRPC", (s, inst, args) =>
            {
                var pa = (inst as EnemyHealth)?.onObjectHurtPlayer;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            });

            // You latched onto something with the grab beam: GrabStarted(PhysGrabber player).
            Trackers.Hook(ObjectsGrabbed, typeof(PhysGrabObject), "GrabStarted", (s, inst, args) =>
            {
                var grabber = args.Length > 0 ? args[0] as PhysGrabber : null;
                var pa = grabber != null ? grabber.playerAvatar : null;
                if (pa != null && IsLocal(pa)) s.Add(pa.steamID, 1);
            }, new[] { typeof(PhysGrabber) });
        }

        // ---- Mischief: friendly fire ----
        private static void WireMischief()
        {
            // Player-on-player damage routes through one RPC carrying attacker + victim view IDs. Credit the
            // dealt side to a local attacker and the taken side to a local victim - whichever this machine is.
            Trackers.Hook(FriendlyFireDealt, typeof(PunManager), "PlayerDamagingPlayerRPC", (s, inst, args) =>
            {
                if (args.Length < 5) return;
                int atkId = args[0] is int a ? a : 0;
                int vicId = args[1] is int v ? v : 0;
                int dmg = args[2] is int d ? d : 0;
                bool killed = args[4] is bool k && k;

                var attacker = SemiFunc.PlayerAvatarGetFromPhotonID(atkId);
                if (attacker != null && IsLocal(attacker))
                {
                    if (dmg > 0) s.Add(attacker.steamID, dmg);
                    if (killed) TeammatesKilled.Add(attacker.steamID, 1);
                }

                var victim = SemiFunc.PlayerAvatarGetFromPhotonID(vicId);
                if (victim != null && IsLocal(victim) && dmg > 0)
                    DamageFromTeammates.Add(victim.steamID, dmg);
            });
        }

        // ---- live expedition mirrors (synced team stats, a demo of the Mirror feature) ----
        private static void WireRunMirrors()
        {
            Trackers.Mirror(RunLevel, () => StatsManager.instance != null ? StatsManager.instance.GetRunStatLevel() : 0, SampleWhen.LevelChange);
            Trackers.Mirror(RunCurrency, () => StatsManager.instance != null ? StatsManager.instance.GetRunStatCurrency() : 0, SampleWhen.Tick);
            Trackers.Mirror(RunTotalHaul, () => StatsManager.instance != null ? StatsManager.instance.GetRunStatTotalHaul() : 0, SampleWhen.Tick);
        }

        // ---- local movement sampling (distance walked/ran) ----
        // Distance can't ride a vanilla event - it's continuous - so the driver feeds us a per-frame slice.
        // We read the LOCAL player's controller velocity (not a raw position delta, so teleports/respawns
        // don't spike), accumulate fractional meters, and commit whole meters to keep change events sane.

        private static float _distAccum, _walkAccum, _runAccum;

        internal static void SampleLocalMovement(float dt)
        {
            if (!_registered || dt <= 0f || !SemiFunc.RunIsLevel()) return;
            var pc = PlayerController.instance;
            var local = PlayerAvatar.instance;
            if (pc == null || local == null) return;
            string id = local.steamID;
            if (string.IsNullOrEmpty(id)) return;

            Vector3 v = pc.Velocity;
            v.y = 0f;
            float meters = v.magnitude * dt;
            if (meters <= 0.01f || meters >= 5f) return;   // idle, or an implausible single-frame jump

            _distAccum += meters;
            if (pc.sprinting) _runAccum += meters; else _walkAccum += meters;

            CommitMetres(DistanceTraveled, id, ref _distAccum);
            CommitMetres(DistanceSprinting, id, ref _runAccum);
            CommitMetres(DistanceWalking, id, ref _walkAccum);
        }

        private static void CommitMetres(Stat stat, string id, ref float accum)
        {
            if (accum < 1f) return;
            int whole = (int)accum;
            accum -= whole;
            stat.Add(id, whole);
        }

        // ---- helpers ----

        private static Stat Career(string key, string name, string description, string category, StatFormat format = StatFormat.Count)
            => Stats.Define(key, StatScope.PerPlayer, StatLifetime.Career).Describe(name, description, category, format);

        // The hooks run on every client; a career stat is each player's own, so only the machine whose local
        // avatar is the subject records it.
        private static bool IsLocal(PlayerAvatar p) =>
            p != null && PlayerAvatar.instance != null && p.steamID == PlayerAvatar.instance.steamID;

        // The killer's name from a PlayerDeathRPC / Hurt enemyIndex (-1 = environment / no enemy).
        private static string EnemyName(int enemyIndex)
        {
            if (enemyIndex < 0) return "Environment";
            var enemy = SemiFunc.EnemyGetFromIndex(enemyIndex);
            string? n = enemy != null && enemy.EnemyParent != null ? enemy.EnemyParent.enemyName : null;
            return string.IsNullOrEmpty(n) ? "Unknown" : n!;
        }

        // Called by StatsDriver on every scene change - counts a visit only for actual gameplay levels (not
        // the menu, truck, shop, arena, or tutorial). Keyed by the level's display name.
        internal static void OnLevelChanged()
        {
            if (!_registered || !SemiFunc.RunIsLevel()) return;
            var lvl = RunManager.instance != null ? RunManager.instance.levelCurrent : null;
            if (lvl == null) return;
            string name = !string.IsNullOrEmpty(lvl.NarrativeName) ? lvl.NarrativeName : lvl.name;
            if (!string.IsNullOrEmpty(name)) MapVisits.Add(name, 1);
        }
    }
}
