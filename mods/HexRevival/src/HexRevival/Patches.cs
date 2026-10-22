using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Client.GameState;
using Game.Client.Network.LoadBalancer;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Replacement methods injected by the patcher into the game DLLs
    /// (method body redirection through Mono.Cecil, without Harmony).
    /// </summary>
    public static class Patches
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>Local PvE encounter "Lair of the Beast" (valid player deck + AI deck).</summary>
        private const string OfflineEncounterGuid = "6aca3608-0ef6-478b-a089-d76e94b13650";

        // ------------------------------------------------------------------
        // Auth / profile
        // ------------------------------------------------------------------

        /// <summary>Replaces Steamy.IsRunningSteam(): behave like a non-Steam build.</summary>
        public static bool Steamy_IsRunningSteam()
        {
            return false;
        }

        /// <summary>
        /// Replaces PlayerProfile.Login(...): bypasses network authentication and
        /// initializes a minimal local profile (identity + champion).
        /// </summary>
        public static bool PlayerProfile_Login(
            PlayerProfile self,
            string username,
            string password,
            string totp,
            ref string error,
            ref Dictionary<string, object> attrs)
        {
            error = null;
            attrs = new Dictionary<string, object>();
            try
            {
                // The account is the save chosen in the account menu (see AccountMenu).
                if (Saves.Current == null) Saves.Select(Saves.FindOrCreate(string.IsNullOrEmpty(username) ? "Offline" : username));
                username = Saves.Current.Name;
                SeedLocalProfile(self, username);
                try { LocalProfile.Load(self, new UID(UID.Type.ServicePlayer, 0x1001UL), string.IsNullOrEmpty(username) ? "Offline" : username); }
                catch (Exception e) { Debug.LogError("[HexRevival] LocalProfile.Load failed: " + e); }
                RaiseLoginResponse(self, true, null);
                Debug.Log("[HexRevival] PlayerProfile.Login -> local profile initialized for '" + username + "'");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] PlayerProfile_Login failed: " + e);
                error = "OFFLINE_ERROR";
                RaiseLoginResponse(self, false, "OFFLINE_ERROR");
                return false;
            }
        }

        private static void SeedLocalProfile(PlayerProfile self, string username)
        {
            string name = string.IsNullOrEmpty(username) ? "Offline" : username;
            UID reckoningId = new UID(UID.Type.ServicePlayer, 0x1001UL);

            SetField(self, "_m_RecID", reckoningId);
            SetField(self, "_m_ProfileId", 1UL);
            SetField(self, "m_LoginName", name);
            SetField(self, "m_UserName", name);
            self.LoginStreamDone = true;

            // Minimal local champion (lets UIBattle.StartEncounter resolve GetCurrentChampion()).
            List<PlayerChampion> champions = (List<PlayerChampion>)GetField(self, "m_Champions");
            champions.Clear();
            ulong first = 0;
            foreach (champion_bits c in Champions.Load(reckoningId, name))
            {
                champions.Add(new PlayerChampion(c));
                if (first == 0) first = c.Id;
            }
            SetField(self, "m_CurrentChampionId", first);
            DeckTemplates.LoadInto(self);
            Mercenaries.LoadInto(self);
        }

        private static PlayerChampion BuildChampion(UID reckoningId, string name)
        {
            champion_bits bits = new champion_bits();
            bits.Id = 1UL;
            bits.Name = name;
            bits.Level = 1;
            bits.Race = ERace.Human;
            bits.ChampionClass = EChampionClass.Warrior;
            bits.Gender = EGender.Male;
            bits.OwnerChampionId = 0UL;
            bits.ReckID = reckoningId;
            return new PlayerChampion(bits);
        }

        private static void RaiseLoginResponse(PlayerProfile self, bool success, string error)
        {
            FieldInfo field = typeof(PlayerProfile).GetField("OnProfileLoginResponse", PrivateInstance);
            if (ReferenceEquals(field, null))
            {
                Debug.LogWarning("[HexRevival] OnProfileLoginResponse field not found.");
                return;
            }
            Delegate handler = field.GetValue(self) as Delegate;
            if (ReferenceEquals(handler, null))
            {
                Debug.LogWarning("[HexRevival] No subscriber to OnProfileLoginResponse.");
                return;
            }
            foreach (Delegate d in handler.GetInvocationList())
            {
                try
                {
                    d.DynamicInvoke(new ProfileBooleanEventArgs(success, error));
                }
                catch (Exception e)
                {
                    Debug.LogError("[HexRevival] Error in a login handler: " + e);
                }
            }
        }

        // ------------------------------------------------------------------
        // Local PvE match
        // ------------------------------------------------------------------

        /// <summary>
        /// Replaces StandaloneGameClient.SendStartEncounter (stub "encounters need to be
        /// launched by a server") with a real local encounter against the AI.
        /// </summary>
        /// <summary>
        /// Replaces StandaloneGameClient.SendStartSession for games against the AI outside encounters
        /// (Practice button of the deck editor). The client's "server" cannot load decks
        /// outside encounters (LoadDeckForPlayers is empty): a local encounter is started instead;
        /// the player keeps their deck there (player.m_DeckID, provided by the local profile).
        /// </summary>
        public static bool StandaloneGameClient_SendStartSession(StandaloneGameClient self, string sessionName, int aiPlayerCount, int flags)
        {
            if (aiPlayerCount != 1)
                return true;
            s_PendingAIDeck = null;
            SessionStateEncounterData data = new SessionStateEncounterData();
            data.SceneTemplateId = new ResourceId(new Guid(PracticeEncounterGuid));
            data.SessionFlags = ESessionFlags.IsEncounter | ESessionFlags.IsPvE;
            Debug.Log("[HexRevival] Practice -> local encounter " + PracticeEncounterGuid + " (original flags " + flags + ")");
            StandaloneGameClient_SendStartEncounter(self, sessionName, data);
            return false;
        }

        /// <summary>
        /// Postfix of Session.AddPlayer on the local authoritative session: StandaloneGameClient
        /// adds fresh RemotePlayers to it (empty PlayerState), without the deck chosen by the player.
        /// We copy m_DeckID (and the deck template) from the matching LocalPlayer, so that
        /// the engine asks for that deck (GetDeckDetails) instead of taking the encounter's.
        /// </summary>
        public static void AuthoritativeSession_AddPlayer(Session self, Player player)
        {
            try
            {
                if (!(self is StandaloneSession) || player == null) return;
                if (player.IsAI() || Server.IsAIId(player.m_PlayerId))
                {
                    if (s_PendingAIDeck != null)
                    {
                        FieldInfo f = typeof(AuthoritativeSessionBase).GetField("m_OverrideDecks", BindingFlags.Instance | BindingFlags.NonPublic);
                        Dictionary<UID, DeckTemplate> over = f.GetValue(self) as Dictionary<UID, DeckTemplate>;
                        over[player.m_PlayerId] = s_PendingAIDeck;
                        // A deck template carries no AI personality (a player deck does): the Siege defender's is set here.
                        player.Personality = s_PendingAIPersonality;
                        Debug.Log("[HexRevival] AI " + player.m_PlayerId + " : deck " + s_PendingAIDeck.m_DeckName + (s_PendingAIPersonality != EDeckPersonality.Default ? ", personality " + s_PendingAIPersonality : ""));
                    }
                    return;
                }
                StandaloneGameClient gc = Singleton<Main>.Instance.m_GameClient as StandaloneGameClient;
                if (gc == null || gc.Player == null || gc.Player.m_PlayerId != player.m_PlayerId) return;
                player.m_DeckID = gc.Player.m_DeckID;
                player.m_DeckTemplateID = gc.Player.m_DeckTemplateID;
                Debug.Log("[HexRevival] player " + player.m_PlayerId + ": deck " + player.m_DeckID);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] AddPlayer : " + e.Message); }
        }

        /// <summary>Encounter used as the "Practice" opponent (the encounter's AI deck).</summary>
        private const string PracticeEncounterGuid = OfflineEncounterGuid;

        public static void StandaloneGameClient_SendStartEncounter(
            StandaloneGameClient self, string sessionName, SessionStateEncounterData encounterData)
        {
            try
            {
                Debug.Log("[HexRevival] starting local encounter: " + encounterData.SceneTemplateId);
                Server.ResetCache();
                FillNullLists(encounterData);
                // Campaign effects of this battle (dungeon boons, the robot, blessings): run like the scene's own.
                encounterData.AbilitiesToRun.AddRange(Campaign.TakeBattleEffects());
                // A Siege or Battlegrounds battle: its own session flags (the game's rules for it, where Quit leads).
                if (PendingEncounterSetup != null)
                {
                    PendingEncounterSetup(encounterData);
                    PendingEncounterSetup = null;
                }

                StandaloneSession sessionServer = new StandaloneSession(self);
                sessionServer.m_SessionId = new UID(UID.Type.AuthoritativeSession, 501UL);
                sessionServer.m_MinimumPlayerCount = 2;
                sessionServer.m_MaximumPlayerCount = 2;
                sessionServer.m_SessionName = sessionName;
                sessionServer.m_EncounterData = encounterData;
                SetField(self, "m_SessionServer", sessionServer);

                const int aiCount = 1;
                List<ClientSessionBase> aiSessions = new List<ClientSessionBase>();
                for (int i = 0; i < aiCount; i++)
                {
                    PlayerState playerState = new PlayerState();
                    bool tutorial = encounterData.SessionFlags.HasAnyFlags(ESessionFlags.IsTutorial);
                    playerState.PlayerId = new UID(tutorial ? UID.Type.ServiceAI : UID.Type.AIPlayer, (ulong)(1000 + i));
                    AIPlayer aiPlayer = new AIPlayer(playerState);

                    SessionClient sessionClient = new SessionClient(BuildSessionState(sessionServer, encounterData), self);
                    sessionClient.m_SessionName = "AILocalSession_" + aiPlayer.m_PlayerId;
                    // Same instance id as the authoritative session: otherwise the
                    // RaiseSessionEventFromNetwork filter (by instance id) rejects every event.
                    sessionClient.m_SessionId = sessionServer.m_SessionId;
                    sessionClient.AddPlayer(aiPlayer);

                    // Attaches the AI tactical component (never initialized anywhere else in the client).
                    aiPlayer.InitializeAITactical(Opponents.TakePersonality());   // the AI skill of the opponent (see Opponents)

                    self.m_LocalPlayerMap[aiPlayer.m_PlayerId] = aiPlayer;
                    aiSessions.Add(sessionClient);
                }
                SetField(self, "m_AISessions", aiSessions);

                StartEncounterResponse response = new StartEncounterResponse
                {
                    Success = true,
                    SessionState = BuildSessionState(sessionServer, encounterData)
                };
                InvokeNonPublic(self, "OnStartEncounter", response);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] SendStartEncounter local failed: " + e);
            }
        }

        /// <summary>
        /// StandaloneSession.GetSessionState() does not include EncounterData, although the client
        /// (ClientSessionBase.SetSessionState -> m_EncounterData) uses it everywhere
        /// (e.g. GoHUDPlayer.SetActiveEquipmentForPlayer -> NRE that breaks the whole champion HUD).
        /// </summary>
        /// <summary>
        /// The server filled the SessionStateEncounterData lists; on the client they stay
        /// null (e.g. InitEncounter calls AbilitiesToRun.AddRange for encounters with "mods").
        /// </summary>
        private static void FillNullLists(object data)
        {
            foreach (FieldInfo f in data.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (f.GetValue(data) != null || !f.FieldType.IsGenericType || f.FieldType.GetGenericTypeDefinition() != typeof(List<>))
                    continue;
                f.SetValue(data, Activator.CreateInstance(f.FieldType));
            }
        }

        private static SessionState BuildSessionState(StandaloneSession server, SessionStateEncounterData encounterData)
        {
            SessionState state = server.GetSessionState();
            state.EncounterData = encounterData;
            return state;
        }

        /// <summary>
        /// Replaces UILandingPageContext.OnClickChallengeAI: instead of matchmaking
        /// (dead server), starts a local PvE encounter against the AI.
        /// </summary>
        public static void UILandingPage_OnClickChallengeAI(UILandingPageContext self)
        {
            StartOfflineEncounter("ChallengeAI");
        }

        // ------------------------------------------------------------------
        // Match started from the mod's menu
        // ------------------------------------------------------------------

        /// <summary>"Neutral" encounter used for deck-versus-deck matches.</summary>
        public static ResourceId NeutralEncounterId { get { return new ResourceId(new Guid(OfflineEncounterGuid)); } }

        /// <summary>Deck forced on the AI for the next match (null: the encounter's deck).</summary>
        private static DeckTemplate s_PendingAIDeck;
        private static EDeckPersonality s_PendingAIPersonality;

        /// <summary>Settings of the next match's encounter data (Siege and Battlegrounds flags), applied once.</summary>
        public static Action<SessionStateEncounterData> PendingEncounterSetup;

        public static void StartMatch(UID playerDeck, ResourceId scene, DeckTemplate aiDeck, string label,
            EDeckPersonality aiPersonality = EDeckPersonality.Default, string siegeMessage = null)
        {
            try
            {
                // A deck saved before the move to the shared decks is found under its new id.
                if (playerDeck.IsValid()) playerDeck = new UID(UID.Type.Deck, Decks.CurrentId(playerDeck.GetInstanceId()));
                s_PendingAIDeck = aiDeck;
                s_PendingAIPersonality = aiPersonality;
                Decks.LastDeck = playerDeck;
                BattleStateContext context = new BattleStateContext();
                context.m_SceneTemplateId = scene;
                context.m_PlayerDeckId = playerDeck;
                context.m_PlayerDeckTemplate = ResourceId.Invalid;
                context.m_AIPlayerDeckId = ResourceId.Invalid;
                context.SiegeFightMessage = siegeMessage;
                Debug.Log("[HexRevival] [Menu] match: deck " + playerDeck + " vs " + label + " (encounter " + scene + (aiDeck != null ? ", AI deck " + aiDeck.m_DeckName : "") + ")");
                Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.Battle, context);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [Menu] StartMatch : " + e);
            }
        }

        private static void StartOfflineEncounter(string source)
        {
            try
            {
                ResourceId sceneId = new ResourceId(new Guid(OfflineEncounterGuid));
                BattleStateContext context = new BattleStateContext();
                context.m_SceneTemplateId = sceneId;
                // Player deck: the last deck saved in the local profile (otherwise the encounter's).
                context.m_PlayerDeckId = Decks.LastDeck;
                context.m_PlayerDeckTemplate = ResourceId.Invalid;
                context.m_AIPlayerDeckId = ResourceId.Invalid;
                Debug.Log("[HexRevival] " + source + " -> encounter PvE local " + sceneId);
                Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.Battle, context);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] StartOfflineEncounter failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // Reflection helpers
        // ------------------------------------------------------------------

        private static object GetField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            if (ReferenceEquals(field, null))
                throw new MissingFieldException(target.GetType().Name + "." + name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            if (ReferenceEquals(field, null))
                throw new MissingFieldException(target.GetType().Name + "." + name);
            field.SetValue(target, value);
        }

        private static void InvokeNonPublic(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
            if (ReferenceEquals(method, null))
                throw new MissingMethodException(target.GetType().Name + "." + name);
            method.Invoke(target, args);
        }
    }
}
