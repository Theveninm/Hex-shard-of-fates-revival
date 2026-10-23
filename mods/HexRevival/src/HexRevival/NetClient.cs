using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Connection to the master server (server/HexRevival.Server, protocol in docs/MULTIPLAYER.md).
    /// The game runs on .NET 3.5 (no async): a reader thread and a writer thread move frames, and the game's main
    /// thread takes what arrived with Poll(). Logs in with the online profile, registering the account the first time.
    /// </summary>
    public class NetClient
    {
        public const int ProtocolVersion = 1;
        private const byte KindJson = 1, KindRelay = 2;
        private const int MaxFrame = 1024 * 1024;

        public enum Status { Connecting, Online, Closed }

        /// <summary>Something received: a JSON control message or relayed game data.</summary>
        public class Incoming
        {
            public JObject Json;
            public byte[] Relay;
        }

        private volatile Status m_Status = Status.Connecting;
        /// <summary>Why the connection closed (a server error code or a network message).</summary>
        public string CloseReason { get; private set; }

        private TcpClient m_Tcp;
        private NetworkStream m_Stream;
        private readonly Queue<Incoming> m_Inbox = new Queue<Incoming>();
        private readonly Queue<byte[]> m_Outbox = new Queue<byte[]>();
        private readonly AutoResetEvent m_OutboxSignal = new AutoResetEvent(false);
        private readonly OnlineProfile m_Profile;

        public Status State { get { return m_Status; } }

        private NetClient(OnlineProfile profile) { m_Profile = profile; }

        /// <summary>Starts connecting in the background (see State).</summary>
        public static NetClient Connect(OnlineProfile profile)
        {
            NetClient c = new NetClient(profile);
            Thread t = new Thread(c.Run) { IsBackground = true, Name = "HexRevival.NetClient" };
            t.Start();
            return c;
        }

        public void Close() { Close("closed"); }

        private void Close(string reason)
        {
            if (m_Status == Status.Closed) return;
            if (CloseReason == null) CloseReason = reason;
            m_Status = Status.Closed;
            try { if (m_Tcp != null) m_Tcp.Close(); } catch { }
            m_OutboxSignal.Set();
        }

        /// <summary>Everything received since the last call (main thread).</summary>
        public List<Incoming> Poll()
        {
            lock (m_Inbox)
            {
                List<Incoming> res = new List<Incoming>(m_Inbox);
                m_Inbox.Clear();
                return res;
            }
        }

        public void Send(JObject msg) { Enqueue(KindJson, Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None))); }

        public void SendRelay(byte[] data) { Enqueue(KindRelay, data); }

        private void Enqueue(byte kind, byte[] payload)
        {
            if (m_Status == Status.Closed) return;
            byte[] frame = new byte[5 + payload.Length];
            int n = payload.Length;
            frame[0] = (byte)(n >> 24); frame[1] = (byte)(n >> 16); frame[2] = (byte)(n >> 8); frame[3] = (byte)n;
            frame[4] = kind;
            Buffer.BlockCopy(payload, 0, frame, 5, n);
            lock (m_Outbox) m_Outbox.Enqueue(frame);
            m_OutboxSignal.Set();
        }

        // ------------------------------------------------------------------
        // Background threads
        // ------------------------------------------------------------------

        private void Run()
        {
            try
            {
                // First a login; an id the server does not know yet is registered (the server then closes, so reconnect).
                JObject hello = Open();
                string error = Login(hello, false);
                if (error == "unknown_id")
                {
                    m_Tcp.Close();
                    hello = Open();
                    error = Login(hello, true);
                }
                if (error != null) { Close(error); return; }

                m_Status = Status.Online;
                Thread writer = new Thread(WriteLoop) { IsBackground = true, Name = "HexRevival.NetClient.Write" };
                writer.Start();
                while (m_Status != Status.Closed)
                {
                    Incoming m = Read();
                    if (m == null) break;
                    lock (m_Inbox) m_Inbox.Enqueue(m);
                }
                Close("disconnected");
            }
            catch (Exception e)
            {
                if (m_Status != Status.Closed) Debug.LogWarning("[HexRevival] [Online] " + e.Message);
                Close("network");
            }
        }

        private JObject Open()
        {
            string host; int port;
            m_Profile.Endpoint(out host, out port);
            m_Tcp = new TcpClient();
            m_Tcp.NoDelay = true;
            m_Tcp.Connect(host, port);
            m_Stream = m_Tcp.GetStream();
            Incoming hello = Read();
            if (hello == null || hello.Json == null || (string)hello.Json["t"] != "hello") throw new IOException("not a HEX Revival server");
            return hello.Json;
        }

        /// <summary>Null when logged in, otherwise the server's error code.</summary>
        private string Login(JObject hello, bool register)
        {
            if ((int)hello["v"] != ProtocolVersion) return "version";
            // The password never travels: only a key derived from it (registration) or a proof of the key.
            byte[] key = new Rfc2898DeriveBytes(m_Profile.Password, Encoding.UTF8.GetBytes(m_Profile.PlayerId), (int)hello["iterations"]).GetBytes(32);
            JObject msg = new JObject();
            msg["v"] = ProtocolVersion;
            msg["id"] = m_Profile.PlayerId;
            msg["name"] = m_Profile.Name;
            if (register)
            {
                msg["t"] = "register";
                msg["key"] = Convert.ToBase64String(key);
            }
            else
            {
                msg["t"] = "login";
                using (HMACSHA256 mac = new HMACSHA256(key))
                    msg["proof"] = Convert.ToBase64String(mac.ComputeHash(Convert.FromBase64String((string)hello["nonce"])));
            }
            WriteNow(KindJson, Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None)));

            // Notices may come before the welcome.
            while (true)
            {
                Incoming m = Read();
                if (m == null) return "disconnected";
                if (m.Json == null) continue;
                string t = (string)m.Json["t"];
                if (t == "error") return (string)m.Json["code"];
                lock (m_Inbox) m_Inbox.Enqueue(m);
                if (t == "welcome") return null;
            }
        }

        private void WriteLoop()
        {
            const int PingEvery = 25000;
            try
            {
                while (m_Status != Status.Closed)
                {
                    if (!m_OutboxSignal.WaitOne(PingEvery, false))
                    {
                        JObject ping = new JObject();
                        ping["t"] = "ping";
                        Send(ping);
                        continue;
                    }
                    while (true)
                    {
                        byte[] frame;
                        lock (m_Outbox)
                        {
                            if (m_Outbox.Count == 0) break;
                            frame = m_Outbox.Dequeue();
                        }
                        m_Stream.Write(frame, 0, frame.Length);
                    }
                }
            }
            catch (Exception e)
            {
                if (m_Status != Status.Closed) Debug.LogWarning("[HexRevival] [Online] send: " + e.Message);
                Close("network");
            }
        }

        private void WriteNow(byte kind, byte[] payload)
        {
            Enqueue(kind, payload);
            lock (m_Outbox)
                while (m_Outbox.Count > 0) { byte[] f = m_Outbox.Dequeue(); m_Stream.Write(f, 0, f.Length); }
        }

        /// <summary>One frame, or null when the server closed the connection.</summary>
        private Incoming Read()
        {
            byte[] head = ReadExactly(5);
            if (head == null) return null;
            int n = (head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3];
            if (n < 0 || n > MaxFrame) throw new IOException("frame too large");
            byte[] payload = ReadExactly(n);
            if (payload == null) return null;
            if (head[4] == KindRelay) return new Incoming { Relay = payload };
            return new Incoming { Json = JObject.Parse(Encoding.UTF8.GetString(payload)) };
        }

        private byte[] ReadExactly(int n)
        {
            byte[] buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = m_Stream.Read(buf, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return buf;
        }
    }
}
