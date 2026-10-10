using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Game.Client;
using Game.Shared;
using HarmonyLib;
using Game.Shared.Mechanics;

namespace HexRevival
{
    /// <summary>
    /// BepInEx 5 entry point of the HexRevival mod.
    ///
    /// Replaces the Mono.Cecil patcher (tools/HexRevival.Patcher): the same
    /// redirections are applied at runtime through Harmony, and the game installation stays
    /// untouched (BepInEx only adds files next to it).
    ///
    /// The logic itself is unchanged and shared with the old plugin:
    /// mods/HexRevival/src/HexRevival/{Bootstrap,Patches,Server,Repl,Test}.cs.
    /// </summary>
    [BepInPlugin("hexrevival", "HexRevival", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        /// <summary>
        /// The mod used to be called "HexOffline" (1.0): takes over its files in the game folder
        /// (decks, champions, settings) under the new name, never overwriting an existing file.
        /// </summary>
        private static void MigrateOldName()
        {
            try
            {
                string root = System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../");
                foreach (string f in System.IO.Directory.GetFiles(root, "HexOffline*"))
                {
                    string name = System.IO.Path.GetFileName(f);
                    string dest = System.IO.Path.Combine(root, "HexRevival" + name.Substring("HexOffline".Length));
                    if (System.IO.File.Exists(dest)) continue;
                    try { System.IO.File.Move(f, dest); } catch (Exception e) { Log.LogWarning("Migration " + name + " : " + e.Message); }
                }
            }
            catch { }
        }

        private void Awake()
        {
            Log = Logger;
            MigrateOldName();
            Harmony harmony = new Harmony("hexrevival");
            int ok = 0, failed = 0;

            Action<string, Action> apply = (label, patch) =>
            {
                try { patch(); ok++; }
                catch (Exception e) { failed++; Log.LogError("Patch KO " + label + " : " + e); }
            };

            Type standaloneSession = typeof(StandaloneSession);

            // Equivalent of the Cecil injection at the start of Main.FinializeInit:
            // config.ini has already been read at this point, so standalone mode can be forced.
            apply("Main.FinializeInit", () => Prefix(harmony, typeof(Main), "FinializeInit", Type.EmptyTypes, "Main_FinializeInit"));

            apply("Steamy.IsRunningSteam", () => Prefix(harmony, typeof(Steamy), "IsRunningSteam", Type.EmptyTypes, "Steamy_IsRunningSteam"));
            apply("PlayerProfile.Login", () => Prefix(harmony, typeof(PlayerProfile), "Login",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, object>).MakeByRefType() },
                "PlayerProfile_Login"));
            apply("StandaloneGameClient.SendStartSession (Practice)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartSession",
                null, "StandaloneGameClient_SendStartSession"));
            apply("Session.AddPlayer (player deck)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(StandaloneSession), "AddPlayer", new[] { typeof(Player) });
                if (m == null) throw new MissingMethodException("AddPlayer");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Session_AddPlayer"));
            });
            // The local AI has an AIPlayer-type id: its "server"-side copy (RemotePlayer)
            // is therefore not recognized as an AI (IsAI tests ServiceAI) and received the
            // player-side deck of the encounter. We recognize it as an AI.
            apply("Player.IsAI (local AI)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Player), "IsAI", Type.EmptyTypes);
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Player_IsAI"));
            });
            // Faster startup: the intro video (HEX Entertainment logo) is skipped
            // automatically, as if the player pressed Space.
            apply("LevelLoader.Update (skip intro)", () => Prefix(harmony, typeof(LevelLoader), "Update", Type.EmptyTypes, "LevelLoader_SkipIntro"));
            apply("StandaloneGameClient.SendStartEncounter", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartEncounter",
                new[] { typeof(string), typeof(SessionStateEncounterData) }, "StandaloneGameClient_SendStartEncounter"));

            apply("UILandingPageContext.OnClickChallengeAI", () => Prefix(harmony, typeof(UILandingPageContext), "OnClickChallengeAI", Type.EmptyTypes, "UILandingPage_OnClickChallengeAI"));
            apply("UILandingPageContext.OnClickContinue", () => Prefix(harmony, typeof(UILandingPageContext), "OnClickContinue", Type.EmptyTypes, "UILandingPage_OnClickContinue"));
            apply("UILandingPageViewModel.OnFrostRingButtonClicked", () => Prefix(harmony, typeof(UILandingPageViewModel), "OnFrostRingButtonClicked", Type.EmptyTypes, "UILandingPageViewModel_OnFrostRingButtonClicked"));

            // Login screen: offline, no credentials are needed (button always enabled,
            // empty name => "Offline" profile).
            apply("UILoginDialogViewModel.CheckForLoginButtonEnable", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(UILoginDialogViewModel), "CheckForLoginButtonEnable");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Login_AlwaysEnabled"));
            });

            // "Authoritative server" layer (StandaloneSession stubs).
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

            // Known UI NREs: the exception is swallowed (replaces WrapVoidInTryCatch).
            apply("GoChampionAbilityButton.SetStateFromAbilityTemplate", () => Finalizer(harmony, typeof(GoChampionAbilityButton), "SetStateFromAbilityTemplate"));
            apply("GoHUDPlayer.UpdateAbilityButtons", () => Finalizer(harmony, typeof(GoHUDPlayer), "UpdateAbilityButtons"));

            // Diagnostics: every rejected transaction (except priority passes) is logged with its cards.
            apply("Transaction.Validate (log)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Mechanics.Transactions.Transaction), "Validate");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "LogFailedTransaction"));
            });

            // Offline solo: no loss on the chess clock or for inactivity (45 min against the AI).
            apply("Player.IsChessTimerExpired", () => Prefix(harmony, typeof(Player), "IsChessTimerExpired", Type.EmptyTypes, "NeverExpired"));
            apply("Player.IsInactiveTimerExpired", () => Prefix(harmony, typeof(Player), "IsInactiveTimerExpired", Type.EmptyTypes, "NeverExpired"));

            // Network services (SFS) without a connection: Produce() calls pub(m_Publish) with null -> NRE.
            // Worst case: AuthoritativeSessionBase.EndGame calls MonitorService.SendBIInfo,
            // and the exception aborts the end of the game on every tick (champion dead but endless game).
            apply("Service<T>.Produce (offline)", () =>
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
                Log.LogInfo("Service.Produce disabled on " + n + " service(s)");
            });

            // Network probe: logs what the client tries to send to the server (HexRevival.net.log).
            apply("ClientConnection.SendCommand (probe)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Network.SFS.ClientConnection), "SendCommand",
                    new[] { typeof(byte[]), typeof(string), typeof(string), typeof(long), typeof(Game.Shared.Network.MessageHandler), typeof(System.Collections.Generic.IDictionary<string, object>) });
                if (m == null) throw new MissingMethodException("ClientConnection.SendCommand");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "ClientConnection_SendCommand"));
            });

            Log.LogInfo("HexRevival loaded: " + ok + " patches OK, " + failed + " failed.");
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
    /// Harmony prefixes. Convention: returning false = the original is not executed,
    /// the existing replacement method (Patches / Server) runs instead.
    /// Arguments are taken by position (__0, __1…) so as not to depend on names.
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

        /// <summary>Why the engine refuses to play the card (sub-conditions of Session.CanPlayCard).</summary>
        private static string PlayDiag(object req, Session session, Player p)
        {
            Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement cr = req as Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement;
            if (cr == null) return "";
            try
            {
                System.Reflection.FieldInfo f = cr.GetType().GetField("m_SessionCardId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                SessionCardId id = (SessionCardId)f.GetValue(cr);
                Card c = session.ResourceCache.GetCard(id);
                if (c == null) return " (card not found)";
                Player prio = session.GetPriorityPlayer();
                Player act = session.GetActivePlayer();
                System.Text.StringBuilder sb = new System.Text.StringBuilder(" (");
                sb.Append("zone=").Append(c.m_CurrentCardCollection);
                sb.Append(" prio=").Append(prio != null ? prio.m_PlayerId.ToString() : "-");
                sb.Append(" active=").Append(act != null ? act.m_PlayerId.ToString() : "-");
                sb.Append(" phase=").Append(session.CurrentTurnPhase);
                sb.Append(" chain=").Append(session.Chain.IsEmpty ? "empty" : "not empty");
                sb.Append(" quick=").Append(c.IsQuickSpeed());
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

        /// <summary>First unmet requirement (re-evaluated: IsValid has no side effect).</summary>
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
                return "(none on re-evaluation)";
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
            line = "FAILED " + FailedRequirement(__instance, __0) + " | " + line;
            UnityEngine.Debug.Log("[HexRevival] [FailTx] " + line.Replace("\n", " | "));
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../"), Instance.Name("HexRevival.failtx.log")),
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

        /// <summary>Skips sending if the service has no connection (offline mode).</summary>
        public static bool Service_Produce(object __instance, Delegate __0, string __1)
        {
            if (__1 != null)
                return true; // the original already handles m_Connections == null
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

        /// <summary>Trace (offline): direct send on the SFS connection, without a session.</summary>
        public static bool ClientConnection_SendCommand(byte[] eventArgsBytes, string toexc, string routeto, Game.Shared.Network.MessageHandler responseHandler)
        {
            bool local = LocalServer.TryHandle(eventArgsBytes, responseHandler);
            NetTrace.Log((local ? "LOCAL " : "SendCommand ") + toexc + "/" + routeto + " " + NetTrace.Peek(eventArgsBytes), null);
            return !local;
        }

        public static Exception SwallowException(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null && Plugin.Log != null)
                Plugin.Log.LogDebug("Exception swallowed in " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": " + __exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Offline network probe: for each blocked send, records the service, the calling method
    /// (stack) and the closure content (captured request objects). File HexRevival.net.log.
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
                if (count >= 3) return; // no flooding for periodic sends
                if (pub != null && pub.Target != null)
                {
                    foreach (FieldInfo f in pub.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object v = null;
                        try { v = f.GetValue(pub.Target); } catch { }
                        sb.Append("\n    ").Append(f.Name).Append(" = ").Append(Describe(v, 0));
                    }
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetFullPath(UnityEngine.Application.dataPath + "/../"), "HexRevival.net.log"),
                    sb.ToString() + "\r\n");
            }
            catch { }
        }

        /// <summary>Readable start of a serialized message (the type name often appears in it).</summary>
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
