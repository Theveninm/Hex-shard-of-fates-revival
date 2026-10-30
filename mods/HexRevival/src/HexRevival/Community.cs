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
    /// The mod's links to its community: the check for a newer release on GitHub, the main menu's "Featured Content"
    /// (the game's news carousel, UINewsEventsViewModel, which downloaded its panels from the HEX website) and a Discord
    /// button in the top bar. Featured Content only exists when a newer release is out: one panel introducing it (its
    /// GitHub release notes, the link to its page); up to date, or not known yet, the frame is hidden (user, 2026-10-30;
    /// before 1.2.10 its panels came from the project's server). The online lobby's update banner uses the same check
    /// (OnlineLobby).
    /// </summary>
    public class Community : MonoBehaviour
    {
        /// <summary>The mod's version (also the BepInEx plugin's version). Release tags on GitHub are the same "x.y.z".</summary>
        public const string Version = "1.2.10";

        public const string RepoUrl = "https://github.com/Theveninm/Hex-shard-of-fates-revival";
        public const string DiscordUrl = "https://discord.gg/4TWeErkhe";
        private const string LatestReleaseApi = "https://api.github.com/repos/Theveninm/Hex-shard-of-fates-revival/releases/latest";

        /// <summary>The newest release when it is newer than this one (null otherwise, or while checking).</summary>
        public static string NewVersion { get; private set; }
        public static string NewVersionUrl { get; private set; }
        public static bool Checked { get; private set; }

        /// <summary>Pictures of the panels, by their "image" value ("card:<name>", a file of the server's news folder or an address).</summary>
        private static readonly Dictionary<string, Texture2D> s_Pictures = new Dictionary<string, Texture2D>();

        private static string PictureFileName(string image)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in image) sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            string n = sb.ToString();
            return n.Length > 80 ? n.Substring(n.Length - 80) : n;
        }

        /// <summary>Dev "news": the update check and the Featured Content given to the game.</summary>
        public static string Describe()
        {
            return "checked " + Checked + ", this " + Version + ", newer " + (NewVersion ?? "none") + " " + (NewVersionUrl ?? "")
                + ", notes " + (NewVersionNotes ?? "").Length + " chars\n" + NewsJson();
        }

        /// <summary>
        /// The last answer, kept 6 hours (HexRevivalSaves/update_check.txt: time, latest release, its page, its notes in
        /// base64), so that
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
                string notes = "";
                if (l.Length > 3) { try { notes = Encoding.UTF8.GetString(Convert.FromBase64String(l[3])); } catch { } }
                Found(l[1], l[2], notes);
                Debug.Log("[HexRevival] update check: latest " + l[1] + " (checked " + (int)age.TotalMinutes + " min ago), this one " + Version);
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] update check cache: " + e.Message); return false; }
        }

        /// <summary>The newer release's notes (its GitHub description, Markdown), null when there is none.</summary>
        public static string NewVersionNotes { get; private set; }

        private static void Found(string tag, string url, string notes)
        {
            if (!IsNewer(tag, Version)) return;
            NewVersion = tag.TrimStart('v', 'V');
            NewVersionUrl = string.IsNullOrEmpty(url) ? RepoUrl + "/releases/latest" : url;
            NewVersionNotes = notes ?? "";
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
                        Found(r.tag_name, r.html_url, r.body);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CacheFile));
                        System.IO.File.WriteAllLines(CacheFile, new[] { DateTime.UtcNow.Ticks.ToString(), r.tag_name, r.html_url ?? "", Convert.ToBase64String(Encoding.UTF8.GetBytes(r.body ?? "")) });
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
            public string tag_name, html_url, body;
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
            ShowFeaturedContent();
            LayOutZoom();
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

        private GameObject m_NewsBox, m_NewsBackdrop, m_NewsBar;

        /// <summary>
        /// The Featured Content frame (NewsEventsVM/News_Events: its title and panels; the landing page's frame around it,
        /// Frame_L/Frame_Ads and its bottom strip Social_Bar) only while a newer release is out.
        /// </summary>
        private void ShowFeaturedContent()
        {
            if (m_News == null) return;
            if (m_NewsBox == null)
            {
                Transform t = m_News.transform.Find("News_Events");
                if (t == null) return;
                m_NewsBox = t.gameObject;
                GameObject frame = GameObject.Find("UILandingPageVM/2D/Buttons/Frame_L");
                if (frame != null)
                {
                    Transform ads = frame.transform.Find("Frame_Ads"), bar = frame.transform.Find("Social_Bar");
                    m_NewsBackdrop = ads != null ? ads.gameObject : null;
                    m_NewsBar = bar != null ? bar.gameObject : null;
                }
            }
            bool show = Checked && NewVersion != null;
            foreach (GameObject g in new[] { m_NewsBox, m_NewsBackdrop, m_NewsBar })
                if (g != null && g.activeSelf != show) g.SetActive(show);
        }

        private bool m_ZoomLaidOut;

        /// <summary>
        /// The large view of the panel (NewsEventsVM/ZoomedView, opened by a click) shows the release notes: the picture
        /// is hidden and the text box fills the frame under the title, in a bigger font (user, 2026-10-30: the game's
        /// small box over a stretched picture was hard to read).
        /// </summary>
        private void LayOutZoom()
        {
            bool zoomed = m_News != null && m_News.Context != null && m_News.Context.ItemZoomed;
            if (!zoomed) { m_ZoomLaidOut = false; return; }
            if (m_ZoomLaidOut) return;
            Transform view = m_News.transform.Find("ZoomedView");
            if (view == null) return;
            int done = 0;
            foreach (string frame in new[] { "Frame_Ad_16x10/Panel_16x10", "Frame_Ad_4x3/Panel_Ad_4x3" })
            {
                Transform panel = view.Find(frame);
                if (panel == null || !panel.gameObject.activeInHierarchy) continue;
                UITexture image = null;
                foreach (UITexture t in panel.GetComponentsInChildren<UITexture>(true)) if (t.name.StartsWith("Image_")) { image = t; break; }
                if (image == null) continue;
                image.alpha = 0f;   // still there: a click on it opens the release page, as before
                Transform content = panel.Find("ContentText");
                if (content == null) continue;
                // Anchored to the panel (its clipped area, what is seen), not to the picture, which is wider.
                foreach (UISprite plate in content.GetComponentsInChildren<UISprite>(true))
                    if (plate.name.StartsWith("Backplate"))
                    {
                        plate.SetAnchor(panel.gameObject, 8, 8, -8, -4);
                        plate.color = new Color(0.04f, 0.04f, 0.06f, 0.93f);
                    }
                foreach (UILabel label in content.GetComponentsInChildren<UILabel>(true))
                {
                    if (label.name != "DetailContentText") continue;
                    label.SetAnchor(panel.gameObject, 40, 24, -40, -106);
                    label.overflowMethod = UILabel.Overflow.ShrinkContent;
                    label.alignment = NGUIText.Alignment.Left;
                    label.fontSize = 30;   // the largest that fits (ShrinkContent)
                    label.spacingY = 2;
                    label.MarkAsChanged();
                }
                done++;
            }
            m_ZoomLaidOut = done > 0;
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
                if (sprite != null) tex.depth = sprite.depth + 1;
                // The logo is what receives the mouse: NGUI ignores a collider whose widget is hidden (Options' sprite).
                foreach (Collider c in b.GetComponents<Collider>()) DestroyImmediate(c);
                foreach (UIButton ub in b.GetComponents<UIButton>()) DestroyImmediate(ub);
                foreach (UITooltipSetText t in b.GetComponentsInChildren<UITooltipSetText>(true)) DestroyImmediate(t);
                if (sprite != null) sprite.enabled = false;
                NGUITools.AddWidgetCollider(tex.gameObject);
                UIButton button = tex.gameObject.AddComponent<UIButton>();
                button.tweenTarget = tex.gameObject;
                button.defaultColor = new Color(0.85f, 0.85f, 0.85f);
                button.hover = Color.white;
                button.pressed = new Color(0.7f, 0.7f, 0.7f);
                tex.color = button.defaultColor;
                // The game's tooltip box, as on the other icons of the bar.
                tex.gameObject.AddComponent<Tooltip>().Key = "lobby_discord";
                UIEventListener.Get(tex.gameObject).onClick = go => Open(DiscordUrl);
                b.SetActive(true);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] Discord icon: " + e); }
        }

        /// <summary>Shows a mod text in the game's tooltip box while the mouse is over the object (NGUI's OnHover), at once like the bar's other icons.</summary>
        private class Tooltip : MonoBehaviour
        {
            public string Key;
            private bool m_Shown;

            private void OnHover(bool over)
            {
                if (over) { m_Shown = true; UITooltip.ShowText(Loc.Get(Key)); }
                else Hide();
            }

            private void Hide()
            {
                if (!m_Shown) return;
                m_Shown = false;
                UITooltip.ShowText(string.Empty);
            }

            private void OnClick() { Hide(); }
            private void OnDisable() { Hide(); }
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
            float until = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < until && (!Checked || !PicturesReady())) yield return null;
            if (vm == null) yield break;
            try
            {
                HarmonyLib.Traverse.Create(vm).Field("m_ResponseText").SetValue(NewsJson());
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] featured content: " + e); }
        }

        /// <summary>The panel picture: Phoenix Guard Messenger's illustration.</summary>
        private const string UpdatePicture = "card:Phoenix Guard Messenger";

        /// <summary>The carousel's panels: the newer release when there is one, else none (the frame is hidden, see Update).</summary>
        private static string NewsJson()
        {
            StringBuilder sb = new StringBuilder("{\"NewsItemList\":[");
            if (NewVersion != null)
            {
                string title = Loc.Get("news_update_title", NewVersion), text = Loc.Get("news_update_text", NewVersion, Version);
                string notes = PlainNotes(NewVersionNotes);
                // The large view: the release notes alone (the title says which version, "Go" opens its page).
                string detail = notes.Length > 0 ? notes : text + "\n\n" + NewVersionUrl;
                string key = Picture(UpdatePicture);
                sb.Append('{')
                  .Append("\"ImageUrl\":").Append(Json(key)).Append(",\"ImageUrlHD\":").Append(Json(key)).Append(",\"ImageUrlHDWide\":").Append(Json(key))
                  .Append(",\"TitleText\":").Append(Json(title)).Append(",\"ContentText\":").Append(Json(text))
                  .Append(",\"LinkUrl\":").Append(Json(NewVersionUrl)).Append(",\"ShowDuration\":8")
                  // Clicked, the panel opens in large with the release notes and the address its "Go" button opens.
                  .Append(",\"UseDetailText\":true,\"DetailTitleText\":").Append(Json(title)).Append(",\"DetailContentText\":").Append(Json(detail)).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        /// <summary>
        /// A release's notes as plain text for the large panel: Markdown marks removed, headings in gold, lists as bullets, the install
        /// instructions left out (the "Go" button opens the release page), at most about 1,500 characters.
        /// </summary>
        public static string PlainNotes(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (string raw in md.Replace("\r", "").Split('\n'))
            {
                string l = raw.TrimEnd();
                string t = l.TrimStart();
                if (t.StartsWith("#") && t.TrimStart('#').Trim().StartsWith("Install", StringComparison.OrdinalIgnoreCase)) break;
                if (t.StartsWith("## ") && sb.Length == 0) continue;   // the release's own title ("HEX Revival x.y.z")
                if (t.StartsWith("#")) l = "[e8c87a]" + t.TrimStart('#').Trim() + "[-]";   // headings in gold (NGUI color code)
                else if (t.StartsWith("- ") || t.StartsWith("* ")) l = new string(' ', Math.Min(4, l.Length - t.Length)) + "\u2022 " + t.Substring(2);
                l = l.Replace("**", "").Replace("__", "").Replace("`", "");
                l = System.Text.RegularExpressions.Regex.Replace(l, @"\[([^\]]*)\]\([^)]*\)", "$1");   // [text](link) -> text
                if (l.Trim().Length == 0 && (sb.Length == 0 || sb.ToString().EndsWith("\n\n"))) continue;
                sb.Append(l).Append('\n');
            }
            string res = sb.ToString().Trim();
            return res.Length > 1500 ? res.Substring(0, res.LastIndexOf('\n', 1500) > 0 ? res.LastIndexOf('\n', 1500) : 1500) + "\n\u2026" : res;
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

        /// <summary>Panel pictures that are card illustrations ("card:<name>"): asked for (loaded in the background), cropped once arrived.</summary>
        private static bool PicturesReady()
        {
            bool ready = true;
            List<string> specs = new List<string> { FallbackPicture };
            if (NewVersion != null) specs.Add(UpdatePicture);
            foreach (string spec in specs)
            {
                Texture2D done;
                if (s_Pictures.TryGetValue(spec, out done) && done != null) continue;
                CardTemplate c = Opponents.CardByName(spec.Substring(5));
                if (c == null) continue;
                Rect uv;
                Texture2D art = CardArt.Get(c, out uv);
                if (art == null) { ready = false; continue; }
                s_Pictures[spec] = Crop(art, uv);
            }
            return ready;
        }

        /// <summary>The picture of a panel without one, or whose picture could not be loaded.</summary>
        private const string FallbackPicture = "card:Argus, Herald of Doom";

        /// <summary>
        /// The picture's key for the carousel: the texture is put in the game's texture cache under that key, where the
        /// carousel's texture loader (ResourceManager.GetTextureAsync) looks first. Put again each time, the cache may
        /// have dropped it.
        /// </summary>
        private static string Picture(string spec)
        {
            Texture2D t;
            if (string.IsNullOrEmpty(spec) || !s_Pictures.TryGetValue(spec, out t) || t == null) spec = FallbackPicture;
            string key = "hexrevival/news/" + PictureFileName(spec);
            if (s_Pictures.TryGetValue(spec, out t) && t != null)
                Singleton<ResourceManager>.Instance.AddTexture(key.GetHashCode(), spec, t, false);
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
