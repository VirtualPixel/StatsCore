using System;
using System.Collections;
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
    // Three flows, all through PunManager's shared PhotonView with owner/master gating:
    //
    //   1. Host-authoritative (default). The host's Add/Set broadcasts the new value;
    //      clients apply it without rebroadcast. Off-host writes are dropped with a debug
    //      log. Gate writes on Stats.IsHost.
    //
    //   2. Client-authoritative (ClientAuthoritative() stats). Each client owns its own
    //      player's slot and pushes changes to everyone, host included. Receipts validate
    //      the sender owns the slot it's writing, so no client can spoof another's value.
    //
    //   3. Host-pull (Stats.RequestFromClients). The host asks every client for its local
    //      value of a key; each client replies for its own player; the host collects them
    //      under a request id and fires a callback when all reply or a timeout elapses.
    //      Works for Career / local-only stats that never sync continuously.
    //
    // Late joiners get a full snapshot of every synced stat (host- and client-authoritative
    // alike) the moment they enter the room. Career never touches the wire except via pull.
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

        // ---- client-authoritative: each client owns its own slot ----------------

        // True if this machine may write the given slot of a client-authoritative stat: offline (own
        // everything), or the slot is this player's own steamID.
        internal static bool OwnsSlot(string steamID)
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return true;
            var me = PlayerAvatar.instance;
            return me != null && steamID == me.steamID;
        }

        // Called by Stat after an owner-side client-authoritative write landed locally.
        internal static void OnOwnerWrote(Stat stat, string steamID, int value)
        {
            if (_instance == null || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode) return;
            _instance.photonView.RPC(nameof(StatsCoreOwnedRPC), RpcTarget.Others, stat.Key, steamID, value);
        }

        [PunRPC]
        private void StatsCoreOwnedRPC(string key, string steamID, int value, PhotonMessageInfo info)
        {
            // Anti-spoof: the sender may only write its own player's slot.
            if (!SenderOwns(info, steamID)) return;
            var stat = Stats.Get(key);
            if (stat != null && stat.IsClientAuthoritative) stat.ApplyFromNetwork(steamID, value);
        }

        // The sender's own player slot, resolved from the Photon player, must match the slot it's writing.
        private static bool SenderOwns(PhotonMessageInfo info, string steamID)
        {
            if (info.Sender == null || string.IsNullOrEmpty(steamID)) return false;
            var avatar = SemiFunc.PlayerAvatarGetFromPhotonPlayer(info.Sender);
            return avatar != null && avatar.steamID == steamID;
        }

        // ---- host-pull: ask every client for its local value of a key -----------

        private sealed class PullRequest
        {
            public Dictionary<string, int> Results = new();
            public int Pending;
            public bool Completed;
            public Action<IReadOnlyDictionary<string, int>> Done = null!;
        }

        private static int _nextRequestId;
        private readonly Dictionary<int, PullRequest> _pulls = new();

        internal static void RequestFromClients(string key, Action<IReadOnlyDictionary<string, int>> onComplete, float timeoutSeconds)
        {
            if (onComplete == null) return;
            // No room, offline, or not the host: there's no one to ask. Answer with just the local value.
            if (_instance == null || !PhotonNetwork.InRoom || PhotonNetwork.OfflineMode || !PhotonNetwork.IsMasterClient)
            {
                onComplete(LocalSlot(key));
                return;
            }
            _instance.BeginPull(key, onComplete, timeoutSeconds);
        }

        private void BeginPull(string key, Action<IReadOnlyDictionary<string, int>> done, float timeoutSeconds)
        {
            int id = ++_nextRequestId;
            var req = new PullRequest
            {
                Done = done,
                Pending = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount - 1 : 0,
            };
            foreach (var kv in LocalSlot(key)) req.Results[kv.Key] = kv.Value;   // seed the host's own value
            _pulls[id] = req;

            if (req.Pending <= 0) { CompletePull(id); return; }
            photonView.RPC(nameof(StatsCorePullRequestRPC), RpcTarget.Others, key, id);
            StartCoroutine(PullTimeout(id, timeoutSeconds));
        }

        [PunRPC]
        private void StatsCorePullRequestRPC(string key, int requestId, PhotonMessageInfo info)
        {
            if (info.Sender == null || info.Sender != PhotonNetwork.MasterClient) return;
            var me = PlayerAvatar.instance;
            string id = me != null ? me.steamID : "";
            int value = 0;
            var stat = Stats.Get(key);
            if (stat != null && !string.IsNullOrEmpty(id)) value = stat.Get(id);
            photonView.RPC(nameof(StatsCorePullResponseRPC), info.Sender, key, requestId, id, value);
        }

        [PunRPC]
        private void StatsCorePullResponseRPC(string key, int requestId, string steamID, int value, PhotonMessageInfo info)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (!SenderOwns(info, steamID)) return;   // a client may only answer for its own slot
            if (!_pulls.TryGetValue(requestId, out var req) || req.Completed) return;
            req.Results[steamID] = value;
            req.Pending--;
            if (req.Pending <= 0) CompletePull(requestId);
        }

        private IEnumerator PullTimeout(int requestId, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            CompletePull(requestId);   // no-op if it already completed
        }

        private void CompletePull(int requestId)
        {
            if (!_pulls.TryGetValue(requestId, out var req) || req.Completed) return;
            req.Completed = true;
            _pulls.Remove(requestId);
            try { req.Done(req.Results); }
            catch (Exception e) { StatsCorePlugin.Logger.LogWarning($"[StatsNet] pull callback for '{requestId}' threw: {e.Message}"); }
        }

        // This machine's own player slot for a key (empty if the stat or local player isn't there yet).
        private static Dictionary<string, int> LocalSlot(string key)
        {
            var result = new Dictionary<string, int>();
            var stat = Stats.Get(key);
            var me = PlayerAvatar.instance;
            if (stat != null && me != null && !string.IsNullOrEmpty(me.steamID))
                result[me.steamID] = stat.Get(me.steamID);
            return result;
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
