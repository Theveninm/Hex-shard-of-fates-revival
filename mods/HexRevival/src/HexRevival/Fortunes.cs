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
    /// (the gaals have an "already has fortune" dialogue); its abilities are activated for the
    /// champion at the start of the next campaign battle, which uses it up.
    /// PLACEHOLDER: which fortune is drawn (random here) and how long it lasts (one battle here).
    /// </summary>
    public static class Fortunes
    {
        /// <summary>Price of a reading. Src.Game: "(Pay 100 gold.)".</summary>
        private const int Price = 100;

        public static bool Has(ulong champ) { return Quests.Flag(champ, "fortune") != null; }

        private static List<CardTemplate> s_Deck;

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

        /// <summary>Start of a campaign battle (after the mulligan): the fortune's abilities for the champion, then it is used up.</summary>
        public static void Apply(StandaloneSession ss, ulong champ)
        {
            string id = Quests.Flag(champ, "fortune");
            if (id == null) return;
            Quests.SetFlag(champ, "fortune", null);
            CardTemplate card = Singleton<TemplateManager>.Instance.GetCardTemplate(new ResourceId(id));
            Player human = Cheats.FindPlayer(ss, "j");
            if (card == null || human == null) return;
            foreach (CardAbilityContainer a in card.m_CardAbilities) ss.ActivateAbilityForPlayer(human, a.m_CardAbilityId);
            Server.MarkDirty();
            Debug.Log("[HexRevival] [Fortunes] " + card.m_Name + " applied");
        }
    }
}
