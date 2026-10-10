using System;
using System.Collections.Generic;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Champion portraits, loaded the way the game does (AssetBundleObject.SetChampionPrefab):
    /// texture "championsandmercenaries/&lt;name&gt;" from the CardSets bundle, cropped on the face
    /// (m_CircleHudLayout, the round thumbnail of deck lists). Loaded on demand, cached.
    /// </summary>
    public static class Portraits
    {
        private class Item
        {
            public Texture2D Texture;
            public Rect Uv = new Rect(0, 0, 1, 1);
            public bool Requested, Failed, Loading;
        }

        private static readonly Dictionary<string, Item> s_Cache = new Dictionary<string, Item>();

        /// <summary>Texture and face area; null until loading has finished.</summary>
        public static Texture2D Get(ChampionTemplate champ, out Rect uv)
        {
            uv = new Rect(0, 0, 1, 1);
            if (champ == null || string.IsNullOrEmpty(champ.m_HudPortrait)) return null;
            string key = champ.m_HudPortrait;
            Item it;
            if (!s_Cache.TryGetValue(key, out it))
            {
                it = new Item();
                s_Cache[key] = it;
                CardTemplateLayout l = champ.m_CircleHudLayout;
                if (l != null && l.m_PortraitRadius > 0f)
                    it.Uv = new Rect(l.m_PortraitCenterX - l.m_PortraitRadius, 1f - l.m_PortraitCenterY - l.m_PortraitRadius, l.m_PortraitRadius * 2f, l.m_PortraitRadius * 2f);
                Request(champ, it);
            }
            // Texture unloaded by the game (screen change): request it again.
            if (it.Requested && !it.Loading && !it.Failed && it.Texture == null)
            {
                it.Requested = false;
                it.Failed = false;
                Request(champ, it);
            }
            uv = it.Uv;
            return it.Texture;
        }

        private static void Request(ChampionTemplate champ, Item it)
        {
            if (it.Requested) return;
            it.Requested = true;
            it.Loading = true;
            try
            {
                if (AssetBundleManager.Instance == null) { it.Failed = true; it.Loading = false; return; }
                string name = FolderUtils.GetFileNameWithoutPathOrExtension(champ.m_HudPortrait);
                AssetRequestItem req = new AssetRequestItem();
                req.Name = name;
                req.Bundle = AssetBundleFile.CardSets;
                req.SubBundle = "championsandmercenaries/" + name;
                req.Type = AssetType.Texture2D;
                req.FullFilePath = champ.m_HudPortrait;
                AssetBundleManager.Instance.RequestBundle(req, new EventDelegate(() =>
                {
                    try { it.Texture = AssetBundleManager.Instance.GetAsset(req) as Texture2D; }
                    catch { }
                    it.Loading = false;
                    if (it.Texture == null) it.Failed = true;
                }));
            }
            catch (Exception e)
            {
                it.Failed = true;
                it.Loading = false;
                Debug.LogWarning("[HexRevival] [Portraits] " + champ.m_HudPortrait + " : " + e.Message);
            }
        }

        /// <summary>
        /// Wide banner centered on the face (like the thumbnails of the collection's "Select Deck"
        /// window): image area with the on-screen width/height ratio "aspect".
        /// </summary>
        public static Texture2D GetBanner(ChampionTemplate champ, float aspect, out Rect uv)
        {
            Rect face;
            Texture2D tex = Get(champ, out face);
            uv = new Rect(0, 0, 1, 1);
            if (tex == null) return null;
            // Official framing of deck lists: m_SelectionLayout (the same as the Select Deck window).
            CardTemplateLayout sel = champ.m_SelectionLayout;
            if (sel != null && sel.m_PortraitRight > sel.m_PortraitLeft && sel.m_PortraitBottom > sel.m_PortraitTop)
            {
                uv = new Rect(sel.m_PortraitLeft, 1f - sel.m_PortraitBottom, sel.m_PortraitRight - sel.m_PortraitLeft, sel.m_PortraitBottom - sel.m_PortraitTop);
                return tex;
            }
            float texAspect = (float)tex.width / Mathf.Max(1, tex.height);
            float cx = face.center.x, cy = face.center.y;
            // Height: a bit more than the face (head and shoulders); width derived from the wanted ratio.
            float h = Mathf.Clamp(face.height * 1.05f, 0.1f, 1f);
            float w = h * aspect / texAspect;
            if (w > 1f) { w = 1f; h = w * texAspect / aspect; }
            float x = Mathf.Clamp(cx - w / 2f, 0f, 1f - w);
            float y = Mathf.Clamp(cy - h * 0.5f, 0f, 1f - h);
            uv = new Rect(x, y, w, h);
            return tex;
        }

        /// <summary>Draws a champion banner (black background while loading).</summary>
        public static void DrawBanner(Rect r, ChampionTemplate champ)
        {
            GUI.DrawTexture(r, GameSkin.Solid(new Color(0, 0, 0, 0.7f)));
            Rect uv;
            Texture2D tex = GetBanner(champ, r.width / Mathf.Max(1f, r.height), out uv);
            if (tex != null) GUI.DrawTextureWithTexCoords(r, tex, uv, true);
        }

        /// <summary>Draws the champion's face in a game frame (or an empty frame while loading).</summary>
        public static void Draw(Rect r, ChampionTemplate champ)
        {
            GUI.DrawTexture(r, GameSkin.Solid(new Color(0, 0, 0, 0.6f)));
            Rect uv;
            Texture2D tex = Get(champ, out uv);
            float inset = Mathf.Max(2f, r.width * 0.06f);
            Rect inner = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset);
            if (tex != null) GUI.DrawTextureWithTexCoords(inner, tex, uv, true);
            GameSkin.Sprite(r, "A_Frames", "Item_Slot_Frame_Rounded");
        }
    }
}
