using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Shared;
using Game.Shared.Mechanics.Transactions;
using Game.Shared.Network;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The bytes of a match between two players over the network, in the game's own formats: the server's events for a
    /// player (NetworkPacketSessionEventArgs, each event in its own binary form) and the player's transactions, both
    /// encoded with the game's serializer (EncData), as the original servers did.
    /// "Loopback" (dev, HexRevival.netloop file next to Hex.exe): a local match passes everything through encoding and
    /// decoding, to check that nothing is lost on the way.
    /// </summary>
    public static class NetCodec
    {
        public static byte[] EncodeEvents(NetworkPacketSessionEventArgs packet) { return EncData.Encode(packet); }

        public static NetworkPacketSessionEventArgs DecodeEvents(byte[] bytes) { return (NetworkPacketSessionEventArgs)Decode(bytes); }

        public static byte[] EncodeTransaction(Transaction tx) { return EncData.Encode(tx); }

        public static Transaction DecodeTransaction(byte[] bytes) { return (Transaction)Decode(bytes); }

        // ---- Decoding what the opponent sent ----
        //
        // The game's decoder (EncData / ObjFmt) was made for the official servers' data: it creates objects of any type
        // named in the data (even private constructors) and fills any of their fields. From another player that is
        // unsafe, so the data's type table is checked first: only the game's own types (namespace Game.*), the basic
        // .NET values and the generic collections are allowed, otherwise nothing is decoded.

        private const int MaxPacket = 1024 * 1024;
        private static readonly MethodInfo s_FindType = typeof(EncData).GetMethod("FindType", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly Dictionary<string, bool> s_Checked = new Dictionary<string, bool>();

        /// <summary>An object of the opponent's game data, or an exception when the data names a type not allowed.</summary>
        public static object Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPacket) throw new InvalidDataException("bad packet size");
            using (MemoryStream ms = new MemoryStream(bytes, false))
            {
                ObjFmt.Decoder d = ObjFmt.MakeDecoder(Resolve);
                HarmonyLib.Traverse t = HarmonyLib.Traverse.Create(d);
                t.Method("Reset").GetValue();
                t.Method("ReadSizeTable", new[] { typeof(Stream) }).GetValue(ms);
                t.Method("ReadTypeTable", new[] { typeof(Stream) }).GetValue(ms);
                // Every object of the data refers to this table (the decoder keeps a cache by name shared with the
                // game's own decoding, so the check cannot rely on the resolver being called).
                List<string> types = t.Field("_types").GetValue<List<string>>();
                if (types == null) throw new InvalidDataException("no type table");
                foreach (string desc in types) Resolve(desc);
                ms.Position = 0;
                return d.Decode(ms);
            }
        }

        private static Type Resolve(string desc)
        {
            bool ok;
            lock (s_Checked)
                if (s_Checked.TryGetValue(desc, out ok) && !ok) throw new InvalidDataException("type not allowed: " + desc);
            Type type = null;
            try { type = (Type)s_FindType.Invoke(null, new object[] { desc }); }
            catch (Exception) { }
            ok = type != null && Allowed(type, 0);
            lock (s_Checked) s_Checked[desc] = ok;
            if (!ok) { Debug.LogWarning("[HexRevival] [Net] refused type " + desc); throw new InvalidDataException("type not allowed: " + desc); }
            return type;
        }

        private static readonly Type[] s_Values =
        {
            typeof(string), typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(char), typeof(Guid),
            typeof(DateTime), typeof(TimeSpan), typeof(object),
        };

        private static readonly Type[] s_Collections =
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(KeyValuePair<,>), typeof(Nullable<>),
            typeof(Queue<>), typeof(Stack<>), typeof(LinkedList<>), typeof(SortedDictionary<,>), typeof(SortedList<,>),
        };

        private static bool Allowed(Type t, int depth)
        {
            if (depth > 8) return false;
            if (t.IsArray) return t.GetArrayRank() == 1 && Allowed(t.GetElementType(), depth + 1);
            if (t.IsGenericType)
            {
                if (Array.IndexOf(s_Collections, t.GetGenericTypeDefinition()) < 0 && !GameType(t)) return false;
                foreach (Type a in t.GetGenericArguments()) if (!Allowed(a, depth + 1)) return false;
                return true;
            }
            if (t.IsEnum) return true;
            if (Array.IndexOf(s_Values, t) >= 0) return true;
            return GameType(t);
        }

        /// <summary>A type of the game's shared model (the assembly of the client and server code, namespaces Game.*).</summary>
        private static bool GameType(Type t)
        {
            string ns = t.Namespace ?? "";
            return t.Assembly == typeof(EncData).Assembly && (ns == "Game" || ns.StartsWith("Game.", StringComparison.Ordinal))
                && !typeof(Delegate).IsAssignableFrom(t);
        }

        private static bool? s_Loopback;

        public static bool Loopback
        {
            get
            {
                if (s_Loopback == null)
                    s_Loopback = Dev.Enabled && File.Exists(Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), "HexRevival.netloop"));
                return s_Loopback.Value;
            }
        }

        public static long BytesEvents, BytesTransactions, Packets, Transactions;

        public static NetworkPacketSessionEventArgs LoopEvents(NetworkPacketSessionEventArgs p)
        {
            byte[] b = EncodeEvents(p);
            BytesEvents += b.Length; Packets++;
            return DecodeEvents(b);
        }

        public static Transaction LoopTransaction(Transaction tx)
        {
            byte[] b = EncodeTransaction(tx);
            BytesTransactions += b.Length; Transactions++;
            Transaction res = DecodeTransaction(b);
            Debug.Log("[HexRevival] [Net] loop " + tx.GetType().Name + " " + b.Length + " bytes -> " + (res != null ? res.GetType().Name : "null"));
            return res;
        }

        public static string Stats()
        {
            return "loopback=" + Loopback + " packets " + Packets + " (" + BytesEvents + " B), transactions " + Transactions + " (" + BytesTransactions + " B)";
        }
    }
}
