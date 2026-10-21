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
            if (Saves.Current.UnlockAll)
            {
                foreach (InventoryItemData item in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (item is InventoryEquipmentData) s_Counts[item.m_Id] = 1;
            }
            else LoadFile();
            List<inventory_bits> res = new List<inventory_bits>();
            foreach (KeyValuePair<ResourceId, int> kv in s_Counts)
                for (int i = 0; i < kv.Value; i++) res.Add(NewInstance(kv.Key));
            inventory_bits sacks = Siege.SackItem(owner);   // Siege Sacks, an inventory currency
            if (sacks != null) res.Add(sacks);
            Debug.Log("[HexRevival] [Inventory] " + res.Count + " item(s)");
            return res;
        }

        /// <summary>Adds an item to the save; returns the instance to hand to the client (null on "unlock everything" saves).</summary>
        public static inventory_bits GrantSilently(ResourceId template)
        {
            if (Saves.Current == null || Saves.Current.UnlockAll) return null;
            int n;
            s_Counts.TryGetValue(template, out n);
            s_Counts[template] = n + 1;
            SaveFile();
            return NewInstance(template);
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
