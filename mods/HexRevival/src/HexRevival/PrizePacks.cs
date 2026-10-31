using System;
using System.Collections.Generic;
using Game.Client;
using Game.Client.Network.Escrow;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The Prize Exchange packs, one item each (Src.HexTCG2018: "Introducing Cosmic Coins and Siege Sacks", May 2018):
    /// - Cosmic Coin Pack: most often an alternate art card (6 of them), nearly 1 in 3 a booster of the latest set,
    ///   rarely 1 of 3 sleeves (never twice).
    /// - Siege Pack: PvE cards (at most 4 copies each) and equipment (once each; which pieces: Src.Zymmer), alternate art cards less often than
    ///   in Cosmic Coin Packs, animated alternate art shards (no limit), sleeves, two mercenaries.
    /// The pack contents were drawn by the server: which cards, equipment and shards they held, and the odds, are
    /// PLACEHOLDER (the sleeves are the game data's own: "Available in the 2018 Siege Pack" / "Cosmic Coin Pack").
    /// </summary>
    public static class PrizePacks
    {
        private static readonly string[] SiegeSleeves =
        {
            "Blood Shard Sleeve", "Diamond Shard Sleeve", "Ruby Shard Sleeve", "Sapphire Shard Sleeve", "Wild Shard Sleeve",
            "Eternal Seeker Sleeve", "Refuel Sleeve", "Siege Golem Sleeve", "Songstring Warden Sleeve"
        };
        private static readonly string[] CosmicSleeves = { "Doombringer Kha Sleeve", "Soaring Cataclyst Sleeve", "Twilight Justice Sleeve" };
        /// <summary>The "pair of new mercenaries" (Src.HexTCG2018: Leckermaul; Src.Game: the item descriptions of Rosina
        /// Leckermaul and Angar Krad read "Siege Pack").</summary>
        private static readonly string[] SiegeMercenaries = { "Rosina Leckermaul", "Angar Krad" };

        /// <summary>PLACEHOLDER sets: the PvE promo cards and the alternate arts of the 2018 set (Doombringer, set 9).</summary>
        private const string PvESet = "Set09_PvE_Promo", AltSet = "Set09_PvP_alternates", LatestBooster = "Doombringer Booster Pack";

        /// <summary>One generator: new ones made in the same tick share their seed (3 packs bought at once gave 3 times the same card).</summary>
        private static readonly System.Random rng = new System.Random();

        public static void OpenSiegePack(PurchaseItemResponse r)
        {
            double x = rng.NextDouble();
            // PLACEHOLDER odds: PvE card 40%, alternate art shard 20%, sleeve 15%, alternate art card 10%, equipment 10%, mercenary 5%.
            if (x < 0.05 && Mercenary(SiegeMercenaries, r)) return;
            if (x < 0.20 && Sleeve(SiegeSleeves, rng, r)) return;
            if (x < 0.30 && Card(AltCards(false), rng, 4, r)) return;
            if (x < 0.40 && SiegeEquipment(rng, r)) return;
            if (x < 0.60 && Card(AltCards(true), rng, int.MaxValue, r)) return;
            if (Card(SetCards(PvESet), rng, 4, r)) return;
            // Everything else owned (the game data has no alternate art shard in the 2018 set): an alternate art card.
            if (!Card(AltCards(true), rng, int.MaxValue, r)) Card(AltCards(false), rng, int.MaxValue, r);
        }

        public static void OpenCosmicPack(PurchaseItemResponse r)
        {
            double x = rng.NextDouble();
            // "Nearly 1 in 3" a booster; sleeves "one of the rarest things" (PLACEHOLDER 5%); otherwise an alternate art card.
            if (x < 0.05 && Sleeve(CosmicSleeves, rng, r)) return;
            if (x < 0.35)
            {
                // The booster itself, unopened (until 1.2.12 its cards; 2026-10-31: rewards as unopened packs).
                InventoryCardPack pack = Find(LatestBooster) as InventoryCardPack;
                inventory_bits b = pack != null ? Inventory.GrantSilently(pack.m_Id) : null;
                if (b != null) { r.GrantedInventory.Add(b); return; }
            }
            Card(AltCards(false), rng, 4, r);
        }

        private static InventoryItemData Find(string name)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i != null && i.m_Name == name) return i;
            return null;
        }

        private static bool Sleeve(string[] names, System.Random rng, PurchaseItemResponse r)
        {
            List<InventoryItemData> left = new List<InventoryItemData>();
            foreach (string n in names)
            {
                InventoryItemData s = Find(n);
                if (s != null && Inventory.Count(s.m_Id) == 0) left.Add(s);   // a sleeve never comes twice
            }
            if (left.Count == 0) return false;
            inventory_bits inv = Inventory.GrantSilently(left[rng.Next(left.Count)].m_Id);
            if (inv != null) r.GrantedInventory.Add(inv);
            return inv != null;
        }

        /// <summary>The Siege Pack's equipment, once each: the pieces Zymmer's notes and Hex Codex list under "Siege Pack" (Src.Zymmer, Src.HexCodex: 15).</summary>
        private static bool SiegeEquipment(System.Random rng, PurchaseItemResponse r)
        {
            List<InventoryItemData> left = Zymmer.Equipment("Siege Pack").FindAll(i => Inventory.Count(i.m_Id) == 0);
            if (left.Count == 0) return false;
            inventory_bits inv = Inventory.GrantSilently(left[rng.Next(left.Count)].m_Id);
            if (inv != null) r.GrantedInventory.Add(inv);
            return inv != null;
        }

        private static bool Mercenary(string[] names, PurchaseItemResponse r)
        {
            List<InventoryMercenaryData> left = new List<InventoryMercenaryData>();
            foreach (string n in names)
            {
                InventoryMercenaryData m = Find(n) as InventoryMercenaryData;
                if (m != null && Inventory.Count(m.m_Id) == 0) left.Add(m);
            }
            return left.Count > 0 && Mercenaries.Grant(left[rng.Next(left.Count)], r);
        }

        private static bool Card(List<CardTemplate> pool, System.Random rng, int maxCopies, PurchaseItemResponse r)
        {
            List<CardTemplate> left = pool.FindAll(c => Collection.Count(c.m_Id) < maxCopies);   // smart drops
            if (left.Count == 0) return false;
            r.GrantedCards.AddRange(Collection.GrantSilently(left[rng.Next(left.Count)].m_Id, 1));
            return true;
        }

        private static List<CardTemplate> SetCards(string setName)
        {
            List<CardTemplate> res = new List<CardTemplate>();
            ResourceId set = SetId(setName);
            if (!set.IsValid) return res;
            foreach (CardTemplate c in Singleton<TemplateManager>.Instance.Cards.Values)
                if (c != null && c.m_SetId == set && c.m_CardType != ECardTypes.Champion) res.Add(c);
            return res;
        }

        /// <summary>The 2018 alternate arts: the shards (resources) or the other cards.</summary>
        private static List<CardTemplate> AltCards(bool shards)
        {
            return SetCards(AltSet).FindAll(c => ((c.m_CardType & ECardTypes.Resource) != 0) == shards);
        }

        private static ResourceId SetId(string name)
        {
            foreach (CardSetTemplate s in Singleton<TemplateManager>.Instance.CardSets.Values)
                if (s != null && s.m_Name == name) return s.m_Id;
            return ResourceId.Invalid;
        }

        /// <summary>Dev: "store pools" = the size of each pool.</summary>
        public static string Describe()
        {
            return "PvE cards " + SetCards(PvESet).Count + ", alternate art cards " + AltCards(false).Count + ", alternate art shards " + AltCards(true).Count
                + ", siege sleeves " + Array.FindAll(SiegeSleeves, n => Find(n) != null).Length + ", cosmic sleeves " + Array.FindAll(CosmicSleeves, n => Find(n) != null).Length
                + ", mercenaries " + Array.FindAll(SiegeMercenaries, n => Find(n) != null).Length + ", equipment " + Zymmer.Equipment("Siege Pack").Count;
        }
    }
}
