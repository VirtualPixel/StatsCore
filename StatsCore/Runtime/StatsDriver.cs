using UnityEngine;

namespace StatsCore
{
    // Drives lifetime resets off REPO's scene transitions - the fiddly part every scorekeeping mod gets
    // wrong. Local: clears the relevant lifetimes on this machine as scenes change, samples mirrors, counts
    // map visits, and flushes dirty career stats to disk on a gentle cadence. DontDestroyOnLoad; one instance.
    internal class StatsDriver : MonoBehaviour
    {
        private object? _lastLevel;
        private bool _wasLobbyMenu;
        private float _saveTimer;
        private float _tickTimer;

        private void Update()
        {
            FlushTick();
            TickSamples();
            GameStats.SampleLocalMovement(Time.deltaTime);   // distance walked/ran (gated on RunIsLevel inside)

            var rm = RunManager.instance;
            if (rm == null) return;

            // Level changed (any scene transition): clear Level-scoped stats, then mirror level-scoped values.
            object cur = rm.levelCurrent;
            if (!ReferenceEquals(cur, _lastLevel))
            {
                _lastLevel = cur;
                Stats.ResetLifetime(StatLifetime.Level);

                // Entering the REAL lobby menu (pre-game / new run) clears Run-scoped stats too. NOT the
                // truck "lobby" between levels - that keeps Run stats alive across the whole expedition.
                bool lobbyMenu = SemiFunc.RunIsLobbyMenu();
                if (lobbyMenu && !_wasLobbyMenu)
                {
                    Stats.ResetLifetime(StatLifetime.Run);
                    Trackers.Sample(SampleWhen.RunReset);
                }
                _wasLobbyMenu = lobbyMenu;

                Trackers.Sample(SampleWhen.LevelChange);
                GameStats.OnLevelChanged();   // count a map visit when it's an actual gameplay level
            }
        }

        // Slow poll - only runs if some mirror actually asked for Tick cadence.
        private void TickSamples()
        {
            if (!Trackers.HasTickMirrors()) return;
            _tickTimer += Time.unscaledDeltaTime;
            if (_tickTimer < 3f) return;
            _tickTimer = 0f;
            Trackers.Sample(SampleWhen.Tick);
        }

        // Debounced career save - career stats can change often, so coalesce writes.
        private void FlushTick()
        {
            if (!Persistence.Dirty) return;
            _saveTimer += Time.unscaledDeltaTime;
            if (_saveTimer < 5f) return;
            _saveTimer = 0f;
            Persistence.Save();
        }

        private void OnApplicationQuit() => Persistence.Save();
    }
}
