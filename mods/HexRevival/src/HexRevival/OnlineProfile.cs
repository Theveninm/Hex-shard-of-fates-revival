using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// The player's online identity, kept between sessions (HexRevivalSaves/online_profile.json): the name shown to the
    /// other players, a player id and a password, both generated once, and the server address. Copying the file to
    /// another PC moves the whole account. Everything can be changed on the online profile screen.
    /// Dev: a game started with "-hexinstance N" uses online_profile_N.json (two players on one PC).
    /// </summary>
    public class OnlineProfile
    {
        public string Name = "";
        public string PlayerId = "";
        public string Password = "";
        public string Server = "";

        public const string DefaultServer = "141.11.103.34:7777";

        private static OnlineProfile s_Current;

        private static string Dir { get { return Path.Combine(LocalServer.Root, "HexRevivalSaves"); } }
        private static string FilePath { get { return Path.Combine(Dir, "online_profile" + Suffix + ".json"); } }

        /// <summary>A second game instance on the same PC (dev, "-hexinstance N") has its own profile.</summary>
        private static string Suffix { get { return Instance.Id == 0 ? "" : "_" + Instance.Id; } }

        /// <summary>The saved profile (a new player id and password when there are none yet).</summary>
        public static OnlineProfile Current
        {
            get
            {
                if (s_Current != null) return s_Current;
                try
                {
                    if (File.Exists(FilePath)) JsonUtils.DeserializeJson(File.ReadAllBytes(FilePath), (OnlineProfile p) => { s_Current = p; });
                }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] profile: " + e.Message); }
                if (s_Current == null) s_Current = new OnlineProfile();
                bool changed = false;
                if (string.IsNullOrEmpty(s_Current.PlayerId)) { s_Current.PlayerId = NewId(); changed = true; }
                if (string.IsNullOrEmpty(s_Current.Password)) { s_Current.Password = NewPassword(); changed = true; }
                if (string.IsNullOrEmpty(s_Current.Server)) { s_Current.Server = DefaultServer; changed = true; }
                if (changed) Save();
                return s_Current;
            }
        }

        public static string NewId() { return Guid.NewGuid().ToString("N"); }

        /// <summary>
        /// 16 characters drawn with the system's cryptographic generator (not a clock-seeded Random, whose
        /// output can be guessed). No look-alike characters (0/O, 1/l/I): it may be typed by hand on another PC.
        /// </summary>
        public static string NewPassword()
        {
            const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            byte[] bytes = new byte[1];
            char[] res = new char[16];
            RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();   // not IDisposable in .NET 3.5
            for (int i = 0; i < res.Length; i++)
            {
                // Rejection sampling: 256 is not a multiple of the alphabet size, some bytes would be favored.
                byte b;
                do { rng.GetBytes(bytes); b = bytes[0]; } while (b >= 256 - 256 % chars.Length);
                res[i] = chars[b % chars.Length];
            }
            return new string(res);
        }

        /// <summary>A short form of the id, to recognize it on screen.</summary>
        public string ShortId() { return string.IsNullOrEmpty(PlayerId) || PlayerId.Length < 8 ? PlayerId : PlayerId.Substring(0, 8); }

        /// <summary>Host and port of the server ("host:port", port 7777 when missing).</summary>
        public void Endpoint(out string host, out int port)
        {
            string s = string.IsNullOrEmpty(Server) ? DefaultServer : Server.Trim();
            int colon = s.LastIndexOf(':');
            port = 7777;
            host = s;
            if (colon > 0 && int.TryParse(s.Substring(colon + 1), out port)) host = s.Substring(0, colon);
            else port = 7777;
        }

        public static void Save()
        {
            if (s_Current == null) return;
            try
            {
                Directory.CreateDirectory(Dir);
                byte[] b = null;
                JsonUtils.SerializeJson(s_Current, (byte[] x) => { b = x; }, true);
                File.WriteAllBytes(FilePath, b);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Online] save: " + e.Message); }
        }
    }
}
