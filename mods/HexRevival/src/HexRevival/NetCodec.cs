using System;
using System.IO;
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

        public static NetworkPacketSessionEventArgs DecodeEvents(byte[] bytes) { return (NetworkPacketSessionEventArgs)EncData.Decode(bytes); }

        public static byte[] EncodeTransaction(Transaction tx) { return EncData.Encode(tx); }

        public static Transaction DecodeTransaction(byte[] bytes) { return (Transaction)EncData.Decode(bytes); }

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
