using System;
using System.Collections.Generic;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Dessin IMGUI avec les ressources graphiques du jeu : sprites des atlas NGUI (cadres, boutons,
    /// onglets, en "9-slice" selon leurs bordures) et polices du jeu (FMBolyar pour les titres,
    /// Hex_Arial pour le texte). Permet aux ecrans du mod d'avoir l'apparence de l'interface d'origine.
    /// </summary>
    public static class GameSkin
    {
        public static readonly Color Gold = new Color(0.875f, 0.796f, 0.659f);
        public static readonly Color GoldDim = new Color(0.796f, 0.722f, 0.573f);
        public static readonly Color Text = new Color(0.93f, 0.90f, 0.84f);
        public static readonly Color Muted = new Color(0.62f, 0.60f, 0.56f);

        private static readonly Dictionary<string, UIAtlas> s_Atlases = new Dictionary<string, UIAtlas>();
        private static readonly Dictionary<string, GUIStyle> s_Text = new Dictionary<string, GUIStyle>();
        private static Font s_Title, s_Body, s_BodyBold, s_Japanese, s_Chinese;
        private static bool s_FontsLoaded;

        /// <summary>Echelle de l'interface : le jeu est concu pour 1000 pixels de haut.</summary>
        public static float Scale { get { return Mathf.Max(0.6f, Screen.height / 1000f); } }

        public static int Px(float v) { return Mathf.RoundToInt(v * Scale); }

        private static UIAtlas Atlas(string name)
        {
            UIAtlas a;
            if (s_Atlases.TryGetValue(name, out a) && a != null) return a;
            a = null;
            foreach (UIAtlas x in Resources.FindObjectsOfTypeAll<UIAtlas>())
                if (x != null && x.name == name && x.texture != null) { a = x; break; }
            s_Atlases[name] = a;
            return a;
        }

        /// <summary>Dessine un sprite d'atlas, etire en conservant ses bordures (9-slice).</summary>
        public static bool Sprite(Rect r, string atlas, string sprite, float alpha = 1f, bool sliced = true)
        {
            UIAtlas a = Atlas(atlas);
            if (a == null) return false;
            UISpriteData d = a.GetSprite(sprite);
            Texture tex = a.texture;
            if (d == null || tex == null) return false;
            Color old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
            float tw = tex.width, th = tex.height;
            float bl = d.borderLeft, br = d.borderRight, bt = d.borderTop, bb = d.borderBottom;
            if (!sliced || (bl + br + bt + bb) <= 0)
            {
                Draw(tex, r, d.x, d.y, d.width, d.height, tw, th);
                GUI.color = old;
                return true;
            }
            // Bordures a l'ecran : a l'echelle, sans depasser la moitie du rectangle.
            float s = Scale;
            float l = Mathf.Min(bl * s, r.width / 2f), ri = Mathf.Min(br * s, r.width / 2f);
            float t = Mathf.Min(bt * s, r.height / 2f), b = Mathf.Min(bb * s, r.height / 2f);
            float[] xs = { r.x, r.x + l, r.xMax - ri, r.xMax };
            float[] ys = { r.y, r.y + t, r.yMax - b, r.yMax };
            float[] sx = { d.x, d.x + bl, d.x + d.width - br, d.x + d.width };
            float[] sy = { d.y, d.y + bt, d.y + d.height - bb, d.y + d.height };
            for (int iy = 0; iy < 3; iy++)
                for (int ix = 0; ix < 3; ix++)
                {
                    float w = xs[ix + 1] - xs[ix], h = ys[iy + 1] - ys[iy];
                    float sw = sx[ix + 1] - sx[ix], sh = sy[iy + 1] - sy[iy];
                    if (w <= 0 || h <= 0 || sw <= 0 || sh <= 0) continue;
                    Draw(tex, new Rect(xs[ix], ys[iy], w, h), sx[ix], sy[iy], sw, sh, tw, th);
                }
            GUI.color = old;
            return true;
        }

        private static void Draw(Texture tex, Rect screen, float sx, float sy, float sw, float sh, float tw, float th)
        {
            // Coordonnees NGUI : origine en haut a gauche ; UV Unity : origine en bas a gauche.
            Rect uv = new Rect(sx / tw, 1f - (sy + sh) / th, sw / tw, sh / th);
            GUI.DrawTextureWithTexCoords(screen, tex, uv, true);
        }

        private static void LoadFonts()
        {
            if (s_FontsLoaded) return;
            s_FontsLoaded = true;
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f == null) continue;
                if (f.name == "FMBolyar_Regular") s_Title = f;
                else if (f.name == "Hex_Arial") s_Body = f;
                else if (f.name == "Hex_Arial_Bold") s_BodyBold = f;
                else if (f.name == "YasashisaAntique_JA") s_Japanese = f;
                else if (f.name == "Hex_FZHTK_0") s_Chinese = f;
            }
        }

        /// <summary>Style de texte : "title" (FMBolyar) ou "body"/"bold" (Arial du jeu), taille de reference 1000 px.</summary>
        public static GUIStyle Style(string kind, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            LoadFonts();
            int px = Px(size);
            string key = Loc.Lang + "|" + kind + "|" + px + "|" + color + "|" + anchor + "|" + wrap;
            GUIStyle st;
            if (s_Text.TryGetValue(key, out st)) return st;
            st = new GUIStyle();
            Font cjk = Loc.Lang == "ja" ? s_Japanese : Loc.Lang == "zh" ? s_Chinese : null;
            if (cjk != null) st.font = cjk;
            else if (Loc.NonLatin) st.font = kind == "body" ? s_Body : (s_BodyBold ?? s_Body); // cyrillique : Arial du jeu
            else st.font = kind == "title" ? (s_Title ?? s_BodyBold) : kind == "bold" ? (s_BodyBold ?? s_Body) : s_Body;
            st.fontSize = px;
            st.normal.textColor = color;
            st.alignment = anchor;
            st.wordWrap = wrap;
            st.richText = false;
            st.clipping = TextClipping.Clip;
            s_Text[key] = st;
            return st;
        }

        /// <summary>Texte avec ombre portee (comme les libelles du jeu).</summary>
        public static void Label(Rect r, string text, GUIStyle style, bool shadow = true)
        {
            if (shadow)
            {
                Color c = style.normal.textColor;
                style.normal.textColor = new Color(0, 0, 0, 0.85f * c.a);
                GUI.Label(new Rect(r.x + Mathf.Max(1f, Scale * 1.5f), r.y + Mathf.Max(1f, Scale * 1.5f), r.width, r.height), text, style);
                style.normal.textColor = c;
            }
            GUI.Label(r, text, style);
        }

        /// <summary>
        /// Bouton dessine avec les sprites du jeu : prefixe "Generic_Button_Decorated_01" etc.
        /// Variantes : Base / Hover / Click (noms du jeu), Inactive si desactive.
        /// </summary>
        public static bool Button(Rect r, string text, string baseSprite, string hoverSprite, string clickSprite, string disabledSprite = null, bool enabled = true, int fontSize = 20)
        {
            Event e = Event.current;
            bool hover = enabled && r.Contains(e.mousePosition);
            bool down = hover && Input.GetMouseButton(0);
            string sprite = !enabled && disabledSprite != null ? disabledSprite : down ? clickSprite : hover ? hoverSprite : baseSprite;
            Sprite(r, "A_Buttons", sprite, enabled ? 1f : 0.6f);
            Label(r, text, Style("title", fontSize, enabled ? (hover ? Color.white : Gold) : Muted, TextAnchor.MiddleCenter));
            return enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        public static bool GoldButton(Rect r, string text, bool enabled = true, int size = 20)
        {
            return Button(r, text, "Generic_Button_Decorated_01_Base", "Generic_Button_Decorated_02_Hover", "Generic_Button_Decorated_03_Click", "Generic_Button_Decorated_04_Inactive", enabled, size);
        }

        public static bool GreenButton(Rect r, string text, bool enabled = true, int size = 22)
        {
            return Button(r, text, "Generic_Button_Decorated_Green_Base", "Generic_Button_Decorated_Green_Hover", "Generic_Button_Decorated_Green_Click", "Generic_Button_Decorated_04_Inactive", enabled, size);
        }

        public static bool RedButton(Rect r, string text, int size = 20)
        {
            return Button(r, text, "Generic_Button_Decorated_Red_Base", "Generic_Button_Decorated_Red_Highlight", "Generic_Button_Decorated_Red_Click", null, true, size);
        }

        public static bool OrangeButton(Rect r, string text, int size = 20)
        {
            return Button(r, text, "Generic_Button_Decorated_Orange_Base", "Generic_Button_Decorated_Orange_Hover", "Generic_Button_Decorated_Orange_Click", null, true, size);
        }

        /// <summary>Icone cliquable du jeu (trois etats : normal, survol, clic).</summary>
        public static bool Icon(Rect r, string atlas, string normal, string hover, string click, string tooltip = null)
        {
            bool over = r.Contains(Event.current.mousePosition);
            bool down = over && Input.GetMouseButton(0);
            Sprite(r, atlas, down ? click : over ? hover : normal, 1f, false);
            return GUI.Button(r, tooltip != null ? new GUIContent("", tooltip) : GUIContent.none, GUIStyle.none);
        }

        /// <summary>Onglet du jeu (Header_Tabs_*).</summary>
        public static bool Tab(Rect r, string text, bool selected)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Sprite(r, "A_Buttons", selected ? "Header_Tabs_03_On" : hover ? "Header_Tabs_02_Hover" : "Header_Tabs_01_Base");
            Label(r, text, Style("title", 17, selected ? Color.white : hover ? Gold : GoldDim, TextAnchor.MiddleCenter));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Ligne de liste selectionnable : titre + detail.</summary>
        public static bool Row(Rect r, string title, string detail, bool selected, ChampionTemplate champion = null, float rightReserve = 0f)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Sprite(r, "A_Buttons", selected ? "Button_Bar_Main_click" : hover ? "Button_Bar_Main_hover" : "Button_Bar_Main_Mid");
            float pad = Px(14);
            if (champion != null)
            {
                // Comme la fenetre "Select Deck" : bandeau du champion, nom du deck sur une bande sombre en bas.
                // Marge pour rester a l'interieur du cadre du bouton.
                float inset = Px(8);
                float bh = r.height - 2 * inset;
                float bw = Mathf.Min(bh * 3.3f, (r.width - rightReserve) * 0.55f);
                Rect banner = new Rect(r.x + inset + Px(4), r.y + inset, bw, bh);
                Portraits.DrawBanner(banner, champion);
                if (selected)
                {
                    GUI.DrawTexture(new Rect(banner.x, banner.y, banner.width, Px(2)), Solid(Gold));
                    GUI.DrawTexture(new Rect(banner.x, banner.yMax - Px(2), banner.width, Px(2)), Solid(Gold));
                }
                Rect dr2 = new Rect(banner.xMax + pad, r.y + Px(4), r.xMax - banner.xMax - 2 * pad - rightReserve, r.height - Px(8));
                Rect t2 = new Rect(dr2.x, dr2.y, dr2.width, dr2.height * 0.5f);
                Rect d2 = new Rect(dr2.x, dr2.y + dr2.height * 0.5f, dr2.width, dr2.height * 0.5f);
                Label(t2, title, Style("bold", 15, selected ? Color.white : Text));
                GUI.Label(d2, detail, Style("body", 13, selected ? Gold : Muted));
                return GUI.Button(r, GUIContent.none, GUIStyle.none);
            }
            Rect tr = new Rect(r.x + pad, r.y + Px(6), r.width - 2 * pad - rightReserve, r.height * 0.5f);
            Rect dr = new Rect(r.x + pad, r.y + r.height * 0.5f, r.width - 2 * pad - rightReserve, r.height * 0.42f);
            Label(tr, title, Style("bold", 17, selected ? Color.white : Text));
            GUI.Label(dr, detail, Style("body", 14, selected ? Gold : Muted));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private static GUIStyle s_ScrollV, s_Thumb, s_Field;
        private static string s_FieldLang;

        /// <summary>Barre de defilement verticale aux couleurs du jeu.</summary>
        public static GUIStyle ScrollBar
        {
            get
            {
                if (s_ScrollV == null)
                {
                    s_ScrollV = new GUIStyle { fixedWidth = Px(14), margin = new RectOffset(4, 0, 0, 0) };
                    s_ScrollV.normal.background = Solid(new Color(0, 0, 0, 0.35f));
                }
                return s_ScrollV;
            }
        }

        public static GUIStyle Thumb
        {
            get
            {
                if (s_Thumb == null)
                {
                    s_Thumb = new GUIStyle { fixedWidth = Px(14) };
                    s_Thumb.normal.background = Solid(new Color(GoldDim.r * 0.75f, GoldDim.g * 0.7f, GoldDim.b * 0.6f, 0.9f));
                }
                return s_Thumb;
            }
        }

        public static GUIStyle Field
        {
            get
            {
                if (s_Field == null || s_FieldLang != Loc.Lang)
                {
                    s_FieldLang = Loc.Lang;
                    s_Field = new GUIStyle(Style("body", 15, Text));
                    s_Field.padding = new RectOffset(Px(10), Px(10), Px(4), Px(4));
                    s_Field.clipping = TextClipping.Clip;
                }
                return s_Field;
            }
        }

        private static readonly Dictionary<Color, Texture2D> s_Solid = new Dictionary<Color, Texture2D>();

        public static Texture2D Solid(Color c)
        {
            Texture2D t;
            if (s_Solid.TryGetValue(c, out t) && t != null) return t;
            t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            s_Solid[c] = t;
            return t;
        }
    }
}
