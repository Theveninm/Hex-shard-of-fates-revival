using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Game.Client;
using Game.Shared;
using HarmonyLib;
using Game.Shared.Mechanics;

namespace HexOffline
{
    /// <summary>
    /// Point d'entrée BepInEx 5 du mod HexOffline.
    ///
    /// Remplace le patcheur Mono.Cecil (tools/HexOffline.Patcher) : les mêmes
    /// redirections sont posées à chaud via Harmony, l'installation du jeu reste
    /// vierge (BepInEx ne fait que déposer des fichiers à côté).
    ///
    /// La logique elle-même est inchangée et partagée avec l'ancien plugin :
    /// mods/HexOffline/src/HexOffline/{Bootstrap,Patches,Server,Repl,Test}.cs.
    /// </summary>
    [BepInPlugin("hexoffline", "HexOffline", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony("hexoffline");
            int ok = 0, failed = 0;

            Action<string, Action> apply = (label, patch) =>
            {
                try { patch(); ok++; }
                catch (Exception e) { failed++; Log.LogError("Patch KO " + label + " : " + e); }
            };

            Type standaloneSession = typeof(StandaloneSession);

            // Equivalent de l'injection Cecil au début de Main.FinializeInit :
            // config.ini est déjà lu à ce stade, on peut forcer le mode standalone.
            apply("Main.FinializeInit", () => Prefix(harmony, typeof(Main), "FinializeInit", Type.EmptyTypes, "Main_FinializeInit"));

            apply("Steamy.IsRunningSteam", () => Prefix(harmony, typeof(Steamy), "IsRunningSteam", Type.EmptyTypes, "Steamy_IsRunningSteam"));
            apply("PlayerProfile.Login", () => Prefix(harmony, typeof(PlayerProfile), "Login",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, object>).MakeByRefType() },
                "PlayerProfile_Login"));
            apply("StandaloneGameClient.SendStartSession (Practice)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartSession",
                null, "StandaloneGameClient_SendStartSession"));
            apply("Session.AddPlayer (deck du joueur)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(StandaloneSession), "AddPlayer", new[] { typeof(Player) });
                if (m == null) throw new MissingMethodException("AddPlayer");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Session_AddPlayer"));
            });
            // L'IA locale a un identifiant de type AIPlayer : sa copie cote "serveur" (RemotePlayer)
            // n'est donc pas reconnue comme IA (IsAI teste ServiceAI) et recevait le deck du cote
            // joueur de la rencontre. On la reconnait comme IA.
            apply("Player.IsAI (IA locale)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Player), "IsAI", Type.EmptyTypes);
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Player_IsAI"));
            });
            // Demarrage plus rapide : la video d'intro (logo HEX Entertainment) est passee
            // automatiquement, comme si le joueur appuyait sur Espace.
            apply("LevelLoader.Update (passer l'intro)", () => Prefix(harmony, typeof(LevelLoader), "Update", Type.EmptyTypes, "LevelLoader_SkipIntro"));
            apply("StandaloneGameClient.SendStartEncounter", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartEncounter",
                new[] { typeof(string), typeof(SessionStateEncounterData) }, "StandaloneGameClient_SendStartEncounter"));

            apply("UILandingPageContext.OnClickChallengeAI", () => Prefix(harmony, typeof(UILandingPageContext), "OnClickChallengeAI", Type.EmptyTypes, "UILandingPage_OnClickChallengeAI"));
            apply("UILandingPageContext.OnClickContinue", () => Prefix(harmony, typeof(UILandingPageContext), "OnClickContinue", Type.EmptyTypes, "UILandingPage_OnClickContinue"));
            apply("UILandingPageViewModel.OnFrostRingButtonClicked", () => Prefix(harmony, typeof(UILandingPageViewModel), "OnFrostRingButtonClicked", Type.EmptyTypes, "UILandingPageViewModel_OnFrostRingButtonClicked"));

            // Ecran de login : en offline, aucun identifiant n'est requis (bouton toujours actif,
            // nom vide => profil "Offline").
            apply("UILoginDialogViewModel.CheckForLoginButtonEnable", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(UILoginDialogViewModel), "CheckForLoginButtonEnable");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Login_AlwaysEnabled"));
            });

            // Couche "serveur autoritatif" (stubs de StandaloneSession).
            apply("StandaloneSession.SendPlayerOptions", () => Prefix(harmony, standaloneSession, "SendPlayerOptions", new[] { typeof(Player) }, "Session_SendPlayerOptions"));
            apply("StandaloneSession.Update", () =>
            {
                MethodInfo m = AccessTools.Method(standaloneSession, "Update", Type.EmptyTypes);
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "Session_Update"), postfix: new HarmonyMethod(typeof(Hooks), "Session_UpdatePost"));
            });
            apply("StandaloneSession.SubmitTransaction", () => Prefix(harmony, standaloneSession, "SubmitTransaction", new[] { typeof(Game.Shared.Mechanics.Transactions.Transaction) }, "Session_SubmitTransaction"));
            apply("StandaloneSession.DispatchSessionEvent(args)", () => Prefix(harmony, standaloneSession, "DispatchSessionEvent", new[] { typeof(SessionEventArgs) }, "Session_DispatchSessionEvent"));
            apply("StandaloneSession.DispatchSessionEvent(player,args)", () => Prefix(harmony, standaloneSession, "DispatchSessionEvent", new[] { typeof(Player), typeof(SessionEventArgs) }, "Session_DispatchSessionEventPlayer"));
            apply("StandaloneSession.SendPlayerInformationUpdates", () => Prefix(harmony, standaloneSession, "SendPlayerInformationUpdates", new[] { typeof(Game.Shared.Mechanics.ETurnPhases) }, "Session_SendPlayerInformationUpdates"));
            apply("StandaloneSession.SendPlayerOptionsFor(card)", () => Prefix(harmony, standaloneSession, "SendPlayerOptionsFor",
                new[] { typeof(Player), typeof(Card), typeof(Reckoning.Game.AbilityTemplate), typeof(Game.Shared.Mechanics.Abilities.AbilityInstance) }, "Session_SendPlayerOptionsFor"));
            apply("StandaloneSession.SendPlayerOptionsFor(list)", () => Prefix(harmony, standaloneSession, "SendPlayerOptionsFor",
                new[] { typeof(Player), typeof(List<Card>), typeof(List<Reckoning.Game.AbilityTemplate>), typeof(List<Game.Shared.Mechanics.Abilities.AbilityInstance>) }, "Session_SendPlayerOptionsForList"));
            apply("StandaloneSession.SendRevealedCards", () => Prefix(harmony, standaloneSession, "SendRevealedCards", new[] { typeof(Player), typeof(List<SessionCardId>) }, "Session_SendRevealedCards"));

            // NRE d'UI connus : on avale l'exception (remplace WrapVoidInTryCatch).
            apply("GoChampionAbilityButton.SetStateFromAbilityTemplate", () => Finalizer(harmony, typeof(GoChampionAbilityButton), "SetStateFromAbilityTemplate"));
            apply("GoHUDPlayer.UpdateAbilityButtons", () => Finalizer(harmony, typeof(GoHUDPlayer), "UpdateAbilityButtons"));

            // Diagnostic : toute transaction refusee (hors passe de priorite) est journalisee avec ses cartes.
            apply("Transaction.Validate (journal)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Mechanics.Transactions.Transaction), "Validate");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "LogFailedTransaction"));
            });

            // Solo hors ligne : pas de defaite au chrono ni pour inactivite (45 min contre l'IA).
            apply("Player.IsChessTimerExpired", () => Prefix(harmony, typeof(Player), "IsChessTimerExpired", Type.EmptyTypes, "NeverExpired"));
            apply("Player.IsInactiveTimerExpired", () => Prefix(harmony, typeof(Player), "IsInactiveTimerExpired", Type.EmptyTypes, "NeverExpired"));

            // Services reseau (SFS) sans connexion : Produce() appelle pub(m_Publish) avec null -> NRE.
            // Le plus grave : AuthoritativeSessionBase.EndGame appelle MonitorService.SendBIInfo,
            // l'exception avorte la fin de partie a chaque tick (champion mort mais partie sans fin).
            apply("Service<T>.Produce (hors ligne)", () =>
            {
                int n = 0;
                foreach (Type t in typeof(Game.Client.Network.Monitor.MonitorService).Assembly.GetTypes())
                {
                    Type b = t.BaseType;
                    if (b == null || !b.IsGenericType || b.GetGenericTypeDefinition() != typeof(Game.Shared.Network.SFS.Service<>))
                        continue;
                    MethodInfo m = AccessTools.Method(b, "Produce");
                    if (m == null) continue;
                    try { harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "Service_Produce")); n++; }
                    catch (Exception e) { Log.LogWarning("Produce " + t.Name + " : " + e.Message); }
                }
                Log.LogInfo("Service.Produce neutralise sur " + n + " service(s)");
            });

            // Sonde reseau : journalise ce que le client tente d'envoyer au serveur (HexOffline.net.log).
            apply("ClientConnection.SendCommand (sonde)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Network.SFS.ClientConnection), "SendCommand",
                    new[] { typeof(byte[]), typeof(string), typeof(string), typeof(long), typeof(Game.Shared.Network.MessageHandler), typeof(System.Collections.Generic.IDictionary<string, object>) });
                if (m == null) throw new MissingMethodException("ClientConnection.SendCommand");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "ClientConnection_SendCommand"));
            });

            Log.LogInfo("HexOffline charge : " + ok + " patchs OK, " + failed + " en echec.");
        }

        private static void Prefix(Harmony h, Type type, string method, Type[] args, string hook)
        {
            MethodInfo target = AccessTools.Method(type, method, args);
            if (target == null)
                throw new MissingMethodException(type.FullName + "." + method);
            h.Patch(target, prefix: new HarmonyMethod(typeof(Hooks), hook));
        }

        private static void Finalizer(Harmony h, Type type, string method)
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null)
                throw new MissingMethodException(type.FullName + "." + method);
            h.Patch(target, finalizer: new HarmonyMethod(typeof(Hooks), "SwallowException"));
        }
    }

    /// <summary>
    /// Prefixes Harmony. Convention : retourner false = l'original n'est pas exécuté,
    /// on exécute à la place la méthode de remplacement existante (Patches / Server).
    /// Les arguments sont pris par position (__0, __1…) pour ne pas dépendre des noms.
    /// </summary>
    internal static class Hooks
    {
        private static bool s_Initialized;

        public static void Main_FinializeInit()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            Bootstrap.Init();
        }

        public static bool Steamy_IsRunningSteam(ref bool __result)
        {
            __result = Patches.Steamy_IsRunningSteam();
            return false;
        }

        public static bool PlayerProfile_Login(PlayerProfile __instance, string __0, string __1, string __2,
            ref string __3, ref Dictionary<string, object> __4, ref bool __result)
        {
            __result = Patches.PlayerProfile_Login(__instance, __0, __1, __2, ref __3, ref __4);
            return false;
        }

        private static bool s_IntroSkipped;

        public static bool LevelLoader_SkipIntro(LevelLoader __instance)
        {
            if (s_IntroSkipped) return true;
            s_IntroSkipped = true;
            try
            {
                if (__instance.m_mediaPlayer != null && __instance.m_mediaPlayer.Control != null)
                    __instance.m_mediaPlayer.Control.Stop();
                MethodInfo m = typeof(LevelLoader).GetMethod("StartLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                __instance.StartCoroutine((System.Collections.IEnumerator)m.Invoke(__instance, null));
                Plugin.Log.LogInfo("Intro passee");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Intro : " + e.Message); }
            return false;
        }

        public static void Player_IsAI(Player __instance, ref bool __result)
        {
            if (!__result && __instance.m_PlayerId.GetUIDType() == UID.Type.AIPlayer) __result = true;
        }

        public static void Session_AddPlayer(Session __instance, Player __0)
        {
            Patches.AuthoritativeSession_AddPlayer(__instance, __0);
        }

        public static bool StandaloneGameClient_SendStartSession(StandaloneGameClient __instance, string __0, int __3, int __5)
        {
            return Patches.StandaloneGameClient_SendStartSession(__instance, __0, __3, __5);
        }

        public static bool StandaloneGameClient_SendStartEncounter(StandaloneGameClient __instance, string __0, SessionStateEncounterData __1)
        {
            Patches.StandaloneGameClient_SendStartEncounter(__instance, __0, __1);
            return false;
        }

        public static bool UILandingPage_OnClickChallengeAI(UILandingPageContext __instance)
        {
            Patches.UILandingPage_OnClickChallengeAI(__instance);
            return false;
        }

        public static bool UILandingPage_OnClickContinue(UILandingPageContext __instance)
        {
            Patches.UILandingPage_OnClickContinue(__instance);
            return false;
        }

        public static bool UILandingPageViewModel_OnFrostRingButtonClicked(UILandingPageViewModel __instance)
        {
            Patches.UILandingPageViewModel_OnFrostRingButtonClicked(__instance);
            return false;
        }

        public static void Login_AlwaysEnabled(UILoginDialogViewModel __instance)
        {
            if (__instance.Context != null)
                __instance.Context.LoginButtonEnabled = true;
        }

        public static bool Session_SendPlayerOptions(StandaloneSession __instance, Player __0, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptions(__instance, __0);
            return false;
        }

        public static void Session_Update(StandaloneSession __instance)
        {
            Server.Pump(__instance);
            Server.BeginTick();
        }

        public static void Session_UpdatePost(StandaloneSession __instance)
        {
            Server.EndTick();
            Server.Flush(__instance);
        }

        public static bool Session_SubmitTransaction(StandaloneSession __instance, Game.Shared.Mechanics.Transactions.Transaction __0, ref bool __result)
        {
            __result = Server.Server_SubmitTransaction(__instance, __0);
            return false;
        }

        public static bool Session_DispatchSessionEvent(StandaloneSession __instance, SessionEventArgs __0)
        {
            Server.Server_DispatchSessionEvent(__instance, __0);
            return false;
        }

        public static bool Session_DispatchSessionEventPlayer(StandaloneSession __instance, Player __0, SessionEventArgs __1)
        {
            Server.Server_DispatchSessionEventPlayer(__instance, __0, __1);
            return false;
        }

        public static bool Session_SendPlayerInformationUpdates(StandaloneSession __instance, Game.Shared.Mechanics.ETurnPhases __0, ref bool __result)
        {
            __result = Server.Server_SendPlayerInformationUpdates(__instance, __0);
            return false;
        }

        public static bool Session_SendPlayerOptionsFor(StandaloneSession __instance, Player __0, Card __1, Reckoning.Game.AbilityTemplate __2, Game.Shared.Mechanics.Abilities.AbilityInstance __3, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptionsFor(__instance, __0, __1, __2, __3);
            return false;
        }

        public static bool Session_SendPlayerOptionsForList(StandaloneSession __instance, Player __0, List<Card> __1, List<Reckoning.Game.AbilityTemplate> __2, List<Game.Shared.Mechanics.Abilities.AbilityInstance> __3, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptionsForList(__instance, __0, __1, __2, __3);
            return false;
        }

        public static bool Session_SendRevealedCards(StandaloneSession __instance, Player __0, List<SessionCardId> __1, ref bool __result)
        {
            __result = Server.Server_SendRevealedCards(__instance, __0, __1);
            return false;
        }

        /// <summary>Pourquoi le moteur refuse de jouer la carte (sous-conditions de Session.CanPlayCard).</summary>
        private static string PlayDiag(object req, Session session, Player p)
        {
            Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement cr = req as Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement;
            if (cr == null) return "";
            try
            {
                System.Reflection.FieldInfo f = cr.GetType().GetField("m_SessionCardId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                SessionCardId id = (SessionCardId)f.GetValue(cr);
                Card c = session.ResourceCache.GetCard(id);
                if (c == null) return " (carte introuvable)";
                Player prio = session.GetPriorityPlayer();
                Player act = session.GetActivePlayer();
                System.Text.StringBuilder sb = new System.Text.StringBuilder(" (");
                sb.Append("zone=").Append(c.m_CurrentCardCollection);
                sb.Append(" prio=").Append(prio != null ? prio.m_PlayerId.ToString() : "-");
                sb.Append(" actif=").Append(act != null ? act.m_PlayerId.ToString() : "-");
                sb.Append(" phase=").Append(session.CurrentTurnPhase);
                sb.Append(" chaine=").Append(session.Chain.IsEmpty ? "vide" : "non vide");
                sb.Append(" rapide=").Append(c.IsQuickSpeed());
                sb.Append(" cout=").Append(c.CastingCost).Append("/res=").Append(p.m_CurrentResourcePool);
                sb.Append(" seuils=").Append(p.HasResourceThresholds(c.Thresholds));
                sb.Append(" condition=").Append(c.m_CardTemplate.m_PlayCondition == null ? "-" : c.m_CardTemplate.m_PlayCondition.IsValid(c, session).ToString());
                if (c.IsSpell())
                {
                    System.Reflection.MethodInfo m = typeof(Session).GetMethod("HasEnoughTargetsToCast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (m != null) sb.Append(" ciblesSuffisantes=").Append(m.Invoke(session, new object[] { c, p }));
                }
                sb.Append(" mobilize=").Append(session.CheckMobilize(c, p));
                sb.Append(")");
                return sb.ToString();
            }
            catch (Exception e) { return " (diag: " + e.Message + ")"; }
        }

        /// <summary>Premiere exigence non satisfaite (reevaluee : IsValid n'a pas d'effet de bord).</summary>
        private static string FailedRequirement(Game.Shared.Mechanics.Transactions.Transaction tx, Session session)
        {
            try
            {
                System.Reflection.FieldInfo f = typeof(Game.Shared.Mechanics.Transactions.Transaction).GetField("m_RequirementList",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Collections.IEnumerable reqs = f != null ? f.GetValue(tx) as System.Collections.IEnumerable : null;
                if (reqs == null) return "?";
                Player p = session.GetPlayer(tx.m_PlayerId);
                foreach (object o in reqs)
                {
                    Game.Shared.Mechanics.Transactions.Requirements.TransactionRequirement r = o as Game.Shared.Mechanics.Transactions.Requirements.TransactionRequirement;
                    if (r != null && !r.IsValid(session, p))
                        return r.GetType().Name + PlayDiag(r, session, p);
                }
                return "(aucune a la reevaluation)";
            }
            catch (Exception e) { return "? " + e.Message; }
        }

        public static void LogFailedTransaction(Game.Shared.Mechanics.Transactions.Transaction __instance, Session __0, bool __result)
        {
            if (__result || !Dev.Enabled || __instance is Game.Shared.Mechanics.Transactions.PassPriorityTransaction)
                return;
            string line;
            try { line = __instance.GetLogLine(__0); }
            catch (Exception e) { line = __instance.GetType().Name + " (GetLogLine: " + e.Message + ")"; }
            line = "ECHEC " + FailedRequirement(__instance, __0) + " | " + line;
            UnityEngine.Debug.Log("[HexOffline] [FailTx] " + line.Replace("\n", " | "));
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../"), Instance.Name("HexOffline.failtx.log")),
                    DateTime.Now.ToString("HH:mm:ss") + " " + line.Replace("\n", " | ") + "\r\n");
            }
            catch { }
            Server.ReportFailedTransaction(__instance, line.Replace("\n", " | "));
        }

        public static bool NeverExpired(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static readonly Dictionary<Type, FieldInfo> s_PublishFields = new Dictionary<Type, FieldInfo>();

        /// <summary>Saute l'envoi si le service n'a aucune connexion (mode hors ligne).</summary>
        public static bool Service_Produce(object __instance, Delegate __0, string __1)
        {
            if (__1 != null)
                return true; // l'original gere deja m_Connections == null
            Type t = __instance.GetType();
            FieldInfo f;
            if (!s_PublishFields.TryGetValue(t, out f))
            {
                f = AccessTools.Field(t, "m_Publish");
                s_PublishFields[t] = f;
            }
            bool send = f == null || f.GetValue(__instance) != null;
            if (!send)
                NetTrace.Log("Produce " + t.Name, __0);
            return send;
        }

        /// <summary>Trace (hors ligne) : envoi direct sur la connexion SFS, sans session.</summary>
        public static bool ClientConnection_SendCommand(byte[] eventArgsBytes, string toexc, string routeto, Game.Shared.Network.MessageHandler responseHandler)
        {
            bool local = LocalServer.TryHandle(eventArgsBytes, responseHandler);
            NetTrace.Log((local ? "LOCAL " : "SendCommand ") + toexc + "/" + routeto + " " + NetTrace.Peek(eventArgsBytes), null);
            return !local;
        }

        public static Exception SwallowException(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null && Plugin.Log != null)
                Plugin.Log.LogDebug("Exception avalee dans " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + " : " + __exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Sonde reseau hors ligne : pour chaque envoi bloque, note le service, la methode appelante
    /// (pile) et le contenu de la fermeture (objets requete captures). Fichier HexOffline.net.log.
    /// </summary>
    public static class NetTrace
    {
        private static readonly System.Collections.Generic.Dictionary<string, int> s_Seen = new System.Collections.Generic.Dictionary<string, int>();

        public static void Log(string what, Delegate pub)
        {
            if (!Dev.Enabled) return;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(what);
                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(2, false);
                sb.Append(" | appel:");
                int n = 0;
                for (int i = 0; i < st.FrameCount && n < 6; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    string dn = m.DeclaringType.FullName ?? m.DeclaringType.ToString();
                    dn = dn.Replace("Game.Shared.Network.", "").Replace("Game.Shared.", "").Replace("Game.Client.", "");
                    if (dn.Contains("ClientConnection") || dn.Contains("NetworkConnection") || dn.StartsWith("Service`") || dn == "NetTrace" || dn.EndsWith("Hooks") || dn.Contains("DMD")) continue;
                    sb.Append(' ').Append(dn).Append('.').Append(m.Name);
                    n++;
                }
                string key = sb.ToString().Substring(9);
                int count;
                s_Seen.TryGetValue(key, out count);
                s_Seen[key] = count + 1;
                if (count >= 3) return; // pas de flood pour les envois periodiques
                if (pub != null && pub.Target != null)
                {
                    foreach (FieldInfo f in pub.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object v = null;
                        try { v = f.GetValue(pub.Target); } catch { }
                        sb.Append("\n    ").Append(f.Name).Append(" = ").Append(Describe(v, 0));
                    }
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../"), "HexOffline.net.log"),
                    sb.ToString() + "\r\n");
            }
            catch { }
        }

        /// <summary>Debut lisible d'un message serialise (le nom du type y figure souvent).</summary>
        public static string Peek(byte[] b)
        {
            if (b == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[" + b.Length + "o] ");
            for (int i = 0; i < b.Length && i < 300; i++)
                sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '.');
            return sb.ToString();
        }

        private static string Describe(object v, int depth)
        {
            if (v == null) return "null";
            Type t = v.GetType();
            if (t.IsPrimitive || v is string || t.IsEnum) return v.ToString();
            if (depth >= 2) return t.Name;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(t.Name).Append(" {");
            int k = 0;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (k++ >= 12) { sb.Append(" ..."); break; }
                object fv = null;
                try { fv = f.GetValue(v); } catch { }
                sb.Append(' ').Append(f.Name).Append('=');
                if (fv is System.Collections.ICollection && !(fv is string))
                    sb.Append(fv.GetType().Name).Append("[").Append(((System.Collections.ICollection)fv).Count).Append("]");
                else sb.Append(Describe(fv, depth + 1));
            }
            return sb.Append(" }").ToString();
        }
    }
}
