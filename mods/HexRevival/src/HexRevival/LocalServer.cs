using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client.Network.Profile;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Network;
using Game.Shared.Network.Profile;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local "server" for non-combat services (profile, decks...).
    ///
    /// The client sends its requests through ClientConnection.SendCommand(DataWrapper bytes, ...); we
    /// decode the DataWrapper (transaction number + arguments), handle the known transactions
    /// and send the response on the next frame, as the generated Dispatch did
    /// (static OnResponse event of the transaction struct + request callback).
    /// </summary>
    public static class LocalServer
    {
        private struct Pending
        {
            public int TransactionId;
            public object Response;
            public MessageHandler Callback;
            /// <summary>Time.realtimeSinceStartup at which the answer is delivered.</summary>
            public float Due;
        }

        /// <summary>
        /// Delay of an opened pack's answer, in seconds, like the real servers' few hundred milliseconds: a pack
        /// dropped into the fountain only starts its camera move and drop animation once the drag is over, and an
        /// answer on the next frame ended the opening before it began (cards scattered, issue #25).
        /// </summary>
        public const float PackAnswerDelay = 1f;

        private static readonly Queue<Pending> s_Pending = new Queue<Pending>();
        private static Dictionary<int, Type> s_TxTypes;

        /// <summary>Called by the ClientConnection.SendCommand hook. True if the request is handled here.</summary>
        public static bool TryHandle(byte[] eventArgsBytes, MessageHandler callback)
        {
            DataWrapper dw;
            object args;
            try
            {
                dw = EncData.Decode(eventArgsBytes) as DataWrapper;
                if (dw == null || dw.Bytes == null) return false;
                args = DataWrapper.FromBytes(dw.Bytes, dw.Comp);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HexRevival] [LocalServer] cannot decode: " + e.Message);
                return false;
            }
            object resp = null;
            try
            {
                resp = Decks.Handle(dw.DataType, args);
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [LocalServer] transaction " + dw.DataType + " (" + (args != null ? args.GetType().Name : "?") + ") : " + e);
            }
            if (resp == null)
            {
                // Goes to the (closed) official servers: the game may wait for an answer that never comes.
                Debug.LogWarning("[HexRevival] [LocalServer] not handled: " + dw.DataType + " " + (args != null ? args.GetType().FullName : "?") + Describe(args));
                return false;
            }
            Debug.Log("[HexRevival] [LocalServer] " + args.GetType().Name + " -> " + resp.GetType().Name);
            OpenCardPackResponse pack = resp as OpenCardPackResponse;
            float delay = pack != null && pack.Error == EOpenCardPackError.Ok ? PackAnswerDelay : 0f;
            lock (s_Pending)
                s_Pending.Enqueue(new Pending { TransactionId = dw.DataType, Response = resp, Callback = callback, Due = Time.realtimeSinceStartup + delay });
            return true;
        }

        /// <summary>A custom request's message type (the generic profile request carries another request inside).</summary>
        private static string Describe(object args)
        {
            try
            {
                if (args == null) return "";
                Type t = args.GetType();
                // The generic profile request: its action as JSON in the envelope.
                FieldInfo env = t.GetField("Envelope");
                byte[] e = env != null ? env.GetValue(args) as byte[] : null;
                if (e != null) { string j = System.Text.Encoding.UTF8.GetString(e); return " " + (j.Length > 160 ? j.Substring(0, 160) : j); }
                PropertyInfo msg = t.GetProperty("Message");
                object m = msg != null ? msg.GetValue(args, null) : null;
                return m != null ? " message " + m.GetType().FullName : "";
            }
            catch { return ""; }
        }

        private static readonly Queue<Action> s_Later = new Queue<Action>();

        /// <summary>Runs an action on the next Pump (main thread), like a network answer.</summary>
        public static void Later(Action a)
        {
            lock (s_Later) s_Later.Enqueue(a);
        }

        /// <summary>
        /// Answers a generated service request (EscrowService, TournamentsService...) like their Dispatch: the
        /// static OnResponse event, then the request's own handler, on the next frame.
        /// </summary>
        public static void Answer(Type tx, object response, Delegate handler)
        {
            Later(() =>
            {
                FieldInfo ev = tx.GetField("OnResponse", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (ev == null)
                    foreach (FieldInfo f in tx.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                        if (typeof(Delegate).IsAssignableFrom(f.FieldType)) { ev = f; break; }
                Delegate d = ev != null ? ev.GetValue(null) as Delegate : null;
                Debug.Log("[HexRevival] " + tx.Name + " answered: listeners " + (d != null ? d.GetInvocationList().Length : 0) + ", handler " + (handler != null));
                if (d != null) foreach (Delegate one in d.GetInvocationList()) one.DynamicInvoke(response);
                if (handler != null) handler.DynamicInvoke(response);
            });
        }

        /// <summary>Delivers pending responses (main thread, one frame after the request, or when due), in order.</summary>
        public static void Pump()
        {
            while (true)
            {
                Action a;
                lock (s_Later)
                {
                    if (s_Later.Count == 0) break;
                    a = s_Later.Dequeue();
                }
                try { a(); }
                catch (Exception e) { Debug.LogError("[HexRevival] [LocalServer] answer: " + e); }
            }
            while (true)
            {
                Pending p;
                lock (s_Pending)
                {
                    // In order: a later answer waits behind a delayed one.
                    if (s_Pending.Count == 0 || s_Pending.Peek().Due > Time.realtimeSinceStartup) return;
                    p = s_Pending.Dequeue();
                }
                try
                {
                    Type tx = TxType(p.TransactionId);
                    if (tx != null)
                    {
                        FieldInfo ev = tx.GetField("OnResponse", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                        Delegate d = ev != null ? ev.GetValue(null) as Delegate : null;
                        if (d != null) d.DynamicInvoke(p.Response);
                    }
                    if (p.Callback != null) p.Callback(p.Response);
                    if (p.Response is OpenCardPackResponse) Packs.OnPackAnswerDelivered();
                }
                catch (Exception e)
                {
                    Debug.LogError("[HexRevival] [LocalServer] delivery " + p.TransactionId + " : " + e);
                }
            }
        }

        /// <summary>Generated transaction struct (e.g. ProfileService+AddNewDeck) by number.</summary>
        private static Type TxType(int id)
        {
            if (s_TxTypes == null)
            {
                s_TxTypes = new Dictionary<int, Type>();
                foreach (Type t in typeof(DataWrapper).Assembly.GetTypes())
                {
                    if (!t.IsNested || !t.IsValueType) continue;
                    FieldInfo f = t.GetField("TransactionId", BindingFlags.Public | BindingFlags.Static);
                    if (f == null || !f.IsLiteral || t.GetField("OnResponse", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) == null) continue;
                    int v = (int)f.GetRawConstantValue();
                    // several client services may share a number: keep the first one on the client side
                    if (!s_TxTypes.ContainsKey(v) || t.FullName.StartsWith("Game.Client")) s_TxTypes[v] = t;
                }
            }
            Type r;
            s_TxTypes.TryGetValue(id, out r);
            return r;
        }

        /// <summary>Creates a response and sets its Error field to "Ok".</summary>
        public static T Ok<T>() where T : new()
        {
            T r = new T();
            FieldInfo f = typeof(T).GetField("Error");
            if (f != null && f.FieldType.IsEnum)
                f.SetValue(r, Enum.Parse(f.FieldType, "Ok"));
            return r;
        }

        /// <summary>A failed response (Error = InternalServerError): the client shows an error instead of waiting.</summary>
        public static T Fail<T>() where T : new()
        {
            T r = new T();
            FieldInfo f = typeof(T).GetField("Error");
            if (f != null && f.FieldType.IsEnum && Enum.IsDefined(f.FieldType, "InternalServerError"))
                f.SetValue(r, Enum.Parse(f.FieldType, "InternalServerError"));
            FieldInfo m = typeof(T).GetField("ErrorMessage");
            if (m != null && m.FieldType == typeof(string)) m.SetValue(r, "HEX Revival: request failed (see the log)");
            return r;
        }

        /// <summary>The game's folder: next to Hex.exe, or next to Hex.app on macOS (whose dataPath is Hex.app/Contents).</summary>
        public static string Root
        {
            get { return Path.GetFullPath(Application.dataPath + (Application.platform == RuntimePlatform.OSXPlayer ? "/../../" : "/../")); }
        }
    }
}
