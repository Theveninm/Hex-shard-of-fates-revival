using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Client;
using Game.Client.Network.Escrow;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Network.Escrow;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Local store (replaces the escrow server's catalog and purchases): the game's booster packs, decks,
    /// primal packs and battleboards for gold, and the Prize Exchange for Siege Sacks and Cosmic Coins.
    /// Bought packs go to the inventory, opened on the game's Pack Opening screen with their chest (Packs).
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
        internal const float PrimalChance = 0.02f;

        private static List<StoreItem> s_Items;
        private static bool s_ItemsInGold;

        /// <summary>A product of the store: what it is, its price, its currency, its tab.</summary>
        private class Product
        {
            public InventoryItemData Item;
            public int Price;
            public string Currency = "Gold";
            public string Tab = "";
        }

        private static readonly Dictionary<ulong, Product> s_Products = new Dictionary<ulong, Product>();

        /// <summary>Gold per platinum: the boosters' 200 platinum (Src.Forum2017, also their base price in the game data) sell for 2000 gold. PLACEHOLDER ratio.</summary>
        public const int GoldPerPlatinum = 10;

        // Prize Exchange (Src.HexTCG2018: "Introducing Cosmic Coins and Siege Sacks", May 2018).
        private static readonly Dictionary<string, KeyValuePair<string, int>> Exchange = new Dictionary<string, KeyValuePair<string, int>>
        {
            { "Siege Pack", new KeyValuePair<string, int>("SiegeSack", 100) },
            { "Molten Coin", new KeyValuePair<string, int>("SiegeSack", 1200) },
            { "Sanctum Of Suffering Battleboard", new KeyValuePair<string, int>("SiegeSack", 5000) },   // "the Sisters of Suffering battleboard"
            { "Cosmic Coin Pack", new KeyValuePair<string, int>("CosmicCoin", 100) },
            { "Great Machine Graveyard Battleboard", new KeyValuePair<string, int>("CosmicCoin", 5000) },
            { "Blood Coin", new KeyValuePair<string, int>("CosmicCoin", 1200) },
            { "Diamond Coin", new KeyValuePair<string, int>("CosmicCoin", 1200) },
            { "Ruby Coin", new KeyValuePair<string, int>("CosmicCoin", 1200) },
            { "Sapphire Coin", new KeyValuePair<string, int>("CosmicCoin", 1200) },
            { "Wild Coin", new KeyValuePair<string, int>("CosmicCoin", 1200) },
        };

        /// <summary>PLACEHOLDER gold prices of products without a known price.</summary>
        private const int CollectorDeckGold = 25000, OtherDeckGold = 10000, PrimalPackGold = 30000, BattleboardGold = 10000;
        /// <summary>PLACEHOLDER gold price of a Kismet's Reserve pack (2 PvE cards, 4 equipment and a mercenary, see Zymmer.KismetBundle).</summary>
        private const int KismetPackGold = 20000;

        /// <summary>
        /// Catalog, by tab (the client sorts products into tabs by their type, or by StoreTab):
        /// Booster Packs: every booster pack of a set with cards; Decks: the game's deck products
        /// (InventoryStarterPack: starter, signature, collector's and Dueling Pit decks); Prize Exchange: the Siege
        /// Sack and Cosmic Coin products; Special: primal packs, battleboards and the Kismet's Reserve packs.
        /// </summary>
        private static List<StoreItem> Items()
        {
            bool goldPrices = Saves.Current == null || Saves.Current.GoldPrices;
            if (s_Items != null && s_ItemsInGold == goldPrices) return s_Items;
            s_ItemsInGold = goldPrices;
            s_Items = new List<StoreItem>();
            s_Products.Clear();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (InventoryItemData item in tm.InventoryItems.Values)
            {
                Product p = Price(item, tm);
                if (p == null) continue;
                // Accounts without "platinum prices in gold": the platinum price (every gold price here is one).
                if (!goldPrices && p.Currency == "Gold") { p.Currency = "Platinum"; p.Price = Math.Max(1, p.Price / GoldPerPlatinum); }
                StoreItem s = new StoreItem();
                s.id = (ulong)(s_Items.Count + 1);
                s.TemplateId = item.m_Id;
                s.Price = p.Price;
                s.ItemType = "Inventory";
                s.CurrencyType = p.Currency;
                s.Name = item.m_Name;
                s.Description = "";
                s.ShortDesc = "";
                s.LongDesc = "";
                s.PurchaseLimit = -1;
                s.SoftPurchaseLimit = -1;
                s.LocationIndex = -1;
                s.StoreTab = p.Tab;
                s.data = new Dictionary<string, object> { { "Price", s.Price }, { "ItemType", s.ItemType }, { "CurrencyType", s.CurrencyType }, { "ItemTemplateId", s.TemplateId.ToString() } };
                s_Items.Add(s);
                s_Products[s.id] = p;
            }
            Debug.Log("[HexRevival] [Store] " + s_Items.Count + " products for sale");
            return s_Items;
        }

        /// <summary>The product made of an item (null: not sold).</summary>
        private static Product Price(InventoryItemData item, TemplateManager tm)
        {
            if (item == null || string.IsNullOrEmpty(item.m_Name)) return null;
            KeyValuePair<string, int> ex;
            if (Exchange.TryGetValue(item.m_Name.Trim(), out ex))
                return new Product { Item = item, Currency = ex.Key, Price = ex.Value, Tab = ex.Key == "SiegeSack" ? "siege" : "ladder" };
            InventoryStarterPack deck = item as InventoryStarterPack;
            if (deck != null)
            {
                if (!item.m_AvailableForPurchase || tm.GetDeckTemplate(deck.m_DeckTemplateId) == null) return null;
                // The game data's base price is the platinum price (the boosters' 200 matches Src.Forum2017).
                int gold = item.m_BasePrice > 1 ? item.m_BasePrice * GoldPerPlatinum
                    : item.m_Name.StartsWith("Collector's Deck") ? CollectorDeckGold : OtherDeckGold;
                return new Product { Item = item, Price = gold, Tab = "collectordeck" };
            }
            string special = SpecialPacks.KindOf(item);
            if (special != null) return new Product { Item = item, Price = SpecialPacks.GoldPrice(special), Tab = special == "common" || special == "fullset" ? "cardpack" : "special" };
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack != null)
            {
                CardSetTemplate set;
                if (!tm.CardSets.TryGetValue(pack.m_SetId, out set) || set == null) return null;
                if (Progression.RandomCard(set, Game.Shared.Mechanics.ERarity.Common, null) == null) return null;
                if (pack.m_CardPackType == ECardPackType.BoosterPack) return new Product { Item = item, Price = BoosterGoldPrice, Tab = "cardpack" };
                if (pack.m_CardPackType == ECardPackType.PrimalPack && Progression.RandomCard(set, Game.Shared.Mechanics.ERarity.Legendary, null) != null)
                    return new Product { Item = item, Price = PrimalPackGold, Tab = "special" };
                return null;
            }
            if (item is InventoryGameboardData && !item.m_Name.StartsWith("Basic")) return new Product { Item = item, Price = BattleboardGold, Tab = "special" };
            if (Zymmer.IsKismetPack(item) && Zymmer.KismetBundle(item.m_Name).Count > 0) return new Product { Item = item, Price = KismetPackGold, Tab = "special" };
            return null;
        }

        /// <summary>EscrowService.GetStoreItems.Request (Harmony prefix, see Plugin.cs).</summary>
        public static void GetStoreItems(EscrowService.GetStoreItems.ResponseHandler handler)
        {
            GetStoreItemsResponse r = LocalServer.Ok<GetStoreItemsResponse>();
            r.StoreItems = new List<StoreItem>();
            // Not StoreItem.Clone(): it only copies rawData (the server bytes), which local items do not have.
            r.StoreItems.AddRange(Items());
            LocalServer.Answer(typeof(EscrowService.GetStoreItems), r, handler);
        }

        /// <summary>EscrowService.PurchaseItem.Request: pays, delivers (packs are opened right away).</summary>
        public static void PurchaseItem(int quantity, ulong id, EscrowService.PurchaseItem.ResponseHandler handler)
        {
            Debug.Log("[HexRevival] [Store] purchase request: " + quantity + " x product " + id);
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
            Items();
            Product p;
            s_Products.TryGetValue(id, out p);
            quantity = Math.Max(1, quantity);
            string currency = p != null ? p.Currency : "Gold";
            r.TransactionCurrencyType = currency;
            long cost = p != null ? (long)p.Price * quantity : 0;
            if (p == null || Saves.Current == null) SetError(r, "TransactionFailed");
            else if (!Pay(currency, cost)) SetError(r, currency == "Gold" ? "InsufficientGold" : "InsufficientCurrency");   // not InsufficientPlatinum: the client offers to buy platinum
            else
            {
                try
                {
                    for (int i = 0; i < quantity; i++) Deliver(p.Item, r);
                    Debug.Log("[HexRevival] [Store] bought " + quantity + " x " + p.Item.m_Name + " for " + cost + " " + currency);
                }
                catch (Exception e)
                {
                    // Not left unanswered: the store would stay on "Processing..." forever.
                    Debug.LogError("[HexRevival] [Store] delivering " + p.Item.m_Name + " failed: " + e);
                    SetError(r, "TransactionFailed");
                }
            }
            if (r.Error != EPurchaseItemError.Ok) Debug.Log("[HexRevival] [Store] purchase of product " + id + " refused: " + r.Error);
            // The client sets its gold from RemainingCurrency for gold, its platinum otherwise (the other currencies
            // come back as their inventory stacks).
            r.RemainingCurrency = Saves.Current == null ? 0 : (int)(currency == "Gold" ? Saves.Current.Gold : Saves.Current.Platinum);
            if (currency == "SiegeSack") r.CurrencyInventory.Add(Siege.SackStack(Owner));
            if (currency == "CosmicCoin") r.CurrencyInventory.Add(CosmicStack(Owner));
            LocalServer.Answer(typeof(EscrowService.PurchaseItem), r, handler);
        }

        private static UID Owner { get { return GameClient.Instance.PlayerProfile.ReckoningID; } }

        private static bool Pay(string currency, long cost)
        {
            switch (currency)
            {
                case "Gold":
                    if (Saves.Current.Gold < cost) return false;
                    Saves.Current.Gold -= cost;
                    break;
                case "Platinum":
                    if (Saves.Current.Platinum < cost) return false;
                    Saves.Current.Platinum -= cost;
                    break;
                case "CosmicCoin":
                    if (Saves.Current.CosmicCoins < cost) return false;
                    Saves.Current.CosmicCoins -= (int)cost;
                    break;
                case "SiegeSack":
                    return Siege.SpendSacks((int)cost);
                default:
                    return false;
            }
            Saves.SaveCurrent();
            return true;
        }

        /// <summary>
        /// A store deck's sleeves (given if not owned yet): the deck's own (Src.Game) and a collector deck's "Obtained in
        /// the [deck] Collector Deck" (Src.HexCodex).
        /// </summary>
        internal static List<ResourceId> DeckSleeves(InventoryItemData item, DeckTemplate t)
        {
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            List<ResourceId> sleeves = new List<ResourceId>();
            if (t.m_DeckSleeve.IsValid && tm.GetInventoryItem(t.m_DeckSleeve) is InventoryDeckSleeve && Sleeves.HasPicture(tm.GetInventoryItem(t.m_DeckSleeve))) sleeves.Add(t.m_DeckSleeve);
            foreach (InventoryDeckSleeve sl in Sleeves.Matching("^Obtained in the .+ Collector Deck"))
            {
                string deckName = System.Text.RegularExpressions.Regex.Replace(sl.m_Name.Trim(), " Sleeve$", "");
                if (item.m_Name.IndexOf(deckName, StringComparison.OrdinalIgnoreCase) >= 0 && !sleeves.Contains(sl.m_Id)) sleeves.Add(sl.m_Id);
            }
            return sleeves;
        }

        /// <summary>One bought product, into the response (opened packs as granted cards: the loot window).</summary>
        private static void Deliver(InventoryItemData item, PurchaseItemResponse r)
        {
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            InventoryStarterPack deckPack = item as InventoryStarterPack;
            if (deckPack != null)
            {
                // A deck: its cards, and the deck itself, ready to play (named as in the store).
                DeckTemplate t = tm.GetDeckTemplate(deckPack.m_DeckTemplateId);
                foreach (TemplateCardStack st in t.m_DeckResources) r.PurchasedCards.AddRange(Collection.GrantSilently(st.m_idTemplate, st.m_Count));
                r.PurchasedDeckBits.Add(Decks.CreateFromTemplate(item.m_Name, t));
                foreach (ResourceId id in DeckSleeves(item, t))
                    if (!Sleeves.Owned(id)) { inventory_bits sb = Inventory.GrantSilently(id); if (sb != null) r.GrantedInventory.Add(sb); }
                return;
            }
            if (SpecialPacks.Deliver(item, r)) return;
            InventoryCardPack pack = item as InventoryCardPack;
            if (pack != null)
            {
                // To the inventory: opened on the Pack Opening screen, with its chest (see Packs).
                inventory_bits bought = Inventory.GrantSilently(pack.m_Id);
                if (bought != null) r.PurchasedInventory.Add(bought);
                if (pack.m_CardPackType == ECardPackType.BoosterPack && UnityEngine.Random.value < PrimalChance)
                {
                    ResourceId primal = PrimalOf(pack.m_SetId);
                    inventory_bits bonus = primal.IsValid ? Inventory.GrantSilently(primal) : null;
                    if (bonus != null) { r.GrantedInventory.Add(bonus); Notices.Show(Loc.Get("primal_pack")); }
                }
                return;
            }
            if (Zymmer.IsKismetPack(item)) { DeliverKismet(item, r); return; }
            if (item.m_Name == "Siege Pack") { PrizePacks.OpenSiegePack(r); return; }
            if (item.m_Name == "Cosmic Coin Pack") { PrizePacks.OpenCosmicPack(r); return; }
            // Battleboards and coins: kept in the inventory.
            inventory_bits inv = Inventory.GrantSilently(item.m_Id);
            if (inv != null) r.PurchasedInventory.Add(inv);
        }

        /// <summary>Dev: "store products [text]" = the catalog's products (name, tab, price, currency) whose name holds the text.</summary>
        public static string Products(string text)
        {
            Items();
            List<string> o = new List<string>();
            foreach (KeyValuePair<ulong, Product> kv in s_Products)
                if (string.IsNullOrEmpty(text) || kv.Value.Item.m_Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    o.Add(kv.Key + " " + kv.Value.Item.m_Name.Trim() + " [" + kv.Value.Tab + "] " + kv.Value.Price + " " + kv.Value.Currency);
            return o.Count + " product(s)" + (o.Count > 0 ? ":\n" + string.Join("\n", o.ToArray()) : "");
        }

        /// <summary>Dev: "store open <product id>" = delivers one product without paying (as bought), and lists what it gave.</summary>
        public static string DevOpen(ulong id)
        {
            Items();
            Product p;
            if (!s_Products.TryGetValue(id, out p)) return "no product " + id;
            PurchaseItemResponse r = LocalServer.Ok<PurchaseItemResponse>();
            r.PurchasedInventory = new List<inventory_bits>(); r.PurchasedDeckBits = new List<deck_bits>(); r.PurchasedCards = new List<card_instance_bits>();
            r.GrantedInventory = new List<inventory_bits>(); r.GrantedDeckBits = new List<deck_bits>(); r.GrantedCards = new List<card_instance_bits>();
            Deliver(p.Item, r);
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            List<string> o = new List<string>();
            foreach (card_instance_bits c in r.PurchasedCards.Concat(r.GrantedCards))
            {
                CardTemplate t = tm.GetCardTemplate(c.TemplateID);
                o.Add(t != null ? t.m_Name.Trim() + " (" + t.m_CardRarity + ", " + t.CardShards + ")" : c.TemplateID.ToString());
            }
            foreach (inventory_bits i in r.PurchasedInventory.Concat(r.GrantedInventory))
            {
                InventoryItemData d = tm.GetInventoryItem(i.TemplateID);
                o.Add(d != null ? d.m_Name.Trim() + " [" + d.GetType().Name + "]" : i.TemplateID.ToString());
            }
            Saves.SaveCurrent();
            return p.Item.m_Name.Trim() + ": " + o.Count + " item(s)\n" + string.Join("\n", o.ToArray());
        }

        /// <summary>A Kismet's Reserve pack: its theme's cards, equipment and mercenary (Src.Zymmer + Src.Game, see Zymmer.KismetBundle).</summary>
        internal static void DeliverKismet(InventoryItemData item, PurchaseItemResponse r)
        {
            foreach (Loot.Drop d in Zymmer.KismetBundle(item.m_Name))
            {
                if (d.Card != null) { r.GrantedCards.AddRange(Collection.GrantSilently(d.Card.m_Id, 1)); continue; }
                InventoryMercenaryData m = Singleton<TemplateManager>.Instance.GetInventoryItem(d.Item) as InventoryMercenaryData;
                if (m != null) { Mercenaries.Grant(m, r); continue; }
                inventory_bits inv = Inventory.GrantSilently(d.Item);
                if (inv != null) r.GrantedInventory.Add(inv);
            }
            // "Available in the [name] Kismet's Reserve Pack" (Src.HexCodex): its sleeve, once, in 1 pack in 5 (PLACEHOLDER).
            string short_ = item.m_Name.Replace("Kismet's Reserve Pack", "").Replace("Pack", "").Trim();
            if (short_.Length > 0 && UnityEngine.Random.value < 0.2f)
            {
                InventoryDeckSleeve sl = Sleeves.Unowned(Sleeves.Matching("^Available in the " + System.Text.RegularExpressions.Regex.Escape(short_) + " Kismet's Reserve Pack"));
                inventory_bits sb = sl != null ? Inventory.GrantSilently(sl.m_Id) : null;
                if (sb != null) r.GrantedInventory.Add(sb);
            }
        }

        /// <summary>The set's primal pack item (invalid if it has none).</summary>
        internal static ResourceId PrimalOf(ResourceId set)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryCardPack p = i as InventoryCardPack;
                if (p != null && p.m_SetId == set && p.m_CardPackType == ECardPackType.PrimalPack) return p.m_Id;
            }
            return ResourceId.Invalid;
        }

        // ------------------------------------------------------------------
        // Cosmic Coins (an inventory currency, like Siege Sacks)
        // ------------------------------------------------------------------

        private const ulong CosmicItemId = 900000002UL;

        public static inventory_bits CosmicCoinItem(UID owner)
        {
            return Saves.Current != null && Saves.Current.CosmicCoins > 0 ? CosmicStack(owner) : null;
        }

        private static inventory_bits CosmicStack(UID owner)
        {
            inventory_bits inv = new inventory_bits();
            inv.Id = CosmicItemId;
            inv.TemplateID = Game.Shared.Resources.BuiltInResources.CurrencyCosmicCoin;
            inv.ItemQuantity = Saves.Current != null ? Saves.Current.CosmicCoins : 0;
            inv.ClaimDate = DateTime.MinValue;
            inv.EscrowStatus = "";
            inv.OwnerReckId = owner.GetInstanceId();
            return inv;
        }

        private static void SetError(PurchaseItemResponse r, string name)
        {
            try { r.Error = (EPurchaseItemError)Enum.Parse(typeof(EPurchaseItemError), name); }
            catch { r.Error = (EPurchaseItemError)1; }
        }

    }
}
