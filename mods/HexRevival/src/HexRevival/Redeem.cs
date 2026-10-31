using System;
using System.Collections.Generic;
using Game.Client;
using Game.Client.Network.Escrow;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Escrow;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Redeem codes of the store (RedeemCode, replaced here).
    ///
    /// The Kickstarter reward tiers (KS-SUPPORTER ... KS-PRODUCER: their cards, boosters, sleeves, mercenaries, starter
    /// decks and equipment, from Criesener's own HEX server project, which rebuilt them from the Kickstarter page) and
    /// codes for the convention and event items (GenCon, holidays, anniversaries...), which only came from those events.
    /// Each code can be redeemed once per account (Saves.Claims "redeem:CODE").
    ///
    /// The client adds GoldDelta/PlatinumDelta to its balance and shows ItemTemplateIds in the loot window; it
    /// does not add those items to the inventory, so items and cards are handed to it here (as a server pushes them),
    /// and CardBits stays empty.
    /// </summary>
    public static class Redeem
    {
        /// <summary>Bounds the loot window for the "every card" tiers (as Criesener's server does); the grant itself is complete.</summary>
        private const int MaxRewardWindowIds = 1000;

        private class Card
        {
            public readonly string Guid, Name;
            public readonly int Qty;
            public Card(string guid, string name, int qty) { Guid = guid.ToLowerInvariant(); Name = name; Qty = qty; }
        }

        private class Equipment
        {
            public readonly string Set;
            public readonly int Count;
            public readonly string[] Rarities;
            public Equipment(string set, int count, params string[] rarities) { Set = set; Count = count; Rarities = rarities; }
        }

        private class Bundle
        {
            public string Title;
            public string[] Includes = new string[0];
            public Dictionary<string, int> Boosters = new Dictionary<string, int>();
            public string[] StarterDecks = new string[0];
            public Card[] Cards = new Card[0];
            public string[] Items = new string[0];
            public Equipment RandomEquipment;
            public int AllCards;
            public bool AllEquipment;
            public string[] GiftCodes = new string[0];
            public int Gold, Platinum;
            public bool Expired;
        }

        /// <summary>The codes (Criesener's server's Kickstarter bundles; the event codes below).</summary>
        private static readonly Dictionary<string, Bundle> Codes = new Dictionary<string, Bundle>
        {
            { "KS-SUPPORTER", new Bundle
            {
                Title = "Kickstarter Supporter Tier",
            } },
            { "KS-SQUIRE", new Bundle
            {
                Title = "Kickstarter Squire Tier",
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 10 } },
                StarterDecks = new[] { "Human", "Orc" },
                Cards = new[]
                {
                    new Card("abd634cb-d56e-43f5-9344-5cefbbcb19e8", "Lady Cassandra", 1),
                },
                Items = new[] { "Million Dollar Deck Sleeve" },
            } },
            { "KS-WARRIOR", new Bundle
            {
                Title = "Kickstarter Warrior Tier",
                Includes = new[] { "KS-SQUIRE" },
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 25 } },
                StarterDecks = new[] { "Human", "Orc", "Dwarf" },
                Cards = new[]
                {
                    new Card("95e3096e-15fa-4bff-a3af-a44df6dc7c2c", "Replicator's Gambit (Kickstarter alternate art)", 1),
                    new Card("36097418-d845-44b7-910d-16f79f17e459", "Slithering Marauder (Void Marauder)", 1),
                },
                Items = new[] { "Zoltog", "Warrior Sleeve" },
            } },
            { "KS-CAPTAIN", new Bundle
            {
                Title = "Kickstarter Captain Tier",
                Includes = new[] { "KS-WARRIOR" },
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 40 } },
                StarterDecks = new[] { "Human", "Orc", "Dwarf", "Elf" },
                Cards = new[]
                {
                    new Card("c87b17fc-f80b-48b7-9314-3646b9cbcc34", "Princess Victoria (Kickstarter alternate art)", 1),
                    new Card("44f75ecb-0d9c-4b79-b84d-e8eab190648c", "Prospero, Sylvan Enchanter", 1),
                },
                Items = new[] { "Burgamot Ebonrock", "Captain Deck Sleeve" },
            } },
            { "KS-KNIGHT", new Bundle
            {
                Title = "Kickstarter Knight Tier",
                Includes = new[] { "KS-CAPTAIN" },
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 60 } },
                Cards = new[]
                {
                    new Card("9e7ac16c-5f12-4148-a5a1-0bbf67db2ccd", "Extinction (Kickstarter alternate art)", 1),
                    new Card("abadad68-7350-49f8-bf98-3434a2171c0e", "Darad, the Scourgeblade (Scourge Knight)", 1),
                },
                Items = new[] { "Mooof", "Knight Sleeve" },
            } },
            { "KS-CHAMPION", new Bundle
            {
                Title = "Kickstarter Champion Tier",
                Includes = new[] { "KS-KNIGHT" },
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 85 } },
                Cards = new[]
                {
                    new Card("ebe97d0d-97bd-46f4-bab4-e22783167a09", "Ragefire (Kickstarter alternate art)", 1),
                    new Card("9d8c42f9-bc5a-4a42-b904-0f98ae83945e", "The Wrath of Zakiir", 1),
                },
                Items = new[] { "Mephistophelius", "Champion Deck Sleeve" },
            } },
            { "KS-KING", new Bundle
            {
                Title = "Kickstarter King Tier",
                Includes = new[] { "KS-CHAMPION" },
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 150 } },
                Cards = new[]
                {
                    new Card("ebb25772-3156-4a99-b8f5-806314f966ed", "Pack Raptor (Kickstarter alternate art)", 1),
                    new Card("79ba006b-5fae-44f2-b5f5-024c5d73a3b0", "Spectral Oak (Spectral Lotus Garden)", 1),
                },
                Items = new[] { "Monkey of the Nine Tails", "King Sleeve" },
            } },
            { "KS-COLLECTOR", new Bundle
            {
                Title = "Kickstarter Collector Tier",
                Includes = new[] { "KS-KING" },
                Cards = new[]
                {
                    new Card("abd634cb-d56e-43f5-9344-5cefbbcb19e8", "Lady Cassandra", 2),
                    new Card("95e3096e-15fa-4bff-a3af-a44df6dc7c2c", "Replicator's Gambit (Kickstarter alternate art)", 2),
                    new Card("36097418-d845-44b7-910d-16f79f17e459", "Slithering Marauder (Void Marauder)", 2),
                    new Card("c87b17fc-f80b-48b7-9314-3646b9cbcc34", "Princess Victoria (Kickstarter alternate art)", 2),
                    new Card("44f75ecb-0d9c-4b79-b84d-e8eab190648c", "Prospero, Sylvan Enchanter", 2),
                    new Card("9e7ac16c-5f12-4148-a5a1-0bbf67db2ccd", "Extinction (Kickstarter alternate art)", 2),
                    new Card("abadad68-7350-49f8-bf98-3434a2171c0e", "Darad, the Scourgeblade (Scourge Knight)", 2),
                    new Card("ebe97d0d-97bd-46f4-bab4-e22783167a09", "Ragefire (Kickstarter alternate art)", 2),
                    new Card("9d8c42f9-bc5a-4a42-b904-0f98ae83945e", "The Wrath of Zakiir", 2),
                    new Card("ebb25772-3156-4a99-b8f5-806314f966ed", "Pack Raptor (Kickstarter alternate art)", 2),
                    new Card("79ba006b-5fae-44f2-b5f5-024c5d73a3b0", "Spectral Oak (Spectral Lotus Garden)", 2),
                },
                Items = new[] { "Collector Deck Sleeve" },
            } },
            { "KS-RAIDLEADER", new Bundle
            {
                Title = "Kickstarter Raid Leader Tier",
                Includes = new[] { "KS-KING" },
                Items = new[] { "Raid Leader Sleeve" },
            } },
            { "KS-DUNGEONCRAWLER", new Bundle
            {
                Title = "Kickstarter Dungeon Crawler Tier",
                Includes = new[] { "KS-KING" },
                Items = new[] { "Dungeon Crawler Deck Sleeve" },
                RandomEquipment = new Equipment("Shards of Fate", 20, "Rare", "Legendary"),
            } },
            { "KS-GUILDMASTER", new Bundle
            {
                Title = "Kickstarter Guild Master Tier",
                Includes = new[] { "KS-KING" },
                Items = new[] { "Guild Master Sleeve" },
                GiftCodes = new[] { "KS-GUILDGIFT" },
            } },
            { "KS-GUILDGIFT", new Bundle
            {
                Title = "Kickstarter Guild Master gift",
                Boosters = new Dictionary<string, int> { { "Shards of Fate", 30 } },
            } },
            { "KS-PROPLAYER", new Bundle
            {
                Title = "Kickstarter Pro Player Tier",
                Includes = new[] { "KS-KING" },
                Items = new[] { "Pro-Player Sleeve" },
            } },
            { "KS-GRANDKING", new Bundle
            {
                Title = "Kickstarter Grand King Tier",
                Includes = new[] { "KS-COLLECTOR", "KS-RAIDLEADER", "KS-DUNGEONCRAWLER", "KS-GUILDMASTER", "KS-PROPLAYER" },
                Items = new[] { "Grand King Deck Sleeve" },
            } },
            { "KS-DRAGONLORD", new Bundle
            {
                Title = "Kickstarter Dragon Lord Tier",
                Includes = new[] { "KS-GRANDKING" },
                Items = new[] { "Dragon Lord Deck Sleeve" },
                AllCards = 1,
            } },
            { "KS-PRIMAL", new Bundle
            {
                Title = "Kickstarter Primal Tier",
                Includes = new[] { "KS-GRANDKING" },
                Items = new[] { "Primal Deck Sleeve" },
                AllCards = 4,
            } },
            { "KS-IMMORTAL", new Bundle
            {
                Title = "Kickstarter Immortal Tier",
                Includes = new[] { "KS-GRANDKING" },
                Items = new[] { "Immortal Sleeve" },
                AllCards = 4,
                AllEquipment = true,
            } },
            { "KS-PRODUCER", new Bundle
            {
                Title = "Kickstarter Producer Tier",
                Includes = new[] { "KS-IMMORTAL" },
                Items = new[] { "Producer Deck Sleeve" },
            } },
            { "KS-EXTRA-MERCS", new Bundle
            {
                Title = "Kickstarter mercenaries outside the pledge tiers",
                Items = new[] { "Aethynia", "B.E.B.O.", "Cardboard Tube Samurai", "Glorfenblort" },
            } },

            // Convention and event items: by their item descriptions and design notes (Src.Game).
            { "GENCON2013", new Bundle
            {
                Title = "GenCon 2013",
                Items = new[] { "GenCon 2013 Deck Sleeve", "Portensio of Avon", "Gax, the Sly Roller" },
            } },
            { "GENCON2014", new Bundle
            {
                Title = "GenCon 2014",
                Items = new[] { "GenCon 2014 Deck Sleeve", "Brosi-Buk" },
            } },
            { "GENCON2015", new Bundle
            {
                Title = "GenCon 2015",
                // "Convention 2015 Attendee": the sleeve's convention is not named; the equipment is GenCon 2015's.
                Items = new[] { "Convention 2015 Deck Sleeve", "Elemental Gloves", "Stormcoat", "Lantern of the Cloud Queen", "Slippers of the Cloud Queen",
                    "Umbrella of the Cloud King", "Crown of the Cloud King" },
            } },
            { "GENCON2016", new Bundle
            {
                Title = "GenCon 2016",
                Items = new[] { "Professor's Sash", "Hookah of Enlightenment", "Shiitake Shiv", "Mushroom Cap", "Psilocybin Slippers", "Porcini Paws" },
            } },
            { "RPC2015", new Bundle { Title = "Role Play Convention 2015", Items = new[] { "RPC 2015 Attendee" } } },
            { "ANIMEFRIENDS2014", new Bundle { Title = "Anime Friends 2014", Items = new[] { "Anime Friends 2014 Deck Sleeve" } } },
            { "CONVENTIONS", new Bundle
            {
                Title = "All convention rewards",
                Includes = new[] { "GENCON2013", "GENCON2014", "GENCON2015", "GENCON2016", "RPC2015", "ANIMEFRIENDS2014" },
            } },
            { "CONVOCATION2017", new Bundle { Title = "2017 Convocation", Items = new[] { "Tealwing" } } },
            { "TOURNAMENT", new Bundle { Title = "Tournament reward", Items = new[] { "Salty Sam, the Swashbuckler" } } },
            { "HOLIDAY2014", new Bundle { Title = "2014 Holiday", Items = new[] { "2014 Holiday Sleeve" } } },
            { "NIGHTOFBELLS", new Bundle
            {
                Title = "Night of Bells",
                Items = new[] { "Night of Bells 2015 Sleeve", "2016 Night of Bells Sleeve", "2017 Night of Bells Sleeve" },
            } },
            { "HAUNTEDHARVEST", new Bundle
            {
                Title = "Haunted Harvest",
                Items = new[] { "Haunted Harvest 2015 Sleeve", "2016 Haunted Harvest Sleeve", "2017 Haunted Harvest Sleeve" },
            } },
            { "FEASTOFABUNDANCE", new Bundle
            {
                Title = "Feast of Abundance",
                Items = new[] { "Feast of Abundance 2015 Sleeve", "2016 Feast of Abundance Sleeve", "2017 Feast of Abundance Sleeve", "2018 Feast of Abundance Sleeve" },
            } },
            { "ARDORDAY", new Bundle
            {
                Title = "Ardor Day",
                Items = new[] { "Ardor Day 2016 Sleeve", "2017 Ardor Day Sleeve", "2018 Ardor Day Sleeve" },
            } },
            { "QUESTMAS", new Bundle { Title = "Questmas", Items = new[] { "2017 Questmas Sleeve", "2018 Questmas Sleeve" } } },
            { "SEAFESTIVAL", new Bundle { Title = "The Sea Festival", Items = new[] { "2017 The Sea Festival Sleeve" } } },
            { "ANNIVERSARY", new Bundle
            {
                Title = "Anniversaries",
                Items = new[] { "1 Year Anniversary Sleeve", "2 Year Anniversary Sleeve", "4 Year Anniversary Sleeve", "5 Year Anniversary Sleeve" },
            } },
            { "CHARITY", new Bundle { Title = "Charity", Items = new[] { "Charity 2014 Sleeve", "Charity 2016 Sleeve", "Charity 2017 Sleeve" } } },
            { "INVITATIONAL2016", new Bundle
            {
                Title = "2016 Invitational",
                Items = new[] { "2016 Invitational Sleeve", "2016 Invitational Champion Sleeve", "2016 Invitational Qualifier Sleeve",
                    "2016 Invitational Qualifier Top 64 Sleeve", "2016 Invitational Poster Sleeve", "2016 Invitational Poster Sleeve (B&W)" },
            } },
            { "EXALTED", new Bundle
            {
                Title = "Exalted",
                Items = new[] { "Exalted 2014 Sleeve", "Exalted 2015 Sleeve", "Exalted 2016 Sleeve",
                    "Exalted 2017 Sleeve - Cabalist", "Exalted 2017 Sleeve - Commander", "Exalted 2017 Sleeve - Pathfinder" },
            } },
            { "ALPHABETA", new Bundle { Title = "Alpha and beta players", Items = new[] { "Alpha Player Sleeve", "Beta Player Sleeve", "Level Up Beta Sleeve" } } },
            { "COSMIC", new Bundle { Title = "Cosmic", Items = new[] { "Cosmic Crown Showdown Sleeve", "Cosmic Flashpaw Sleeve" } } },
            { "EVENTS", new Bundle
            {
                Title = "All event rewards",
                Includes = new[] { "CONVOCATION2017", "TOURNAMENT", "HOLIDAY2014", "NIGHTOFBELLS", "HAUNTEDHARVEST", "FEASTOFABUNDANCE", "ARDORDAY", "QUESTMAS",
                    "SEAFESTIVAL", "ANNIVERSARY", "CHARITY", "INVITATIONAL2016", "EXALTED", "ALPHABETA", "COSMIC" },
            } },
        };

        /// <summary>The starter decks a tier names, by race.</summary>
        private static readonly Dictionary<string, ResourceId> StarterDecks = new Dictionary<string, ResourceId>
        {
            { "Human", BuiltInResources.HumanStarterDeckTemplateId },
            { "Orc", BuiltInResources.OrcStarterDeckTemplateId },
            { "Dwarf", BuiltInResources.DwarfStarterDeckTemplateId },
            { "Elf", BuiltInResources.ElfStarterDeckTemplateId },
        };

        /// <summary>Uppercase letters and digits only: the client strips "-" from typed codes (as Criesener's server does).</summary>
        public static string Key(string code)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char ch in (code ?? "").ToUpperInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        private static string Find(string code)
        {
            string key = Key(code);
            foreach (string c in Codes.Keys) if (Key(c) == key) return c;
            return null;
        }

        /// <summary>A bundle with everything it includes: lists are unioned, counts take the maximum (as Criesener's server does).</summary>
        private static Bundle Resolve(string code, HashSet<string> seen)
        {
            Bundle merged = new Bundle();
            Bundle own;
            if (code == null || !seen.Add(code) || !Codes.TryGetValue(code, out own)) return merged;
            foreach (string inc in own.Includes) Merge(merged, Resolve(inc, seen));
            Merge(merged, own);
            merged.Title = own.Title;
            merged.Expired = own.Expired;
            return merged;
        }

        private static void Merge(Bundle into, Bundle b)
        {
            foreach (KeyValuePair<string, int> kv in b.Boosters)
            {
                int n;
                into.Boosters.TryGetValue(kv.Key, out n);
                into.Boosters[kv.Key] = Math.Max(n, kv.Value);
            }
            into.StarterDecks = Union(into.StarterDecks, b.StarterDecks);
            into.Items = Union(into.Items, b.Items);
            into.GiftCodes = Union(into.GiftCodes, b.GiftCodes);
            List<Card> cards = new List<Card>(into.Cards);
            foreach (Card c in b.Cards)
            {
                int i = cards.FindIndex(x => x.Guid == c.Guid);
                if (i < 0) cards.Add(c);
                else if (c.Qty > cards[i].Qty) cards[i] = c;
            }
            into.Cards = cards.ToArray();
            if (b.RandomEquipment != null && (into.RandomEquipment == null || b.RandomEquipment.Count > into.RandomEquipment.Count)) into.RandomEquipment = b.RandomEquipment;
            into.AllCards = Math.Max(into.AllCards, b.AllCards);
            into.AllEquipment |= b.AllEquipment;
            into.Gold = Math.Max(into.Gold, b.Gold);
            into.Platinum = Math.Max(into.Platinum, b.Platinum);
        }

        private static string[] Union(string[] a, string[] b)
        {
            List<string> res = new List<string>(a);
            foreach (string s in b) if (!res.Contains(s)) res.Add(s);
            return res.ToArray();
        }

        /// <summary>EscrowService.RedeemCode.Request: checks the code, grants it once per account, answers as a server would.</summary>
        public static void Request(string code, EscrowService.RedeemCode.ResponseHandler handler)
        {
            RedeemCodeResponse r = new RedeemCodeResponse();
            r.ItemTemplateIds = new List<ResourceId>();
            r.CardBits = new List<card_instance_bits>();
            r.StarterDecksBits = new List<deck_bits>();
            string found = Find(code);
            string claim = "redeem:" + Key(code);
            if (Saves.Current == null || found == null) Refuse(r, "redeem_invalid");
            else if (Codes[found].Expired) Refuse(r, "redeem_expired");
            else if (Saves.Current.Claims.Contains(claim)) Refuse(r, "redeem_used");
            else
            {
                Saves.Current.Claims.Add(claim);
                Bundle b = Resolve(found, new HashSet<string>());
                List<string> got = Grant(b, r);
                Saves.SaveCurrent();
                Notices.Show(Loc.Get("redeem_done", b.Title));
                Debug.Log("[HexRevival] [Redeem] " + found + ": " + string.Join(", ", got.ToArray()));
            }
            if (r.Error != ERedeemCodeError.Ok) Debug.Log("[HexRevival] [Redeem] '" + code + "' refused: " + r.ErrorMessage);
            LocalServer.Answer(typeof(EscrowService.RedeemCode), r, handler);
        }

        private static void Refuse(RedeemCodeResponse r, string key)
        {
            r.Error = ERedeemCodeError.RedeemFailed;
            r.ErrorMessage = Loc.Get(key);
        }

        /// <summary>Grants a resolved bundle into the save and to the client; returns what was given (for the log).</summary>
        private static List<string> Grant(Bundle b, RedeemCodeResponse r)
        {
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            List<string> got = new List<string>();

            if (b.Gold > 0) { Saves.Current.Gold += b.Gold; r.GoldDelta = b.Gold; got.Add(b.Gold + " gold"); }
            if (b.Platinum > 0) { Saves.Current.Platinum += b.Platinum; r.PlatinumDelta = b.Platinum; got.Add(b.Platinum + " platinum"); }

            // Boosters: into the inventory, opened on the packs screen (each with its chest, see Packs).
            foreach (KeyValuePair<string, int> kv in b.Boosters)
            {
                CardSetTemplate set = Progression.FindSet(kv.Key);
                InventoryItemData pack = set != null ? BoosterOf(set.m_Id) : null;
                if (pack == null) { Debug.LogWarning("[HexRevival] [Redeem] no booster pack for set '" + kv.Key + "'"); continue; }
                for (int i = 0; i < kv.Value; i++) GiveItem(pack, r, profile);
                got.Add(kv.Value + " " + kv.Key + " booster packs");
            }

            foreach (string name in b.Items)
            {
                InventoryItemData item = ItemNamed(name);
                if (item == null) { Debug.LogWarning("[HexRevival] [Redeem] unknown inventory item '" + name + "'"); continue; }
                GiveItem(item, r, profile);
                got.Add(name);
            }

            List<InventoryEquipmentData> equipment = new List<InventoryEquipmentData>();
            if (b.RandomEquipment != null)
            {
                CardSetTemplate set = Progression.FindSet(b.RandomEquipment.Set);
                List<InventoryEquipmentData> pool = new List<InventoryEquipmentData>();
                foreach (InventoryItemData i in tm.InventoryItems.Values)
                {
                    InventoryEquipmentData e = i as InventoryEquipmentData;
                    // The set's chest equipment (the equipment the set's chests give).
                    if (e != null && set != null && e.m_IsLive && e.m_SetId == set.m_Id && (e.m_DesignNotes ?? "").ToLowerInvariant().Contains("chest")
                        && Array.IndexOf(b.RandomEquipment.Rarities, e.m_Rarity.ToString()) >= 0) pool.Add(e);
                }
                for (int k = 0; k < b.RandomEquipment.Count && pool.Count > 0; k++) equipment.Add(pool[UnityEngine.Random.Range(0, pool.Count)]);
                got.Add(b.RandomEquipment.Count + " random " + string.Join("/", b.RandomEquipment.Rarities) + " equipment");
            }
            if (b.AllEquipment)
            {
                int before = equipment.Count;
                foreach (InventoryItemData i in tm.InventoryItems.Values)
                {
                    InventoryEquipmentData e = i as InventoryEquipmentData;
                    if (e != null && e.m_IsLive) equipment.Add(e);
                }
                got.Add("all " + (equipment.Count - before) + " pieces of equipment");
            }
            foreach (InventoryEquipmentData e in equipment) GiveItem(e, r, profile);

            // Cards: told to the client directly (a server pushes them too), listed in the loot window.
            Dictionary<ResourceId, int> cards = new Dictionary<ResourceId, int>();
            foreach (Card c in b.Cards)
            {
                ResourceId id = new ResourceId(new Guid(c.Guid));
                if (tm.GetCardTemplate(id) == null) { Debug.LogWarning("[HexRevival] [Redeem] unknown card " + c.Name + " " + c.Guid); continue; }
                cards[id] = c.Qty;
                got.Add(c.Qty + "x " + c.Name);
            }
            if (b.AllCards > 0)
            {
                int n = 0;
                foreach (CardTemplate t in PvPCards()) { int q; cards.TryGetValue(t.m_Id, out q); cards[t.m_Id] = Math.Max(q, b.AllCards); n++; }
                got.Add(b.AllCards + "x every PvP card (" + n + ")");
            }
            foreach (KeyValuePair<ResourceId, int> kv in cards)
                for (int i = 0; i < kv.Value; i++) AddRewardId(r, kv.Key);

            // Starter decks: once each; overlapping tiers only add the cards again (as on Criesener's server).
            List<string> decks = new List<string>();
            List<KeyValuePair<string, DeckTemplate>> newDecks = new List<KeyValuePair<string, DeckTemplate>>();
            foreach (string race in b.StarterDecks)
            {
                ResourceId id;
                DeckTemplate t;
                if (!StarterDecks.TryGetValue(race, out id) || !tm.Decks.TryGetValue(id, out t) || t == null) { Debug.LogWarning("[HexRevival] [Redeem] unknown starter deck '" + race + "'"); continue; }
                foreach (TemplateCardStack st in t.m_DeckResources) { int q; cards.TryGetValue(st.m_idTemplate, out q); cards[st.m_idTemplate] = q + st.m_Count; }
                string name = race + " Starter Deck";
                if (!Decks.HasNamed(name)) newDecks.Add(new KeyValuePair<string, DeckTemplate>(name, t));
                decks.Add(race);
            }
            Collection.Grant(cards);   // the cards and the decks' cards, saved once
            // The decks after their cards: made before, they held copies of their own, which the client (PlayerProfile.AddDeck
            // adds a deck's cards to the collection) showed on top of the granted ones until the next login (user, 2026-10-31).
            foreach (KeyValuePair<string, DeckTemplate> d in newDecks) r.StarterDecksBits.Add(Decks.CreateFromTemplate(d.Key, d.Value));
            if (decks.Count > 0) got.Add("starter decks: " + string.Join(", ", decks.ToArray()));
            foreach (string gift in b.GiftCodes) got.Add("gift code for friends: " + gift);
            return got;
        }

        /// <summary>One inventory item into the save and to the client (mercenaries with their deck and party slot), listed in the loot window.</summary>
        private static void GiveItem(InventoryItemData item, RedeemCodeResponse r, PlayerProfile profile)
        {
            List<inventory_bits> given = new List<inventory_bits>();
            InventoryMercenaryData m = item as InventoryMercenaryData;
            if (m != null)
            {
                PurchaseItemResponse holder = new PurchaseItemResponse();
                holder.GrantedInventory = new List<inventory_bits>();
                Mercenaries.Grant(m, holder);
                given.AddRange(holder.GrantedInventory);
            }
            else
            {
                inventory_bits inv = Inventory.GrantSilently(item.m_Id);
                if (inv != null) given.Add(inv);
            }
            foreach (inventory_bits inv in given) profile.AddInventoryItem(inv);
            AddRewardId(r, item.m_Id);
        }

        private static void AddRewardId(RedeemCodeResponse r, ResourceId id)
        {
            if (r.ItemTemplateIds.Count < MaxRewardWindowIds) r.ItemTemplateIds.Add(id);
        }

        /// <summary>An inventory item by name, preferring the Kickstarter one when several share it (as Criesener's server does).</summary>
        private static InventoryItemData ItemNamed(string name)
        {
            List<InventoryItemData> matches = new List<InventoryItemData>();
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i != null && (i.m_Name ?? "").Trim() == name) matches.Add(i);
            if (matches.Count == 0) return null;
            List<InventoryItemData> labeled = matches.FindAll(i => (i.m_Description ?? "").ToLowerInvariant().Contains("kickstarter"));
            if (labeled.Count > 0) matches = labeled;
            matches.Sort((a, c) => string.CompareOrdinal(a.m_Id.ToString().ToLowerInvariant(), c.m_Id.ToString().ToLowerInvariant()));
            return matches[0];
        }

        /// <summary>The booster pack of a card set.</summary>
        private static InventoryItemData BoosterOf(ResourceId setId)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryCardPack p = i as InventoryCardPack;
                if (p != null && p.m_SetId == setId && p.m_CardPackType == ECardPackType.BoosterPack) return p;
            }
            return null;
        }

        private static List<CardTemplate> s_PvPCards;

        /// <summary>
        /// Every standard PvP card of the boosters (as Criesener's server counts them): not PvE, common to legendary,
        /// no basic shards, no equipment-modified printings.
        /// </summary>
        private static List<CardTemplate> PvPCards()
        {
            if (s_PvPCards != null) return s_PvPCards;
            s_PvPCards = new List<CardTemplate>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_IsPvE || t.m_EquipmentModifiedCard) continue;
                if (t.m_CardRarity != ERarity.Common && t.m_CardRarity != ERarity.Uncommon && t.m_CardRarity != ERarity.Rare && t.m_CardRarity != ERarity.Legendary) continue;
                bool ownable;
                try { ownable = t.IsOwnable() && !t.IsBasicResource(); } catch { ownable = false; }
                if (ownable) s_PvPCards.Add(t);
            }
            return s_PvPCards;
        }

        /// <summary>Dev: "redeem &lt;code&gt;" = redeems a code as the store's dialog does; "redeem list" = the codes and whether this account used them.</summary>
        public static string Command(string[] parts)
        {
            if (parts.Length < 2) return "usage: redeem <code> | redeem list";
            if (parts[1] == "list")
            {
                List<string> o = new List<string>();
                foreach (KeyValuePair<string, Bundle> kv in Codes)
                    o.Add(kv.Key + " - " + kv.Value.Title + (Saves.Current != null && Saves.Current.Claims.Contains("redeem:" + Key(kv.Key)) ? " (redeemed)" : ""));
                return string.Join("\n", o.ToArray());
            }
            string code = parts[1].Replace("-", "");
            EscrowService.RedeemCode.Request(code, (RedeemCodeResponse rs) =>
                Debug.Log("[HexRevival] [Redeem] dev answer: " + rs.Error + " " + rs.ErrorMessage + ", gold " + rs.GoldDelta + ", platinum " + rs.PlatinumDelta
                    + ", " + rs.ItemTemplateIds.Count + " reward id(s), " + rs.StarterDecksBits.Count + " deck(s)"));
            return "requested '" + code + "' (answer in the log)";
        }
    }
}
