using System;
using System.Collections.Generic;
using Game.Client;
using Game.Shared;
using Game.Client.Tournament;
using Game.Shared.Network.Tournaments;
using Game.Shared.TournamentSystemMkII;
using Game.Shared.Tournaments;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The Dueling Pit: 1-on-1 battles with the game's 5 prebuilt Dueling Pit decks, no deck to build or own (Src.HexCodex:
    /// "The Dueling Pit is all about 1-on-1 battles using pre-built decks"; Src.Game: the screen DuelingPitVM, its decks
    /// BuiltInResources.DuelingPitDecks and their "- AI" versions). The client had it on consoles only, as a main menu
    /// button; on PC the mod lists it in the Battlegrounds (user, 2026-10-30: the main menu's column has no room for it):
    /// its "Find match" goes back to the main menu and opens the game's Dueling Pit screen there. "Versus AI" plays the
    /// chosen deck against another of the five, piloted by the AI with its "- AI" version (which one: PLACEHOLDER, drawn
    /// at random). "Versus Player" stays locked (no matchmaking).
    /// </summary>
    public static class DuelingPit
    {
        /// <summary>Its tournament id in the Battlegrounds list (after the gauntlets', see Battlegrounds).</summary>
        public const ulong Id = 90000004;
        /// <summary>Its name's localization key (the list asks the game's Localization for the name and "-description").</summary>
        public const string NameKey = "hexrevival-dueling-pit";

        /// <summary>The Battlegrounds list's entry.</summary>
        public static TournamentDesc Describe()
        {
            TournamentDesc d = new TournamentDesc();
            d.TournamentID = Id;
            d.RoomType = "";
            d.Description = NameKey;
            d.Style = ETournamentStyle.Asynchronous;
            d.Format = ETournamentFormats.Constructed;
            d.TournamentState = TournamentState.PlayGames.Asynchronous;
            d.TournamentStatus = ETournamentStatus.Asynchronous;
            d.MinPlayers = 1;
            d.MaxPlayers = 0;
            d.MaxRounds = 1;
            d.Players = new string[0];
            d.tournamentFees = new Dictionary<int, TournamentEntryInfo> { { 0, new TournamentEntryInfo { goldCost = 0 } } };
            d.tournamentRewards = new TournamentRewardCollection();
            return d;
        }

        /// <summary>Localization.Get (postfix): the entry's name and description, in the mod's languages.</summary>
        public static string Localize(string key)
        {
            if (key == NameKey) return Loc.Get("dueling_pit");
            if (key == NameKey + "-description") return Loc.Get("dueling_pit_desc");
            return null;
        }

        /// <summary>
        /// UITournamentPendingViewModel.OnJoinTournamentClicked / OnTournamentDoubleClicked (prefix): the Dueling Pit's
        /// entry opens its screen on the main menu. False: the original is skipped.
        /// </summary>
        public static bool OnJoin(UITournamentPendingViewModel vm, UITournamentDescContext clicked)
        {
            UITournamentDescContext sel = clicked ?? (vm != null && vm.m_Context != null ? vm.m_Context.SelectedTournament : null);
            if (sel == null || sel.m_TournamentDesc == null || sel.m_TournamentDesc.TournamentID != Id) return true;
            s_Open = true;
            Debug.Log("[HexRevival] [DuelingPit] opening from the Battlegrounds");
            GameClient.Instance.StateManager.TransitionToState(EGameState.LandingPage, null);
            return false;
        }

        private static bool s_Open;
        private static float s_OpenAt = -1f;

        /// <summary>Main menu (each frame): the Dueling Pit screen, when the Battlegrounds asked for it.</summary>
        public static void OpenIfAsked(UILandingPageViewModel vm)
        {
            if (!s_Open || vm == null || vm.DuelingPit == null) return;
            // Shortly after the menu appears (its own start, 0.25 s, sets the buttons).
            if (s_OpenAt < 0f) { s_OpenAt = Time.realtimeSinceStartup + 0.5f; return; }
            if (Time.realtimeSinceStartup < s_OpenAt) return;
            s_Open = false;
            s_OpenAt = -1f;
            try
            {
                vm.DuelingPit.gameObject.SetActive(true);
                vm.DuelingPit.OnClick_DuelingPit();
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [DuelingPit] open: " + e.Message); }
        }

        /// <summary>DuelingPitVM.FindMatch (prefix): Versus AI as a local match. Returns false (the original is skipped).</summary>
        public static bool FindMatch(DuelingPitVM vm)
        {
            try
            {
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                UIDeckContext selected = vm != null ? typeof(DuelingPitVM).GetField("SelectedDeck", F).GetValue(vm) as UIDeckContext : null;
                // Versus Player (locked by the client on PC): the original matchmaking, which nothing answers.
                if (selected == null || !(bool)typeof(DuelingPitVM).GetField("PlayingAi", F).GetValue(vm)) return false;
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                DeckTemplate mine = tm.GetDeckTemplate(selected.DeckTemplateResourceID);
                if (mine == null) return false;
                // The opponent: another Dueling Pit deck, in its AI version when the game has one.
                List<DeckTemplate> others = new List<DeckTemplate>();
                foreach (ResourceId id in BuiltInResources.DuelingPitDecks)
                {
                    DeckTemplate d = tm.GetDeckTemplate(id);
                    if (d != null && d.m_Id != mine.m_Id) others.Add(d);
                }
                if (others.Count == 0) return false;
                DeckTemplate opp = others[UnityEngine.Random.Range(0, others.Count)];
                foreach (DeckTemplate d in tm.Decks.Values)
                    if (d != null && d.m_DeckName == opp.m_DeckName + " - AI" && d.m_ChampionId == opp.m_ChampionId) { opp = d; break; }
                vm.SetView(DuelingPitVM.EDuelingPitView.None);
                Opponents.SetNext(new Opponents.Opponent { Name = opp.m_DeckName, Prebuilt = opp.m_DeckName });
                Patches.PendingEncounterSetup = e => e.SessionFlags |= ESessionFlags.IsDuelingPit;
                Debug.Log("[HexRevival] [DuelingPit] " + mine.m_DeckName + " vs " + opp.m_DeckName);
                Patches.StartMatch(Decks.SetTemporary(mine), Patches.NeutralEncounterId, opp, "Dueling Pit vs " + opp.m_DeckName, checkOwned: false);
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [DuelingPit] " + e); }
            return false;
        }
    }
}
