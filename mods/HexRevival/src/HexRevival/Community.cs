using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The mod's links to its community: the check for a newer release on GitHub, and the panels of the main menu's
    /// "Featured Content" (the game's news carousel, UINewsEventsViewModel, which downloaded its panels from the HEX
    /// website): a new version when there is one, the Discord server, the project page. The online lobby shows the same
    /// links in a banner (OnlineLobby).
    /// </summary>
    public class Community : MonoBehaviour
    {
        /// <summary>The mod's version (also the BepInEx plugin's version). Release tags on GitHub are the same "x.y.z".</summary>
        public const string Version = "1.1.0";

        public const string RepoUrl = "https://github.com/Theveninm/Hex-shard-of-fates-revival";
        public const string DiscordUrl = "https://discord.gg/4TWeErkhe";
        private const string LatestReleaseApi = "https://api.github.com/repos/Theveninm/Hex-shard-of-fates-revival/releases/latest";

        /// <summary>The newest release when it is newer than this one (null otherwise, or while checking).</summary>
        public static string NewVersion { get; private set; }
        public static string NewVersionUrl { get; private set; }
        public static bool Checked { get; private set; }

        /// <summary>The panels' pictures: card illustrations of the game, cropped to their art.</summary>
        private static readonly string[][] PanelArt =
        {
            new[] { "update", "Phoenix Guard Messenger" },
            new[] { "discord", "Royal Herald" },
            new[] { "project", "Argus, Herald of Doom" },
        };

        private static readonly Dictionary<string, Texture2D> s_Pictures = new Dictionary<string, Texture2D>();

        private IEnumerator Start()
        {
            // The game's Unity (5.6) asks Windows to download (WinINet), which handles GitHub's TLS.
            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers["Accept"] = "application/vnd.github+json";
            WWW www = new WWW(LatestReleaseApi, null, headers);
            float until = Time.realtimeSinceStartup + 20f;
            while (!www.isDone && Time.realtimeSinceStartup < until) yield return null;
            try
            {
                if (!www.isDone) Debug.LogWarning("[HexRevival] update check: no answer");
                else if (!string.IsNullOrEmpty(www.error)) Debug.LogWarning("[HexRevival] update check: " + www.error);
                else
                {
                    Release r = JsonConvert.DeserializeObject<Release>(www.text);
                    if (r != null && !r.draft && !r.prerelease && IsNewer(r.tag_name, Version))
                    {
                        NewVersion = r.tag_name.TrimStart('v', 'V');
                        NewVersionUrl = string.IsNullOrEmpty(r.html_url) ? RepoUrl + "/releases/latest" : r.html_url;
                    }
                    Debug.Log("[HexRevival] update check: latest " + (r != null ? r.tag_name : "?") + ", this one " + Version);
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] update check: " + e.Message); }
            finally { www.Dispose(); }
            Checked = true;
        }

        private class Release
        {
            public string tag_name, html_url;
            public bool draft, prerelease;
        }

        /// <summary>"1.10.0" is newer than "1.9.2": numbers compared one by one ("v" prefix and suffixes ignored).</summary>
        public static bool IsNewer(string tag, string current)
        {
            int[] a = Numbers(tag), b = Numbers(current);
            if (a == null || b == null) return false;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private static int[] Numbers(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            string[] parts = v.Trim().TrimStart('v', 'V').Split('.');
            int[] res = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string digits = "";
                foreach (char c in parts[i]) { if (char.IsDigit(c)) digits += c; else break; }
                if (digits.Length == 0 || !int.TryParse(digits, out res[i])) return null;
            }
            return res;
        }

        public static void Open(string url)
        {
            try { Main.OpenURL(url); }
            catch (Exception e) { Debug.LogWarning("[HexRevival] open " + url + ": " + e.Message); }
        }

        // ---- Featured Content ----

        /// <summary>
        /// Replaces the download of the news (Harmony prefix of UINewsEventsViewModel.GetNewsAndEvents): the panels are
        /// given to the carousel once the update check and the pictures are ready (a few seconds at most).
        /// </summary>
        public static void FillNews(UINewsEventsViewModel vm)
        {
            vm.StartCoroutine(FillNewsWhenReady(vm));
        }

        private static IEnumerator FillNewsWhenReady(UINewsEventsViewModel vm)
        {
            float until = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < until && (!Checked || !PicturesReady())) yield return null;
            if (vm == null) yield break;
            try
            {
                HarmonyLib.Traverse.Create(vm).Field("m_ResponseText").SetValue(NewsJson());
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] featured content: " + e); }
        }

        private static string NewsJson()
        {
            StringBuilder sb = new StringBuilder("{\"NewsItemList\":[");
            bool first = true;
            Action<string, string, string, string> add = (picture, title, text, url) =>
            {
                string key = Picture(picture);
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{')
                  .Append("\"ImageUrl\":").Append(Json(key)).Append(",\"ImageUrlHD\":").Append(Json(key)).Append(",\"ImageUrlHDWide\":").Append(Json(key))
                  .Append(",\"TitleText\":").Append(Json(title)).Append(",\"ContentText\":").Append(Json(text))
                  .Append(",\"LinkUrl\":").Append(Json(url)).Append(",\"ShowDuration\":8")
                  // Clicked, the panel opens in large with its text and the address its "Go" button opens.
                  .Append(",\"UseDetailText\":true,\"DetailTitleText\":").Append(Json(title)).Append(",\"DetailContentText\":").Append(Json(text + "\n\n" + url)).Append('}');
            };
            if (NewVersion != null)
                add("update", Loc.Get("news_update_title", NewVersion), Loc.Get("news_update_text", NewVersion, Version), NewVersionUrl);
            add("discord", Loc.Get("news_discord_title"), Loc.Get("news_discord_text"), DiscordUrl);
            add("project", Loc.Get("news_project_title", Version), Loc.Get("news_project_text"), RepoUrl);
            return sb.Append("]}").ToString();
        }

        private static string Json(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>Asks for the card illustrations (loaded in the background) and crops those that arrived.</summary>
        private static bool PicturesReady()
        {
            bool ready = true;
            foreach (string[] p in PanelArt)
            {
                Texture2D done;
                if (s_Pictures.TryGetValue(p[0], out done) && done != null) continue;
                CardTemplate c = Opponents.CardByName(p[1]);
                if (c == null) continue;
                Rect uv;
                Texture2D art = CardArt.Get(c, out uv);
                if (art == null) { ready = false; continue; }
                s_Pictures[p[0]] = Crop(art, uv);
            }
            return ready;
        }

        /// <summary>
        /// The picture's key for the carousel: the texture is put in the game's texture cache under that key, where the
        /// carousel's texture loader (ResourceManager.GetTextureAsync) looks first. Put again each time, the cache may
        /// have dropped it.
        /// </summary>
        private static string Picture(string name)
        {
            string key = "hexrevival/news/" + name;
            Texture2D t;
            if (s_Pictures.TryGetValue(name, out t) && t != null)
                Singleton<ResourceManager>.Instance.AddTexture(key.GetHashCode(), name, t, false);
            return key;
        }

        /// <summary>The uv area of a texture copied into a new one (the card art without the rest of the card's picture).</summary>
        private static Texture2D Crop(Texture2D src, Rect uv)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(src.width * uv.width)), h = Mathf.Max(1, Mathf.RoundToInt(src.height * uv.height));
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, w, h, 0);
                Graphics.DrawTexture(new Rect(0, 0, w, h), src, uv, 0, 0, 0, 0);
                GL.PopMatrix();
                Texture2D res = new Texture2D(w, h, TextureFormat.RGB24, false);
                res.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                res.Apply();
                res.name = "HexRevivalNews";
                return res;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
