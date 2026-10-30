using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace StatsCore
{
    // Stat-tracking library for REPO mods: track a value, timestamp it, persist it, sync it. Level/Run/
    // Session stats are host-authoritative and replicate to every client (late joiners get a snapshot);
    // Career stats persist locally to disk. Start with Stats.Define, or read the built-in pack via GameStats.
    [BepInPlugin("Vippy.StatsCore", "StatsCore", BuildInfo.Version)]
    [BepInDependency("nickklmao.menulib", BepInDependency.DependencyFlags.SoftDependency)]
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
            StatsNet.Install();      // sync layer for Level/Run/Session lifetimes
            if (Chainloader.PluginInfos.ContainsKey("nickklmao.menulib")) StatsScreenEntry.Install(Config);   // the stats screen

            Logger.LogInfo($"StatsCore v{BuildInfo.Version} loaded");
        }
    }
}
