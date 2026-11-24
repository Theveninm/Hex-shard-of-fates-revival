using System;
using System.Collections.Generic;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Where an equipment piece can be found, and whether the player owns it (TalekithOP, 2026-11-24: "Hard to know which
    /// equipment you get in FRA for example and from which packs and/or chest spinning", and "Not knowing if you own the
    /// equipment when you check a card").
    /// - The equipment's tooltip (UIEquipmentItemContext.BuildInfoTooltip, postfixed) gets two lines: how many the player
    ///   owns, and where it comes from.
    /// - A card's equipment icons (the collection's and deck editor's card view, see MarkIcon) show the deck builder's
    ///   owned / not owned box.
    /// The sources: the campaign packs as the mod draws them (Loot.Describe: their equipment and unique-equipment
    /// slots), and every source of Zymmer's notes (Src.Zymmer: Frost Ring Arena, chests and Wheels of Fate per set,
    /// race rewards, encounters, quests, packs), which the mod's arena, chests and wheels also draw from.
    /// </summary>
    public static class EquipmentSources
    {
        private static Dictionary<ResourceId, List<string>> s_Index;

        /// <summary>The sources of a piece (empty if none is known).</summary>
        public static List<string> Of(ResourceId id)
        {
            if (s_Index == null) Build();
            List<string> l;
            return s_Index.TryGetValue(id, out l) ? l : new List<string>();
        }

        private static void Add(ResourceId id, string source)
        {
            if (id == ResourceId.Invalid || string.IsNullOrEmpty(source)) return;
            List<string> l;
            if (!s_Index.TryGetValue(id, out l)) s_Index[id] = l = new List<string>();
            if (!l.Contains(source)) l.Add(source);
        }

        private static void Build()
        {
            s_Index = new Dictionary<ResourceId, List<string>>();
            try
            {
                foreach (string pack in Loot.CampaignPacks)
                {
                    string name = Loot.PackItemName(pack);
                    foreach (Loot.Slot s in Loot.Describe(pack))
                    {
                        if (s.Kind != "equipment" && s.Kind != "unique") continue;
                        foreach (KeyValuePair<Loot.Drop, float> it in s.Items) if (it.Key != null) Add(it.Key.Item, name);
                    }
                }
                foreach (string[] src in ZymmerData.Sources)
                    for (int i = 1; i + 3 < src.Length; i += 4)
                    {
                        if (src[i] != "e") continue;
                        try { Add(new ResourceId(src[i + 1]), src[0]); } catch { }
                    }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [EquipmentSources] " + e); }
            Debug.Log("[HexRevival] [EquipmentSources] " + s_Index.Count + " pieces with a known source");
        }

        /// <summary>The tooltip's extra lines: owned count and sources.</summary>
        public static string TooltipLines(InventoryItemData item)
        {
            if (!(item is InventoryEquipmentData)) return "";
            int owned = Inventory.Count(item.m_Id);
            string res = "\n\n" + (owned > 0 ? "[66dd66]" + Loc.Get("eq_owned", owned) + "[-]" : "[aaaaaa]" + Loc.Get("eq_not_owned") + "[-]");
            List<string> src = Of(item.m_Id);
            res += "\n" + Loc.Get("eq_found_in") + " " + (src.Count > 0 ? string.Join(", ", src.ToArray()) : Loc.Get("eq_source_unknown"));
            return res;
        }

        /// <summary>
        /// A card's equipment icon (NguiGoInventoryIconBinding.UpdateBinding, postfixed; only the icons of a card's
        /// equipment list, under an "Equipment" grid): the deck builder's own "owned" / "not owned" check box
        /// (A_DeckBuilder Check_Box_Collection_Owned / _Not_Owned) in the icon's corner.
        /// </summary>
        public static void MarkIcon(NguiGoInventoryIconBinding b, InventoryItemData item)
        {
            if (b == null) return;
            Transform t = b.transform;
            if (t.parent == null || t.parent.name != "Equipment") return;
            Transform old = t.Find(MarkName);
            InventoryEquipmentData e = item as InventoryEquipmentData;
            if (e == null) { if (old != null) old.gameObject.SetActive(false); return; }
            UIAtlas atlas = DeckBuilderAtlas();
            if (atlas == null) return;
            UISprite sp = old != null ? old.GetComponent<UISprite>() : null;
            if (sp == null)
            {
                GameObject go = new GameObject(MarkName);
                go.layer = b.gameObject.layer;
                go.transform.parent = t;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                sp = go.AddComponent<UISprite>();
                sp.atlas = atlas;
                int depth = 0;
                foreach (UIWidget w in b.GetComponentsInChildren<UIWidget>(true)) if (w != sp) depth = Mathf.Max(depth, w.depth);
                sp.depth = depth + 2;
                // The icon's top right corner (its widgets are centred, the icon's frame is the widest of them).
                float half = 32f;
                foreach (UIWidget w in b.GetComponentsInChildren<UIWidget>(true)) if (w != sp) half = Mathf.Max(half, w.width / 2f);
                go.transform.localPosition = new Vector3(half - 10f, half - 10f, 0f);
            }
            sp.gameObject.SetActive(true);
            sp.spriteName = Inventory.Count(e.m_Id) > 0 ? "Check_Box_Collection_Owned" : "Check_Box_Collection_Not_Owned";
            sp.width = 20;
            sp.height = 20;
        }

        private const string MarkName = "HexRevival_Owned";
        private static UIAtlas s_Atlas;

        private static UIAtlas DeckBuilderAtlas()
        {
            if (s_Atlas != null) return s_Atlas;
            foreach (UIAtlas a in Resources.FindObjectsOfTypeAll<UIAtlas>()) if (a != null && a.name == "A_DeckBuilder") { s_Atlas = a; break; }
            return s_Atlas;
        }

        /// <summary>Dev: "eqsrc &lt;equipment name&gt;" = the piece's owned count and sources.</summary>
        public static string Command(string name)
        {
            foreach (InventoryItemData d in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryEquipmentData e = d as InventoryEquipmentData;
                if (e == null || e.m_Name == null || !e.m_Name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                return e.m_Name.Trim() + ": owned " + Inventory.Count(e.m_Id) + "; " + string.Join(", ", Of(e.m_Id).ToArray())
                    + "\ntooltip: " + UIEquipmentItemContext.BuildInfoTooltip(e);
            }
            return "no equipment " + name;
        }
    }
}
