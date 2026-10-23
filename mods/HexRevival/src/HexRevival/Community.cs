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
    /// website): a new version when there is one, the project page; a Discord button in the top bar, right of Options.
    /// The online lobby shows the Discord server and the new version in a banner (OnlineLobby).
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
            new[] { "project", "Argus, Herald of Doom" },
        };

        private static readonly Dictionary<string, Texture2D> s_Pictures = new Dictionary<string, Texture2D>();

        /// <summary>
        /// The last answer, kept 6 hours (HexRevivalSaves/update_check.txt: time, latest release, its page), so that
        /// restarting the game often does not ask GitHub each time (60 requests per hour and address without account).
        /// </summary>
        private static string CacheFile { get { return System.IO.Path.Combine(System.IO.Path.Combine(LocalServer.Root, "HexRevivalSaves"), "update_check.txt"); } }
        private static readonly TimeSpan CacheTime = TimeSpan.FromHours(6);

        private static bool FromCache()
        {
            try
            {
                if (!System.IO.File.Exists(CacheFile)) return false;
                string[] l = System.IO.File.ReadAllLines(CacheFile);
                long ticks;
                if (l.Length < 3 || !long.TryParse(l[0], out ticks)) return false;
                TimeSpan age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                if (age < TimeSpan.Zero || age > CacheTime) return false;
                Found(l[1], l[2]);
                Debug.Log("[HexRevival] update check: latest " + l[1] + " (checked " + (int)age.TotalMinutes + " min ago), this one " + Version);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] update check cache: " + e.Message); return false; }
        }

        private static void Found(string tag, string url)
        {
            if (!IsNewer(tag, Version)) return;
            NewVersion = tag.TrimStart('v', 'V');
            NewVersionUrl = string.IsNullOrEmpty(url) ? RepoUrl + "/releases/latest" : url;
        }

        private IEnumerator Start()
        {
            if (FromCache()) { Checked = true; yield break; }
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
                    if (r != null && !r.draft && !r.prerelease && !string.IsNullOrEmpty(r.tag_name))
                    {
                        Found(r.tag_name, r.html_url);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CacheFile));
                        System.IO.File.WriteAllLines(CacheFile, new[] { DateTime.UtcNow.Ticks.ToString(), r.tag_name, r.html_url ?? "" });
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

        // ---- Featured Content: arrows, no change under the mouse ----

        private UINewsEventsViewModel m_News;
        private UIWidget m_NewsFrame;
        private Rect m_NewsRect;
        private bool m_NewsHover;
        private float m_NextSearch;
        private readonly Controls.UiBlocker m_ArrowBlock = new Controls.UiBlocker();

        /// <summary>
        /// While the mouse is over the panel, it does not change by itself (the game only paused it over the picture);
        /// it starts again 8 s after the mouse leaves.
        /// </summary>
        private void Update()
        {
            AddDiscordIcon();
            bool hover = false;
            if ((m_News == null || m_NewsFrame == null) && Time.realtimeSinceStartup > m_NextSearch)
            {
                m_NextSearch = Time.realtimeSinceStartup + 1f;
                m_News = FindObjectOfType<UINewsEventsViewModel>();
                m_NewsFrame = null;
                if (m_News != null)
                    foreach (UIWidget w in m_News.GetComponentsInChildren<UIWidget>(true))
                        if (w.name == "_Subframe") { m_NewsFrame = w; break; }
            }
            if (m_News != null && m_NewsFrame != null && m_News.isActiveAndEnabled && m_News.Context != null && !m_News.Context.ItemZoomed && PanelCount() > 0)
            {
                m_NewsRect = ScreenRect(m_NewsFrame);
                Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (m_NewsRect.Contains(mouse))
                {
                    // Over the arrows the game's input is off (its click would open the panel): the hover goes on.
                    GameObject over = UICamera.hoveredObject;
                    hover = (m_NewsHover && OverArrow(mouse)) || (over != null && over.transform.IsChildOf(m_News.transform));
                }
            }
            if (hover) Traverse(m_News).Field("m_UpdatePanelTime").SetValue(DateTime.MaxValue);
            else if (m_NewsHover && m_News != null) Traverse(m_News).Field("m_UpdatePanelTime").SetValue(DateTime.Now.AddSeconds(8));
            m_NewsHover = hover;
            m_ArrowBlock.Update(false, hover ? LeftArrow() : new Rect(), hover ? RightArrow() : new Rect());
        }

        private void OnGUI()
        {
            if (!m_NewsHover || PanelCount() < 2) return;
            GUI.depth = 10;
            if (Controls.Small(LeftArrow(), "<")) Step(-1);
            if (Controls.Small(RightArrow(), ">")) Step(1);
        }

        // ---- Discord button in the top bar ----

        private float m_NextNavCheck, m_NavStep;

        /// <summary>
        /// The top bar's icons (MainMenu/Buttons/Anchor_R, Siege leftmost, Options rightmost): a copy of Options showing
        /// the Discord logo, one place left of Siege (away from Options, which is clicked often). Done again when the game
        /// rebuilds the bar.
        /// </summary>
        private void AddDiscordIcon()
        {
            if (Time.realtimeSinceStartup < m_NextNavCheck) return;
            m_NextNavCheck = Time.realtimeSinceStartup + 1f;
            GameObject options = GameObject.Find("MainMenu/Buttons/Anchor_R/Icon_Options");
            if (options == null) return;
            Transform bar = options.transform.parent;
            Transform existing = bar.Find("Icon_Discord");
            if (existing != null)
            {
                // The bar's icons can be laid out again (the game moves them by screen): kept one place left of Siege.
                Transform s = bar.Find("Icon_Siege");
                if (s != null && m_NavStep > 0f) existing.localPosition = s.localPosition - new Vector3(m_NavStep, 0f, 0f);
                return;
            }
            try
            {
                // One place = the distance from Options to the nearest icon on its left.
                float x = options.transform.localPosition.x, step = 0f;
                foreach (Transform t in bar)
                {
                    float d = x - t.localPosition.x;
                    if (t != options.transform && t.GetComponentInChildren<UIWidget>(true) != null && d > 1f && (step == 0f || d < step)) step = d;
                }
                if (step == 0f) step = 60f;
                m_NavStep = step;
                Texture2D badge = Embedded("discord_badge.png");
                if (badge == null) return;

                Transform siege = bar.Find("Icon_Siege");
                GameObject b = UiClone.Copy(options, "Icon_Discord");
                UiClone.Strip(b);
                // No movement of its own (Options' tweens would move the copy away from its place).
                foreach (MonoBehaviour m in b.GetComponentsInChildren<MonoBehaviour>(true))
                    if (m is UITweener || m is UIButtonOffset) DestroyImmediate(m);
                b.transform.localPosition = (siege != null ? siege.localPosition : options.transform.localPosition) - new Vector3(step, 0f, 0f);
                UISprite sprite = b.GetComponent<UISprite>();
                UITexture tex = NGUITools.AddWidget<UITexture>(b);
                tex.name = "Discord";
                tex.mainTexture = badge;
                tex.width = Mathf.RoundToInt((sprite != null ? sprite.width : 54) * 0.75f);   // the size of the other icons' frame
                tex.height = tex.width;
                if (sprite != null) { tex.depth = sprite.depth + 1; sprite.enabled = false; }
                foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true))
                {
                    ub.tweenTarget = tex.gameObject;
                    ub.defaultColor = new Color(0.85f, 0.85f, 0.85f);
                    ub.hover = Color.white;
                    ub.pressed = new Color(0.7f, 0.7f, 0.7f);
                    ub.onClick.Clear();
                }
                // The game's tooltip, as on the other icons of the bar.
                UITooltipSetText[] tips = b.GetComponentsInChildren<UITooltipSetText>(true);
                if (tips.Length == 0) tips = new[] { b.AddComponent<UITooltipSetText>() };
                foreach (UITooltipSetText tip in tips)
                {
                    tip.TooltipLocalizationScript = null;
                    tip.TooltipText = Loc.Get("lobby_discord");
                }
                UIEventListener.Get(b).onClick = go => Open(DiscordUrl);
                b.SetActive(true);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] Discord icon: " + e); }
        }

        private static HarmonyLib.Traverse Traverse(object o) { return HarmonyLib.Traverse.Create(o); }

        private int PanelCount() { return m_News != null ? Traverse(m_News).Field("m_PanelCount").GetValue<int>() : 0; }

        /// <summary>The previous or next panel (the game's own "previous" goes back to the first one from the first).</summary>
        private void Step(int delta)
        {
            int count = PanelCount();
            if (count < 2) return;
            int cur = Traverse(m_News).Field("m_CurrentPanel").GetValue<int>();
            Traverse(m_News).Field("m_CurrentPanel").SetValue(((cur + delta) % count + count) % count);
            Traverse(m_News).Method("LoadCurrentPanel").GetValue();
            Traverse(m_News).Field("m_UpdatePanelTime").SetValue(DateTime.MaxValue);
        }

        private Rect LeftArrow()
        {
            float S = GameSkin.Scale, w = 36 * S, h = 56 * S;
            return new Rect(m_NewsRect.x + 6 * S, m_NewsRect.center.y - h / 2f, w, h);
        }

        private Rect RightArrow()
        {
            Rect l = LeftArrow();
            return new Rect(m_NewsRect.xMax - 6 * GameSkin.Scale - l.width, l.y, l.width, l.height);
        }

        private bool OverArrow(Vector2 mouse) { return LeftArrow().Contains(mouse) || RightArrow().Contains(mouse); }

        /// <summary>An NGUI widget's area on the screen, in GUI coordinates (y from the top).</summary>
        private static Rect ScreenRect(UIWidget w)
        {
            Camera cam = NGUITools.FindCameraForLayer(w.gameObject.layer);
            if (cam == null) return new Rect();
            Vector3[] c = w.worldCorners;
            Vector3 a = cam.WorldToScreenPoint(c[0]), b = cam.WorldToScreenPoint(c[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
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

        /// <summary>A picture embedded in the mod's DLL (build.sh: -resource).</summary>
        private static Texture2D Embedded(string name)
        {
            try
            {
                using (System.IO.Stream st = typeof(Community).Assembly.GetManifestResourceStream("HexRevival." + name))
                {
                    if (st == null) return null;
                    byte[] bytes = new byte[st.Length];
                    int read = 0;
                    while (read < bytes.Length) { int n = st.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    Texture2D t = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    return t.LoadImage(bytes) ? t : null;
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] picture " + name + ": " + e.Message); return null; }
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
