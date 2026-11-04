using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Client;
using Game.Client.Network.Escrow;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Network.Escrow;
using Game.Shared.Resources;
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
        private static string s_ItemsKey;   // the account settings the catalog was priced with

        /// <summary>
        /// The current account's "additional items in the store" option (Saves.Info.ExtraStore, user 2026-11-02): products
        /// the original store did not sell. For now the PvE full sets (PveFullSets) and the holiday gift boxes (also found
        /// in boosters opened during their holiday, Packs); later, items that only Kickstarter tiers or events gave.
        /// </summary>
        public static bool ExtraItems { get { return Saves.Current != null && !Saves.Current.UnlockAll && Saves.Current.ExtraStore; } }

        /// <summary>A product of the store: what it is, its price, its currency, its tab.</summary>
        private class Product
        {
            public InventoryItemData Item;
            public int Price;
            public string Currency = "Gold";
            public string Tab = "";
            /// <summary>A Full Set with the additional items: its PvE package, and the prices of its variants (VariantCount).</summary>
            public InventoryItemData Pve;
            public int[] VariantPrices;
        }

        // ------------------------------------------------------------------
        // Full Set variants (user 2026-11-02): with the additional items, a Full Set's bundle buttons choose PvP (the
        // Full Set), PvE (the set's PvE full set, PveFullSets) or both, once or x4, on two rows in place of 1, 3 or 10
        // copies (StoreVariants). Without them, a Full Set sells by 1, 2 or 4 (FullSetQuantity).
        // ------------------------------------------------------------------

        /// <summary>
        /// The variants: 0 PvP, 1 PvE, 2 PvP+PvE, then the same x4 (3 to 5): 4 Full Sets, and the PvE package with 4
        /// copies of its cards in place of 1 (its equipment, mercenaries and sleeves are unique: once either way).
        /// </summary>
        public const int VariantCount = 6;

        /// <summary>A variant's Full Sets (PvP copies) and copies of the PvE package's cards.</summary>
        public static int FullSetsOf(int variant) { return variant % 3 == 1 ? 0 : variant >= 3 ? 4 : 1; }
        public static int PveCopiesOf(int variant) { return variant % 3 == 0 ? 0 : variant >= 3 ? 4 : 1; }

        /// <summary>The variant chosen per Full Set item.</summary>
        private static readonly Dictionary<ResourceId, int> s_Variant = new Dictionary<ResourceId, int>();

        /// <summary>A Full Set sold with its variants: their prices (in the product's currency) and its PvE package.</summary>
        public static bool Variants(InventoryItemData item, out int[] prices, out InventoryItemData pve)
        {
            prices = null; pve = null;
            if (item == null) return false;
            foreach (Product p in s_Products.Values)
                if (p.Item == item && p.Pve != null) { prices = p.VariantPrices; pve = p.Pve; return true; }
            return false;
        }

        public static int VariantOf(InventoryItemData item)
        {
            int v;
            return item != null && s_Variant.TryGetValue(item.m_Id, out v) ? v : 0;
        }

        public static void SetVariant(InventoryItemData item, int v) { if (item != null) s_Variant[item.m_Id] = Mathf.Clamp(v, 0, VariantCount - 1); }

        /// <summary>"Shards of Fate Full Set (PvE)".</summary>
        public static string VariantName(InventoryItemData item, int v)
        {
            v = Mathf.Clamp(v, 0, VariantCount - 1);
            string kind = Loc.Get(VariantKeys[v % 3]);
            return Loc.Get("fullset_variant", item.m_Name.Trim(), v >= 3 ? Loc.Get("variant_x4", kind) : kind);
        }

        /// <summary>The variants' kinds (Loc keys), the x4 ones (3 to 5) as the first three.</summary>
        public static readonly string[] VariantKeys = { "variant_pvp", "variant_pve", "variant_both" };

        /// <summary>
        /// The copies of a Full Set sold without the variants (user 2026-11-02): 1, 2 or 4, the game's 3 standing for 2
        /// and its 10 for 4. Other products keep their quantity.
        /// </summary>
        public static int FullSetQuantity(InventoryItemData item, int quantity)
        {
            if (SpecialPacks.KindOf(item) != "fullset") return quantity;
            int[] prices; InventoryItemData pve;
            if (Variants(item, out prices, out pve)) return 1;
            return quantity <= 1 ? 1 : quantity <= 3 ? 2 : 4;
        }

        private static readonly Dictionary<ulong, Product> s_Products = new Dictionary<ulong, Product>();

        /// <summary>Gold per platinum: the boosters' 200 platinum (Src.Forum2017, also their base price in the game data) sell for 2000 gold. PLACEHOLDER ratio.</summary>
        public const int DefaultGoldPerPlatinum = 10;

        /// <summary>Gold per platinum of the current account (Saves.Info.GoldPerPlatinum; the default without one).</summary>
        public static int GoldPerPlatinum { get { return Saves.Current != null && Saves.Current.GoldPerPlatinum > 0 ? Saves.Current.GoldPerPlatinum : DefaultGoldPerPlatinum; } }

        /// <summary>
        /// The platinum behind a price written in gold at the default ratio (the mod's gold prices of what sold for
        /// platinum: boosters 2000, decks base price x 10, the Battlegrounds fees, special packs).
        /// </summary>
        public static int PlatinumOf(int goldAtDefault) { return Math.Max(1, goldAtDefault / DefaultGoldPerPlatinum); }

        /// <summary>What sold for platinum, in gold: the platinum price times the account's gold per platinum.</summary>
        public static int GoldOfPlatinum(int platinum) { return GoldOfPlatinum(platinum, GoldPerPlatinum); }
        public static int GoldOfPlatinum(int platinum, int goldPerPlatinum) { return Math.Max(1, platinum) * (goldPerPlatinum > 0 ? goldPerPlatinum : DefaultGoldPerPlatinum); }

        /// <summary>The current account's divisor of every gold price (Saves.Info.GoldDivisor; 1 without one).</summary>
        public static int GoldDivisor { get { return Saves.Current != null && Saves.Current.GoldDivisor > 1 ? Saves.Current.GoldDivisor : 1; } }

        /// <summary>A price that was gold in the original (stardust, Wheel of Fate spins), after the account's divisor (at least 1 gold when the price was not free).</summary>
        public static int Gold(int price) { return Gold(price, GoldDivisor); }

        /// <summary>A gold price after a divisor (at least 1 gold when the price was not free).</summary>
        public static int Gold(int price, int divisor) { return divisor > 1 && price > 0 ? Math.Max(1, price / divisor) : price; }

        /// <summary>
        /// Example prices for the account settings (Criesener, 2026-11-01): what a booster and a Common chest spin cost
        /// with these settings, as a sentence ("For example: a booster 200 gold, a Common chest spin 120 gold.").
        /// </summary>
        public static string PriceExample(bool goldPrices, int goldPerPlatinum, int divisor)
        {
            if (goldPerPlatinum < 1) goldPerPlatinum = DefaultGoldPerPlatinum;
            if (divisor < 1) divisor = 1;
            // A booster was a platinum price: gold per platinum decides it; the spin a gold price: the divisor does.
            string booster = goldPrices ? Loc.Get("reward_gold", GoldOfPlatinum(PlatinumOf(BoosterGoldPrice), goldPerPlatinum).ToString("N0"))
                : Loc.Get("reward_platinum", PlatinumOf(BoosterGoldPrice).ToString("N0"));
            string spin = Loc.Get("reward_gold", Gold(Packs.CommonSpinCost, divisor).ToString("N0"));
            return Loc.Get("price_example", booster, spin);
        }

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
        /// <summary>
        /// Gold prices of stardust (Common, Uncommon, Rare, Legendary, Promo; PLACEHOLDER: a player's memory of the original store (Criesener), which
        /// sold it for gold only; the game data has none, its base price is 1): sold only in the extended art panel's stardust store (UIExtendedArtViewModel.ShowStardustStore,
        /// which takes the stardust products of the catalog), hidden in the store (StoreItem.HideInStore).
        /// </summary>
        private static readonly int[] StardustGold = { 1000, 2000, 5000, 7000, 10000 };
        private static readonly ResourceId[] StardustKinds = { BuiltInResources.CommonStardust, BuiltInResources.UncommonStardust, BuiltInResources.RareStardust, BuiltInResources.LegendaryStardust, BuiltInResources.PromoStardust };

        /// <summary>
        /// Gold prices of the campaign packs, sold on every progression account (user 2026-11-04).
        /// Normal prices: the Howling Plains pack is the original store's "Adventure Pack 1", 1,000 gold (Src.Video: Water
        /// Elemental part 20 0:25-0:52, Nov 2016, by 1, 3 or 10, with a "Bonus chance!" badge; the bonus seen was a card,
        /// Uzume's Handmaiden, not given by the mod). The other packs were not sold: their normal prices keep the reduced
        /// ones' proportions to the Howling Plains pack (PLACEHOLDER).
        /// Reduced prices, with the account's "cheaper campaign packs" option (Saves.Info.CheapPacks): Zymmer's proposal
        /// (2026-11-02), about half a booster's contents so half its price, the earlier packs cheaper and the later ones
        /// dearer in the order the campaign reaches them (Loot.CampaignPacks), then halved (user 2026-11-02): from 300
        /// (Howling Plains) to 700 (Ruins of Kukatan).
        /// </summary>
        public static int CampaignPackGold(string pack)
        {
            int reduced = ReducedCampaignPackGold(pack);
            if (Saves.Current != null && Saves.Current.CheapPacks) return reduced;
            if (pack == Loot.AZ1Pack) return AdventurePackGold;
            return (int)Math.Round(reduced * (double)AdventurePackGold / ReducedCampaignPackGold(Loot.AZ1Pack) / 50.0) * 50;
        }

        /// <summary>The Howling Plains pack's price in the original store (see CampaignPackGold).</summary>
        public const int AdventurePackGold = 1000;

        private static int ReducedCampaignPackGold(string pack)
        {
            switch (pack)
            {
                case Loot.AZ1Pack: return 300;
                case "Tranquil Dream": return 350;
                case "Fort Romor": case "The Usurper": return 400;
                case "Smoldering Dead": return 450;
                case "Devonshire Keep": case Loot.AZ2Pack: return 500;
                case "Brutecrown Bluff": return 550;
                case "Great Machine Graveyard": return 600;
                case "The Kraken": return 650;
                default: return 700;   // Ruins of Kukatan
            }
        }

        /// <summary>
        /// Whether a campaign pack can be bought (user 2026-11-02): once a champion of this account has unlocked its area,
        /// the zone for the zone packs, the dungeon's node on the zone's map for the dungeon packs (Dungeons' map nodes).
        /// </summary>
        public static bool CampaignPackUnlocked(string pack)
        {
            switch (pack)
            {
                case Loot.AZ1Pack: return Campaign.AnyChampionReached("AZ1", null);
                case "Tranquil Dream": return Campaign.AnyChampionReached("AZ1", "Node040");
                case "Fort Romor": return Campaign.AnyChampionReached("AZ1", "Node011");
                case "The Usurper": return Campaign.AnyChampionReached("AZ1", "Node008");
                case "Smoldering Dead": return Campaign.AnyChampionReached("AZ1", "Node024");
                case "Devonshire Keep": return Campaign.AnyChampionReached("AZ1", "Node068");
                case Loot.AZ2Pack: return Campaign.AnyChampionReached("AZ2", null);
                case "Brutecrown Bluff": return Campaign.AnyChampionReached("AZ2", "Node002");
                case "Great Machine Graveyard": return Campaign.AnyChampionReached("AZ2", "Node044");
                case "The Kraken": return Campaign.AnyChampionReached("AZ2", "Node019");
                case "Ruins of Kukatan": return Campaign.AnyChampionReached("AZ2", "Node040");
                default: return true;
            }
        }

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
            bool pve = PveFullSets.Enabled;
            string key = goldPrices + "/" + GoldPerPlatinum + "/" + GoldDivisor + "/" + pve + "/" + (Saves.Current != null && Saves.Current.CheapPacks);
            if (s_Items != null && s_ItemsKey == key) return s_Items;
            s_ItemsKey = key;
            s_Items = new List<StoreItem>();
            s_Products.Clear();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            // The PvE full sets' items, made before the catalog is read (they join the game's items).
            if (pve) PveFullSets.Items();
            foreach (InventoryItemData item in new List<InventoryItemData>(tm.InventoryItems.Values))
            {
                Product p = Price(item, tm);
                if (p == null) continue;
                // Every gold price here but stardust's was a platinum price (written at the default ratio): accounts
                // without "platinum prices in gold" pay that platinum, the others its gold at the account's gold per
                // platinum. Stardust always sold for gold (Criesener, 2026-10-31): the account's divisor.
                if (p.Currency == "Gold" && item is InventoryStardust) p.Price = Gold(p.Price);
                else if (p.Currency == "Gold" && !goldPrices) { p.Currency = "Platinum"; p.Price = PlatinumOf(p.Price); }
                else if (p.Currency == "Gold") p.Price = GoldOfPlatinum(PlatinumOf(p.Price));
                if (p.Pve != null)
                {
                    // The variants' prices, converted as the Full Set's (PvE: the PvE full set's price; x4: 4 times).
                    int full = SpecialPacks.GoldPrice("fullset"), once = PveFullSets.GoldPrice;
                    int[] raw = { full, once, full + once, 4 * full, 4 * once, 4 * (full + once) };
                    p.VariantPrices = new int[VariantCount];
                    for (int k = 0; k < VariantCount; k++)
                        p.VariantPrices[k] = p.Currency == "Platinum" ? PlatinumOf(raw[k]) : GoldOfPlatinum(PlatinumOf(raw[k]));
                }
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
                s.HideInStore = item is InventoryStardust;
                s.data = new Dictionary<string, object> { { "Price", s.Price }, { "ItemType", s.ItemType }, { "CurrencyType", s.CurrencyType }, { "ItemTemplateId", s.TemplateId.ToString() } };
                s_Items.Add(s);
                s_Products[s.id] = p;
            }
            // The campaign packs after the other packs, in the order the campaign reaches them.
            Func<StoreItem, int> campOrder = i =>
            {
                Product pr;
                string camp = s_Products.TryGetValue(i.id, out pr) ? Loot.PackOf(pr.Item) : null;
                return camp != null ? Array.IndexOf(Loot.CampaignPacks, camp) : -1;
            };
            List<StoreItem> camps = s_Items.Where(i => campOrder(i) >= 0).OrderBy(campOrder).ToList();
            s_Items.RemoveAll(i => camps.Contains(i));
            s_Items.AddRange(camps);
            // The stardust store lists them in catalog order: by rarity.
            List<StoreItem> dust = s_Items.Where(i => Array.IndexOf(StardustKinds, i.TemplateId) >= 0).OrderBy(i => Array.IndexOf(StardustKinds, i.TemplateId)).ToList();
            s_Items.RemoveAll(i => dust.Contains(i));
            s_Items.AddRange(dust);
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
                int gold = item.m_BasePrice > 1 ? item.m_BasePrice * DefaultGoldPerPlatinum
                    : item.m_Name.StartsWith("Collector's Deck") ? CollectorDeckGold : OtherDeckGold;
                return new Product { Item = item, Price = gold, Tab = "collectordeck" };
            }
            InventoryStardust dust = item as InventoryStardust;
            if (dust != null)
            {
                int k = Array.IndexOf(StardustKinds, item.m_Id);
                return k < 0 ? null : new Product { Item = item, Price = StardustGold[k], Tab = "stardust" };
            }
            string special = SpecialPacks.KindOf(item);
            // A PvE full set: no product of its own, a variant of the set's Full Set (see Variants).
            if (special == "pvefullset") return null;
            if (special == "fullset")
                return new Product { Item = item, Price = SpecialPacks.GoldPrice(special), Tab = "cardpack", Pve = PveFullSets.ForFullSet(item) };
            // The campaign packs, on every progression account (user 2026-11-04; Zymmer's proposal 2026-11-02): see CampaignPackGold.
            string camp = Loot.PackOf(item);
            if (camp != null) return Saves.Current != null && !Saves.Current.UnlockAll ? new Product { Item = item, Price = CampaignPackGold(camp), Tab = "cardpack" } : null;
            // The holiday gift boxes came with opened boosters (Packs); they are products only with the additional items.
            if (special == "giftbox") return ExtraItems ? new Product { Item = item, Price = SpecialPacks.GiftBoxGold, Tab = "special" } : null;
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
            if (p != null) quantity = FullSetQuantity(p.Item, quantity);
            string currency = p != null ? p.Currency : "Gold";
            r.TransactionCurrencyType = currency;
            int variant = p != null && p.Pve != null ? VariantOf(p.Item) : 0;
            long cost = p != null ? (long)(p.Pve != null ? p.VariantPrices[variant] : p.Price) * quantity : 0;
            string camp = p != null ? Loot.PackOf(p.Item) : null;
            if (p == null || Saves.Current == null) SetError(r, "TransactionFailed");
            else if (camp != null && !CampaignPackUnlocked(camp))
            {
                SetError(r, "TransactionFailed");
                Notices.Show(Loc.Get("store_pack_locked"));
            }
            else if (!Pay(currency, cost)) SetError(r, currency == "Gold" ? "InsufficientGold" : "InsufficientCurrency");   // not InsufficientPlatinum: the client offers to buy platinum
            else
            {
                try
                {
                    for (int i = 0; i < quantity; i++)
                    {
                        for (int k = 0; k < FullSetsOf(variant); k++) Deliver(p.Item, r);
                        if (PveCopiesOf(variant) > 0) PveFullSets.Deliver(PveFullSets.SetOf(p.Pve), r, PveCopiesOf(variant));
                    }
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
            string camp = Loot.PackOf(item);
            if (camp != null)
            {
                // A campaign pack, unopened (opened later as the ones the campaign gives, Packs.GiveCampaignPack).
                inventory_bits bought = Packs.GiveCampaignPack(camp);
                if (bought != null) { Packs.ShowChest(bought.Id); Notices.Show(Loc.Get("store_pack_bought", item.m_Name.Trim())); }
                return;
            }
            if (SpecialPacks.KindOf(item) == "giftbox")
            {
                // A bought gift box: opened at once, as the other special products (its holiday's sleeve and equipment).
                List<Loot.Drop> gift = SpecialPacks.OpenGiftBox(item);
                foreach (Loot.Drop d in gift)
                {
                    inventory_bits b = Inventory.GrantSilently(d.Item);
                    if (b != null) r.GrantedInventory.Add(b);
                }
                if (gift.Count == 0)
                {
                    // Nothing left of that holiday: its price back.
                    int back = GoldOfPlatinum(PlatinumOf(SpecialPacks.GiftBoxGold));
                    Saves.Current.Gold += back;
                    Saves.SaveCurrent();
                    Notices.Show(Loc.Get("giftbox_empty", back.ToString("N0")));
                }
                return;
            }
            CardSetTemplate pveSet = PveFullSets.SetOf(item);
            if (pveSet != null) { PveFullSets.Deliver(pveSet, r, Collection.CopiesPerCard); return; }
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
