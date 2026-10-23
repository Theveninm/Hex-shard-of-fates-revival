using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics.Transactions;
using Game.Shared.Network;
using Newtonsoft.Json.Linq;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// A game against another player, relayed by the master server (see docs/MULTIPLAYER.md).
    ///
    /// The host (the challenger) plays a normal solo game whose opponent seat is not the AI: the events meant for that
    /// seat are sent to the guest (Server.Flush), and the guest's actions come back as transactions submitted to the
    /// host's session. The guest runs no game session at all: its game client is fed the host's session state and
    /// events as if they came from the original HEX servers.
    ///
    /// Both games call their own player ServicePlayer.4097 (the login's id). On the host the guest's seat is
    /// ServicePlayer.4098, so the guest swaps the two ids in everything it receives and sends.
    /// </summary>
    public static class OnlineMatch
    {
        public enum Role { None, Host, Guest }

        // Relayed message kinds (first byte of the relayed data).
        private const byte MsgDeck = 1, MsgStart = 2, MsgEvents = 3, MsgTransaction = 4, MsgReject = 5;

        public static readonly UID LocalId = new UID(UID.Type.ServicePlayer, 0x1001UL);
        public static readonly UID RemoteId = new UID(UID.Type.ServicePlayer, 0x1002UL);

        public static Role Current { get; private set; }
        public static string OpponentName { get; private set; }
        public static bool IsHost { get { return Current == Role.Host; } }
        public static bool IsGuest { get { return Current == Role.Guest; } }
        public static bool IsRemote(UID id) { return Current == Role.Host && id == RemoteId; }

        private static NetClient s_Net;
        private static UID s_MyDeck;
        private static bool s_Started, s_SeenBattle;
        private static float s_StartedAt;

        // Guest: the host's session state and events wait until the game client is ready for them.
        private static StandaloneGameClient s_GuestClient;
        private static SessionState s_GuestState;
        private static bool s_GuestJoined;
        private static readonly Queue<NetworkPacketSessionEventArgs> s_GuestEvents = new Queue<NetworkPacketSessionEventArgs>();

        /// <summary>The game's format (see OnlineFormats): the host checks the guest's deck against it.</summary>
        public static string Format { get; private set; }

        /// <summary>Host: the guest's deck (Decks.SetRemote), given to the opponent seat (Patches.AuthoritativeSession_AddPlayer).</summary>
        public static UID RemoteDeck { get; private set; }

        /// <summary>
        /// Online, each side's name in the battle is the player's (the game showed the champion's name twice: its
        /// session names the players after their champions). Postfix of UIBattle.OnChampionCardPlayed.
        /// </summary>
        public static void ShowPlayerNames(UIBattle ui, ChampionCardPlayedSessionEventArgs args)
        {
            if (Current == Role.None || ui == null || args == null) return;
            try
            {
                bool local = ui.IsLocalPlayer(args.PlayerId);
                string name = local ? OnlineProfile.Current.Name : OpponentName;
                if (string.IsNullOrEmpty(name)) return;
                BattleScenePlayerObjects o = ui.GetPlayerSceneObjects(args.PlayerId);
                if (o == null || o.PlayerHUD == null || o.PlayerHUD.Context == null) return;
                string champion = o.PlayerHUD.Context.NameText;
                o.PlayerHUD.Context.NameText = name;
                o.PlayerHUD.Context.KeepText = champion != name ? champion : string.Empty;
                if (!local)
                {
                    // "<name>'s turn" and the like.
                    o.PlayerHUD.m_OpponentName = name;
                    HarmonyLib.Traverse.Create(ui).Field("m_OpponentName").SetValue(name);
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] names: " + e.Message); }
        }

        /// <summary>
        /// The lobby announced the match: the guest sends its deck (cards, champion, a campaign deck's PvE champion) and
        /// waits for the host's session; the host checks the deck against the format, then starts.
        /// </summary>
        public static void Begin(NetClient net, bool host, string opponent, UID myDeck, string format)
        {
            End();
            s_Net = net;
            Current = host ? Role.Host : Role.Guest;
            OpponentName = opponent;
            s_MyDeck = myDeck;
            Format = string.IsNullOrEmpty(format) ? "free" : format;
            RemoteDeck = UID.Invalid;
            s_StartedAt = Time.realtimeSinceStartup;
            Debug.Log("[HexRevival] [Online] match against " + opponent + (host ? " (host)" : " (guest)") + ", deck " + myDeck);
            if (!host) SendRelay(MsgDeck, Encoding.UTF8.GetBytes(Decks.Export(myDeck).ToString(Newtonsoft.Json.Formatting.None)));
        }

        public static void End()
        {
            if (Current != Role.None) Debug.Log("[HexRevival] [Online] match over");
            Current = Role.None;
            s_Started = s_SeenBattle = false;
            s_GuestClient = null;
            s_GuestState = null;
            s_GuestJoined = false;
            s_GuestEvents.Clear();
            s_AckDue = 0;
        }

        /// <summary>Game data from the opponent (main thread).</summary>
        public static void OnRelay(byte[] data)
        {
            if (Current == Role.None || data.Length == 0) return;
            byte[] body = new byte[data.Length - 1];
            Buffer.BlockCopy(data, 1, body, 0, body.Length);
            try
            {
                switch (data[0])
                {
                    case MsgDeck: if (IsHost && !s_Started) OnGuestDeck(JObject.Parse(Encoding.UTF8.GetString(body))); break;
                    case MsgReject: if (IsGuest) Refused(Loc.Get("online_deck_refused", Encoding.UTF8.GetString(body))); break;
                    case MsgStart: if (IsGuest) OnHostStarted(body); break;
                    case MsgEvents: if (IsGuest) s_GuestEvents.Enqueue(DecodeEvents(body)); break;
                    case MsgTransaction: if (IsHost) OnGuestTransaction(body); break;
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Online] message " + data[0] + ": " + e); }
        }

        /// <summary>Every frame: the guest's waiting events, and the end of the match when the battle is left.</summary>
        public static void Update()
        {
            if (Current == Role.None) return;
            GameClient gc = Singleton<Main>.Instance != null ? Singleton<Main>.Instance.m_GameClient : null;
            bool inBattle = gc != null && gc.StateManager.CurrentGameState == Reckoning.Game.EGameState.Battle;
            if (inBattle) s_SeenBattle = true;
            else if (s_SeenBattle) { OnlineLobby.LeaveMatch(); return; }

            if (IsHost) SendAckIfDue();
            if (IsGuest && s_GuestJoined && gc != null && gc.Session != null)
            {
                // The game handles one packet per frame (SessionClient queue); several may be queued at once.
                // Each one counts as the answer to the last transaction (Hooks.Session_PumpSync).
                while (s_GuestEvents.Count > 0) gc.Session.AddSyncEventToQueue(s_GuestEvents.Dequeue());
            }
        }

        // ------------------------------------------------------------------
        // Starting the battle
        // ------------------------------------------------------------------

        /// <summary>Both sides enter the battle with their own deck; the host's opponent seat gets the guest's deck.</summary>
        private static void StartBattle()
        {
            s_Started = true;
            Patches.StartMatch(s_MyDeck, Patches.NeutralEncounterId, null, OpponentName);
        }

        /// <summary>Host: the guest's deck must be valid for the game's format (its game checked it too, but it may lie).</summary>
        private static void OnGuestDeck(JObject deck)
        {
            RemoteDeck = Decks.SetRemote(deck);
            List<string> errors = new List<string>();
            if (Format != "free" && !OnlineFormats.Validate(Format, RemoteDeck, errors))
            {
                string why = OnlineFormats.Name(Format) + (errors.Count > 0 ? " (" + errors[0] + ")" : "");
                SendRelay(MsgReject, Encoding.UTF8.GetBytes(why));
                Refused(Loc.Get("online_opponent_deck_refused", why));
                return;
            }
            StartBattle();
        }

        /// <summary>The game does not start: back to the lobby with the reason.</summary>
        private static void Refused(string why)
        {
            Debug.Log("[HexRevival] [Online] " + why);
            Notices.Show(why);
            OnlineLobby.LeaveMatch();
        }

        /// <summary>Host: the session exists (Patches.SendStartEncounter); the guest gets its state.</summary>
        public static void OnHostSessionCreated(SessionState state)
        {
            SendRelay(MsgStart, EncData.Encode(state));
        }

        /// <summary>Guest: the game asked for the encounter; the answer is the host's state, possibly not there yet.</summary>
        public static void OnGuestStartRequested(StandaloneGameClient client)
        {
            s_GuestClient = client;
            TryStartGuest();
        }

        private static void OnHostStarted(byte[] body)
        {
            byte[] swapped = SwapInEncoded(body);
            s_GuestState = (SessionState)EncData.Decode(swapped);
            // The host accepted the deck and started: the guest enters the battle (its game then asks for the session).
            if (!s_Started) StartBattle();
            TryStartGuest();
        }

        private static void TryStartGuest()
        {
            if (s_GuestClient == null || s_GuestState == null) return;
            Debug.Log("[HexRevival] [Online] host session received, joining");
            Invoke(s_GuestClient, "OnStartEncounter", new Game.Client.Network.LoadBalancer.StartEncounterResponse { Success = true, SessionState = s_GuestState });
        }

        /// <summary>
        /// Guest, replaces StandaloneGameClient.SendJoinSession: joins the host's game like a network client (own player,
        /// then the opponent) without any local session.
        /// </summary>
        public static void GuestJoinSession(StandaloneGameClient client)
        {
            client.m_LocalPlayerMap[client.Player.m_PlayerId] = client.Player;
            Invoke(client, "OnJoinSession", new Game.Client.Network.LoadBalancer.JoinSessionResponse { Success = true, SessionState = s_GuestState });
            PlayerState opponent = new PlayerState();
            opponent.PlayerId = RemoteId;
            Invoke(client, "OnPlayerAdded", new Game.Shared.Network.GameSession.PlayerAddedEventArgs { PlayerState = opponent });
            s_GuestJoined = true;
        }

        // ------------------------------------------------------------------
        // During the battle
        // ------------------------------------------------------------------

        /// <summary>Host (Server.Flush): the events of the guest's seat.</summary>
        public static void SendEvents(NetworkPacketSessionEventArgs packet)
        {
            s_AckDue = 0;
            SendRelay(MsgEvents, NetCodec.EncodeEvents(packet));
        }

        /// <summary>
        /// The game client submits nothing more until a packet answers its last transaction (SessionClient,
        /// m_HasPreviousTransactionBeenRespondedByServer). The original server always answered; the host only sends a
        /// packet when there are events for the guest, so a transaction without visible effect gets an empty packet.
        /// </summary>
        private static int s_AckDue;
        private const int AckFrames = 10;

        private static void SendAckIfDue()
        {
            if (s_AckDue == 0 || Time.frameCount < s_AckDue) return;
            BulkSessionEventSessionEventArgs bulk = new BulkSessionEventSessionEventArgs { PlayerId = UID.Invalid, EventList = new List<SessionEventArgs>() };
            StandaloneSession ss = Server.CurrentSession;
            if (ss != null) bulk.SessionId = ss.m_SessionId;
            NetworkPacketSessionEventArgs packet = NetworkPacketSessionEventArgs.Create(bulk);
            packet.SessionId = bulk.SessionId;
            SendEvents(packet);
        }

        private static NetworkPacketSessionEventArgs DecodeEvents(byte[] body)
        {
            NetworkPacketSessionEventArgs packet = NetCodec.DecodeEvents(body);
            // Each event is written with BinaryWriter: the ids are raw 8-byte values.
            for (int i = 0; i < packet.EventData.Count; i++) SwapRaw(packet.EventData[i]);
            if (packet.PlayerId == LocalId) packet.PlayerId = RemoteId; else if (packet.PlayerId == RemoteId) packet.PlayerId = LocalId;
            if (Server.TraceEvents)
            {
                List<string> names = new List<string>();
                foreach (int id in packet.EventIds) names.Add(id.ToString());
                Debug.Log("[HexRevival] [Online] << " + packet.EventIds.Count + " event(s): " + string.Join(",", names.ToArray()));
            }
            return packet;
        }

        /// <summary>Guest (StandaloneGameClient.SendSessionTransaction): the player's action goes to the host.</summary>
        public static void SendTransaction(Transaction tx)
        {
            if (Server.TraceEvents) Debug.Log("[HexRevival] [Online] >> " + tx.GetType().Name);
            SendRelay(MsgTransaction, SwapInEncoded(NetCodec.EncodeTransaction(tx)));
        }

        private static void OnGuestTransaction(byte[] body)
        {
            Transaction tx = NetCodec.DecodeTransaction(body);
            if (tx.m_PlayerId != RemoteId)
            {
                Debug.LogWarning("[HexRevival] [Online] transaction for " + tx.m_PlayerId + " refused");
                return;
            }
            StandaloneSession ss = Server.CurrentSession;
            if (ss == null) return;
            ss.SubmitTransaction(tx);
            s_AckDue = Time.frameCount + AckFrames;
        }

        private static void SendRelay(byte kind, byte[] body)
        {
            if (s_Net == null) return;
            byte[] data = new byte[body.Length + 1];
            data[0] = kind;
            Buffer.BlockCopy(body, 0, data, 1, body.Length);
            s_Net.SendRelay(data);
        }

        // ------------------------------------------------------------------
        // Id swap (guest side)
        // ------------------------------------------------------------------

        /// <summary>Swaps the two player ids, written as raw little-endian 8-byte values, in place.</summary>
        private static void SwapRaw(byte[] data)
        {
            if (data == null) return;
            byte[] a = BitConverter.GetBytes(LocalId.m_UID64), b = BitConverter.GetBytes(RemoteId.m_UID64);
            for (int i = 0; i + 8 <= data.Length; i++)
            {
                byte[] to = Matches(data, i, a) ? b : Matches(data, i, b) ? a : null;
                if (to == null) continue;
                Buffer.BlockCopy(to, 0, data, i, 8);
                i += 7;
            }
        }

        private static bool Matches(byte[] data, int at, byte[] pattern)
        {
            for (int k = 0; k < pattern.Length; k++) if (data[at + k] != pattern[k]) return false;
            return true;
        }

        /// <summary>
        /// Swaps the two player ids in an EncData object (transaction, session state): decoded to fresh objects (only
        /// the serialized members, nothing shared with the running game), ids swapped field by field, encoded again.
        /// </summary>
        private static byte[] SwapInEncoded(byte[] encoded)
        {
            object o = EncData.Decode(encoded);
            SwapFields(o, new Dictionary<object, bool>(new ByReference()));
            return EncData.Encode(o);
        }

        private static object SwapValue(object v, Dictionary<object, bool> seen)
        {
            if (v == null) return null;
            Type t = v.GetType();
            if (t == typeof(UID))
            {
                UID id = (UID)v;
                return id == LocalId ? RemoteId : id == RemoteId ? LocalId : id;
            }
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid)) return v;
            if (t.IsValueType) { SwapFields(v, seen); return v; }   // boxed copy, written back by the caller
            if (seen.ContainsKey(v)) return v;
            seen[v] = true;
            SwapFields(v, seen);
            return v;
        }

        private static void SwapFields(object o, Dictionary<object, bool> seen)
        {
            if (o == null) return;
            System.Collections.IList list = o as System.Collections.IList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) list[i] = SwapValue(list[i], seen);
                return;
            }
            System.Collections.IDictionary dict = o as System.Collections.IDictionary;
            if (dict != null)
            {
                List<object> keys = new List<object>();
                foreach (object k in dict.Keys) keys.Add(k);
                List<KeyValuePair<object, object>> entries = new List<KeyValuePair<object, object>>();
                foreach (object k in keys) entries.Add(new KeyValuePair<object, object>(SwapValue(k, seen), SwapValue(dict[k], seen)));
                dict.Clear();
                foreach (KeyValuePair<object, object> e in entries) dict[e.Key] = e.Value;
                return;
            }
            for (Type t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                    object v = f.GetValue(o);
                    object nv = SwapValue(v, seen);
                    if (f.FieldType.IsValueType || !ReferenceEquals(v, nv)) f.SetValue(o, nv);
                }
        }

        private class ByReference : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }

        // ------------------------------------------------------------------
        // Deck sent by the guest (card ids only)
        // ------------------------------------------------------------------

        private static void Invoke(object target, string method, object arg)
        {
            MethodInfo m = null;
            for (Type t = target.GetType(); t != null && m == null; t = t.BaseType)
                m = t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (m == null) throw new MissingMethodException(method);
            m.Invoke(target, new[] { arg });
        }
    }
}
