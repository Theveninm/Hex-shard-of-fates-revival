using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Breaking down and crafting (personal fork; HEX had none: its surplus went to trades and the Auction House,
    /// which an offline game does not have, Criesener, 2026-10-31).
    ///
    /// - A card's copies above 4 (above the most one of the account's decks holds, if more) break down into one gem
    ///   each, of the card's rarity and of one of its colors (a random one for a multi-color card); an artifact or a
    ///   colorless card gives Magical Ore. Never broken down: Spectral Oak, Spectral Acorn and Mystic Squirrel, which
    ///   are meant to pile up, and basic shards.
    /// - An equipment's copies above 1 break down into one part each, of its slot and rarity (a rare weapon: a Rare Grip).
    /// - A card is crafted from GemsPerCard gems of its rarity and colors (Magical Ore for artifacts and colorless
    ///   cards) and stardust of its rarity (the stardust of its extended art, see ExtendedArt.StardustFor), DustCost
    ///   by rarity. An equipment from PartsPerEquipment parts of its slot and rarity, one gem of its card (that card's
    ///   rarity and colors) and the stardust of its rarity.
    /// Rarity tiers: Common, Uncommon, Rare, Legendary (epic and promo cards count as Legendary).
    ///
    /// The gems, ores and parts are inventory items of the mod's own (the game has no such items: its gems are the
    /// socket gems, owned by everyone), added to the game's templates at login and kept in inventory.txt like any
    /// item. They are "materials" of the Stash (InventoryIngredientData, shown with the stardust's type), pictured
    /// with the game's coins (gems, ore) and an equipment of the part's slot and rarity.
    /// Not on "unlock everything" accounts and the online account: nothing to break down or to craft there.
    /// </summary>
    public static class Crafting
    {
        /// <summary>BepInEx config [Crafting] Enabled.</summary>
        public static bool Enabled = true;
        /// <summary>BepInEx config [Crafting] GemsPerCard: gems (or ores) to craft a card.</summary>
        public static int GemsPerCard = 2;
        /// <summary>BepInEx config [Crafting] PartsPerEquipment: parts to craft an equipment.</summary>
        public static int PartsPerEquipment = 1;
        /// <summary>BepInEx config [Crafting] DustCost: stardust to craft, by tier (Common, Uncommon, Rare, Legendary).</summary>
        public static int[] DustCost = { 1, 1, 1, 1 };

        /// <summary>Whether the current account can break down and craft.</summary>
        public static bool Active { get { return Enabled && Saves.Current != null && Saves.Current.Crafting && !Saves.Current.UnlockAll && !Saves.IsOnline; } }

        private static readonly ECardShards[] Colors = { ECardShards.Blood, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild, ECardShards.Diamond };
        private static readonly string[] ColorKeys = { "blood", "ruby", "sapphire", "wild", "diamond" };
        private static readonly EEquipmentType[] Slots = { EEquipmentType.Head, EEquipmentType.Chest, EEquipmentType.Gloves, EEquipmentType.Feet, EEquipmentType.Weapon, EEquipmentType.Trinket };
        private static readonly string[] SlotKeys = { "head", "chest", "gloves", "feet", "weapon", "trinket" };
        private static readonly string[] TierKeys = { "common", "uncommon", "rare", "legendary" };

        /// <summary>Spectral Oak, Spectral Acorn, Mystic Squirrel (BuiltInResources.MysticSquirrel): kept in any number.</summary>
        private static readonly HashSet<ResourceId> NeverBroken = new HashSet<ResourceId>
        {
            new ResourceId("79ba006b-5fae-44f2-b5f5-024c5d73a3b0"), new ResourceId("0cfb7ee4-bb48-4606-bd27-fa6e13ad21c4"), new ResourceId("ec42c3d2-4077-4b58-903e-aff3e7fe76c5"),
        };
        /// <summary>
        /// Spectral Acorn, Mystic Squirrel: never crafted either (they come from the Oak and the cards that make them).
        /// The Spectral Oak itself can be crafted (Criesener, 2026-10-31: there is no other way to get it offline).
        /// </summary>
        private static readonly HashSet<ResourceId> NeverCrafted = new HashSet<ResourceId>
        {
            new ResourceId("0cfb7ee4-bb48-4606-bd27-fa6e13ad21c4"), new ResourceId("ec42c3d2-4077-4b58-903e-aff3e7fe76c5"),
        };

        // ------------------------------------------------------------------
        // The materials
        // ------------------------------------------------------------------

        private static ResourceId Id(int code) { return new ResourceId(new Guid("4e0c7a00-c4af-4ce1-a000-" + code.ToString("x12"))); }
        public static ResourceId GemId(int color, int tier) { return Id(0x100 + color * 16 + tier); }
        public static ResourceId OreId(int tier) { return Id(0x200 + tier); }
        public static ResourceId PartId(int slot, int tier) { return Id(0x300 + slot * 16 + tier); }

        private static readonly ERarity[] TierRarity = { ERarity.Common, ERarity.Uncommon, ERarity.Rare, ERarity.Legendary };

        /// <summary>The rarity of one of the mod's materials (Unknown for any other item).</summary>
        public static ERarity MaterialRarity(ResourceId item)
        {
            string s = item.ToString();
            if (!s.StartsWith("4e0c7a00-c4af-4ce1-a000-", StringComparison.OrdinalIgnoreCase)) return ERarity.Unknown;
            int code;
            if (!int.TryParse(s.Substring(s.Length - 4), System.Globalization.NumberStyles.HexNumber, null, out code) || code < 0x100) return ERarity.Unknown;
            return TierRarity[Math.Min(3, code & 0xF)];
        }

        /// <summary>Whether the item is one of the crafting materials (gems, ores, parts).</summary>
        public static bool IsMaterial(ResourceId item) { return MaterialRarity(item) != ERarity.Unknown; }

        /// <summary>The info button's text: the rules, with the configured amounts.</summary>
        public static string InfoText()
        {
            bool same = DustCost[0] == DustCost[1] && DustCost[1] == DustCost[2] && DustCost[2] == DustCost[3];
            string dust = same ? Loc.Get("craft_info_dust_same", DustCost[0]) : Loc.Get("craft_info_dust_list", DustCost[0], DustCost[1], DustCost[2], DustCost[3]);
            return Loc.Get("craft_info_text", Collection.CopiesPerCard, GemsPerCard, dust, PartsPerEquipment);
        }

        private static CardSetTemplate s_MarkSet;

        /// <summary>
        /// GoInventoryIcon.SetIconFromData (postfixed): a material shows the rarity mark of the other items, the AZ1 set's
        /// symbol in the rarity's color (as an AZ1 equipment, GoInventoryIcon.SetEquipmentFromData), and the crafting
        /// materials' rounded border.
        /// </summary>
        public static void OnIcon(GoInventoryIcon icon, InventoryItemData item)
        {
            if (icon == null || item == null) return;
            ShardIcon shard = icon.GetComponent<ShardIcon>();
            CardTemplate gem = GemShard(item.m_Id);
            if (gem != null)
            {
                if (shard == null) shard = icon.gameObject.AddComponent<ShardIcon>();
                shard.Card = gem;
                shard.enabled = true;
            }
            else if (shard != null) { shard.Card = null; shard.enabled = false; }   // the icon reused for another item
            ERarity r = MaterialRarity(item.m_Id);
            if (r == ERarity.Unknown) return;
            try
            {
                if (s_MarkSet == null)
                    foreach (CardSetTemplate set in Singleton<TemplateManager>.Instance.CardSets.Values)
                        if (set != null && set.m_Name != null && set.m_Name.IndexOf("AZ1", StringComparison.OrdinalIgnoreCase) >= 0 && (s_MarkSet == null || set.m_SetNo < s_MarkSet.m_SetNo)) s_MarkSet = set;
                if (s_MarkSet != null) icon.SetSetRarity(s_MarkSet.m_SetNo, r, Game.Shared.Resources.BuiltInResources.Format_Standard.Contains(s_MarkSet.m_Id));
                icon.SetRarityBorder(r, false, true);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Crafting] icon: " + e.Message); }
        }

        private static bool s_Registered;

        /// <summary>Adds the materials to the game's templates (once, before the first profile is built).</summary>
        public static void Register()
        {
            if (s_Registered) return;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            if (tm == null || tm.InventoryItems == null || tm.InventoryItems.Count == 0) return;
            s_Registered = true;
            int n = 0;
            for (int tier = 0; tier < 4; tier++)
            {
                for (int c = 0; c < Colors.Length; c++)
                    if (Add(tm, GemId(c, tier), Loc.Get("craft_gem", Loc.Get("craft_" + TierKeys[tier]), Loc.Get("craft_" + ColorKeys[c])), Loc.Get("craft_gem_desc"), CoinTexture(tm, Colors[c] + " Coin"))) n++;
                if (Add(tm, OreId(tier), Loc.Get("craft_ore", Loc.Get("craft_" + TierKeys[tier])), Loc.Get("craft_ore_desc"), CoinTexture(tm, "Molten Coin"))) n++;
                for (int s = 0; s < Slots.Length; s++)
                    if (Add(tm, PartId(s, tier), Loc.Get("craft_part_" + SlotKeys[s], Loc.Get("craft_" + TierKeys[tier])), Loc.Get("craft_part_desc"), EquipmentTexture(tm, Slots[s], tier))) n++;
            }
            Debug.Log("[HexRevival] [Crafting] " + n + " material templates");
        }

        private static bool Add(TemplateManager tm, ResourceId id, string name, string desc, string texture)
        {
            if (tm.InventoryItems.ContainsKey(id)) return false;   // GetInventoryItem logs a warning for each unknown id
            // The stardust's type shows it in the Stash; not an InventoryStardust, which would replace the game's stardust
            // of its rarity (TemplateManager.StardustLookup).
            InventoryIngredientData d = new InventoryIngredientData();
            d.m_Id = id;
            d.m_Type = EInventoryItemType.Stardust;
            d.m_Name = name;
            d.m_Description = desc;
            d.m_TexturePath = texture ?? "";
            return tm.AddInventoryItemData(d);
        }

        /// <summary>
        /// The card whose art a gem shows: its color's basic shard (Criesener, 2026-10-31), the "<Color> Shard" of the earliest
        /// set (the later sets have their own art of it). Card art is not in the icons' bundle: see ShardIcon.
        /// </summary>
        private static CardTemplate ShardCard(TemplateManager tm, ECardShards color)
        {
            CardTemplate best = null;
            int bestNo = int.MaxValue;
            string name = color + " Shard";
            foreach (CardTemplate t in tm.Cards.Values)
            {
                if (t == null || t.m_Name == null || t.m_Name.Trim() != name || string.IsNullOrEmpty(t.m_CardImagePath)) continue;
                try { if (!t.IsBasicResource()) continue; } catch { continue; }
                CardSetTemplate set;
                int no = tm.CardSets.TryGetValue(t.m_SetId, out set) && set != null ? set.m_SetNo : int.MaxValue;
                if (best == null || no < bestNo) { best = t; bestNo = no; }
            }
            return best;
        }

        /// <summary>The card whose art a material shows: a gem its basic shard, Magical Ore the Scrios Limestone (Criesener, 2026-10-31).</summary>
        private static CardTemplate GemShard(ResourceId item)
        {
            string s = item.ToString();
            if (!s.StartsWith("4e0c7a00-c4af-4ce1-a000-", StringComparison.OrdinalIgnoreCase)) return null;
            int code;
            if (!int.TryParse(s.Substring(s.Length - 4), System.Globalization.NumberStyles.HexNumber, null, out code) || code < 0x100 || code >= 0x300) return null;
            if (code >= 0x200)
            {
                if (s_OreCard == null)
                    foreach (CardTemplate t0 in Singleton<TemplateManager>.Instance.Cards.Values)
                        if (t0 != null && t0.m_Name != null && t0.m_Name.Trim() == "Scrios Limestone" && !string.IsNullOrEmpty(t0.m_CardImagePath)) { s_OreCard = t0; break; }
                return s_OreCard;
            }
            int color = (code - 0x100) / 16;
            if (color < 0 || color >= Colors.Length) return null;
            CardTemplate t;
            if (!s_ShardCards.TryGetValue(color, out t)) s_ShardCards[color] = t = ShardCard(Singleton<TemplateManager>.Instance, Colors[color]);
            return t;
        }

        private static readonly Dictionary<int, CardTemplate> s_ShardCards = new Dictionary<int, CardTemplate>();
        private static CardTemplate s_OreCard;

        /// <summary>
        /// A gem's icon: its shard card's art, square from the middle of the card's portrait, once loaded (CardArt, the
        /// card sets' bundle; the coin of the item's texture until then and in the windows without this icon).
        /// </summary>
        private class ShardIcon : MonoBehaviour
        {
            public CardTemplate Card;
            private static FieldInfo s_IconField;

            private void Update()
            {
                if (Card == null) { enabled = false; return; }
                Rect uv;
                Texture2D t = CardArt.Get(Card, out uv);
                if (t == null) return;
                if (s_IconField == null) s_IconField = typeof(GoInventoryIcon).GetField("m_uiIcon", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                UITexture tex = s_IconField != null ? s_IconField.GetValue(GetComponent<GoInventoryIcon>()) as UITexture : null;
                if (tex == null) return;
                AssetBundleSprite loader = tex.GetComponent<AssetBundleSprite>();
                if (loader != null) Destroy(loader);   // it would put the coin back when it is done
                float w = uv.width * t.width, h = uv.height * t.height;
                if (w > h) { float nw = uv.width * h / w; uv = new Rect(uv.x + (uv.width - nw) / 2f, uv.y, nw, uv.height); }
                else if (h > w) { float nh = uv.height * w / h; uv = new Rect(uv.x, uv.y + (uv.height - nh) / 2f, uv.width, nh); }
                tex.mainTexture = t;
                tex.uvRect = uv;
                tex.color = Color.white;
                enabled = false;
            }
        }

        /// <summary>
        /// The picture of a coin (the socket gems have none): the color's coin for its gems ("Ruby Coin"...), the Molten
        /// Coin for Magical Ore.
        /// </summary>
        private static string CoinTexture(TemplateManager tm, string coin)
        {
            foreach (InventoryItemData i in tm.InventoryItems.Values)
                if (i is InventoryCoinData && i.m_Name != null && i.m_Name.Trim() == coin && !string.IsNullOrEmpty(i.m_TexturePath)) return i.m_TexturePath;
            return null;
        }

        /// <summary>The picture of an equipment of that slot and tier (the first by id, so always the same).</summary>
        private static string EquipmentTexture(TemplateManager tm, EEquipmentType slot, int tier)
        {
            InventoryEquipmentData best = null;
            foreach (InventoryItemData i in tm.InventoryItems.Values)
            {
                InventoryEquipmentData e = i as InventoryEquipmentData;
                if (e == null || e.m_EquipmentType != slot || Tier(e.m_Rarity) != tier || string.IsNullOrEmpty(e.m_TexturePath)) continue;
                if (best == null || string.CompareOrdinal(e.m_Id.ToString(), best.m_Id.ToString()) < 0) best = e;
            }
            return best != null ? best.m_TexturePath : null;
        }

        // ------------------------------------------------------------------
        // Rules
        // ------------------------------------------------------------------

        /// <summary>Tier 0-3 of a rarity (Common, Uncommon, Rare, Legendary; epic and promo as Legendary), -1 if none.</summary>
        public static int Tier(ERarity r)
        {
            switch (r)
            {
                case ERarity.Common: return 0;
                case ERarity.Uncommon: return 1;
                case ERarity.Rare: return 2;
                case ERarity.Epic:
                case ERarity.Legendary:
                case ERarity.Promo: return 3;
                default: return -1;
            }
        }

        /// <summary>The card's colors (indexes into Colors); none for an artifact or a colorless card (Magical Ore).</summary>
        private static List<int> ColorsOf(CardTemplate t)
        {
            List<int> res = new List<int>();
            if ((t.m_CardType & ECardTypes.Artifact) != 0) return res;
            ECardShards s = t.CardShards;
            for (int c = 0; c < Colors.Length; c++) if ((s & Colors[c]) != 0) res.Add(c);
            return res;
        }

        /// <summary>Whether this card can be crafted (and, unless NeverBroken, broken down).</summary>
        public static bool IsCraftable(CardTemplate t)
        {
            if (t == null || NeverCrafted.Contains(t.m_Id) || Tier(t.m_CardRarity) < 0) return false;
            try { return !t.IsBasicResource() && !t.IsChoiceCard() && t.IsOwnable(); } catch { return false; }
        }

        /// <summary>Copies of the card that can be broken down (0 if none).</summary>
        public static int Surplus(ResourceId card)
        {
            if (!Active) return 0;
            CardTemplate t = Singleton<TemplateManager>.Instance.GetCardTemplate(card);
            if (!IsCraftable(t) || NeverBroken.Contains(card)) return 0;
            return Math.Max(0, Collection.Count(card) - Math.Max(Collection.CopiesPerCard, Decks.MostInOneDeck(card)));
        }

        /// <summary>Copies of the equipment that can be broken down (0 if none).</summary>
        public static int EquipmentSurplus(ResourceId item)
        {
            if (!Active) return 0;
            InventoryEquipmentData e = Singleton<TemplateManager>.Instance.GetInventoryItem(item) as InventoryEquipmentData;
            return e != null && Tier(e.m_Rarity) >= 0 && SlotIndex(e) >= 0 ? Math.Max(0, Inventory.Count(item) - 1) : 0;
        }

        private static int SlotIndex(InventoryEquipmentData e) { return Array.IndexOf(Slots, e.m_EquipmentType); }

        private static readonly System.Random s_Rng = new System.Random();

        // ------------------------------------------------------------------
        // Breaking down
        // ------------------------------------------------------------------

        /// <summary>What a copy of the card breaks down into ("Rare Ruby Gem", "Rare Blood Gem / Rare Sapphire Gem").</summary>
        public static string Yield(CardTemplate t)
        {
            List<string> l = new List<string>();
            foreach (ResourceId id in GemsOf(t)) l.Add(Name(id));
            return string.Join(" / ", l.ToArray());
        }

        /// <summary>Breaks down up to n surplus copies of a card; returns the materials gained (empty if none).</summary>
        public static Dictionary<ResourceId, int> BreakCard(ResourceId card, int n)
        {
            Dictionary<ResourceId, int> got = new Dictionary<ResourceId, int>();
            if (BreakCardInto(card, n, got, true) > 0) Quietly(() => Give(got));
            return got;
        }

        /// <summary>Takes up to n surplus copies away and adds their gems to got (given later); returns how many.</summary>
        private static int BreakCardInto(ResourceId card, int n, Dictionary<ResourceId, int> got, bool save)
        {
            n = Math.Min(n, Surplus(card));
            if (n <= 0) return 0;
            CardTemplate t = Singleton<TemplateManager>.Instance.GetCardTemplate(card);
            int tier = Tier(t.m_CardRarity);
            List<int> colors = ColorsOf(t);
            n = Collection.Take(card, n, save);
            Dictionary<ResourceId, int> mine = new Dictionary<ResourceId, int>();
            for (int i = 0; i < n; i++)
                Merge(mine, colors.Count == 0 ? OreId(tier) : GemId(colors[s_Rng.Next(colors.Count)], tier), 1);
            foreach (KeyValuePair<ResourceId, int> kv in mine) Merge(got, kv.Key, kv.Value);
            Debug.Log("[HexRevival] [Crafting] broke down " + n + " x " + t.m_Name + ": " + Describe(mine));
            return n;
        }

        /// <summary>
        /// Runs a batch with the client's PlayerProfile.OnInventoryUpdated held back, then raised once: every inventory
        /// change raises it, and screens listening rebuild their lists each time (the packs screen after opening boosters;
        /// a thousand gems took minutes, Criesener, 2026-10-31).
        /// </summary>
        private static void Quietly(Action batch)
        {
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            FieldInfo f = typeof(PlayerProfile).GetField("OnInventoryUpdated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Delegate held = f != null ? f.GetValue(profile) as Delegate : null;
            if (held != null) f.SetValue(profile, null);
            try { batch(); }
            finally
            {
                if (held != null)
                {
                    f.SetValue(profile, held);
                    try { held.DynamicInvoke(); }
                    catch (Exception e) { Debug.LogWarning("[HexRevival] [Crafting] OnInventoryUpdated: " + e.Message); }
                }
            }
        }

        /// <summary>Breaks down the surplus copies of an equipment; returns the parts gained (empty if none).</summary>
        public static Dictionary<ResourceId, int> BreakEquipment(ResourceId item, int n) { return BreakEquipment(item, n, true); }

        /// <summary>Breaks n surplus copies of an equipment down; give false: the parts are only returned (given later).</summary>
        private static Dictionary<ResourceId, int> BreakEquipment(ResourceId item, int n, bool give)
        {
            Dictionary<ResourceId, int> got = new Dictionary<ResourceId, int>();
            n = Math.Min(n, EquipmentSurplus(item));
            if (n <= 0) return got;
            InventoryEquipmentData e = (InventoryEquipmentData)Singleton<TemplateManager>.Instance.GetInventoryItem(item);
            n = ExtendedArt.Spend(item, n, false, out _);
            if (n > 0) got[PartId(SlotIndex(e), Tier(e.m_Rarity))] = n;
            if (give) Quietly(() => Give(got));
            Debug.Log("[HexRevival] [Crafting] broke down " + n + " x " + e.m_Name + ": " + Describe(got));
            return got;
        }

        /// <summary>How many surplus copies the account has: cards and equipment.</summary>
        public static int TotalSurplus()
        {
            if (!Active) return 0;
            int n = 0;
            foreach (ResourceId c in Collection.Owned()) n += Surplus(c);
            foreach (ResourceId i in Inventory.Owned()) n += EquipmentSurplus(i);
            return n;
        }

        /// <summary>Every surplus card and equipment of the account broken down; returns the materials gained.</summary>
        public static Dictionary<ResourceId, int> BreakAll(out int cards, out int items)
        {
            Dictionary<ResourceId, int> all = new Dictionary<ResourceId, int>();
            int c0 = 0, i0 = 0;
            List<ResourceId> broken = new List<ResourceId>();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            // One save of the collection and of the inventory, one inventory update for the client (see Quietly).
            Quietly(() =>
            {
                foreach (ResourceId c in new List<ResourceId>(Collection.Owned()))
                {
                    int s = Surplus(c);
                    if (s > 0) { c0 += BreakCardInto(c, s, all, false); broken.Add(c); }
                }
                Collection.Save();
                foreach (ResourceId i in new List<ResourceId>(Inventory.Owned()))
                {
                    int s = EquipmentSurplus(i);
                    if (s <= 0) continue;
                    i0 += s;
                    foreach (KeyValuePair<ResourceId, int> kv in BreakEquipment(i, s, false)) Merge(all, kv.Key, kv.Value);
                }
                Give(all);
            });
            Resync(broken);
            cards = c0;
            items = i0;
            Debug.Log("[HexRevival] [Crafting] broke down everything: " + c0 + " card(s), " + i0 + " equipment in " + watch.ElapsedMilliseconds + " ms");
            return all;
        }

        /// <summary>Dev "craft check": per card, the save's count against the client's, and the save's instances the client lacks.</summary>
        private static string CheckClient()
        {
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            object list = typeof(PlayerProfile).GetProperty("CardList").GetValue(profile, null);
            System.Collections.IDictionary cards = (System.Collections.IDictionary)list.GetType().GetProperty("Cards").GetValue(list, null);
            Dictionary<ResourceId, int> client = new Dictionary<ResourceId, int>();
            int wrongTemplate = 0, unknown = 0, total = 0;
            foreach (object o in cards.Values)
            {
                ICard ic = o as ICard;
                if (ic == null) continue;
                total++;
                Merge(client, ic.TemplateId, 1);
                card_instance_bits mine = Collection.Instance(ic.CardId.InstanceId);
                if (mine == null) unknown++;
                else if (mine.TemplateID != ic.TemplateId) wrongTemplate++;
            }
            List<string> o2 = new List<string>();
            HashSet<ResourceId> all = new HashSet<ResourceId>(client.Keys);
            foreach (ResourceId t in Collection.Owned()) all.Add(t);
            foreach (ResourceId t in all)
            {
                int c;
                client.TryGetValue(t, out c);
                if (c != Collection.Count(t)) o2.Add(Singleton<TemplateManager>.Instance.GetCardTemplate(t).m_Name.Trim() + ": save " + Collection.Count(t) + ", client " + c);
            }
            return "client " + total + " copies, " + unknown + " with an id the save does not know, " + wrongTemplate + " with an id of another card; " + o2.Count + " card(s) differ" + (o2.Count > 0 ? ":\n" + string.Join("\n", o2.ToArray()) : "");
        }

        /// <summary>
        /// The client's collection after a break-down: copies of these cards it still lists beyond the save's count are
        /// removed from it (Criesener, 2026-10-31: after breaking every surplus down, the collection kept the old counts until
        /// the next login; not reproduced, checked here so it cannot stay wrong).
        /// </summary>
        private static void Resync(List<ResourceId> templates)
        {
            if (templates.Count == 0) return;
            try
            {
                PlayerProfile profile = GameClient.Instance.PlayerProfile;
                object list = typeof(PlayerProfile).GetProperty("CardList").GetValue(profile, null);
                System.Collections.IDictionary cards = (System.Collections.IDictionary)list.GetType().GetProperty("Cards").GetValue(list, null);
                HashSet<ResourceId> wanted = new HashSet<ResourceId>(templates);
                Dictionary<ResourceId, List<ICard>> held = new Dictionary<ResourceId, List<ICard>>();
                foreach (object o in cards.Values)
                {
                    ICard ic = o as ICard;
                    if (ic == null || !wanted.Contains(ic.TemplateId)) continue;
                    List<ICard> l;
                    if (!held.TryGetValue(ic.TemplateId, out l)) held[ic.TemplateId] = l = new List<ICard>();
                    l.Add(ic);
                }
                int fixedCount = 0;
                List<string> which = new List<string>();
                foreach (KeyValuePair<ResourceId, List<ICard>> kv in held)
                {
                    int extra = kv.Value.Count - Collection.Count(kv.Key);
                    if (extra <= 0) continue;
                    // The copies the save no longer has first.
                    kv.Value.Sort((a, b) => (Collection.Instance(a.CardId.InstanceId) == null ? 0 : 1) - (Collection.Instance(b.CardId.InstanceId) == null ? 0 : 1));
                    int strangers = 0;
                    for (int i = 0; i < extra; i++)
                    {
                        if (Collection.Instance(kv.Value[i].CardId.InstanceId) == null) strangers++;
                        profile.RemoveCardFromCollection(kv.Value[i].CardId.UID);
                        fixedCount++;
                    }
                    // Which cards and whether the client's copies had ids the save never handed out (to find where they came from).
                    which.Add(Singleton<TemplateManager>.Instance.GetCardTemplate(kv.Key).m_Name.Trim() + " " + extra + (strangers > 0 ? " (" + strangers + " unknown id)" : ""));
                }
                if (fixedCount > 0) Debug.LogWarning("[HexRevival] [Crafting] the client still listed " + fixedCount + " broken-down copies: removed: " + string.Join(", ", which.ToArray()));
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Crafting] resync: " + e.Message); }
        }

        private static void Merge(Dictionary<ResourceId, int> into, ResourceId id, int n)
        {
            int k;
            into.TryGetValue(id, out k);
            into[id] = k + n;
        }

        /// <summary>The materials to the save and the client's inventory.</summary>
        private static void Give(Dictionary<ResourceId, int> items)
        {
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            foreach (KeyValuePair<ResourceId, int> kv in items)
                foreach (inventory_bits b in Inventory.GrantMany(kv.Key, kv.Value)) profile.AddInventoryItem(b);
        }

        // ------------------------------------------------------------------
        // Crafting
        // ------------------------------------------------------------------

        /// <summary>What crafting costs: alternatives (any of these items, together count) and fixed items.</summary>
        public class Cost
        {
            /// <summary>Items that pay together: Need from any of Choices (gems of a multi-color card's colors).</summary>
            public List<ResourceId> Choices = new List<ResourceId>();
            public int Need;
            public Dictionary<ResourceId, int> Fixed = new Dictionary<ResourceId, int>();

            public bool Affordable()
            {
                int have = 0;
                foreach (ResourceId c in Choices) have += Inventory.Count(c);
                if (have < Need) return false;
                foreach (KeyValuePair<ResourceId, int> kv in Fixed) if (Inventory.Count(kv.Key) < kv.Value) return false;
                return true;
            }

            public string Text()
            {
                List<string> l = new List<string>();
                if (Need > 0)
                {
                    List<string> names = new List<string>();
                    foreach (ResourceId c in Choices) names.Add(Name(c) + " (" + Inventory.Count(c) + ")");
                    l.Add(Need + " × " + string.Join(Loc.Get("craft_or"), names.ToArray()));
                }
                foreach (KeyValuePair<ResourceId, int> kv in Fixed) l.Add(kv.Value + " × " + Name(kv.Key) + " (" + Inventory.Count(kv.Key) + ")");
                return string.Join("\n", l.ToArray());
            }

            /// <summary>Takes the cost: from the choices, the most owned first.</summary>
            public void Pay()
            {
                int left = Need;
                List<ResourceId> order = new List<ResourceId>(Choices);
                order.Sort((a, b) => Inventory.Count(b).CompareTo(Inventory.Count(a)));
                foreach (ResourceId c in order)
                {
                    if (left <= 0) break;
                    left -= ExtendedArt.Spend(c, Math.Min(left, Inventory.Count(c)), false, out _);
                }
                foreach (KeyValuePair<ResourceId, int> kv in Fixed) ExtendedArt.Spend(kv.Key, kv.Value, false, out _);
            }
        }

        private static ResourceId StardustOfTier(int tier)
        {
            switch (tier)
            {
                case 0: return Game.Shared.Resources.BuiltInResources.CommonStardust;
                case 1: return Game.Shared.Resources.BuiltInResources.UncommonStardust;
                case 2: return Game.Shared.Resources.BuiltInResources.RareStardust;
                default: return Game.Shared.Resources.BuiltInResources.LegendaryStardust;
            }
        }

        /// <summary>The gems (or ores) of a card's tier and colors.</summary>
        private static List<ResourceId> GemsOf(CardTemplate t)
        {
            int tier = Tier(t.m_CardRarity);
            List<int> colors = ColorsOf(t);
            List<ResourceId> res = new List<ResourceId>();
            if (colors.Count == 0) res.Add(OreId(tier));
            foreach (int c in colors) res.Add(GemId(c, tier));
            return res;
        }

        /// <summary>The cost of a card (null if it cannot be crafted).</summary>
        public static Cost CardCost(CardTemplate t)
        {
            if (!IsCraftable(t)) return null;
            int tier = Tier(t.m_CardRarity);
            Cost c = new Cost { Choices = GemsOf(t), Need = GemsPerCard };
            ResourceId dust = ExtendedArt.StardustFor(t);
            if (dust.IsValid && DustCost[tier] > 0) c.Fixed[dust] = DustCost[tier];
            return c;
        }

        /// <summary>Copies of the card that can still be crafted (up to 4 owned).</summary>
        public static int Missing(ResourceId card)
        {
            if (!Active || !IsCraftable(Singleton<TemplateManager>.Instance.GetCardTemplate(card))) return 0;
            return Math.Max(0, Collection.CopiesPerCard - Collection.Count(card));
        }

        /// <summary>Crafts one copy of a card; false if it is not missing or not affordable.</summary>
        public static bool CraftCard(ResourceId card)
        {
            CardTemplate t = Singleton<TemplateManager>.Instance.GetCardTemplate(card);
            Cost c = CardCost(t);
            if (c == null || Missing(card) <= 0 || !c.Affordable()) return false;
            c.Pay();
            Collection.Grant(card, 1);
            Debug.Log("[HexRevival] [Crafting] crafted " + t.m_Name);
            return true;
        }

        private static Dictionary<ResourceId, CardTemplate> s_CardOfEquipment;

        /// <summary>The card an equipment changes (CardTemplate.m_EquipmentSlots), null if none.</summary>
        public static CardTemplate CardOf(InventoryEquipmentData e)
        {
            if (s_CardOfEquipment == null)
            {
                s_CardOfEquipment = new Dictionary<ResourceId, CardTemplate>();
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                    if (t != null && t.m_EquipmentSlots != null)
                        foreach (ResourceId id in t.m_EquipmentSlots)
                            if (!s_CardOfEquipment.ContainsKey(id)) s_CardOfEquipment[id] = t;
            }
            CardTemplate c;
            return e != null && s_CardOfEquipment.TryGetValue(e.m_Id, out c) ? c : null;
        }

        /// <summary>The cost of an equipment (null if it cannot be crafted).</summary>
        public static Cost EquipmentCost(InventoryEquipmentData e)
        {
            if (e == null || Tier(e.m_Rarity) < 0 || SlotIndex(e) < 0) return null;
            CardTemplate card = CardOf(e);
            int tier = Tier(e.m_Rarity);
            Cost c = new Cost();
            if (card != null && Tier(card.m_CardRarity) >= 0) { c.Choices = GemsOf(card); c.Need = 1; }
            c.Fixed[PartId(SlotIndex(e), tier)] = PartsPerEquipment;
            if (DustCost[tier] > 0) c.Fixed[StardustOfTier(tier)] = DustCost[tier];
            return c;
        }

        /// <summary>Crafts an equipment the account does not own; false if owned or not affordable.</summary>
        public static bool CraftEquipment(ResourceId item)
        {
            if (!Active) return false;
            InventoryEquipmentData e = Singleton<TemplateManager>.Instance.GetInventoryItem(item) as InventoryEquipmentData;
            Cost c = EquipmentCost(e);
            if (c == null || Inventory.Count(item) > 0 || !c.Affordable()) return false;
            c.Pay();
            inventory_bits b = Inventory.GrantSilently(item);
            if (b != null) GameClient.Instance.PlayerProfile.AddInventoryItem(b);
            Debug.Log("[HexRevival] [Crafting] crafted " + e.m_Name);
            return true;
        }

        // ------------------------------------------------------------------
        // Texts
        // ------------------------------------------------------------------

        public static string Name(ResourceId item)
        {
            InventoryItemData d = Singleton<TemplateManager>.Instance.GetInventoryItem(item);
            return d != null ? (d.Name ?? d.m_Name).Trim() : item.ToString();
        }

        public static string Describe(Dictionary<ResourceId, int> items)
        {
            List<string> l = new List<string>();
            foreach (KeyValuePair<ResourceId, int> kv in items) l.Add(kv.Value + " × " + Name(kv.Key));
            return l.Count > 0 ? string.Join(", ", l.ToArray()) : "-";
        }

        // ------------------------------------------------------------------
        // Dev
        // ------------------------------------------------------------------

        private static CardTemplate FindCard(string name)
        {
            CardTemplate best = null;
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                if (t != null && t.m_Name != null && t.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    if (best == null || Collection.Count(t.m_Id) > Collection.Count(best.m_Id) || (IsCraftable(t) && !IsCraftable(best))) best = t;
            return best;
        }

        private static InventoryEquipmentData FindEquipment(string name)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i is InventoryEquipmentData && i.m_Name != null && i.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return (InventoryEquipmentData)i;
            return null;
        }

        /// <summary>
        /// Dev "craft": list | break &lt;card&gt; | breakall | make &lt;card&gt; | cost &lt;card&gt; | ebreak &lt;equipment&gt; |
        /// emake &lt;equipment&gt; | ecost &lt;equipment&gt; | give &lt;n&gt; (n of every material and stardust) | items.
        /// </summary>
        public static string Command(string[] parts)
        {
            string sub = parts.Length > 1 ? parts[1] : "list";
            string arg = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "";
            StringBuilder sb = new StringBuilder();
            switch (sub)
            {
                case "show":
                case "eshow":
                case "ui":
                    return CraftUi.Command(sub, arg);
                case "list":
                    sb.Append("active ").Append(Active).Append('\n');
                    foreach (ResourceId c in Collection.Owned())
                    {
                        int s = Surplus(c);
                        if (s > 0) sb.Append("card ").Append(Singleton<TemplateManager>.Instance.GetCardTemplate(c).m_Name).Append(": ").Append(s).Append(" surplus\n");
                    }
                    foreach (ResourceId i in Inventory.Owned())
                    {
                        int s = EquipmentSurplus(i);
                        if (s > 0) sb.Append("equipment ").Append(Name(i)).Append(": ").Append(s).Append(" surplus\n");
                        else if (Name(i) != null && i.ToString().StartsWith("4e0c7a00")) sb.Append("material ").Append(Name(i)).Append(": ").Append(Inventory.Count(i)).Append('\n');
                    }
                    return sb.ToString();
                case "break":
                {
                    CardTemplate t = FindCard(arg);
                    if (t == null) return "no card " + arg;
                    return t.m_Name + ": " + Describe(BreakCard(t.m_Id, Surplus(t.m_Id)));
                }
                case "breakall":
                {
                    int cards, items;
                    Dictionary<ResourceId, int> got = BreakAll(out cards, out items);
                    return cards + " card(s), " + items + " equipment: " + Describe(got);
                }
                case "cost":
                case "make":
                {
                    CardTemplate t = FindCard(arg);
                    if (t == null) return "no card " + arg;
                    Cost c = CardCost(t);
                    if (c == null) return t.m_Name + " cannot be crafted";
                    if (sub == "cost") return t.m_Name + " (owned " + Collection.Count(t.m_Id) + ", missing " + Missing(t.m_Id) + ", affordable " + c.Affordable() + "):\n" + c.Text();
                    return CraftCard(t.m_Id) ? "crafted " + t.m_Name + " (now " + Collection.Count(t.m_Id) + ")" : "not crafted:\n" + c.Text();
                }
                case "ebreak":
                {
                    InventoryEquipmentData e = FindEquipment(arg);
                    return e == null ? "no equipment " + arg : e.m_Name + ": " + Describe(BreakEquipment(e.m_Id, EquipmentSurplus(e.m_Id)));
                }
                case "ecost":
                case "emake":
                {
                    InventoryEquipmentData e = FindEquipment(arg);
                    if (e == null) return "no equipment " + arg;
                    Cost c = EquipmentCost(e);
                    if (c == null) return e.m_Name + " cannot be crafted";
                    CardTemplate card = CardOf(e);
                    if (sub == "ecost") return e.m_Name + " (" + e.m_EquipmentType + " " + e.m_Rarity + ", card " + (card != null ? card.m_Name : "-") + ", owned " + Inventory.Count(e.m_Id) + ", affordable " + c.Affordable() + "):\n" + c.Text();
                    return CraftEquipment(e.m_Id) ? "crafted " + e.m_Name : "not crafted:\n" + c.Text();
                }
                case "check":   // dev: cards whose copies differ between the save and the client (and instances the client lacks)
                    return CheckClient();
                case "dupall":   // dev: "craft dupall <n>" = n more copies of every card the account owns (many surpluses at once)
                {
                    int n = arg.Length > 0 ? int.Parse(arg) : 3;
                    Dictionary<ResourceId, int> all = new Dictionary<ResourceId, int>();
                    foreach (ResourceId c in Collection.Owned()) if (IsCraftable(Singleton<TemplateManager>.Instance.GetCardTemplate(c))) all[c] = n;
                    Collection.Grant(all);
                    return "gave " + n + " more of " + all.Count + " cards";
                }
                case "give":
                {
                    int n = arg.Length > 0 ? int.Parse(arg) : 5;
                    Dictionary<ResourceId, int> all = new Dictionary<ResourceId, int>();
                    for (int tier = 0; tier < 4; tier++)
                    {
                        for (int c = 0; c < Colors.Length; c++) all[GemId(c, tier)] = n;
                        all[OreId(tier)] = n;
                        for (int s = 0; s < Slots.Length; s++) all[PartId(s, tier)] = n;
                        all[StardustOfTier(tier)] = n;
                    }
                    Give(all);
                    return "gave " + n + " of " + all.Count + " items";
                }
                case "items":
                    for (int tier = 0; tier < 4; tier++)
                    {
                        InventoryItemData g = Singleton<TemplateManager>.Instance.GetInventoryItem(GemId(1, tier));
                        InventoryItemData p = Singleton<TemplateManager>.Instance.GetInventoryItem(PartId(4, tier));
                        sb.Append(g != null ? g.Name + " [" + g.m_TexturePath + "]" : "-").Append(" | ").Append(p != null ? p.Name + " [" + p.m_TexturePath + "]" : "-").Append('\n');
                    }
                    return sb.ToString();
                case "mark":
                    foreach (CardSetTemplate set in Singleton<TemplateManager>.Instance.CardSets.Values)
                        if (set != null && set.m_Name != null && set.m_Name.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0) sb.Append(set.m_Name).Append(" no=").Append(set.m_SetNo).Append('\n');
                    return sb.ToString();
            }
            return "craft list | break <card> | breakall | cost/make <card> | ebreak/ecost/emake <equipment> | give <n> | items";
        }
    }
}
