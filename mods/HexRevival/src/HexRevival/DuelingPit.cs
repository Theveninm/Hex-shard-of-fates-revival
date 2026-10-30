using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The Dueling Pit: 1-on-1 battles with the game's 5 prebuilt Dueling Pit decks, no deck to build or own (Src.HexCodex:
    /// "The Dueling Pit is all about 1-on-1 battles using pre-built decks"; Src.Game: the screen DuelingPitVM, its decks
    /// BuiltInResources.DuelingPitDecks and their "- AI" versions). The client had it on consoles only; the mod shows its
    /// main menu button on PC too. "Versus AI" plays the chosen deck against another of the five, piloted by the AI with
    /// its "- AI" version (which one: PLACEHOLDER, drawn at random). "Versus Player" stays locked (no matchmaking).
    /// </summary>
    public static class DuelingPit
    {
        /// <summary>How far the main menu's column and the Featured Content frame move up for the Dueling Pit's button.</summary>
        public const int ColumnShift = 58, FeaturedShift = 48;

        /// <summary>Main menu: the game's own Dueling Pit button and screen, shown on PC too (once per menu).</summary>
        public static void EnsureButton(UILandingPageViewModel vm)
        {
            try
            {
                if (vm == null || vm.MenuUI.DuelingPit == null || vm.DuelingPit == null || vm.MenuUI.DuelingPit.gameObject.activeSelf) return;
                vm.MenuUI.DuelingPit.gameObject.SetActive(true);
                vm.DuelingPit.gameObject.SetActive(true);
                UIGrid g = vm.MenuUI.DuelingPit.transform.parent != null ? vm.MenuUI.DuelingPit.transform.parent.GetComponent<UIGrid>() : null;
                if (g != null)
                {
                    // The column grows downward by a row: up 58 pixels, and the Featured Content frame above it up 48
                    // (to just below the top bar; see Community), so that all of it stays between the bars.
                    UiClone.ShiftUp(g.gameObject, ColumnShift);
                    g.Reposition();
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [DuelingPit] button: " + e.Message); }
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
