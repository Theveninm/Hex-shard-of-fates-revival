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

namespace HexOffline
{
    /// <summary>
    /// Méthodes de remplacement injectées par le patcher dans les DLL du jeu
    /// (redirection de corps de méthode via Mono.Cecil, sans Harmony).
    /// </summary>
    public static class Patches
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>Encounter PvE local "Lair of the Beast" (deck joueur + deck IA valides).</summary>
        private const string OfflineEncounterGuid = "6aca3608-0ef6-478b-a089-d76e94b13650";

        // ------------------------------------------------------------------
        // Auth / profil
        // ------------------------------------------------------------------

        /// <summary>Remplace Steamy.IsRunningSteam() : on se comporte comme un build non-Steam.</summary>
        public static bool Steamy_IsRunningSteam()
        {
            return false;
        }

        /// <summary>
        /// Remplace PlayerProfile.Login(...) : contourne l'authentification réseau et
        /// initialise un profil local minimal (identité + champion).
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
                SeedLocalProfile(self, username);
                try { LocalProfile.Load(self, new UID(UID.Type.ServicePlayer, 0x1001UL), string.IsNullOrEmpty(username) ? "Offline" : username); }
                catch (Exception e) { Debug.LogError("[HexOffline] LocalProfile.Load failed: " + e); }
                RaiseLoginResponse(self, true, null);
                Debug.Log("[HexOffline] PlayerProfile.Login -> profil local initialise pour '" + username + "'");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] PlayerProfile_Login failed: " + e);
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

            // Champion local minimal (permet a UIBattle.StartEncounter de resoudre GetCurrentChampion()).
            List<PlayerChampion> champions = (List<PlayerChampion>)GetField(self, "m_Champions");
            champions.Clear();
            ulong first = 0;
            foreach (champion_bits c in Champions.Load(reckoningId, name))
            {
                champions.Add(new PlayerChampion(c));
                if (first == 0) first = c.Id;
            }
            SetField(self, "m_CurrentChampionId", first);
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
                Debug.LogWarning("[HexOffline] Champ OnProfileLoginResponse introuvable.");
                return;
            }
            Delegate handler = field.GetValue(self) as Delegate;
            if (ReferenceEquals(handler, null))
            {
                Debug.LogWarning("[HexOffline] Aucun abonne a OnProfileLoginResponse.");
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
                    Debug.LogError("[HexOffline] Erreur dans un handler de login: " + e);
                }
            }
        }

        // ------------------------------------------------------------------
        // Combat PvE local
        // ------------------------------------------------------------------

        /// <summary>
        /// Remplace StandaloneGameClient.SendStartEncounter (stub "encounters need to be
        /// launched by a server") par un vrai encounter local contre l'IA.
        /// </summary>
        /// <summary>
        /// Remplace StandaloneGameClient.SendStartSession pour les parties contre l'IA hors rencontre
        /// (bouton Practice de l'editeur de deck). Le "serveur" du client ne sait pas charger les decks
        /// hors rencontre (LoadDeckForPlayers est vide) : on lance a la place une rencontre locale ;
        /// le joueur y garde son deck (player.m_DeckID, fourni par le profil local).
        /// </summary>
        public static bool StandaloneGameClient_SendStartSession(StandaloneGameClient self, string sessionName, int aiPlayerCount, int flags)
        {
            if (aiPlayerCount != 1)
                return true;
            s_PendingAIDeck = null;
            SessionStateEncounterData data = new SessionStateEncounterData();
            data.SceneTemplateId = new ResourceId(new Guid(PracticeEncounterGuid));
            data.SessionFlags = ESessionFlags.IsEncounter | ESessionFlags.IsPvE;
            Debug.Log("[HexOffline] Practice -> rencontre locale " + PracticeEncounterGuid + " (flags d'origine " + flags + ")");
            StandaloneGameClient_SendStartEncounter(self, sessionName, data);
            return false;
        }

        /// <summary>
        /// Postfix de Session.AddPlayer sur la session autoritative locale : StandaloneGameClient
        /// y ajoute des RemotePlayer neufs (PlayerState vide), sans le deck choisi par le joueur.
        /// On recopie m_DeckID (et le modele de deck) depuis le LocalPlayer correspondant, pour que
        /// le moteur demande ce deck (GetDeckDetails) au lieu de prendre celui de la rencontre.
        /// </summary>
        public static void AuthoritativeSession_AddPlayer(Session self, Player player)
        {
            try
            {
                if (!(self is StandaloneSession) || player == null) return;
                if (player.IsAI() || player.m_PlayerId.GetUIDType() == UID.Type.AIPlayer)
                {
                    if (s_PendingAIDeck != null)
                    {
                        FieldInfo f = typeof(AuthoritativeSessionBase).GetField("m_OverrideDecks", BindingFlags.Instance | BindingFlags.NonPublic);
                        Dictionary<UID, DeckTemplate> over = f.GetValue(self) as Dictionary<UID, DeckTemplate>;
                        over[player.m_PlayerId] = s_PendingAIDeck;
                        Debug.Log("[HexOffline] IA " + player.m_PlayerId + " : deck " + s_PendingAIDeck.m_DeckName);
                    }
                    return;
                }
                StandaloneGameClient gc = Singleton<Main>.Instance.m_GameClient as StandaloneGameClient;
                if (gc == null || gc.Player == null || gc.Player.m_PlayerId != player.m_PlayerId) return;
                player.m_DeckID = gc.Player.m_DeckID;
                player.m_DeckTemplateID = gc.Player.m_DeckTemplateID;
                Debug.Log("[HexOffline] joueur " + player.m_PlayerId + " : deck " + player.m_DeckID);
            }
            catch (Exception e) { Debug.LogWarning("[HexOffline] AddPlayer : " + e.Message); }
        }

        /// <summary>Rencontre servant d'adversaire pour "Practice" (deck IA de la rencontre).</summary>
        private const string PracticeEncounterGuid = OfflineEncounterGuid;

        public static void StandaloneGameClient_SendStartEncounter(
            StandaloneGameClient self, string sessionName, SessionStateEncounterData encounterData)
        {
            try
            {
                Debug.Log("[HexOffline] Demarrage encounter local : " + encounterData.SceneTemplateId);
                Server.ResetCache();
                FillNullLists(encounterData);

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
                    playerState.PlayerId = new UID(UID.Type.AIPlayer, (ulong)(1000 + i));
                    AIPlayer aiPlayer = new AIPlayer(playerState);

                    SessionClient sessionClient = new SessionClient(BuildSessionState(sessionServer, encounterData), self);
                    sessionClient.m_SessionName = "AILocalSession_" + aiPlayer.m_PlayerId;
                    // Meme instance id que la session autoritative : sinon le filtre
                    // RaiseSessionEventFromNetwork (par instance id) rejette tous les evenements.
                    sessionClient.m_SessionId = sessionServer.m_SessionId;
                    sessionClient.AddPlayer(aiPlayer);

                    // Attache le composant tactique IA (jamais initialise ailleurs dans le client).
                    aiPlayer.InitializeAITactical(new Game.Shared.AI.AIPersonality());

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
                Debug.LogError("[HexOffline] SendStartEncounter local failed: " + e);
            }
        }

        /// <summary>
        /// StandaloneSession.GetSessionState() n'inclut pas EncounterData, alors que le client
        /// (ClientSessionBase.SetSessionState -> m_EncounterData) s'en sert partout
        /// (ex. GoHUDPlayer.SetActiveEquipmentForPlayer -> NRE qui casse tout le HUD du champion).
        /// </summary>
        /// <summary>
        /// Le serveur remplissait les listes de SessionStateEncounterData ; cote client elles restent
        /// nulles (ex. InitEncounter fait AbilitiesToRun.AddRange pour les rencontres avec "mods").
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
        /// Remplace UILandingPageContext.OnClickChallengeAI : au lieu du matchmaking
        /// (serveur mort), lance un encounter PvE local contre l'IA.
        /// </summary>
        public static void UILandingPage_OnClickChallengeAI(UILandingPageContext self)
        {
            StartOfflineEncounter("ChallengeAI");
        }

        /// <summary>Remplace UILandingPageViewModel.OnFrostRingButtonClicked (ecran mort) par un encounter local.</summary>
        public static void UILandingPageViewModel_OnFrostRingButtonClicked(UILandingPageViewModel self)
        {
            OfflineMenu.Open();
        }

        // ------------------------------------------------------------------
        // Partie lancee depuis le menu du mod
        // ------------------------------------------------------------------

        /// <summary>Rencontre "neutre" utilisee pour les parties deck contre deck.</summary>
        public static ResourceId NeutralEncounterId { get { return new ResourceId(new Guid(OfflineEncounterGuid)); } }

        /// <summary>Deck impose a l'IA pour la prochaine partie (null : deck de la rencontre).</summary>
        private static DeckTemplate s_PendingAIDeck;

        public static void StartMatch(UID playerDeck, ResourceId scene, DeckTemplate aiDeck, string label)
        {
            try
            {
                s_PendingAIDeck = aiDeck;
                Decks.LastDeck = playerDeck;
                BattleStateContext context = new BattleStateContext();
                context.m_SceneTemplateId = scene;
                context.m_PlayerDeckId = playerDeck;
                context.m_PlayerDeckTemplate = ResourceId.Invalid;
                context.m_AIPlayerDeckId = ResourceId.Invalid;
                Debug.Log("[HexOffline] [Menu] partie : deck " + playerDeck + " contre " + label + " (rencontre " + scene + (aiDeck != null ? ", deck IA " + aiDeck.m_DeckName : "") + ")");
                Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.Battle, context);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] [Menu] StartMatch : " + e);
            }
        }

        /// <summary>Remplace UILandingPageContext.OnClickContinue (bouton Campagne) par un encounter local.</summary>
        public static void UILandingPage_OnClickContinue(UILandingPageContext self)
        {
            StartOfflineEncounter("Campaign");
        }

        private static void StartOfflineEncounter(string source)
        {
            try
            {
                ResourceId sceneId = new ResourceId(new Guid(OfflineEncounterGuid));
                BattleStateContext context = new BattleStateContext();
                context.m_SceneTemplateId = sceneId;
                // Deck du joueur : le dernier deck enregistre du profil local (sinon celui de la rencontre).
                context.m_PlayerDeckId = Decks.LastDeck;
                context.m_PlayerDeckTemplate = ResourceId.Invalid;
                context.m_AIPlayerDeckId = ResourceId.Invalid;
                Debug.Log("[HexOffline] " + source + " -> encounter PvE local " + sceneId);
                Singleton<Main>.Instance.m_GameClient.StateManager.TransitionToState(EGameState.Battle, context);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] StartOfflineEncounter failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // Helpers reflection
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
