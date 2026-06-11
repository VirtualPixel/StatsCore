using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace StatsCore
{
    // Local stat-tracking library for REPO mods: track a value, timestamp it, persist it. No networking -
    // each machine keeps its own player's record; a consuming mod handles any sharing it wants. Career stats
    // persist to disk. Start with Stats.Define, or read the built-in pack via GameStats.
    [BepInPlugin("Vippy.StatsCore", "StatsCore", BuildInfo.Version)]
    public class StatsCorePlugin : BaseUnityPlugin
    {
        internal static StatsCorePlugin Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger => Instance.BaseLogger;
        private ManualLogSource BaseLogger => base.Logger;

        private void Awake()
        {
            Instance = this;
            gameObject.transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            var driver = new GameObject("StatsCore_Driver");
            driver.AddComponent<StatsDriver>();
            DontDestroyOnLoad(driver);

            GameStats.Register();   // the built-in REPO stat pack any mod can read

            Logger.LogInfo($"StatsCore v{BuildInfo.Version} loaded");
        }
    }
}
