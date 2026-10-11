using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client.Network.Escrow;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Network.Escrow;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local store (replaces the escrow server's catalog and purchases): the game's booster
    /// packs, paid in gold, opened as soon as they are bought.
    ///
    /// Prices: the original store sold boosters for platinum, 200 platinum ($2) for a current set
    /// pack (Progression.Src.Forum2017). Platinum cannot be earned offline, so the mod sells them
    /// for gold: the gold price is a PLACEHOLDER. Bought packs had about a 2% chance of a bonus
    /// primal pack (Src.Forum2017), kept here.
    /// </summary>
    public static class Store
    {
        /// <summary>Gold price of a booster. PLACEHOLDER (no source: gold only bought packs from players, ~22,000 on the auction house in 2016).</summary>
        public const int BoosterGoldPrice = 2000;
        /// <summary>Chance of a bonus primal pack with a bought pack. Src.Forum2017 ("about a 2% chance").</summary>
        private const float PrimalChance = 0.02f;

        private static List<StoreItem> s_Items;

        /// <summary>Catalog: one item per booster pack whose set has collectible cards.</summary>
        private static List<StoreItem> Items()
        {
            if (s_Items != null) return s_Items;
            s_Items = new List<StoreItem>();
            ulong id = 1;
            foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryCardPack pack = item as InventoryCardPack;
                if (pack == null || pack.m_CardPackType != ECardPackType.BoosterPack) continue;
                CardSetTemplate set;
                if (!Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set) || set == null) continue;
                if (Progression.RandomCard(set, Game.Shared.Mechanics.ERarity.Common, null) == null) continue;
                StoreItem s = new StoreItem();
                s.id = id++;
                s.TemplateId = pack.m_Id;
                s.Price = BoosterGoldPrice;
                s.ItemType = "Inventory";
                s.CurrencyType = "Gold";
                s.Name = pack.m_Name;
                s.PurchaseLimit = -1;
                s.SoftPurchaseLimit = -1;
                s.LocationIndex = -1;
                s.StoreTab = "";
                s.data = new Dictionary<string, object> { { "Price", s.Price }, { "ItemType", s.ItemType }, { "CurrencyType", s.CurrencyType }, { "ItemTemplateId", s.TemplateId.ToString() } };
                s_Items.Add(s);
            }
            Debug.Log("[HexRevival] [Store] " + s_Items.Count + " booster packs for sale");
            return s_Items;
        }

        /// <summary>EscrowService.GetStoreItems.Request (Harmony prefix, see Plugin.cs).</summary>
        public static void GetStoreItems(EscrowService.GetStoreItems.ResponseHandler handler)
        {
            GetStoreItemsResponse r = LocalServer.Ok<GetStoreItemsResponse>();
            r.StoreItems = new List<StoreItem>();
            foreach (StoreItem s in Items()) r.StoreItems.Add(s.Clone());
            Deliver(typeof(EscrowService.GetStoreItems), r, handler);
        }

        /// <summary>EscrowService.PurchaseItem.Request: pays in gold, opens the packs right away.</summary>
        public static void PurchaseItem(int quantity, ulong id, EscrowService.PurchaseItem.ResponseHandler handler)
        {
            PurchaseItemResponse r = LocalServer.Ok<PurchaseItemResponse>();
            r.PurchasedInventory = new List<inventory_bits>();
            r.PurchasedDeckBits = new List<deck_bits>();
            r.PurchasedCards = new List<card_instance_bits>();
            r.GrantedInventory = new List<inventory_bits>();
            r.GrantedDeckBits = new List<deck_bits>();
            r.GrantedCards = new List<card_instance_bits>();
            r.ConsumedInventory = new List<inventory_bits>();
            r.ConsumedCards = new List<card_instance_bits>();
            r.CurrencyInventory = new List<inventory_bits>();
            r.TransactionCurrencyType = "Gold";
            StoreItem item = Items().Find(s => s.id == id);
            quantity = Math.Max(1, quantity);
            InventoryCardPack pack = item != null ? Singleton<TemplateManager>.Instance.GetInventoryItem(item.TemplateId) as InventoryCardPack : null;
            CardSetTemplate set = null;
            if (pack != null) Singleton<TemplateManager>.Instance.CardSets.TryGetValue(pack.m_SetId, out set);
            long cost = item != null ? (long)item.Price * quantity : 0;
            if (set == null || Saves.Current == null || Saves.Current.Gold < cost)
            {
                SetError(r, set == null ? "TransactionFailed" : "InsufficientGold");
                r.RemainingCurrency = Saves.Current != null ? (int)Saves.Current.Gold : 0;
            }
            else
            {
                Saves.Current.Gold -= cost;
                Saves.SaveCurrent();
                r.RemainingCurrency = (int)Saves.Current.Gold;
                for (int i = 0; i < quantity; i++)
                {
                    foreach (CardTemplate t in Progression.OpenPack(set)) r.GrantedCards.AddRange(Collection.GrantSilently(t.m_Id, 1));
                    if (UnityEngine.Random.value < PrimalChance)
                    {
                        // Primal pack: 2 legendaries and 13 rares (Src.Forum2017; its legendary chest is not given).
                        for (int k = 0; k < 2; k++) AddRandom(set, Game.Shared.Mechanics.ERarity.Legendary, r.GrantedCards);
                        for (int k = 0; k < 13; k++) AddRandom(set, Game.Shared.Mechanics.ERarity.Rare, r.GrantedCards);
                        Notices.Show(Loc.Get("primal_pack"));
                    }
                }
                Debug.Log("[HexRevival] [Store] bought " + quantity + " x " + item.Name + " for " + cost + " gold");
            }
            Deliver(typeof(EscrowService.PurchaseItem), r, handler);
        }

        private static void AddRandom(CardSetTemplate set, Game.Shared.Mechanics.ERarity rarity, List<card_instance_bits> into)
        {
            CardTemplate t = Progression.RandomCard(set, rarity, null);
            if (t != null) into.AddRange(Collection.GrantSilently(t.m_Id, 1));
        }

        private static void SetError(PurchaseItemResponse r, string name)
        {
            try { r.Error = (EPurchaseItemError)Enum.Parse(typeof(EPurchaseItemError), name); }
            catch { r.Error = (EPurchaseItemError)1; }
        }

        /// <summary>Like the generated Dispatch: OnResponse event, then the request's handler (next frame).</summary>
        private static void Deliver(Type tx, object response, Delegate handler)
        {
            LocalServer.Later(() =>
            {
                FieldInfo ev = tx.GetField("OnResponse", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                Delegate d = ev != null ? ev.GetValue(null) as Delegate : null;
                if (d != null) d.DynamicInvoke(response);
                if (handler != null) handler.DynamicInvoke(response);
            });
        }
    }
}
