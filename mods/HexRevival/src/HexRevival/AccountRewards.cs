using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Network;
using Reckoning.Game;
using UnityEngine;
using Net = Game.Shared.Profile.Network;
using JsonUtils = Game.Shared.Network.HConnect.Utils;

namespace HexRevival
{
    /// <summary>
    /// Account level rewards, claimed on the game's account panel (a click on the XP bar: UIAccountLevelRewards), as in
    /// the original game (Zymmer 2026-10-31: "Leveling up your account doesn't let you collect the level up rewards on the
    /// account screen"). Until 1.2.12 the mod gave them at once when the level was reached.
    /// - Profile.Network.ProfileAccountXpState ("acctxpget"): the levels reached and not claimed, with their rewards, and
    ///   the next level's rewards (shown when nothing is left to claim).
    /// - Profile.Network.ProfileAccountXpClaim ("acctclaim"): the panel claims everything at once.
    /// What each reward is in the mod: Progression.GrantLevelReward. The panel shows them with the game's icons: gold,
    /// item templates (packs, mercenary), chests, and a text for the random cards and sleeves.
    /// The save keeps the last level claimed (Claims "lvlclaimed:N"); saves without it had every level given.
    /// </summary>
    public static class AccountRewards
    {
        private const string Mark = "lvlclaimed:";
        private const string Platform = "PC";

        /// <summary>The last account level whose rewards were claimed (set to the current level on older saves).</summary>
        public static int ClaimedLevel(Saves.Info s)
        {
            if (s == null) return 0;
            int n;
            foreach (string m in s.Claims)
                if (m.StartsWith(Mark, StringComparison.Ordinal) && int.TryParse(m.Substring(Mark.Length), out n)) return n;
            SetClaimed(s, s.AccountLevel);
            Saves.SaveCurrent();
            return s.AccountLevel;
        }

        private static void SetClaimed(Saves.Info s, int level)
        {
            s.Claims.RemoveWhere(x => x.StartsWith(Mark, StringComparison.Ordinal));
            s.Claims.Add(Mark + level);
        }

        // ------------------------------------------------------------------
        // The panel's list
        // ------------------------------------------------------------------

        /// <summary>acctxpget: the claimable levels and the rewards to show.</summary>
        public static void OnState(Action<CustomNetworkResponse<Net.Response>> notify)
        {
            Saves.Info s = Saves.Current;
            Net.AccountXpStateResponse res = new Net.AccountXpStateResponse
            {
                State = new List<Net.AccountClaimable>(),
                Rewards = new List<Net.AccountRewardData>(),
            };
            if (s != null && !s.UnlockAll)
            {
                int claimed = ClaimedLevel(s);
                // The panel keys its rows by reward (two rows with the same key break it): the same reward of several
                // levels becomes one row, its quantities added, on the first of those levels.
                Dictionary<string, Net.AccountRewardData> rows = new Dictionary<string, Net.AccountRewardData>();
                for (int level = claimed + 1; level <= s.AccountLevel; level++)
                {
                    res.State.Add(new Net.AccountClaimable { Level = level, Platform = Platform, RewardID = RewardId(level), Claimable = true, MinXpForLevel = Progression.TotalXpForLevel(level) });
                    foreach (string code in Progression.RewardsForLevel(level)) AddRow(rows, level, code);
                }
                foreach (Net.AccountRewardData r in rows.Values) res.Rewards.Add(r);
                if (s.AccountLevel < Progression.MaxAccountLevel)
                {
                    Dictionary<string, Net.AccountRewardData> next = new Dictionary<string, Net.AccountRewardData>();
                    foreach (string code in Progression.RewardsForLevel(s.AccountLevel + 1)) AddRow(next, s.AccountLevel + 1, code);
                    foreach (Net.AccountRewardData r in next.Values) res.Rewards.Add(r);
                }
            }
            Debug.Log("[HexRevival] [AccountRewards] state: " + res.State.Count + " level(s) to claim, " + res.Rewards.Count + " row(s)");
            Answer(notify, res);
        }

        private static string RewardId(int level) { return "hexrevival-level-" + level; }

        private static void AddRow(Dictionary<string, Net.AccountRewardData> rows, int level, string code)
        {
            string type, payout;
            int qty;
            Describe(code, level, out type, out payout, out qty);
            if (type == null) return;
            string key = type + "|" + payout;
            Net.AccountRewardData r;
            if (rows.TryGetValue(key, out r)) { r.Quantity += qty; return; }
            rows[key] = new Net.AccountRewardData { Level = level, Platform = Platform, RewardID = RewardId(level), PayoutID = payout, Quantity = qty, RewardType = type, LinkReward = "", ChanceOfReward = 1f };
        }

        /// <summary>How the panel shows a reward code (see Progression.GrantLevelReward for what it gives).</summary>
        private static void Describe(string code, int level, out string type, out string payout, out int qty)
        {
            string[] p = code.Split(':');
            int n = 0;
            if (p.Length > 1) int.TryParse(p[1], out n);
            type = null; payout = ""; qty = 1;
            CardSetTemplate cur = Progression.CurrentSet();
            switch (p[0])
            {
                case "gold": type = "GOLD"; qty = n; return;
                case "stardust": type = "GOLD"; qty = 300; return;   // PLACEHOLDER (see GrantLevelReward)
                case "ticket":
                case "pack":
                {
                    InventoryItemData item = p.Length > 1 && p[1] == "hp" ? Item("Howling Plains Pack")
                        : p.Length > 1 && p[1] == "as" ? Item("Alachian Sea Pack") : Booster(cur);
                    if (item != null) { type = "INVENTORY"; payout = Guid(item.m_Id); }
                    return;
                }
                case "chests":
                case "rarechest":
                    if (cur != null) { type = "CHEST|" + (p[0] == "rarechest" ? "R" : "C") + "|X"; payout = Guid(cur.m_Id); }
                    return;
                case "merc":
                {
                    Loot.Drop d = Loot.Resolve("Sin'glar (mercenary)", false);
                    if (d != null) { type = "INVENTORY"; payout = Guid(d.Item); }
                    return;
                }
                case "commons":
                case "cards":
                case "sleeve":
                {
                    // A text row (the game's "DPAYOUT_<id>" localization key, added here).
                    type = "DPAYOUT";
                    payout = "hexrevival_" + p[0] + "_" + level;
                    string text = p[0] == "sleeve" ? Loc.Get("axp_sleeve") : Loc.Get(p[0] == "commons" ? "axp_commons" : "axp_cards", n, cur != null ? cur.m_Name : "?");
                    AddLocalization("DPAYOUT_" + payout, text);
                    return;
                }
            }
        }

        private static string Guid(ResourceId id) { return id.ToString(); }

        private static InventoryItemData Item(string name)
        {
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                if (i != null && i.m_Name != null && i.m_Name.Trim() == name) return i;
            return null;
        }

        private static InventoryItemData Booster(CardSetTemplate set)
        {
            if (set == null) return null;
            foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
            {
                InventoryCardPack p = i as InventoryCardPack;
                if (p != null && p.m_SetId == set.m_Id && p.m_CardPackType == ECardPackType.BoosterPack) return p;
            }
            return null;
        }

        /// <summary>Adds a text to the game's localization (NGUI Localization.mDictionary: key in lower case, one text per language).</summary>
        private static void AddLocalization(string key, string text)
        {
            try
            {
                FieldInfo f = typeof(Localization).GetField("mDictionary", BindingFlags.Static | BindingFlags.NonPublic);
                Dictionary<string, string[]> d = f != null ? f.GetValue(null) as Dictionary<string, string[]> : null;
                if (d == null) return;
                int langs = 1;
                foreach (string[] v in d.Values) { langs = Math.Max(langs, v.Length); break; }
                string[] all = new string[langs];
                for (int i = 0; i < langs; i++) all[i] = text;
                d[key.ToLowerInvariant()] = all;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [AccountRewards] localization: " + e.Message); }
        }

        // ------------------------------------------------------------------
        // Claiming
        // ------------------------------------------------------------------

        /// <summary>acctclaim: every level reached and not claimed. Gold is added by the client from the answer; the rest is given here.</summary>
        public static void OnClaim(Action<CustomNetworkResponse<Net.Response>> notify)
        {
            Saves.Info s = Saves.Current;
            Net.AccountXpClaimResult res = new Net.AccountXpClaimResult { State = Net.AccountXpClaimResult.Status.SUCCESS, LevelCodes = new Net.LevelClaim[0], Chests = new Game.Shared.Domain.chest_bits[0], Error = "" };
            List<Net.RewardResult> results = new List<Net.RewardResult>();
            List<string> got = new List<string>();
            if (s != null && !s.UnlockAll)
            {
                int claimed = ClaimedLevel(s), gold = 0;
                List<Net.LevelClaim> levels = new List<Net.LevelClaim>();
                for (int level = claimed + 1; level <= s.AccountLevel; level++)
                {
                    levels.Add(new Net.LevelClaim { Level = level, RewardCode = RewardId(level) });
                    foreach (string code in Progression.RewardsForLevel(level))
                    {
                        int n;
                        if (code.StartsWith("gold:") && int.TryParse(code.Substring(5), out n)) gold += n;
                        else if (code == "stardust") gold += 300;
                        else if ((code.StartsWith("pack:") || code == "ticket") && GivePack(code, results, got)) { }
                        else
                        {
                            try { got.Add(Progression.GrantLevelReward(code)); }
                            catch (Exception e) { Debug.LogError("[HexRevival] [AccountRewards] " + code + ": " + e); }
                        }
                    }
                }
                // Gold: into the save only; the client adds the answer's GOLD to its balance (UIAccountLevelRewards).
                if (gold > 0)
                {
                    s.Gold += gold;
                    results.Add(new Net.RewardResult { Id = "0", Template = "", Quantity = gold, Type = "GOLD" });
                    got.Add(Loc.Get("reward_gold", gold));
                }
                SetClaimed(s, s.AccountLevel);
                Saves.SaveCurrent();
                res.LevelCodes = levels.ToArray();
                Debug.Log("[HexRevival] [AccountRewards] claimed levels " + (claimed + 1) + "-" + s.AccountLevel + ": " + string.Join(", ", got.FindAll(x => x != null).ToArray()));
            }
            res.Rewards = results.ToArray();
            Answer(notify, res);
            got.RemoveAll(x => x == null);
            if (got.Count > 0) Notices.Show(string.Join(", ", got.ToArray()));
            Campaign.Enqueue(Progression.RefreshAccountUi);
        }

        /// <summary>
        /// A pack reward as a pack (Zymmer 2026-10-31: packs were opened at once): a current set booster to the inventory,
        /// a Howling Plains / Alachian Sea Pack as the campaign's pack (Packs.GiveCampaignPack). The client adds the answer's
        /// INVENTORY lines itself (one line per item: the same booster twice is one line of 2). A second campaign pack of
        /// the same kind in one claim (levels 2 and 12) cannot be a second line: false, it is opened at once as before.
        /// </summary>
        private static bool GivePack(string code, List<Net.RewardResult> results, List<string> got)
        {
            bool campaign = code == "pack:hp" || code == "pack:as";
            Game.Shared.Domain.inventory_bits bits;
            if (campaign)
            {
                string name = Loot.PackItemName(code == "pack:hp" ? Loot.AZ1Pack : Loot.AZ2Pack);
                if (results.Exists(r => r.Type == "INVENTORY" && Item(name) != null && r.Template == Guid(Item(name).m_Id))) return false;
                bits = Packs.GiveCampaignPack(code == "pack:hp" ? Loot.AZ1Pack : Loot.AZ2Pack);
            }
            else
            {
                InventoryItemData booster = Booster(Progression.CurrentSet());
                bits = booster != null ? Inventory.GrantSilently(booster.m_Id) : null;
            }
            if (bits == null) return false;
            string template = Guid(bits.TemplateID);
            Net.RewardResult same = results.Find(r => r.Type == "INVENTORY" && r.Template == template);
            if (same != null) same.Quantity++;
            else results.Add(new Net.RewardResult { Id = bits.Id.ToString(), Template = template, Quantity = 1, Type = "INVENTORY" });
            InventoryItemData item = Singleton<TemplateManager>.Instance.GetInventoryItem(bits.TemplateID);
            if (item != null) got.Add(item.m_Name.Trim());
            return true;
        }

        private static void Answer(Action<CustomNetworkResponse<Net.Response>> notify, object message)
        {
            if (notify == null) return;
            byte[] json = null;
            JsonUtils.SerializeJson(message, (byte[] x) => { json = x; });
            CustomNetworkResponse<Net.Response> rsp = new CustomNetworkResponse<Net.Response>();
            rsp.Message = new Net.Response { Envelope = json };
            Campaign.Enqueue(() => notify(rsp));
        }
    }
}
