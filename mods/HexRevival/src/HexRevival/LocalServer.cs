using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Network;
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
        }

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
                return false;
            Debug.Log("[HexRevival] [LocalServer] " + args.GetType().Name + " -> " + resp.GetType().Name);
            lock (s_Pending)
                s_Pending.Enqueue(new Pending { TransactionId = dw.DataType, Response = resp, Callback = callback });
            return true;
        }

        private static readonly Queue<Action> s_Later = new Queue<Action>();

        /// <summary>Runs an action on the next Pump (main thread), like a network answer.</summary>
        public static void Later(Action a)
        {
            lock (s_Later) s_Later.Enqueue(a);
        }

        /// <summary>Delivers pending responses (main thread, one frame after the request).</summary>
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
                    if (s_Pending.Count == 0) return;
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

        public static string Root { get { return Path.GetFullPath(Application.dataPath + "/../"); } }
    }
}
