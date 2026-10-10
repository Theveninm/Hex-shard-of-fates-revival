using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Mechanics.Transactions;
using Reckoning.Game;
using UnityEngine;

namespace HexOffline
{
    /// <summary>
    /// Couche "serveur autoritatif" minimale pour le mode offline.
    ///
    /// Le client ne fait que consommer des evenements d'etat (CardUpdated / PlayerUpdated)
    /// et une liste d'options joueur (PlayerOptionList) generes normalement par le serveur.
    /// Ces methodes sont injectees dans StandaloneSession (qui les stubbait).
    /// </summary>
    public static class Server
    {
        /// <summary>Derniere signature de carte envoyee, par (carte, destinataire).</summary>
        private static readonly Dictionary<string, string> s_CardSig = new Dictionary<string, string>();
        /// <summary>Cartes revelees a un joueur (SendRevealedCards) : cle (carte|joueur) -> zone au moment de la revelation.</summary>
        private static readonly Dictionary<string, ECardCollections> s_Revealed = new Dictionary<string, ECardCollections>();
        private static readonly Dictionary<UID, string> s_PlayerSig = new Dictionary<UID, string>();

        public static void ResetCache()
        {
            s_CardSig.Clear();
            s_Revealed.Clear();
            s_PlayerSig.Clear();
            s_Dirty = true;
            s_LastOptionsSig = null;
            s_LastGreenLightSig = null;
            s_SuppressNextGreenLight = false;
            s_InDispatch = false;
            s_OptionsSigByPlayer.Clear();
            s_CombatSig.Clear();
            s_Pending.Clear();
            s_Outbox.Clear();
            s_LastTurnPhaseSig = null;
            FailuresByCard.Clear();
        }

        private static string CardSig(Card c)
        {
            return string.Concat(
                (int)c.m_CurrentCardCollection, "|", (c.m_ControllingPlayer != null ? c.m_ControllingPlayer.m_PlayerId.ToString() : "?"), "|",
                c.CastingCost, "|", c.CurrentAttackValue, "|", c.CurrentDefenseValue, "|", (int)c.CurrentCardState, "|", (int)c.CurrentType, "|",
                (int)c.CurrentAttributeFlags, "|", c.CurrentAbilities.Count, "|", c.EscalationCount, "|", (int)c.ActiveGem, "|", c.Counters.Count, "|",
                c.CurrentSubtype ?? "", "|", ExtraSig(c));
        }

        /// <summary>Partie de la signature couvrant les donnees "secondaires" envoyees au client
        /// (attributs NotifyClient, variables, valeurs des compteurs, modificateurs de cout).</summary>
        private static string ExtraSig(Card c)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            try
            {
                Dictionary<string, int> ints = new Dictionary<string, int>();
                Dictionary<string, string> strs = new Dictionary<string, string>();
                FillClientAttributes(c.GetCardContext(), ints, strs);
                foreach (KeyValuePair<string, int> kv in ints) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
                foreach (KeyValuePair<string, string> kv in strs) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
                if (c.CardIntegerVariables != null)
                    foreach (KeyValuePair<string, int> kv in c.CardIntegerVariables) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
                if (c.Counters != null)
                    foreach (KeyValuePair<CardCounterTemplate, int> kv in c.Counters) sb.Append(kv.Value).Append(',');
                AppendDict(sb, c.GetActivationCostModifiers());
                AppendDict(sb, c.GetChargePointCostModifiers());
                AppendDict(sb, c.GetSpellPointCostModifiers());
                AppendDict(sb, c.GetUsesPerGameCounts());
                AppendDict(sb, c.GetCooldownCounts());
            }
            catch (Exception) { }
            return sb.ToString();
        }

        private static void AppendDict(System.Text.StringBuilder sb, Dictionary<ResourceId, int> d)
        {
            sb.Append(';');
            if (d != null)
                foreach (KeyValuePair<ResourceId, int> kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
        }

        private static string PlayerSig(Player p)
        {
            int hp = (p.m_ChampionCard != null) ? p.m_ChampionCard.CurrentDefenseValue : 0;
            return string.Concat(hp, "|", p.m_ChargePoints, "|", p.m_CurrentResourcePool, "|", p.m_TotalResourcePool, "|", p.m_SpellPoints, "|", p.m_ResourceThresholds.Count);
        }


        private struct PendingTransaction
        {
            public Transaction Tx;
            public float Time;
        }

        private static readonly Queue<PendingTransaction> s_Pending = new Queue<PendingTransaction>();
        private static FieldInfo s_TxQueueField;

        /// <summary>
        /// Remplace StandaloneSession.SubmitTransaction : la transaction est mise en attente
        /// (voir Pump) au lieu d'etre traitee a la frame suivante.
        ///
        /// Pourquoi : l'UI fait des animations "predictives" (ex. PredictivePushOnChain met la
        /// carte jouee sur la chaine sous un id provisoire, enregistre au DEMARRAGE de l'animation).
        /// Le vrai serveur repondait avec la latence reseau, donc apres ce demarrage ; si on repond
        /// trop tot, OnAbilityPushedOnChain ne trouve pas l'id provisoire et ajoute un CLONE de la
        /// carte sur la chaine (shard qui reste affichee au centre du plateau).
        /// </summary>
        public static bool Server_SubmitTransaction(StandaloneSession self, Transaction transaction)
        {
            if (transaction == null)
                return false;
            Debug.Log("[HexOffline] [Server] SubmitTransaction " + transaction.GetType().Name + (s_InServerTick ? " (serveur)" : ""));
            if (transaction is RequestPlayerOptionsTransaction)
            {
                // Le client redemande ses options (ex. carte annulee pendant le choix des cibles) et
                // attend options + feu vert. L'etat n'a pas change : sans forcage, la deduplication
                // n'envoyait rien et la partie restait bloquee.
                s_Dirty = true;
                s_SuppressNextGreenLight = false;
                s_LastGreenLightSig = null;
                try { self.SendGreenLight(s_LastGreenLightContext); }
                catch (Exception e) { Debug.LogError("[HexOffline] RequestPlayerOptions : " + e); }
                return true;
            }
            if (s_InServerTick)
            {
                // Transaction emise par le moteur lui-meme pendant son tick (ex. passe automatique
                // DoPassPriorityTransaction) : file directe, sinon PostUpdate la re-soumet a chaque tick.
                EnqueueNow(self, transaction);
                return true;
            }
            s_Pending.Enqueue(new PendingTransaction { Tx = transaction, Time = UnityEngine.Time.realtimeSinceStartup });
            // Pas de MarkDirty ici : renvoyer GreenLight avant que la transaction soit traitee
            // pousse l'IA a la soumettre en double (boucle). Voir Pump.
            return true;
        }

        /// <summary>Delai minimal simule (s) et attente max des animations de l'UI (s).</summary>
        public static float MinLatency = 0f;
        public static float MaxAnimationWait = 0f;

        private static bool s_InServerTick;

        public static void BeginTick() { s_InServerTick = true; }
        public static void EndTick() { s_InServerTick = false; }

        private static void EnqueueNow(StandaloneSession self, Transaction tx)
        {
            if (ReferenceEquals(s_TxQueueField, null))
                s_TxQueueField = typeof(Session).GetField("m_TransactionQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            object queue = s_TxQueueField.GetValue(self);
            queue.GetType().GetMethod("Enqueue").Invoke(queue, new object[] { tx });
        }

        /// <summary>Appele avant chaque StandaloneSession.Update : libere les transactions en attente.</summary>
        public static void Pump(StandaloneSession self)
        {
            s_InServerTick = false;
            if (s_Pending.Count == 0)
                return;
            if (ReferenceEquals(s_TxQueueField, null))
                s_TxQueueField = typeof(Session).GetField("m_TransactionQueue", BindingFlags.Instance | BindingFlags.NonPublic);
            object queue = ReferenceEquals(s_TxQueueField, null) ? null : s_TxQueueField.GetValue(self);
            if (ReferenceEquals(queue, null))
                return;
            MethodInfo enqueue = queue.GetType().GetMethod("Enqueue");

            float now = UnityEngine.Time.realtimeSinceStartup;
            bool uiBusy = !ReferenceEquals(UIBattle.Instance, null)
                && !ReferenceEquals(UIBattle.Instance.AnimationManager, null)
                && !UIBattle.Instance.AnimationManager.IsEmpty;
            while (s_Pending.Count > 0)
            {
                PendingTransaction head = s_Pending.Peek();
                float age = now - head.Time;
                if (age < MinLatency)
                    break;
                if (uiBusy && age < MaxAnimationWait)
                    break;
                s_Pending.Dequeue();
                enqueue.Invoke(queue, new object[] { head.Tx });
                MarkDirty();
            }
        }

        private static FieldInfo s_GameClientField;
        private static MethodInfo s_OnSyncMethod;
        private static MethodInfo s_OnSyncPlayerMethod;
        private static FieldInfo s_MapField;
        private static FieldInfo s_RespondedField;

        // --- Etat du flux sortant (equivalent du tampon d'envoi du vrai serveur) ---
        private static bool s_InDispatch;
        /// <summary>Vrai si un evenement "narratif" (autre qu'options/greenlight) est parti depuis le dernier envoi d'options.</summary>
        private static bool s_Dirty = true;
        private static string s_LastOptionsSig;
        private static bool s_SuppressNextGreenLight;
        private static string s_LastGreenLightSig;
        private static EPriorityContext s_LastGreenLightContext;

        /// <summary>Force le renvoi des options + greenlight au prochain tick (ex. transaction recue).</summary>
        public static void MarkDirty()
        {
            s_Dirty = true;
        }

        /// <summary>Trace de chaque evenement envoye au client (output_log.txt).</summary>
        public static bool TraceEvents = Dev.Enabled;

        private static string Describe(SessionEventArgs a)
        {
            CardUpdatedSessionEventArgs cu = a as CardUpdatedSessionEventArgs;
            if (cu != null)
                return "CardUpdated " + cu.SessionCardId + " " + cu.Collection + (cu.Nulling ? " NULL" : "") + " st=" + cu.State;
            PlayerOptionListSessionEventArgs ol = a as PlayerOptionListSessionEventArgs;
            if (ol != null)
                return "Options n=" + ol.Options.Count;
            GreenLightSessionEventArgs gl = a as GreenLightSessionEventArgs;
            if (gl != null)
                return "GreenLight " + gl.PlayerId + " " + gl.Context;
            TurnPhaseUpdatedSessionEventArgs tp = a as TurnPhaseUpdatedSessionEventArgs;
            if (tp != null)
                return "TurnPhase " + tp.TurnPhase + " act=" + tp.ActivePlayerId + " pri=" + tp.PriorityPlayerId;
            return a.GetType().Name.Replace("SessionEventArgs", "");
        }

        private static string TurnSig(ETurnPhases phase, UID active, UID priority)
        {
            return phase + "|" + active + "|" + priority;
        }

        /// <summary>
        /// Le moteur change parfois le joueur prioritaire sans TurnPhaseUpdated (ex.
        /// WaitForTriggeredAbilitiesAction -> UpdatePriorityPlayer). Le client garderait une priorite
        /// perimee (ses PassPriority echouent : PlayerHasPriorityRequirement) : on resynchronise.
        /// </summary>
        private static bool s_InSyncTurnPhase;

        private static void SyncTurnPhase(StandaloneSession self)
        {
            if (!self.IsGamePlaying)
                return;
            Player active = self.GetActivePlayer();
            Player prio = self.GetPriorityPlayer();
            string sig = TurnSig(self.CurrentTurnPhase, active != null ? active.m_PlayerId : UID.Invalid, prio != null ? prio.m_PlayerId : UID.Invalid);
            if (s_LastTurnPhaseSig != null && sig != s_LastTurnPhaseSig)
                self.SendTurnPhaseUpdate();
        }

        private static bool IsStateEvent(SessionEventArgs a)
        {
            return a is CardUpdatedSessionEventArgs || a is PlayerUpdatedSessionEventArgs;
        }

        /// <summary>
        /// Remplace StandaloneSession.DispatchSessionEvent.
        ///
        /// Regles (deduites des handlers client) :
        /// 1. Les deltas d'etat (CardUpdated/PlayerUpdated) partent AVANT chaque evenement narratif :
        ///    l'UI calcule la destination d'une carte (GetCardViewHome) a partir du cache au moment
        ///    ou elle recoit l'evenement (ex. ResourceCardPlayed -> shard detruite seulement si le
        ///    cache la sait deja en PlayedResources ; sinon elle reste bloquee sur la chaine).
        /// 2. La session est tickee a chaque frame et renvoie GreenLight + options a chaque tick
        ///    d'attente : on supprime les doublons tant que rien n'a change.
        /// </summary>
        public static void Server_DispatchSessionEvent(StandaloneSession self, SessionEventArgs args)
        {
            Dispatch(self, null, args);
        }

        public static void Server_DispatchSessionEventPlayer(StandaloneSession self, Player player, SessionEventArgs args)
        {
            Dispatch(self, player, args);
        }

        private static void Dispatch(StandaloneSession self, Player player, SessionEventArgs args)
        {
            if (ReferenceEquals(args, null))
                return;
            try
            {
                if (!s_InDispatch && !IsStateEvent(args))
                {
                    s_InDispatch = true;
                    try { BroadcastState(self); }
                    finally { s_InDispatch = false; }
                }

                // Le vrai serveur change la priorite (UpdatePriorityPlayer) AVANT de demander des
                // donnees d'activation : la mise a jour de phase doit donc preceder la demande.
                // Envoyee apres, elle empile BattleStateInactivePriorityWindow PAR-DESSUS l'ecran de
                // ciblage (coverflow de choix) : partie figee, sans bouton.
                if ((args is AbilityActivationDataRequiredSessionEventArgs || args is TriggeredAbilityActivationDataRequiredSessionEventArgs) && !s_InSyncTurnPhase)
                {
                    s_InSyncTurnPhase = true;
                    try { SyncTurnPhase(self); }
                    finally { s_InSyncTurnPhase = false; }
                }

                if (args is GreenLightSessionEventArgs)
                {
                    GreenLightSessionEventArgs gl = (GreenLightSessionEventArgs)args;
                    string sig = gl.PlayerId + "|" + gl.Context;
                    s_LastGreenLightContext = gl.Context;
                    bool skip = s_SuppressNextGreenLight && sig == s_LastGreenLightSig;
                    s_SuppressNextGreenLight = false;
                    s_LastGreenLightSig = sig;
                    if (skip)
                        return;
                }
                else if (!(args is PlayerOptionListSessionEventArgs) && !IsStateEvent(args))
                {
                    s_Dirty = true;
                    s_LastGreenLightSig = null;
                }

                TurnPhaseUpdatedSessionEventArgs tpu = args as TurnPhaseUpdatedSessionEventArgs;
                if (tpu != null)
                    s_LastTurnPhaseSig = TurnSig(tpu.TurnPhase, tpu.ActivePlayerId, tpu.PriorityPlayerId);
                if (TraceEvents)
                    Debug.Log("[HexOffline] >> " + Describe(args) + (player != null ? " @" + player.m_PlayerId : ""));
                Send(self, player, args);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] Dispatch failed: " + e);
            }
        }

        private struct Outgoing
        {
            public UID Target; // UID.Invalid = tous les joueurs
            public SessionEventArgs Args;
        }

        private static readonly List<Outgoing> s_Outbox = new List<Outgoing>();

        private static void Send(StandaloneSession self, Player player, SessionEventArgs args)
        {
            args.SessionId = self.m_SessionId;
            s_Outbox.Add(new Outgoing { Target = player != null ? player.m_PlayerId : UID.Invalid, Args = args });
        }

        /// <summary>
        /// Appele apres chaque StandaloneSession.Update (et au besoin ailleurs) : envoie a chaque
        /// joueur local UN paquet reseau contenant tous les evenements du tick, via la file
        /// SessionClient.AddSyncEventToQueue — exactement comme NetworkGameClient.
        ///
        /// Le client depile un paquet par frame, et l'UI du joueur humain uniquement quand sa file
        /// d'animations est vide (UIBattle.Update -> m_Session.Update). C'est ce cadencement qui
        /// garantit que les animations predictives (carte jouee mise sur la chaine) ont demarre
        /// avant que la reponse du serveur soit traitee.
        /// </summary>
        private static string s_LastTurnPhaseSig;

        /// <summary>Derniere session autoritative vue (outils de test).</summary>
        public static StandaloneSession CurrentSession;

        public static void Flush(StandaloneSession self)
        {
            CurrentSession = self;
            SyncTurnPhase(self);
            if (s_Outbox.Count == 0)
                return;
            List<Outgoing> batch = new List<Outgoing>(s_Outbox);
            s_Outbox.Clear();

            if (ReferenceEquals(s_GameClientField, null))
                s_GameClientField = typeof(StandaloneSession).GetField("m_GameClient", BindingFlags.Instance | BindingFlags.NonPublic);
            object gameClient = ReferenceEquals(s_GameClientField, null) ? null : s_GameClientField.GetValue(self);
            if (ReferenceEquals(gameClient, null))
                return;
            if (ReferenceEquals(s_MapField, null))
                s_MapField = gameClient.GetType().GetField("m_LocalPlayerMap");
            Dictionary<UID, LocalPlayer> map = s_MapField.GetValue(gameClient) as Dictionary<UID, LocalPlayer>;
            if (map == null)
                return;
            if (ReferenceEquals(s_RespondedField, null))
                s_RespondedField = typeof(ClientSessionBase).GetField("m_HasPreviousTransactionBeenRespondedByServer", BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (LocalPlayer lp in map.Values)
            {
                if (lp == null || lp.m_Session == null)
                    continue;
                List<SessionEventArgs> events = new List<SessionEventArgs>();
                foreach (Outgoing o in batch)
                {
                    if (!o.Target.IsValid() || o.Target == lp.m_PlayerId)
                        events.Add(o.Args);
                }
                if (events.Count == 0)
                    continue;

                BulkSessionEventSessionEventArgs bulk = new BulkSessionEventSessionEventArgs
                {
                    SessionId = self.m_SessionId,
                    PlayerId = UID.Invalid,
                    EventList = events
                };
                NetworkPacketSessionEventArgs packet = NetworkPacketSessionEventArgs.Create(bulk);
                packet.SessionId = self.m_SessionId;

                // Un handler (ex. l'IA sur GreenLight) peut soumettre une transaction pendant le
                // traitement du paquet, avant que le paquet lui-meme ne remette ce drapeau a vrai :
                // on le leve des l'envoi (le serveur local repond toujours).
                if (!ReferenceEquals(s_RespondedField, null))
                    s_RespondedField.SetValue(lp.m_Session, true);

                Game.Client.SessionClient sc = lp.m_Session as Game.Client.SessionClient;
                if (sc != null)
                    sc.AddSyncEventToQueue(packet);
                else
                {
                    ClientSessionBase cs = lp.m_Session as ClientSessionBase;
                    if (cs != null)
                        cs.RaiseSessionEventFromNetwork(packet);
                }
            }
        }

        private static void MarkClientSessionsResponded(object gameClient)
        {
            if (ReferenceEquals(s_MapField, null))
                s_MapField = gameClient.GetType().GetField("m_LocalPlayerMap");
            if (ReferenceEquals(s_RespondedField, null))
                s_RespondedField = typeof(ClientSessionBase).GetField("m_HasPreviousTransactionBeenRespondedByServer", BindingFlags.Instance | BindingFlags.NonPublic);

            Dictionary<UID, LocalPlayer> map = ReferenceEquals(s_MapField, null)
                ? null
                : s_MapField.GetValue(gameClient) as Dictionary<UID, LocalPlayer>;
            if (ReferenceEquals(map, null))
                return;

            foreach (LocalPlayer player in map.Values)
            {
                if (ReferenceEquals(player, null) || ReferenceEquals(player.m_Session, null))
                    continue;
                if (!ReferenceEquals(s_RespondedField, null))
                    s_RespondedField.SetValue(player.m_Session, true);
            }
        }

        // ------------------------------------------------------------------
        // Options joueur
        // ------------------------------------------------------------------

        private static readonly Dictionary<UID, string> s_OptionsSigByPlayer = new Dictionary<UID, string>();

        public static bool Server_SendPlayerOptions(StandaloneSession self, Player player)
        {
            try
            {
                if (player == null)
                    return false;
                // Rien n'a bouge depuis le dernier envoi : pas de recalcul ni de renvoi (tick d'attente).
                bool stateChanged = BroadcastState(self);
                if (!s_Dirty && !stateChanged)
                {
                    s_SuppressNextGreenLight = true;
                    return true;
                }

                List<SessionEventArgs> options = new List<SessionEventArgs>();
                foreach (Card card in player.GetAllCards())
                {
                    if (card == null || !card.m_SessionCardId.IsValid())
                        continue;
                    if (IsBlocked(self, card))
                        continue;

                    ECardUsage usage = ECardUsage.None;
                    List<SessionEventArgs> instances = new List<SessionEventArgs>();

                    if (self.CanPlayCard(card, player))
                    {
                        usage |= ECardUsage.Play;
                        // Le client (BattleStatePlayCard / IA) lit :
                        //  - la capacite generique "Play Card" : couts additionnels de la carte
                        //    (X, sacrifice, defausse... cf. GetPotentialCostsForAbility) ;
                        //  - CHAQUE capacite automatique/indexee de la carte (CanUseAbility exige une
                        //    entree d'options par capacite), avec ses cibles et couts.
                        SafeAddAbilityOptions(self, player, card, Game.Shared.Resources.BuiltInResources.PlayCardAbilityTemplateId, instances);
                        List<AbilityTemplate> automatic = card.GetAutomaticAbilities();
                        if (automatic != null)
                            foreach (AbilityTemplate ability in automatic)
                                SafeAddAbilityOptions(self, player, card, ability.AbilityTemplateId, instances);
                    }

                    List<AbilityTemplate> manual = card.GetManualAbilities();
                    if (manual != null)
                    {
                        foreach (AbilityTemplate ability in manual)
                        {
                            ResourceId abilityId = ability.AbilityTemplateId;
                            if (self.CanActivateAbility(card, player, abilityId))
                            {
                                usage |= ECardUsage.Activate;
                                AddAbilityOptions(self, player, card, abilityId, instances);
                            }
                        }
                    }

                    // Combat : usages d'attaque/defense selon la phase et le role du joueur.
                    ETurnPhases phase = self.CurrentTurnPhase;
                    Player activePlayer = self.GetActivePlayer();
                    bool isActive = activePlayer != null && activePlayer.m_PlayerId == player.m_PlayerId;
                    if (phase == ETurnPhases.DeclareAttack && isActive)
                    {
                        if (card.CanAttack() == ECombatantStatus.Ok)
                            usage |= ECardUsage.Attack;
                        if (card.IsForceAttack())
                            usage |= ECardUsage.ForcedAttack;
                    }
                    else if (phase == ETurnPhases.DeclareDefense && !isActive)
                    {
                        // Bloqueur : usage Defend + instance "Blocking" listant les attaquants qu'il peut
                        // bloquer (lue par BattleStateDeclareBlockers / BattleStateAssignBlocker).
                        if (card.IsInPlay() && card.IsTroop() && card.CanBlock() == ECombatantStatus.Ok)
                        {
                            List<SessionCardId> blockable = new List<SessionCardId>();
                            foreach (Combat combat in self.CombatManager.Combats)
                            {
                                if (combat.Attacker == null || combat.Defender == null || combat.Defender.m_ControllingPlayer != player)
                                    continue;
                                if (card.CanBlock(combat.Attacker) == ECombatantStatus.Ok)
                                    blockable.Add(combat.Attacker.m_SessionCardId);
                            }
                            if (blockable.Count > 0)
                            {
                                usage |= ECardUsage.Defend;
                                int min = card.GetCardContext().GetBool(IntAttrs.MustBlock) ? 1 : 0;
                                instances.Add(new OptionInstanceSessionEventArgs
                                {
                                    SessionId = self.m_SessionId,
                                    Id = ResourceId.Blocking,
                                    TargetIds = new List<ResourceId> { ResourceId.Blocking },
                                    MinTargetCounts = new List<int> { min },
                                    MaxTargetCounts = new List<int> { 1 },
                                    TargetInstances = new List<SessionEventArgs>
                                    {
                                        new TargetInstanceSessionEventArgs
                                        {
                                            SessionId = self.m_SessionId,
                                            TargetIndex = 0,
                                            TargetId = ResourceId.Blocking,
                                            Targets = blockable,
                                            AdditionalTargets = new List<SessionCardId>()
                                        }
                                    }
                                });
                            }
                        }
                    }

                    if (usage != ECardUsage.None)
                    {
                        options.Add(new PlayerOptionSessionEventArgs
                        {
                            SessionId = self.m_SessionId,
                            Card = card.m_SessionCardId,
                            State = usage,
                            Instances = instances
                        });
                    }
                }

                SendCombats(self, player);
                SendOptionList(self, player, options);

                // Les autres joueurs n'ont plus la priorite : leurs options sont videes
                // (sinon le client garde des cartes "jouables" hors de sa fenetre de priorite).
                foreach (Player other in self.GetAllPlayers())
                {
                    if (other != null && other.m_PlayerId != player.m_PlayerId)
                        SendOptionList(self, other, new List<SessionEventArgs>());
                }

                s_Dirty = false;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] SendPlayerOptions failed: " + e);
                return false;
            }
        }

        /// <summary>
        /// Combats vus par le client (cache.Combats) :
        ///  - DeclareDefense : CombatListing (attaquant -> bloqueurs) pour le defenseur ;
        ///  - AssignDamage / AssignFirstStrikeDamage : CombatsThatNeedDamage pour l'attaquant
        ///    (combats bloques par plusieurs troupes : ordre des degats, BattleStateAssignDamage).
        /// </summary>
        private static void SendCombats(StandaloneSession self, Player player)
        {
            ETurnPhases phase = self.CurrentTurnPhase;
            bool listing = phase == ETurnPhases.DeclareDefense || phase == ETurnPhases.DeclareDefensePriorityWindow;
            bool damage = phase == ETurnPhases.AssignDamage || phase == ETurnPhases.AssignFirstStrikeDamage;
            if (!listing && !damage)
            {
                // Hors combat : vider les combats du cache client s'il en reste.
                string prev;
                if (s_CombatSig.TryGetValue(player.m_PlayerId.ToString(), out prev) && prev != "")
                {
                    s_CombatSig[player.m_PlayerId.ToString()] = "";
                    Dispatch(self, player, new CombatListingSessionEventArgs { SessionId = self.m_SessionId, PlayerId = player.m_PlayerId, Combats = new List<SessionEventArgs>() });
                }
                return;
            }
            List<SessionEventArgs> combats = new List<SessionEventArgs>();
            System.Text.StringBuilder sig = new System.Text.StringBuilder(phase.ToString());
            foreach (Combat combat in self.CombatManager.Combats)
            {
                if (combat.Attacker == null)
                    continue;
                if (damage && (combat.Instigator != player || combat.Blockers.Count < 2))
                    continue;
                List<SessionCardId> blockers = new List<SessionCardId>();
                foreach (Card b in combat.Blockers)
                    if (b != null) blockers.Add(b.m_SessionCardId);
                combats.Add(new CombatSessionEventArgs
                {
                    SessionId = self.m_SessionId,
                    PlayerId = player.m_PlayerId,
                    Id = combat.CombatId,
                    Attacker = combat.Attacker.m_SessionCardId,
                    Blockers = blockers
                });
                sig.Append('|').Append(combat.CombatId).Append(':').Append(combat.Attacker.m_SessionCardId).Append(':').Append(blockers.Count);
            }
            string key = player.m_PlayerId.ToString();
            string old;
            if (s_CombatSig.TryGetValue(key, out old) && old == sig.ToString())
                return;
            s_CombatSig[key] = sig.ToString();
            if (listing)
                Dispatch(self, player, new CombatListingSessionEventArgs { SessionId = self.m_SessionId, PlayerId = player.m_PlayerId, Combats = combats });
            else
                Dispatch(self, player, new CombatsThatNeedDamageSessionEventArgs { SessionId = self.m_SessionId, PlayerId = player.m_PlayerId, Combats = combats });
        }

        private static readonly Dictionary<string, string> s_CombatSig = new Dictionary<string, string>();

        private static string OptionsSig(List<SessionEventArgs> options)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (SessionEventArgs o in options)
            {
                PlayerOptionSessionEventArgs po = o as PlayerOptionSessionEventArgs;
                if (po == null) continue;
                sb.Append(po.Card).Append(':').Append((int)po.State).Append(':');
                if (po.Instances != null)
                {
                    foreach (SessionEventArgs i in po.Instances)
                    {
                        OptionInstanceSessionEventArgs oi = i as OptionInstanceSessionEventArgs;
                        if (oi == null) continue;
                        sb.Append(oi.Id).Append('[');
                        if (oi.TargetInstances != null)
                        {
                            foreach (SessionEventArgs t in oi.TargetInstances)
                            {
                                TargetInstanceSessionEventArgs ti = t as TargetInstanceSessionEventArgs;
                                if (ti != null && ti.Targets != null)
                                    foreach (SessionCardId c in ti.Targets) sb.Append(c).Append(',');
                                CostInstanceSessionEventArgs ci = t as CostInstanceSessionEventArgs;
                                if (ci != null && ci.Targets != null)
                                    foreach (SessionCardId c in ci.Targets) sb.Append('$').Append(c).Append(',');
                            }
                        }
                        sb.Append(']');
                    }
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Cartes pour lesquelles une option "jouer" a ete envoyee (diagnostic du banc).</summary>
        public static readonly HashSet<SessionCardId> OfferedToPlay = new HashSet<SessionCardId>();
        public static readonly Dictionary<UID, HashSet<SessionCardId>> LastPlayOffer = new Dictionary<UID, HashSet<SessionCardId>>();
        public static readonly Dictionary<UID, bool> LastOfferWasFor = new Dictionary<UID, bool>();

        private static void SendOptionList(StandaloneSession self, Player player, List<SessionEventArgs> options, bool force = false)
        {
            string sig = OptionsSig(options);
            string old;
            if (!force && s_OptionsSigByPlayer.TryGetValue(player.m_PlayerId, out old) && old == sig)
                return;
            s_OptionsSigByPlayer[player.m_PlayerId] = sig;
            HashSet<SessionCardId> last = new HashSet<SessionCardId>();
            foreach (SessionEventArgs o in options)
            {
                PlayerOptionSessionEventArgs po = o as PlayerOptionSessionEventArgs;
                if (po != null && (po.State & (ECardUsage.Play | ECardUsage.PlayForFree)) != 0)
                {
                    OfferedToPlay.Add(po.Card);
                    last.Add(po.Card);
                }
            }
            LastPlayOffer[player.m_PlayerId] = last;
            LastOfferWasFor[player.m_PlayerId] = force;
            Dispatch(self, player, new PlayerOptionListSessionEventArgs
            {
                SessionId = self.m_SessionId,
                PlayerId = player.m_PlayerId,
                Options = options
            });
            Log("[Server] Options " + self.CurrentTurnPhase + " -> " + player.m_PlayerId + " : " + options.Count + " carte(s)");
        }

        /// <summary>
        /// Capacite automatique optionnelle (ex. Scrounge) : ne la proposer que si ses couts a cibles
        /// (cartes a bannir/defausser/sacrifier...) sont payables, sinon l'IA la choisit et la
        /// transaction echoue en boucle (XCostRequirement).
        /// </summary>
        private static bool CostsPayable(StandaloneSession self, Player player, Card card, ResourceId abilityId)
        {
            Dictionary<EAbilityCostType, List<sCostInfo>> costs = self.GetPotentialCostsForAbility(card, player, abilityId);
            if (costs == null)
                return true;
            foreach (KeyValuePair<EAbilityCostType, List<sCostInfo>> kv in costs)
            {
                if (kv.Key == EAbilityCostType.XCostAbilityCostType || kv.Value == null)
                    continue;
                foreach (sCostInfo info in kv.Value)
                {
                    int available = info.Targets != null ? info.Targets.Count : 0;
                    if (info.Min > 0 && available < info.Min)
                        return false;
                }
            }
            return true;
        }

        // --- Garde-fou contre les boucles de transactions refusees ---
        private static readonly Dictionary<string, int> s_FailCounts = new Dictionary<string, int>();
        private static readonly HashSet<SessionCardId> s_BlockedThisTurn = new HashSet<SessionCardId>();
        private static int s_BlockedTurn = -1;

        /// <summary>
        /// Appele quand une transaction est refusee. Trois refus identiques d'affilee pour une
        /// meme carte : la carte n'est plus proposee comme jouable/activable jusqu'au tour suivant
        /// (le joueur ou l'IA ne peut plus reessayer indefiniment la meme action invalide).
        /// </summary>
        /// <summary>Nombre total de refus par carte (outil de test : banc d'essai).</summary>
        public static readonly Dictionary<SessionCardId, int> FailuresByCard = new Dictionary<SessionCardId, int>();

        public static void ReportFailedTransaction(Transaction tx, string description)
        {
            SessionCardId card = SessionCardId.Invalid;
            FieldInfo f = tx.GetType().GetField("m_SessionCardId", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null)
                card = (SessionCardId)f.GetValue(tx);
            if (!card.IsValid())
                return;
            int total;
            FailuresByCard.TryGetValue(card, out total);
            FailuresByCard[card] = total + 1;
            string key = tx.GetType().Name + "|" + card;
            int n;
            s_FailCounts.TryGetValue(key, out n);
            s_FailCounts[key] = ++n;
            if (n >= 3 && !s_BlockedThisTurn.Contains(card))
            {
                s_BlockedThisTurn.Add(card);
                Debug.Log("[HexOffline] [Garde-fou] " + card + " retiree des options pour ce tour apres " + n + " refus : " + description);
                MarkDirty();
            }
        }

        private static bool IsBlocked(StandaloneSession self, Card card)
        {
            if (s_BlockedTurn != self.m_TotalTurnsTaken)
            {
                s_BlockedTurn = self.m_TotalTurnsTaken;
                s_BlockedThisTurn.Clear();
                s_FailCounts.Clear();
            }
            return s_BlockedThisTurn.Contains(card.m_SessionCardId);
        }

        private static void SafeAddAbilityOptions(StandaloneSession self, Player player, Card card, ResourceId abilityId, List<SessionEventArgs> instances)
        {
            foreach (SessionEventArgs e in instances)
            {
                OptionInstanceSessionEventArgs oi = e as OptionInstanceSessionEventArgs;
                if (oi != null && oi.Id == abilityId)
                    return;
            }
            try
            {
                if (abilityId != Game.Shared.Resources.BuiltInResources.PlayCardAbilityTemplateId && !CostsPayable(self, player, card, abilityId))
                    return;
                AddAbilityOptions(self, player, card, abilityId, instances);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HexOffline] options " + card.m_CardTemplate.m_Name + " / " + abilityId + " : " + ex.Message);
            }
        }

        private static void AddAbilityOptions(StandaloneSession self, Player player, Card card, ResourceId abilityId, List<SessionEventArgs> instances, Game.Shared.Mechanics.Abilities.AbilityInstance abilityInstance = null)
        {
            OptionInstanceSessionEventArgs instance = new OptionInstanceSessionEventArgs
            {
                SessionId = self.m_SessionId,
                Id = abilityId,
                TargetIds = self.GetTargetTemplateIdsForAbility(card, player, abilityId) ?? new List<ResourceId>(),
                MinTargetCounts = self.GetMinimumTargetCountsForAbility(card, player, abilityId, abilityInstance) ?? new List<int>(),
                MaxTargetCounts = self.GetMaximumTargetCountsForAbility(card, player, abilityId, abilityInstance) ?? new List<int>(),
                TargetInstances = new List<SessionEventArgs>()
            };

            Dictionary<int, Dictionary<ResourceId, List<SessionCardId>>> targets =
                self.GetPotentialTargetsForAbility(card, player, abilityId, abilityInstance);
            if (targets != null)
            {
                foreach (KeyValuePair<int, Dictionary<ResourceId, List<SessionCardId>>> byIndex in targets)
                {
                    foreach (KeyValuePair<ResourceId, List<SessionCardId>> byTemplate in byIndex.Value)
                    {
                        instance.TargetInstances.Add(new TargetInstanceSessionEventArgs
                        {
                            SessionId = self.m_SessionId,
                            TargetIndex = byIndex.Key,
                            TargetId = byTemplate.Key,
                            Targets = new List<SessionCardId>(byTemplate.Value),
                            AdditionalTargets = new List<SessionCardId>()
                        });
                    }
                }
            }

            Dictionary<EAbilityCostType, List<sCostInfo>> costs = self.GetPotentialCostsForAbility(card, player, abilityId);
            if (costs != null)
            {
                foreach (KeyValuePair<EAbilityCostType, List<sCostInfo>> byType in costs)
                {
                    if (byType.Value == null)
                        continue;
                    foreach (sCostInfo info in byType.Value)
                    {
                        instance.TargetInstances.Add(new CostInstanceSessionEventArgs
                        {
                            SessionId = self.m_SessionId,
                            Min = info.Min,
                            Max = info.Max,
                            CostType = byType.Key,
                            Targets = info.Targets != null ? new List<SessionCardId>(info.Targets) : new List<SessionCardId>(),
                            TargetTemplateId = info.TargetTemplateId
                        });
                    }
                }
            }

            instances.Add(instance);
        }

        // ------------------------------------------------------------------
        // Options pour des capacites precises (declenchees / en cours de resolution)
        // ------------------------------------------------------------------

        /// <summary>
        /// Remplace les deux StandaloneSession.SendPlayerOptionsFor (stubs). Le moteur les appelle
        /// juste avant AbilityActivationDataRequired / TriggeredAbilityActivationDataRequired :
        /// le client (UI ou IA) lit ensuite cibles/couts dans son cache d'options
        /// (State.GetTargetsFor(carte, capacite)). Sans elles, l'IA envoie des donnees vides,
        /// la transaction echoue (XCostRequirement) et la partie se bloque.
        /// </summary>
        public static bool Server_SendPlayerOptionsFor(StandaloneSession self, Player player, Card card, AbilityTemplate template, Game.Shared.Mechanics.Abilities.AbilityInstance instance)
        {
            return Server_SendPlayerOptionsForList(self, player,
                new List<Card> { card }, new List<AbilityTemplate> { template },
                new List<Game.Shared.Mechanics.Abilities.AbilityInstance> { instance });
        }

        public static bool Server_SendPlayerOptionsForList(StandaloneSession self, Player player, List<Card> cards, List<AbilityTemplate> templates, List<Game.Shared.Mechanics.Abilities.AbilityInstance> instances)
        {
            try
            {
                if (player == null || cards == null || templates == null)
                    return false;
                Dictionary<SessionCardId, PlayerOptionSessionEventArgs> byCard = new Dictionary<SessionCardId, PlayerOptionSessionEventArgs>();
                List<SessionEventArgs> options = new List<SessionEventArgs>();
                for (int i = 0; i < cards.Count && i < templates.Count; i++)
                {
                    Card card = cards[i];
                    AbilityTemplate template = templates[i];
                    if (card == null || template == null)
                        continue;
                    PlayerOptionSessionEventArgs option;
                    if (!byCard.TryGetValue(card.m_SessionCardId, out option))
                    {
                        option = new PlayerOptionSessionEventArgs
                        {
                            SessionId = self.m_SessionId,
                            Card = card.m_SessionCardId,
                            State = ECardUsage.None,
                            Instances = new List<SessionEventArgs>()
                        };
                        byCard[card.m_SessionCardId] = option;
                        options.Add(option);
                    }
                    Game.Shared.Mechanics.Abilities.AbilityInstance instance = (instances != null && i < instances.Count) ? instances[i] : null;
                    AddAbilityOptions(self, player, card, template.AbilityTemplateId, option.Instances, instance);
                }
                SendOptionList(self, player, options, true);
                s_Dirty = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[HexOffline] SendPlayerOptionsFor failed: " + e);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Mises a jour d'etat
        // ------------------------------------------------------------------

        public static bool Server_SendPlayerInformationUpdates(StandaloneSession self, ETurnPhases phase)
        {
            BroadcastState(self);
            return true;
        }

        /// <summary>
        /// Remplace StandaloneSession.SendRevealedCards (stub). Appele juste avant CardsRevealed :
        /// les cartes deviennent visibles pour ce joueur tant qu'elles restent dans la meme zone ;
        /// le flush d'etat qui precede l'evenement leur envoie alors leur contenu complet.
        /// </summary>
        public static bool Server_SendRevealedCards(StandaloneSession self, Player player, List<SessionCardId> cards)
        {
            if (player == null || cards == null)
                return false;
            foreach (SessionCardId id in cards)
            {
                Card card = self.ResourceCache.GetCard(id);
                if (card != null)
                    s_Revealed[id + "|" + player.m_PlayerId] = card.m_CurrentCardCollection;
            }
            return true;
        }

        /// <summary>
        /// Ce que le joueur a le droit de voir. Le client attend les cartes cachees sous forme
        /// "Nulling" (CardRepresentation.Null) : la pioche de chacun, la main adverse, le souterrain
        /// adverse... Une carte de pioche non nulle est affichee FACE VISIBLE sur la pioche
        /// (BattleAnimationUpdateDeckView) : c'etait la carte visible sur les decks.
        /// </summary>
        private static bool IsVisibleTo(Card card, Player viewer)
        {
            string key = card.m_SessionCardId + "|" + viewer.m_PlayerId;
            ECardCollections revealedIn;
            if (s_Revealed.TryGetValue(key, out revealedIn))
            {
                if (revealedIn == card.m_CurrentCardCollection)
                    return true;
                s_Revealed.Remove(key);
            }
            bool mine = card.m_ControllingPlayer != null && card.m_ControllingPlayer.m_PlayerId == viewer.m_PlayerId;
            ECardCollections c = card.m_CurrentCardCollection;
            if ((c & ECardCollections.Deck) != 0)
                return false;
            if ((c & (ECardCollections.Hand | ECardCollections.Underground | ECardCollections.Choosing)) != 0)
                return mine;
            return true;
        }

        /// <summary>Envoie les deltas d'etat (joueurs puis cartes). Retourne vrai si quelque chose est parti.</summary>
        public static bool BroadcastState(StandaloneSession self)
        {
            bool sent = false;
            foreach (Player player in self.GetAllPlayers())
            {
                if (ReferenceEquals(player, null))
                    continue;
                string sig = PlayerSig(player);
                string old;
                if (!s_PlayerSig.TryGetValue(player.m_PlayerId, out old) || old != sig)
                {
                    s_PlayerSig[player.m_PlayerId] = sig;
                    self.DispatchSessionEvent(BuildPlayerUpdate(self, player));
                    sent = true;
                }
            }
            List<Player> viewers = self.GetAllPlayers();
            foreach (Card card in self.GetAllCards())
            {
                if (card == null || !card.m_SessionCardId.IsValid())
                    continue;
                string full = null;
                foreach (Player viewer in viewers)
                {
                    if (viewer == null)
                        continue;
                    bool visible = IsVisibleTo(card, viewer);
                    string sig;
                    if (visible)
                    {
                        if (full == null) full = CardSig(card);
                        sig = "V|" + full;
                    }
                    else
                    {
                        sig = "N|" + (int)card.m_CurrentCardCollection + "|" + (card.m_ControllingPlayer != null ? card.m_ControllingPlayer.m_PlayerId.ToString() : "?") + "|" + (int)card.CurrentCardState;
                    }
                    string key = card.m_SessionCardId + "|" + viewer.m_PlayerId;
                    string old;
                    if (s_CardSig.TryGetValue(key, out old) && old == sig)
                        continue;
                    s_CardSig[key] = sig;
                    CardUpdatedSessionEventArgs e = BuildCardUpdate(self, card);
                    if (!visible)
                        e.Nulling = true;
                    Dispatch(self, viewer, e);
                    sent = true;
                }
            }
            return sent;
        }

        private static PlayerUpdatedSessionEventArgs BuildPlayerUpdate(StandaloneSession self, Player player)
        {
            PlayerUpdatedSessionEventArgs e = new PlayerUpdatedSessionEventArgs();
            e.SessionId = self.m_SessionId;
            e.PlayerId = player.m_PlayerId;
            e.Health = (player.m_ChampionCard != null) ? player.m_ChampionCard.CurrentDefenseValue : 0;
            e.Charges = player.m_ChargePoints;
            e.Resources = player.m_CurrentResourcePool;
            e.TotalResources = player.m_TotalResourcePool;
            e.SpellPoints = player.m_SpellPoints;
            e.TurnNumber = self.m_TotalTurnsTaken;
            e.RemainingTime = TimeSpan.FromMinutes(10.0);
            e.DeckSleeveId = player.m_DeckSleeveId;
            e.MaxHandSize = player.CalculateMaximumHandSize();
            e.CanSeeEnemyHand = false;
            e.CanSeeEnemyUnderground = false;
            e.ChampionId = (player.m_ChampionCard != null) ? player.m_ChampionCard.m_SessionCardId : SessionCardId.Invalid;
            e.Thresholds = new List<ECardShards>(player.m_ResourceThresholds.Keys);
            e.ThresholdValues = new List<int>(player.m_ResourceThresholds.Values);
            return e;
        }

        private static CardUpdatedSessionEventArgs BuildCardUpdate(StandaloneSession self, Card card)
        {
            CardUpdatedSessionEventArgs e = new CardUpdatedSessionEventArgs();
            e.SessionId = self.m_SessionId;
            e.PlayerId = (card.m_ControllingPlayer != null) ? card.m_ControllingPlayer.m_PlayerId : UID.Invalid;
            e.Controller = e.PlayerId;
            e.SessionCardId = card.m_SessionCardId;
            e.Cost = card.CastingCost;
            e.Attack = card.CurrentAttackValue;
            e.Defense = card.CurrentDefenseValue;
            e.Armor = card.GetCardContext().Get(IntAttrs.Armor);
            e.CurrentArmor = e.Armor;
            e.Rage = card.GetCardContext().Get(IntAttrs.Rage);
            e.DMult = card.GetCardContext().Get(IntAttrs.DamageMultiplier, 1);
            e.CDMult = card.GetCardContext().Get(IntAttrs.CombatDamageMultiplier, 1);
            e.MinConst = card.GetCardContext().Get(IntAttrs.MinConstructedDeckSize);
            e.MinLimited = card.GetCardContext().Get(IntAttrs.MinLimitedDeckSize);
            e.Tunneling = card.GetCardContext().Get(IntAttrs.Tunneling);
            e.Lethal = card.GetCardContext().GetBool(IntAttrs.Lethal);
            e.Feral = card.GetCardContext().GetBool(IntAttrs.Feral);
            e.DamageShield = card.HasDamageShield;
            e.ExtendedArt = card.m_IsExtended;
            e.SubType = card.CurrentSubtype;
            e.Id = card.m_CardTemplate.m_Id;
            e.OrigTemplate = card.m_OriginalCardTemplateId;
            e.AffectedId = card.m_AffectedId;
            e.Type = card.CurrentType;
            e.State = card.CurrentCardState;
            e.AICardStates = EAICardStates.None;
            e.Attributes = card.CurrentAttributeFlags;
            e.Collection = card.m_CurrentCardCollection;
            e.Abilities = new List<ResourceId>(card.CurrentAbilities.ToList());
            e.Escalation = card.EscalationCount;
            e.Gems = card.ActiveGem;

            // Attributs que le client doit connaitre (NotifyClient) : Indexed (cartes "choisissez-en
            // une"), MustAttack/MustBlock, Spectral, Marked... Sans Indexed, l'IA client joue la carte
            // avec Index=-1 et le moteur attend un choix qui ne vient jamais (blocage du banc).
            e.IntegerVariables = (card.CardIntegerVariables != null)
                ? new Dictionary<string, int>(card.CardIntegerVariables) : new Dictionary<string, int>();
            e.IntAttributes = new Dictionary<string, int>();
            e.StringAttrs = new Dictionary<string, string>();
            FillClientAttributes(card.GetCardContext(), e.IntAttributes, e.StringAttrs);
            e.ActivationCostModifiers = SafeDict(delegate { return card.GetActivationCostModifiers(); });
            e.ChargePointCostModifiers = SafeDict(delegate { return card.GetChargePointCostModifiers(); });
            e.SpellPointCostModifiers = SafeDict(delegate { return card.GetSpellPointCostModifiers(); });
            e.UsesPerGameCounts = SafeDict(delegate { return card.GetUsesPerGameCounts(); });
            e.CooldownCounts = SafeDict(delegate { return card.GetCooldownCounts(); });
            e.CounterTemplates = new List<ResourceId>();
            e.CounterCounts = new List<int>();
            e.RelatedCards = new List<SessionCardId>();
            e.AffectingAbilities = new List<ResourceId>();
            e.ThresholdList = new List<ECardShards>();
            e.Nulling = false;

            if (card.Counters != null)
            {
                foreach (KeyValuePair<CardCounterTemplate, int> counter in card.Counters)
                {
                    e.CounterTemplates.Add(counter.Key.CardCounterId);
                    e.CounterCounts.Add(counter.Value);
                }
            }
            if (card.Thresholds != null)
            {
                foreach (CardThreshold threshold in card.Thresholds)
                {
                    for (int i = 0; i < threshold.m_ThresholdColorRequirement; i++)
                        e.ThresholdList.Add(threshold.m_ColorFlags);
                }
            }
            return e;
        }

        private delegate Dictionary<ResourceId, int> DictGetter();

        private static Dictionary<ResourceId, int> SafeDict(DictGetter getter)
        {
            try
            {
                Dictionary<ResourceId, int> d = getter();
                if (d != null)
                    return d;
            }
            catch (Exception) { }
            return new Dictionary<ResourceId, int>();
        }

        private static System.Reflection.FieldInfo s_TacInts =
            typeof(TAC).GetField("ints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static System.Reflection.FieldInfo s_TacStrings =
            typeof(TAC).GetField("strings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        /// <summary>Copie les attributs marques NotifyClient de la TAC de la carte (cles = nom).</summary>
        private static void FillClientAttributes(TAC tac, Dictionary<string, int> ints, Dictionary<string, string> strings)
        {
            if (tac == null)
                return;
            try
            {
                Dictionary<IntAttrs, int> srcInts = (s_TacInts != null) ? s_TacInts.GetValue(tac) as Dictionary<IntAttrs, int> : null;
                if (srcInts != null)
                    foreach (KeyValuePair<IntAttrs, int> kv in srcInts)
                        if (kv.Key != null && kv.Key.NotifyClient)
                            ints[kv.Key.name] = kv.Value;
                Dictionary<StringAttrs, string> srcStrings = (s_TacStrings != null) ? s_TacStrings.GetValue(tac) as Dictionary<StringAttrs, string> : null;
                if (srcStrings != null)
                    foreach (KeyValuePair<StringAttrs, string> kv in srcStrings)
                        if (kv.Key != null && kv.Key.NotifyClient)
                            strings[kv.Key.name] = kv.Value;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HexOffline] FillClientAttributes: " + ex.Message);
            }
        }

        private static void Log(string message)
        {
            Debug.Log("[HexOffline] " + message);
        }
    }
}
