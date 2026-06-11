using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace StatsCore
{
    // The sync layer for Level/Run/Session stats. Rides PunManager's GameObject so the
    // [PunRPC]s dispatch through its stable, shared PhotonView.
    //
    // Model: synced writes are host-authoritative. The host's Add/Set broadcasts the new
    // value; clients apply it without rebroadcast. Off-host writes to synced stats are
    // dropped with a debug log so a consuming mod author finds out in their LogOutput
    // instead of wondering where the value went. Late joiners get a full snapshot of
    // every synced stat the moment they enter the room. Career never touches the wire.
    //
    // Resets stay local on every machine: StatsDriver clears lifetimes off the same
    // scene transitions everywhere, so there is nothing to negotiate.
    internal sealed class StatsNet : MonoBehaviourPunCallbacks
    {
        private static StatsNet? _instance;
        private static readonly Harmony _harmony = new("Vippy.StatsCore.Net");

        // Snapshot chunks stay well under Photon's message ceiling.
        private const int ChunkChars = 32_000;

        internal static void Install()
        {
            var awake = AccessTools.Method(typeof(PunManager), "Awake");
            if (awake == null)
            {
                StatsCorePlugin.Logger.LogWarning("[StatsNet] PunManager.Awake not found - sync disabled, stats run local-only");
                return;
            }
            _harmony.Patch(awake, postfix: new HarmonyMethod(typeof(StatsNet), nameof(AttachPostfix)));
        }

        private static void AttachPostfix(PunManager __instance)
        {
            if (__instance.GetComponent<StatsNet>() == null)
                __instance.gameObject.AddComponent<StatsNet>();
        }

        private void Awake() => _instance = this;

        // Singleplayer and offline write freely; in a room only the master does.
        internal static bool CanWriteSynced =>
            !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;

        // Called by Stat after a host-side synced write landed locally.
        internal static void OnHostWrote(Stat stat, string steamID, int value)
        {
            if (_instance == null || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return;
            if (!PhotonNetwork.IsMasterClient) return;
            _instance.photonView.RPC(nameof(StatsCoreSetRPC), RpcTarget.Others, stat.Key, steamID, value);
        }

        [PunRPC]
        private void StatsCoreSetRPC(string key, string steamID, int value, PhotonMessageInfo info)
        {
            if (info.Sender == null || info.Sender != PhotonNetwork.MasterClient) return;
            Stats.Get(key)?.ApplyFromNetwork(steamID, value);
        }

        // ---- late-join snapshot -------------------------------------------------

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            foreach (string chunk in BuildSnapshotChunks())
                photonView.RPC(nameof(StatsCoreSnapshotRPC), newPlayer, chunk);
        }

        [PunRPC]
        private void StatsCoreSnapshotRPC(string payload, PhotonMessageInfo info)
        {
            if (info.Sender == null || info.Sender != PhotonNetwork.MasterClient) return;
            ApplySnapshot(payload);
        }

        // Wire format (framing chars are reserved at Define time):
        //   key|id=v,id=v;key|id=v;...
        private static IEnumerable<string> BuildSnapshotChunks()
        {
            var sb = new StringBuilder();
            foreach (var stat in Stats.All)
            {
                if (stat.Lifetime == StatLifetime.Career) continue;
                var entry = new StringBuilder(stat.Key).Append('|');
                bool any = false;
                foreach (var kv in stat.Raw)
                {
                    if (any) entry.Append(',');
                    entry.Append(kv.Key).Append('=').Append(kv.Value);
                    any = true;
                }
                if (!any) continue;
                if (sb.Length > 0 && sb.Length + entry.Length + 1 > ChunkChars)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                if (sb.Length > 0) sb.Append(';');
                sb.Append(entry);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static void ApplySnapshot(string payload)
        {
            int applied = 0;
            foreach (string block in payload.Split(';'))
            {
                int bar = block.IndexOf('|');
                if (bar <= 0) continue;
                var stat = Stats.Get(block.Substring(0, bar));
                if (stat == null) continue;   // consumer mod not loaded here; their loss, not an error
                foreach (string pair in block.Substring(bar + 1).Split(','))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0 || !int.TryParse(pair.Substring(eq + 1), out int value)) continue;
                    stat.ApplyFromNetwork(pair.Substring(0, eq), value);
                    applied++;
                }
            }
            StatsCorePlugin.Logger.LogDebug($"[StatsNet] snapshot applied ({applied} entries)");
        }
    }
}
