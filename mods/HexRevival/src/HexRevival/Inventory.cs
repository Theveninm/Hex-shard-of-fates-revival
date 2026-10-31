using System;
using System.Collections.Generic;
using System.IO;
using Game.Shared;
using Game.Shared.Domain;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Inventory items (champion equipment) owned by the current save.
    ///
    /// "Unlock everything" saves own one of each equipment. Progression saves own what is listed
    /// in inventory.txt (one "template count" line per item), earned by playing. Instance ids are
    /// numbered again at each login.
    /// </summary>
    public static class Inventory
    {
        private static ulong s_NextId = 1;
        private static UID s_Owner;
        private static readonly Dictionary<ResourceId, int> s_Counts = new Dictionary<ResourceId, int>();

        private static string FilePath { get { return Saves.File("inventory.txt"); } }

        /// <summary>Inventory of the current save, for the profile sent at login.</summary>
        public static List<inventory_bits> Build(UID owner)
        {
            s_Owner = owner;
            s_NextId = 1;
            s_Counts.Clear();
            Crafting.Register();   // the crafting materials' templates, before the client reads the items
            // Items earned or bought (sleeves, coins, battleboards; equipment on progression saves).
            LoadFile();
            if (Saves.Current.UnlockAll)
            {
                // Everything unlocked: every equipment, sleeve, battleboard and coin.
                foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (item is InventoryEquipmentData || (item is InventoryDeckSleeve && Sleeves.HasPicture(item)) || item is InventoryGameboardData || item is InventoryCoinData) s_Counts[item.m_Id] = 1;
                foreach (InventoryMercenaryData m in Mercenaries.Obtainable()) s_Counts[m.m_Id] = 1;
            }
            List<inventory_bits> res = new List<inventory_bits>();
            foreach (KeyValuePair<ResourceId, int> kv in s_Counts)
            {
                if (!Crafting.Active && Crafting.IsMaterial(kv.Key)) continue;   // crafting off: its materials kept, not shown
                for (int i = 0; i < kv.Value; i++) res.Add(NewInstance(kv.Key));
            }
            inventory_bits sacks = Siege.SackItem(owner);   // Siege Sacks and Cosmic Coins, inventory currencies
            if (sacks != null) res.Add(sacks);
            inventory_bits coins = Store.CosmicCoinItem(owner);
            if (coins != null) res.Add(coins);
            Debug.Log("[HexRevival] [Inventory] " + res.Count + " item(s)");
            return res;
        }

        /// <summary>
        /// Adds an item to the save; returns the instance to hand to the client (null for equipment on "unlock
        /// everything" saves, which own every equipment already).
        /// </summary>
        public static inventory_bits GrantSilently(ResourceId template)
        {
            if (Saves.Current == null) return null;
            InventoryItemData data = Singleton<TemplateManager>.Instance.GetInventoryItem(template);
            if (Saves.Current.UnlockAll && (data is InventoryEquipmentData || data is InventoryDeckSleeve || data is InventoryGameboardData || data is InventoryCoinData || (data is InventoryMercenaryData && Count(template) > 0))) return null;
            int n;
            s_Counts.TryGetValue(template, out n);
            s_Counts[template] = n + 1;
            SaveFile();
            return NewInstance(template);
        }

        /// <summary>n of an item (crafting materials), saved once; the instances to hand to the client.</summary>
        public static List<inventory_bits> GrantMany(ResourceId template, int n)
        {
            List<inventory_bits> res = new List<inventory_bits>();
            if (Saves.Current == null || n <= 0) return res;
            int had;
            s_Counts.TryGetValue(template, out had);
            s_Counts[template] = had + n;
            for (int i = 0; i < n; i++) res.Add(NewInstance(template));
            SaveFile();
            return res;
        }

        /// <summary>Removes up to n of an item from the save (stardust, crafting materials); returns how many were removed.</summary>
        public static int Consume(ResourceId template, int n)
        {
            int taken = Math.Min(n, Count(template));
            if (taken > 0) Remove(template, taken);
            return Math.Max(0, taken);
        }

        /// <summary>Takes n of this item from the save (opened packs).</summary>
        public static void Remove(ResourceId template, int n)
        {
            int have;
            if (!s_Counts.TryGetValue(template, out have)) return;
            if (have <= n) s_Counts.Remove(template); else s_Counts[template] = have - n;
            SaveFile();
        }

        /// <summary>The items the save owns (templates).</summary>
        public static List<ResourceId> Owned() { return new List<ResourceId>(s_Counts.Keys); }

        /// <summary>How many of this item the save owns.</summary>
        public static int Count(ResourceId template)
        {
            int n;
            return s_Counts.TryGetValue(template, out n) ? n : 0;
        }

        private static inventory_bits NewInstance(ResourceId template)
        {
            inventory_bits inv = new inventory_bits();
            inv.Id = s_NextId++;
            inv.TemplateID = template;
            inv.ItemQuantity = 1;
            inv.ClaimDate = DateTime.MinValue;
            inv.EscrowStatus = "";
            inv.OwnerReckId = s_Owner.GetInstanceId();
            return inv;
        }

        private static void LoadFile()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split(' ');
                    int n;
                    if (p.Length == 2 && int.TryParse(p[1], out n) && n > 0) s_Counts[new ResourceId(p[0])] = n;
                }
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Inventory] read: " + e); }
        }

        private static void SaveFile()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<ResourceId, int> kv in s_Counts) lines.Add(kv.Key + " " + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Inventory] save: " + e); }
        }
    }
}
