using System;
using System.IO;
using UnityEngine;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// The player's online identity, kept between sessions (HexRevivalSaves/online_profile.json): the name shown to the
    /// other players and a player id generated once (the credentials the lobby will be joined with). Both can be changed
    /// on the online profile screen. A password or key comes later, with the server.
    /// </summary>
    public class OnlineProfile
    {
        public string Name = "";
        public string PlayerId = "";

        private static OnlineProfile s_Current;

        private static string Dir { get { return Path.Combine(LocalServer.Root, "HexRevivalSaves"); } }
        private static string FilePath { get { return Path.Combine(Dir, "online_profile.json"); } }
        /// <summary>The first version kept the name alone in a text file.</summary>
        private static string OldPath { get { return Path.Combine(Dir, "online_profile.txt"); } }

        /// <summary>The saved profile (a new player id when there is none yet).</summary>
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
                if (s_Current == null)
                {
                    s_Current = new OnlineProfile();
                    try { if (File.Exists(OldPath)) s_Current.Name = File.ReadAllText(OldPath).Trim(); } catch { }
                }
                if (string.IsNullOrEmpty(s_Current.PlayerId)) { s_Current.PlayerId = NewId(); Save(); }
                return s_Current;
            }
        }

        public static string NewId() { return Guid.NewGuid().ToString("N"); }

        /// <summary>A short form of the id, to recognize it on screen.</summary>
        public string ShortId() { return string.IsNullOrEmpty(PlayerId) || PlayerId.Length < 8 ? PlayerId : PlayerId.Substring(0, 8); }

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
