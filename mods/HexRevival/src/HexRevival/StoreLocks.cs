using System;
using System.Collections.Generic;
using Game.Client;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// What can't be bought is shown before the click (user 2026-11-12, after Stuie's "Purchase could not be completed"):
    /// - BUY greyed out (disabled) while the selected product is a campaign pack whose area no champion has unlocked yet
    ///   (Store.CampaignPackUnlocked) or costs more than the player has. The game computes "not enough" itself
    ///   (UIPremiumStoreItemContext.InsufficientFunds) but its BUY button has no binding to it: it let the click through
    ///   to an error (or, for platinum, to the Buy Platinum offer).
    /// - A lock on the banner (the catalog's tile) of each campaign pack still locked, its picture darkened.
    /// The store tiles are reused by the list for other products: each one is set again from its current product.
    /// </summary>
    public class StoreLocks : MonoBehaviour
    {
        private const string LockName = "HexRevival_Lock";
        private const string BuyPath = "Panel_R/Panel_Overlay/Frame_Buy_Widget/Inset_Buy/Button_Buy";
        private static readonly Color Dimmed = new Color(0.45f, 0.45f, 0.45f, 1f);

        private UIPremiumStoreViewModel m_Store;
        private float m_NextFind, m_NextTiles;
        private bool m_BuyBlocked;
        private readonly Dictionary<UITexture, Color> m_Wallpapers = new Dictionary<UITexture, Color>();

        private void Update()
        {
            if (m_Store == null && Time.realtimeSinceStartup >= m_NextFind)
            {
                m_NextFind = Time.realtimeSinceStartup + 1f;
                m_Store = FindObjectOfType<UIPremiumStoreViewModel>();
            }
            if (m_Store == null || !m_Store.isActiveAndEnabled || m_Store.Context == null) { m_BuyBlocked = false; return; }
            try
            {
                UpdateBuy();
                if (Time.realtimeSinceStartup >= m_NextTiles)
                {
                    m_NextTiles = Time.realtimeSinceStartup + 0.25f;
                    UpdateTiles();
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [StoreLocks] " + e.Message); m_NextTiles = Time.realtimeSinceStartup + 5f; }
        }

        /// <summary>A campaign pack no champion of the account has unlocked yet (see Store.CampaignPackUnlocked).</summary>
        private static bool Locked(InventoryItemData item)
        {
            string camp = item != null ? Loot.PackOf(item) : null;
            return camp != null && !Store.CampaignPackUnlocked(camp);
        }

        /// <summary>Whether the selected product's total (the quantity or variant chosen) is more than the player has.</summary>
        private static bool TooExpensive(UIPremiumStoreItemContext it)
        {
            PlayerProfile p = GameClient.Instance != null ? GameClient.Instance.PlayerProfile : null;
            if (p == null) return false;
            long have;
            switch (it.CurrencyType)
            {
                case UIPremiumStoreViewModel.EStoreCurrency.Gold: have = p.Gold; break;
                case UIPremiumStoreViewModel.EStoreCurrency.Platinum: have = p.Platinum; break;
                case UIPremiumStoreViewModel.EStoreCurrency.SiegeSack: have = p.SiegeSacks; break;
                case UIPremiumStoreViewModel.EStoreCurrency.CosmicCoin: have = p.CosmicCoins; break;
                default: return false;
            }
            return it.TotalCost > have;
        }

        private void UpdateBuy()
        {
            Transform buy = m_Store.transform.Find(BuyPath);
            if (buy == null) return;
            UIPremiumStoreItemContext it = m_Store.Context.Catalog != null ? m_Store.Context.Catalog.SelectedItem : null;
            bool blocked = it != null && (Locked(it.ItemData) || TooExpensive(it));
            UIButton[] buttons = buy.GetComponents<UIButton>();
            foreach (UIButton b in buttons) if (b != null && b.isEnabled == blocked) b.isEnabled = !blocked;
            // The blue glow behind BUY says "you can": hidden with it.
            Transform fx = buy.parent.Find("Button_Buy_FXBlue");
            UIWidget w = fx != null ? fx.GetComponent<UIWidget>() : null;
            if (w != null && blocked) w.alpha = 0f;
            else if (w != null && m_BuyBlocked) w.alpha = 1f;
            Transform label = buy.Find("Label_Buy");
            UILabel l = label != null ? label.GetComponent<UILabel>() : null;
            if (l != null) l.color = blocked ? GameSkin.Muted : Color.white;
            m_BuyBlocked = blocked;
        }

        private void UpdateTiles()
        {
            var cat = m_Store.Context.Catalog;
            if (cat == null) return;
            UIAtlas icons = GameSkin.Atlas("A_Icons");
            foreach (NguiItemDataContext tile in m_Store.GetComponentsInChildren<NguiItemDataContext>(false))
            {
                if (tile == null || tile.transform.parent == null || tile.transform.parent.name != "Grid") continue;
                UIPremiumStoreItemContext it = tile.Index >= 0 && tile.Index < cat.Count ? cat.GetItem(tile.Index) : null;
                bool locked = it != null && Locked(it.ItemData);
                Transform overlay = tile.transform.Find("Panel_Overlay");
                Transform existing = overlay != null ? overlay.Find(LockName) : null;
                if (locked && existing == null && overlay != null && icons != null)
                {
                    UISprite s = NGUITools.AddSprite(overlay.gameObject, icons, "Icon_Lock_Large_BW", 200);
                    s.name = LockName;
                    s.width = 80;
                    s.height = 86;
                    existing = s.transform;
                }
                if (existing != null && existing.gameObject.activeSelf != locked) existing.gameObject.SetActive(locked);
                if (locked && existing != null)
                {
                    // On the middle of the pack's picture, at the left of the banner (its bounds: the tile's thumbnail).
                    Transform thumb = tile.transform.Find("Panel_Thumb");
                    if (thumb != null)
                    {
                        Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(thumb);
                        if (b.size.x > 0f) existing.localPosition = overlay.InverseTransformPoint(b.center);
                    }
                }
                Transform wp = tile.transform.Find("Panel_Wallpaper/Wallpapers/WallpaperSprite");
                UITexture tex = wp != null ? wp.GetComponent<UITexture>() : null;
                if (tex == null) continue;
                if (locked)
                {
                    if (!m_Wallpapers.ContainsKey(tex)) m_Wallpapers[tex] = tex.color;
                    tex.color = Dimmed;
                }
                else
                {
                    Color c;
                    if (m_Wallpapers.TryGetValue(tex, out c)) { tex.color = c; m_Wallpapers.Remove(tex); }
                }
            }
        }
    }
}
