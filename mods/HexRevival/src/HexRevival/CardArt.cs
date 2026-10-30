using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Card illustrations for the mod's windows: the card's art loaded from the game's asset bundles the way the game's
    /// card builder does (GoCardBuilder: bundle CardSets, "set bundle/image name"), cropped to its portrait area, and a
    /// small card tile (art, name, cost, frame in the card's colors).
    /// </summary>
    public static class CardArt
    {
        private class Item
        {
            public Texture2D Texture;
            public bool Requested, Loading, Failed;
            public float FailedAt;
        }

        private static readonly Dictionary<string, Item> s_Cache = new Dictionary<string, Item>();

        /// <summary>The card's art and its portrait area (null while loading or if missing).</summary>
        public static Texture2D Get(CardTemplate c, out Rect uv)
        {
            uv = new Rect(0, 0, 1, 1);
            if (c == null || string.IsNullOrEmpty(c.m_CardImagePath)) return null;
            CardTemplateLayout l = c.m_DefaultLayout;
            if (l != null && l.m_PortraitRight > l.m_PortraitLeft && l.m_PortraitBottom > l.m_PortraitTop)
                uv = new Rect(l.m_PortraitLeft, 1f - l.m_PortraitBottom, l.m_PortraitRight - l.m_PortraitLeft, l.m_PortraitBottom - l.m_PortraitTop);
            Item it;
            if (!s_Cache.TryGetValue(c.m_CardImagePath, out it)) { it = new Item(); s_Cache[c.m_CardImagePath] = it; }
            // Unloaded by the game (screen change): asked again.
            if (it.Requested && !it.Loading && !it.Failed && it.Texture == null) it.Requested = false;
            // Failed (asked while the game was still loading its bundles, at login): asked again 5 s later.
            if (it.Failed && Time.realtimeSinceStartup - it.FailedAt > 5f) { it.Failed = false; it.Requested = false; }
            if (!it.Requested) Request(c, it);
            return it.Texture;
        }

        private static void Request(CardTemplate c, Item it)
        {
            it.Requested = true;
            it.Loading = true;
            try
            {
                CardSetTemplate set;
                if (AssetBundleManager.Instance == null || !Singleton<TemplateManager>.Instance.CardSets.TryGetValue(c.m_SetId, out set) || set == null)
                {
                    // Asked before the game loaded its bundles or sets: asked again next time.
                    it.Requested = false; it.Loading = false; return;
                }
                string bundle = string.IsNullOrEmpty(set.m_BundleName) ? set.m_Name : set.m_BundleName;
                string name = FolderUtils.GetFileNameWithoutPathOrExtension(c.m_CardImagePath);
                AssetRequestItem req = new AssetRequestItem();
                req.Name = name;
                req.Bundle = AssetBundleFile.CardSets;
                req.SubBundle = bundle + "/" + name;
                req.Type = AssetType.Texture2D;
                req.FullFilePath = c.m_CardImagePath;
                AssetBundleManager.Instance.RequestBundle(req, new EventDelegate(() =>
                {
                    try { it.Texture = AssetBundleManager.Instance.GetAsset(req) as Texture2D; }
                    catch { }
                    it.Loading = false;
                    if (it.Texture == null) { it.Failed = true; it.FailedAt = Time.realtimeSinceStartup; }
                }));
            }
            catch (Exception e)
            {
                it.Failed = true;
                it.FailedAt = Time.realtimeSinceStartup;
                it.Loading = false;
                Debug.LogWarning("[HexRevival] [CardArt] " + c.m_CardImagePath + " : " + e.Message);
            }
        }

        /// <summary>The frame color of a card: its shard, gold when several, grey when none.</summary>
        public static Color FrameColor(CardTemplate c)
        {
            ECardShards s = ECardShards.Unknown;
            try { s = c.CardShards; } catch { }
            switch (s)
            {
                case ECardShards.Blood: return new Color(0.62f, 0.35f, 0.85f);
                case ECardShards.Diamond: return new Color(0.92f, 0.92f, 0.95f);
                case ECardShards.Ruby: return new Color(0.9f, 0.25f, 0.2f);
                case ECardShards.Sapphire: return new Color(0.3f, 0.55f, 0.95f);
                case ECardShards.Wild: return new Color(0.35f, 0.8f, 0.3f);
                case ECardShards.Colorless: case ECardShards.Unknown: return new Color(0.6f, 0.58f, 0.55f);
                default: return GameSkin.Gold;   // several shards
            }
        }

        /// <summary>A card tile: art, cost and name in a frame of the card's colors; true when clicked.</summary>
        public static bool Tile(Rect r, CardTemplate c, bool highlight = false)
        {
            float S = GameSkin.Scale;
            bool hover = r.Contains(Event.current.mousePosition);
            Color frame = FrameColor(c);
            GUI.DrawTexture(r, GameSkin.Solid(hover || highlight ? Color.white : frame));
            Rect inner = new Rect(r.x + 3 * S, r.y + 3 * S, r.width - 6 * S, r.height - 6 * S);
            GUI.DrawTexture(inner, GameSkin.Solid(new Color(0.06f, 0.06f, 0.08f, 1f)));
            Rect art = new Rect(inner.x, inner.y, inner.width, inner.height * 0.68f);
            Rect uv;
            Texture2D tex = Get(c, out uv);
            if (tex != null)
            {
                // Fill the art area keeping the art's proportions (cropped to the center).
                float want = art.width / Mathf.Max(1f, art.height), have = uv.width * tex.width / Mathf.Max(1f, uv.height * tex.height);
                if (have > want) { float w = uv.width * want / have; uv = new Rect(uv.x + (uv.width - w) / 2f, uv.y, w, uv.height); }
                else { float h = uv.height * have / want; uv = new Rect(uv.x, uv.y + (uv.height - h) / 2f, uv.width, h); }
                GUI.DrawTextureWithTexCoords(art, tex, uv, true);
            }
            else GUI.DrawTexture(art, GameSkin.Solid(new Color(frame.r * 0.25f, frame.g * 0.25f, frame.b * 0.25f, 1f)));
            if (c.m_ResourceCost > 0 || c.m_CardType != ECardTypes.Resource)
            {
                Rect cost = new Rect(art.x + 3 * S, art.y + 3 * S, 24 * S, 24 * S);
                GUI.DrawTexture(cost, GameSkin.Solid(new Color(0, 0, 0, 0.75f)));
                GameSkin.Label(cost, c.m_ResourceCost.ToString(), GameSkin.Style("bold", 15, Color.white, TextAnchor.MiddleCenter));
            }
            Rect name = new Rect(inner.x + 3 * S, art.yMax + 2 * S, inner.width - 6 * S, inner.yMax - art.yMax - 4 * S);
            GUI.Label(name, c.m_Name, GameSkin.Style("bold", 12, hover ? Color.white : GameSkin.Text, TextAnchor.MiddleCenter, true));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A card to show and pick: not an internal one ("#Pet_baby_spider#", hidden from players).</summary>
        public static bool IsShown(CardTemplate c)
        {
            return c != null && !string.IsNullOrEmpty(c.m_Name) && c.m_Name[0] != '#' && c.m_CardType != ECardTypes.Champion;
        }
    }
}
