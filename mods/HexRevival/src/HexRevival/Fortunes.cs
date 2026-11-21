using System;
using System.Collections.Generic;
using Game.Client;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Gaal fortunes (Milosh, Simza, Gyorgy): for 100 gold the gaal draws a card from the Deck of
    /// Fates that "helps in travels" (Src.Game dialogues). The fortunes are the game's "Fortune of ..."
    /// effect cards (set PvE_AZ1_Created_Effects, Src.Game). The champion holds one fortune at a time
    /// (the gaals have an "already has fortune" dialogue); it is one of the next campaign battle's effects, which uses
    /// it up (Take).
    /// Duration, Src.Video: the window reads "Gaal Camp - Your fortune for the next battle" (MHS UW 2017-01-25
    /// [W__kpqC147U] 0:51, Fortune of Diamond). PLACEHOLDER: how the fortune is drawn (random here; the runs show
    /// different fortunes at different visits, no pattern known: the same gaal, Gyorgy, before the same Pale Moon battle
    /// read Ruby (MHS UW 2016-10-24 [DeQ3uQpnnEE] 1:42) and Diamond (MHS Ardent 2017-01-25); the Ardent champion drew
    /// Ruby (2017-01-04 [EXvODvsTTSo] 1:47) then Diamond; MHS UW's 2017-06-24 stream drew Wild then Fury).
    /// </summary>
    public static class Fortunes
    {
        /// <summary>Price of a reading. Src.Game: "(Pay 100 gold.)".</summary>
        private const int Price = 100;

        public static bool Has(ulong champ) { return Quests.Flag(champ, "fortune") != null; }

        private static List<CardTemplate> s_Deck;

        /// <summary>The fortune of the battle launched last (shown in its Terrain Effects panel), null if none.</summary>
        private static CardTemplate s_InBattle;

        /// <summary>
        /// UIBattle.OnGameStarted (Harmony postfix): the fortune is listed in the battle's Terrain Effects panel (the
        /// game's UIChallengeAlert, which lists the battle scene's mods), with its text; the fortune's effect card was not
        /// shown anywhere (mornox 2026-11-21). Once per battle.
        /// </summary>
        public static void ShowInBattle(UIBattle battle)
        {
            CardTemplate card = s_InBattle;
            s_InBattle = null;
            if (card == null || battle == null) return;
            try
            {
                const System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                object scene = typeof(UIBattle).GetField("m_SceneObjects", f).GetValue(battle);
                UIChallengeAlert alert = scene != null ? scene.GetType().GetField("ChallengeAlert", f).GetValue(scene) as UIChallengeAlert : null;
                if (alert == null || alert.Context == null) return;
                if (string.IsNullOrEmpty(alert.Context.LabelText)) alert.Context.LabelText = Localization.Get("Terrain_Effects");
                ChallengeItemContext item = new ChallengeItemContext();
                item.TitleText = card.m_Name;
                // The card's text without its markup ("<b>", "#IGNORE_CHAIN#"), read from the player's side: the fortune is
                // played by the encounter, so its "opposing champion" is the player.
                string text = System.Text.RegularExpressions.Regex.Replace(card.m_GameText ?? "", "<[^>]*>|#[A-Z_]+#", "");
                item.LabelText = text.Replace("Each opposing champion", "Your champion");
                alert.Context.Challenges.Add(item);
                Debug.Log("[HexRevival] [Fortunes] " + card.m_Name + " listed in the battle's terrain effects");
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Fortunes] terrain effects: " + e.Message); }
        }

        private static List<CardTemplate> Deck()
        {
            if (s_Deck != null) return s_Deck;
            s_Deck = new List<CardTemplate>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (CardTemplate t in tm.Cards.Values)
            {
                if (t == null || t.m_Name == null || !t.m_Name.StartsWith("Fortune of ")) continue;
                CardSetTemplate set;
                if (tm.CardSets.TryGetValue(t.m_SetId, out set) && set != null && set.m_Name == "PvE_AZ1_Created_Effects") s_Deck.Add(t);
            }
            return s_Deck;
        }

        /// <summary>Dialogue event "gaal_camp_accept": pays and draws a fortune.</summary>
        public static void Buy(ulong champ)
        {
            List<CardTemplate> deck = Deck();
            if (deck.Count == 0 || Saves.Current == null) return;
            Saves.Current.Gold = Math.Max(0L, Saves.Current.Gold - Price);
            Saves.SaveCurrent();
            if (!QuestSim.Active)
                try { GameClient.Instance.PlayerProfile.UpdateBalance((int)Saves.Current.Gold, (int)Saves.Current.Platinum); } catch { }
            CardTemplate card = deck[UnityEngine.Random.Range(0, deck.Count)];
            Quests.SetFlag(champ, "fortune", card.m_Id.ToString());
            Debug.Log("[HexRevival] [Fortunes] " + champ + " drew " + card.m_Name);
            QuestSim.Note("  fortune: " + card.m_Name);
            if (!QuestSim.Active) Notices.Show(card.m_Name);
        }

        /// <summary>
        /// A campaign battle launched: the champion's fortune card, used up, for the battle's effects (null if none).
        /// The fortunes are written from the encounter's side ("Each opposing champion gains 6 health", Fortune of
        /// Diamond), like the dungeon boons: the AI side runs them at the start (Patches: encounter AbilitiesToRun), and
        /// the player gets the boon. Until 1.2.37 their abilities were activated for the player, so the boon went to the
        /// AI's champion (mornox 2026-11-21: "you don't get the boon").
        /// </summary>
        public static ResourceId Take(ulong champ)
        {
            string id = Quests.Flag(champ, "fortune");
            s_InBattle = null;
            if (id == null) return ResourceId.Invalid;
            Quests.SetFlag(champ, "fortune", null);
            CardTemplate card = Singleton<TemplateManager>.Instance.GetCardTemplate(new ResourceId(id));
            if (card == null) return ResourceId.Invalid;
            Debug.Log("[HexRevival] [Fortunes] " + card.m_Name + " for this battle");
            s_InBattle = card;
            QuestSim.Note("  effect: " + card.m_Name + " (fortune)");
            return card.m_Id;
        }
    }
}
