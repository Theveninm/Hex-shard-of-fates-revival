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
    /// "Serveur" local pour les services hors combat (profil, decks...).
    ///
    /// Le client envoie ses requetes par ClientConnection.SendCommand(octets DataWrapper, ...) ; on
    /// decode le DataWrapper (numero de transaction + arguments), on traite les transactions connues
    /// et on renvoie la reponse a l'image suivante, comme le faisait la Dispatch generee
    /// (evenement statique OnResponse de la struct de transaction + callback de la requete).
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

        /// <summary>Appele par le hook de ClientConnection.SendCommand. Vrai si la requete est traitee ici.</summary>
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
                Debug.LogWarning("[HexRevival] [LocalServer] decodage impossible : " + e.Message);
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

        /// <summary>Delivre les reponses en attente (thread principal, une image apres la requete).</summary>
        public static void Pump()
        {
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
                    Debug.LogError("[HexRevival] [LocalServer] livraison " + p.TransactionId + " : " + e);
                }
            }
        }

        /// <summary>Struct de transaction generee (ex. ProfileService+AddNewDeck) par numero.</summary>
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
                    // plusieurs services clients peuvent partager un numero : on garde le premier cote client
                    if (!s_TxTypes.ContainsKey(v) || t.FullName.StartsWith("Game.Client")) s_TxTypes[v] = t;
                }
            }
            Type r;
            s_TxTypes.TryGetValue(id, out r);
            return r;
        }

        /// <summary>Cree une reponse et met son champ Error a "Ok".</summary>
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
