namespace StatsCore
{
    // The built-in REPO stat pack - a permanent, per-player record any mod can read by key without writing
    // its own trackers. Define once at boot; the values fill themselves from vanilla events and persist to
    // disk across sessions. Want to reward a good player? Read GameStats.CrownWins.Get(steamID) and go.
    //
    //   int crowns = GameStats.CrownWins.Get(steamID);          // typed handle
    //   int deaths = Stats.Get(GameStats.K_Deaths)?.Get(id) ?? 0;  // or by stable key, no reference needed
    //
    // These are CAREER stats: persisted, and LOCAL to each machine - every player's file holds their own
    // record (the hooks fire on all clients, but each only writes for its own local avatar). That keeps a
    // clean per-player history without one machine guessing at another's lifetime totals.
    public static class GameStats
    {
        // Stable, public keys - the contract other mods read against. Don't rename these.
        public const string K_Deaths = "statscore.deaths";
        public const string K_DeathsByEnemy = "statscore.deathsByEnemy";   // keyed by ENEMY NAME, not steamID
        public const string K_Knockdowns = "statscore.knockdowns";
        public const string K_Revives = "statscore.revives";
        public const string K_SpewerFaces = "statscore.spewerFaces";
        public const string K_CrownWins = "statscore.crownWins";
        public const string K_MapVisits = "statscore.mapVisits";           // keyed by LEVEL NAME, not steamID

        public static Stat Deaths { get; private set; } = null!;
        public static Stat DeathsByEnemy { get; private set; } = null!;
        public static Stat Knockdowns { get; private set; } = null!;
        public static Stat Revives { get; private set; } = null!;
        public static Stat SpewerFaces { get; private set; } = null!;
        public static Stat CrownWins { get; private set; } = null!;
        public static Stat MapVisits { get; private set; } = null!;

        private static bool _registered;

        // Called once from StatsCorePlugin.Awake. Defines the pack and wires each stat to the vanilla event
        // that feeds it. A target the current game build no longer has just disables that one tracker
        // (logged by Trackers) - the rest keep working.
        internal static void Register()
        {
            if (_registered) return;
            _registered = true;

            Deaths = Stats.Define(K_Deaths, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Deaths", "Times you've gone down for good.", "Misfortune");
            DeathsByEnemy = Stats.Define(K_DeathsByEnemy, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Deaths by Enemy", "Your deaths, broken down by what killed you (keyed by enemy name).", "Misfortune");
            Knockdowns = Stats.Define(K_Knockdowns, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Knockdowns", "Times something put you on the floor.", "Misfortune");
            Revives = Stats.Define(K_Revives, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Revives", "Times you were brought back.", "Misfortune");
            SpewerFaces = Stats.Define(K_SpewerFaces, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Spewer Faces", "Times a spewer latched onto your face.", "Misfortune");
            CrownWins = Stats.Define(K_CrownWins, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Crown Wins", "King-of-the-losers victories.", "Glory");
            MapVisits = Stats.Define(K_MapVisits, StatScope.PerPlayer, StatLifetime.Career)
                .Describe("Map Visits", "How many times you've dropped into each level (keyed by level name).", "Expeditions");

            // You died - total, plus a per-enemy breakdown (PlayerDeathRPC(int enemyIndex)).
            Trackers.Hook(Deaths, typeof(PlayerAvatar), "PlayerDeathRPC", (s, inst, args) =>
            {
                if (!(inst is PlayerAvatar pa) || !IsLocal(pa)) return;
                s.Add(pa.steamID, 1);
                int idx = (args.Length > 0 && args[0] is int i) ? i : -1;
                DeathsByEnemy.Add(EnemyName(idx), 1);
            });

            // Brought back.
            Trackers.Hook(Revives, typeof(PlayerAvatar), "ReviveRPC", (s, inst, args) =>
            {
                if (inst is PlayerAvatar pa && IsLocal(pa)) s.Add(pa.steamID, 1);
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
        }

        // The hooks run on every client; a career stat is each player's own, so only the machine whose local
        // avatar is the subject records it.
        private static bool IsLocal(PlayerAvatar p) =>
            p != null && PlayerAvatar.instance != null && p.steamID == PlayerAvatar.instance.steamID;

        // The killer's name from a PlayerDeathRPC enemyIndex (-1 = environment / no enemy).
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
