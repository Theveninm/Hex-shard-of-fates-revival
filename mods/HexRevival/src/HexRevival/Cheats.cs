using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Cheat injector FOR TESTING, deliberately kept apart from the mod's logic.
    /// Inactive unless the file "HexRevival.cheats" exists in the game folder.
    /// Acts on the "server" side (authoritative session): the client receives the changes through the
    /// normal stream (state deltas + options), which also tests that stream.
    ///
    /// Commands (HexRevival.cmd channel):
    ///   cheat find &lt;text&gt;               cards whose name contains the text
    ///   cheat give &lt;j|ia&gt; &lt;card name&gt;   puts the card in the hand
    ///   cheat res &lt;j|ia&gt; &lt;n&gt;               +n resources (total, current, thresholds of every color)
    ///   cheat health &lt;j|ia&gt; &lt;n&gt;            sets the champion's health
    /// </summary>
    public static class Cheats
    {
        public static bool Enabled()
        {
            return File.Exists(Path.Combine(LocalServer.Root, "HexRevival.cheats"));
        }

        public static string Execute(StandaloneGameClient gc, string[] parts)
        {
            if (!Enabled())
                return "cheats disabled (create HexRevival.cheats)";
            if (parts.Length < 2)
                return "usage: cheat find|give|res ...";
            string sub = parts[1].ToLower();
            if (sub == "find")
                return Find(string.Join(" ", parts, 2, parts.Length - 2));
            if (sub == "text")
                return FindText(string.Join(" ", parts, 2, parts.Length - 2));
            if (sub == "cost" && parts.Length >= 3)
                return FindCost(parts[2], parts.Length >= 4 ? parts[3] : "");

            StandaloneSession ss = GetServer(gc);
            if (ss == null)
                return "no game in progress";
            if (parts.Length < 4)
                return "usage: cheat " + sub + " <j|ai> ...";
            Player p = FindPlayer(ss, parts[2]);
            if (p == null)
                return "player not found (j or ai)";

            switch (sub)
            {
                case "give":
                {
                    CardTemplate t = FindCard(string.Join(" ", parts, 3, parts.Length - 3));
                    if (t == null)
                        return "card not found";
                    GiveToHand(ss, p, t);
                    Server.MarkDirty();
                    return "gave " + t.m_Name + " to " + p.m_PlayerId;
                }
                case "health":
                {
                    // dev: "cheat health j 17" = sets the champion's health
                    int h;
                    if (!int.TryParse(parts[3], out h))
                        return "invalid number";
                    ss.SetChampionHealth(p.m_ChampionCard, h, false);
                    Server.MarkDirty();
                    return "health " + h + " for " + p.m_PlayerId;
                }
                case "res":
                {
                    int n;
                    if (!int.TryParse(parts[3], out n))
                        return "invalid number";
                    AddResources(ss, p, n);
                    return "resources +" + n + " for " + p.m_PlayerId;
                }
            }
            return "unknown subcommand";
        }

        /// <summary>
        /// Puts a new card in the hand, like the game's debug cheat
        /// (DebugCheatTransaction): creation, top of the deck, creation abilities,
        /// PERMANENT abilities only, then draw. CreateEncounterCardsInZone(Main) created
        /// ALL automatic abilities at creation: they were created again when the card entered
        /// play, hence doubled static effects (e.g. Briar Legion +4/+4 instead of +2/+2).
        /// </summary>
        public static Card GiveToHand(StandaloneSession ss, Player p, CardTemplate t)
        {
            Card card = ss.CreateCard(t.m_Id, false, false, p);
            if (card == null) return null;
            p.m_Deck.AddCardAtLocation(card, ECardLocations.Top);
            ss.ActivateCardCreationAbilities(p, card);
            if (card.IsArtifact() || card.IsTroop() || card.IsConstant())
                ss.CreateAutomaticAbilitiesForTransform(card, EAbilityDurations.Permanent);
            ss.CheatCreateCardActivatePermanentCEs(card);
            ss.DrawCard(p, card.m_SessionCardId, new TAC());
            return card;
        }

        /// <summary>+n resources: total, current, and thresholds of every color.</summary>
        internal static void AddResources(StandaloneSession ss, Player p, int n)
        {
            ss.IncrementPlayerTotalResourcePool(p, n, false);
            ss.IncrementPlayerCurrentResourcePool(p, n, false);
            AddThresholds(ss, p, n);
        }

        /// <summary>
        /// In-game cheat: +n maximum resources (the resources received each turn), also available
        /// right away for the current turn. Shard thresholds are unchanged.
        /// </summary>
        internal static void AddResourcesOnly(StandaloneSession ss, Player p, int n)
        {
            ss.IncrementPlayerTotalResourcePool(p, n, false);
            ss.IncrementPlayerCurrentResourcePool(p, n, false);
            Server.MarkDirty();
        }

        /// <summary>+n to the threshold of every shard color.</summary>
        internal static void AddThresholds(StandaloneSession ss, Player p, int n)
        {
            foreach (ECardShards c in new[] { ECardShards.Blood, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild, ECardShards.Diamond })
                ss.IncrementPlayerResourceThreshold(p, c, n, false);
            Server.MarkDirty();
        }

        /// <summary>The player draws a card, as with a normal draw.</summary>
        internal static void DrawCard(StandaloneSession ss, Player p)
        {
            ss.DrawCard(p, SessionCardId.Invalid, new TAC());
            Server.MarkDirty();
        }

        internal static StandaloneSession GetServer(StandaloneGameClient gc)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(gc) as StandaloneSession;
        }

        internal static Player FindPlayer(StandaloneSession ss, string who)
        {
            bool wantAI = who.ToLower().StartsWith("ia") || who.ToLower().StartsWith("ai");
            foreach (Player p in ss.GetAllPlayers())
            {
                if (p == null) continue;
                bool isAI = Server.IsAIId(p.m_PlayerId);
                if (isAI == wantAI) return p;
            }
            return null;
        }

        private static CardTemplate FindCard(string name)
        {
            name = name.Trim().ToLower();
            CardTemplate partial = null;
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_Name == null) continue;
                string n = t.m_Name.ToLower();
                if (n == name) return t;
                if (partial == null && n.Contains(name)) partial = t;
            }
            return partial;
        }

        /// <summary>Full text of a card (card text + texts of its abilities).</summary>
        private static string CardText(CardTemplate t)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(t.m_GameText ?? "");
            if (t.m_CardAbilities != null)
                foreach (CardAbilityContainer a in t.m_CardAbilities)
                {
                    try { sb.Append(' ').Append((string)a); } catch { }
                }
            return sb.ToString();
        }

        /// <summary>cheat text &lt;words&gt;: cards (one per name) whose text contains the words.</summary>
        private static string FindText(string text)
        {
            text = text.Trim().ToLower();
            HashSet<string> seen = new HashSet<string>();
            List<string> res = new List<string>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_Name == null || t.CardRarity <= ERarity.Land || seen.Contains(t.m_Name)) continue;
                if (!CardText(t).ToLower().Contains(text)) continue;
                seen.Add(t.m_Name);
                string txt = CardText(t).Replace("\n", " ");
                res.Add(t.m_Name + " [" + t.m_CardType + ", " + t.m_ResourceCost + "] " + (txt.Length > 160 ? txt.Substring(0, 160) + "..." : txt));
                if (res.Count >= 40) break;
            }
            return res.Count + " card(s)\n" + string.Join("\n", res.ToArray());
        }

        /// <summary>cheat cost &lt;min&gt; [type]: cards costing &gt;= min (40 max).</summary>
        private static string FindCost(string min, string type)
        {
            int m;
            if (!int.TryParse(min, out m)) return "invalid cost";
            type = type.ToLower();
            HashSet<string> seen = new HashSet<string>();
            List<string> res = new List<string>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_Name == null || t.CardRarity <= ERarity.Land || seen.Contains(t.m_Name)) continue;
                if (t.m_ResourceCost < m || (t.m_CardType & (ECardTypes.Champion | ECardTypes.Resource)) != 0) continue;
                if (type.Length > 0 && !t.m_CardType.ToString().ToLower().Contains(type)) continue;
                seen.Add(t.m_Name);
                res.Add(t.m_Name + " [" + t.m_CardType + ", " + t.m_ResourceCost + "]");
            }
            res.Sort();
            return res.Count + " card(s)\n" + string.Join("\n", res.GetRange(0, Math.Min(60, res.Count)).ToArray());
        }

        private static string Find(string text)
        {
            text = text.Trim().ToLower();
            List<string> res = new List<string>();
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_Name == null || !t.m_Name.ToLower().Contains(text)) continue;
                res.Add(t.m_Name + " [" + t.m_CardType + "]");
                if (res.Count >= 25) break;
            }
            return string.Join("\n", res.ToArray());
        }
    }
}

namespace HexRevival
{
    /// <summary>
    /// Card test bench (test tool, separate from the mod). Active if "HexRevival.gauntlet" exists
    /// (and HexRevival.cheats). At the start of each main phase, the active player (played by the AI)
    /// receives the next K cards from the list of collectible cards + resources;
    /// we then record whether each card left the hand (= played). Results:
    /// HexRevival.gauntlet.csv (card;game;turn;player;result;zone). Position: HexRevival.gauntlet.
    /// </summary>
    public static class Gauntlet
    {
        public const int CardsPerTurn = 5;
        private const int MinResources = 15;
        private const int MinThreshold = 6;

        private class Given
        {
            public SessionCardId Id;
            public string Name;
            public int Game;
            public int Turn;
            public UID Player;
            public bool PlayableAtFeed;
            public Card Created;
            public bool Checked;
            public int Windows;   // windows where the holder had priority (main phase, empty chain)
            public int CanPlay;   // ... of which those where the engine said the card was playable
            public int Forced;
            public int Skips;
            public int ClientWindows;      // windows (seen by the client) where the "play" option was present
            public string LastWindowKey, LastForcedKey;    // forced play attempts (HexRevival.gauntlet.force mode)
        }

        private static List<CardTemplate> s_Cards;
        private static readonly List<Given> s_Given = new List<Given>();
        private static string s_LastFeed;
        private static int s_Game;

        private static string Root { get { return LocalServer.Root; } }
        /// <summary>Activation: HexRevival.gauntlet file (shared). Position: per instance.</summary>
        private static string MarkerPath { get { return Path.Combine(Root, "HexRevival.gauntlet"); } }
        private static string StatePath { get { return Path.Combine(Root, Instance.Id == 0 ? "HexRevival.gauntlet" : "HexRevival.gauntlet.pos" + Instance.Suffix); } }
        private static string CsvPath { get { return Path.Combine(Root, Instance.Name("HexRevival.gauntlet.csv")); } }

        public static bool Enabled() { return Cheats.Enabled() && File.Exists(MarkerPath); }

        /// <summary>"Target" mode (HexRevival.gauntlet.focus file): fewer cards per turn and a
        /// board prepared at each deal (troops in play and in the graveyard on both sides,
        /// one artifact each) so that spells always have targets.</summary>
        private static bool Focus() { return File.Exists(Path.Combine(Root, "HexRevival.gauntlet.focus")); }

        private static CardTemplate s_FillerTroop, s_FillerArtifact;
        private static bool s_FillersSearched;

        /// <summary>"Neutral" cards to fill the board: no ability, 2-3 / 2-4 troop.</summary>
        private static void FindFillers()
        {
            if (s_FillersSearched) return;
            s_FillersSearched = true;
            List<CardTemplate> all = new List<CardTemplate>(Singleton<TemplateManager>.Instance.Cards.Values);
            all.Sort((a, b) => string.CompareOrdinal(a.m_Name ?? "", b.m_Name ?? ""));
            foreach (CardTemplate t in all)
            {
                if (t == null || t.m_Name == null || t.CardRarity <= ERarity.Land) continue;
                if (t.m_CardAbilities != null && t.m_CardAbilities.Count > 0) continue;
                if (s_FillerTroop == null && (t.m_CardType & ECardTypes.Troop) != 0 && (t.m_CardType & ECardTypes.Champion) == 0
                    && t.m_BaseAttackValue >= 2 && t.m_BaseAttackValue <= 3 && t.m_BaseDefenseValue >= 2 && t.m_BaseDefenseValue <= 4)
                    s_FillerTroop = t;
                if (s_FillerArtifact == null && (t.m_CardType & ECardTypes.Artifact) != 0 && (t.m_CardType & ECardTypes.Troop) == 0)
                    s_FillerArtifact = t;
            }
            Debug.Log("[HexRevival] [Gauntlet] filler cards: troop=" + (s_FillerTroop != null ? s_FillerTroop.m_Name : "-")
                + " artefact=" + (s_FillerArtifact != null ? s_FillerArtifact.m_Name : "-"));
        }

        private static int Count(Player p, ECardCollections zone, ECardTypes type)
        {
            int n = 0;
            foreach (Card c in p.GetCardCollection(zone))
                if (c != null && (c.CurrentType & type) != 0) n++;
            return n;
        }

        private static void PrepareBoard(StandaloneSession ss)
        {
            FindFillers();
            foreach (Player p in ss.GetAllPlayers())
            {
                if (p == null) continue;
                try
                {
                    if (s_FillerTroop != null)
                    {
                        int war = Count(p, ECardCollections.Warzone, ECardTypes.Troop);
                        if (war < 2) ss.CreateEncounterCardsInZone(p, s_FillerTroop.m_Id, 2 - war, ECardCollections.Warzone, ECardLocations.Unknown);
                        int crypt = Count(p, ECardCollections.Discard, ECardTypes.Troop);
                        if (crypt < 2) ss.CreateEncounterCardsInZone(p, s_FillerTroop.m_Id, 2 - crypt, ECardCollections.Discard, ECardLocations.Unknown);
                    }
                    if (s_FillerArtifact != null && Count(p, ECardCollections.Warzone, ECardTypes.Artifact) < 1)
                        ss.CreateEncounterCardsInZone(p, s_FillerArtifact.m_Id, 1, ECardCollections.Warzone, ECardLocations.Unknown);
                }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Gauntlet] PrepareBoard: " + e.Message); }
            }
        }

        /// <summary>Summary for the status file: position, total, played / unplayed cards.</summary>
        public static string Summary()
        {
            int total = Cards().Count, pos = ReadIndex(), played = 0, notPlayed = 0;
            try
            {
                if (File.Exists(CsvPath))
                    foreach (string l in File.ReadAllLines(CsvPath))
                    {
                        if (l.Contains(";played;")) played++;
                        else if (l.Contains(";NOT_PLAYED;")) notPlayed++;
                    }
            }
            catch { }
            int done = Math.Min(pos, total);
            return "Test bench: card " + done + " / " + total + " (" + (total > 0 ? (100 * done / total) : 0) + " %)"
                + (pos >= total ? "  -> DONE" : "")
                + "\nPlayed cards: " + played + "   not played: " + notPlayed;
        }

        /// <summary>
        /// Every card of the game playable in a match (including PvE / AI-only), one per name:
        /// excluding champions, resources and tokens ("Land"). Sorted by name for a stable order.
        /// </summary>
        private static List<CardTemplate> Cards()
        {
            // Do not freeze the list before the cards have finished loading (the status file
            // queries the bench from the login screen on).
            if (s_Cards == null && !Singleton<TemplateManager>.Instance.LoadCompleted)
                return new List<CardTemplate>();
            string listPath = Path.Combine(Root, "HexRevival.gauntlet.list");
            if (s_Cards == null && File.Exists(listPath))
            {
                // Restricted list (one card name per line), e.g. to retest the unplayed cards.
                HashSet<string> wanted = new HashSet<string>();
                foreach (string l in File.ReadAllLines(listPath))
                    if (l.Trim().Length > 0) wanted.Add(l.Trim());
                Dictionary<string, CardTemplate> found = new Dictionary<string, CardTemplate>();
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                    if (t != null && t.m_Name != null && wanted.Contains(t.m_Name) && !found.ContainsKey(t.m_Name)
                        && (t.m_CardType & (ECardTypes.Champion | ECardTypes.Resource)) == 0)
                        found[t.m_Name] = t;
                s_Cards = new List<CardTemplate>(found.Values);
                s_Cards.Sort((a, b) => string.CompareOrdinal(a.m_Name, b.m_Name));
            }
            if (s_Cards == null)
            {
                Dictionary<string, CardTemplate> byName = new Dictionary<string, CardTemplate>();
                foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
                {
                    if (t == null || string.IsNullOrEmpty(t.m_Name)) continue;
                    if ((t.m_CardType & (ECardTypes.Champion | ECardTypes.Resource)) != 0) continue;
                    if (t.CardRarity <= ERarity.Land) continue;
                    if (!byName.ContainsKey(t.m_Name)) byName[t.m_Name] = t;
                }
                s_Cards = new List<CardTemplate>(byName.Values);
                s_Cards.Sort((a, b) => string.CompareOrdinal(a.m_Name, b.m_Name));
            }
            return s_Cards;
        }

        /// <summary>
        /// Position in the list. With N instances, instance k handles cards k, k+N, k+2N...
        /// (position file specific to each instance; only instance 0 uses HexRevival.gauntlet).
        /// </summary>
        private static int ReadIndex()
        {
            try { int i; if (File.Exists(StatePath) && int.TryParse(File.ReadAllText(StatePath).Trim(), out i)) return i; } catch { }
            return Instance.Supervised ? Instance.Id - 1 : 0;
        }

        public static void OnNewGame()
        {
            Resolve(true);
            s_Given.Clear();
            Server.OfferedToPlay.Clear();
            s_LastFeed = null;
            s_Game++;
        }

        /// <summary>Called every second during a match.</summary>
        public static void Tick(StandaloneSession ss, LocalPlayer clientPlayer)
        {
            s_ClientPlayer = clientPlayer;
            if (ss == null || !ss.IsGamePlaying)
                return;
            Player active = ss.GetActivePlayer();
            SampleWindows(ss);
            if (active == null || ss.CurrentTurnPhase != ETurnPhases.FirstMainPhase)
            {
                Resolve(false);
                return;
            }
            // Forced mode: only the "client" player's cards can be forced; give cards to that player only.
            if (ForceMode() && s_ClientPlayer != null && active.m_PlayerId != s_ClientPlayer.m_PlayerId)
                return;
            string key = s_Game + "|" + ss.m_TotalTurnsTaken + "|" + active.m_PlayerId;
            if (key == s_LastFeed)
                return;
            s_LastFeed = key;

            List<CardTemplate> cards = Cards();
            int index = ReadIndex();
            bool focus = Focus();
            if (focus && index < cards.Count)
                PrepareBoard(ss);
            int perTurn = focus ? 2 : CardsPerTurn;
            for (int k = 0; k < perTurn && cards.Count > 0; k++)
            {
                if (index >= cards.Count)
                {
                    Debug.Log("[HexRevival] [Gauntlet] list finished");
                    break;
                }
                CardTemplate t = cards[index];
                index += Instance.Count;
                Card created = null;
                try { created = Cheats.GiveToHand(ss, active, t); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Gauntlet] GiveToHand " + t.m_Name + ": " + e.Message); }
                if (created != null && created.m_CurrentCardCollection == ECardCollections.Hand)
                    s_Given.Add(new Given { Id = created.m_SessionCardId, Name = t.m_Name, Game = s_Game, Turn = ss.m_TotalTurnsTaken, Player = active.m_PlayerId, Created = created });
            }
            try { File.WriteAllText(StatePath, index.ToString()); } catch { }

            if (active.m_TotalResourcePool < MinResources)
            {
                int add = MinResources - active.m_TotalResourcePool;
                ss.IncrementPlayerTotalResourcePool(active, add, false);
                ss.IncrementPlayerCurrentResourcePool(active, add, false);
            }
            foreach (ECardShards c in new[] { ECardShards.Blood, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild, ECardShards.Diamond })
            {
                int have;
                active.m_ResourceThresholds.TryGetValue(c, out have);
                if (have < MinThreshold)
                    ss.IncrementPlayerResourceThreshold(active, c, MinThreshold - have, false);
            }
            foreach (Given g in s_Given)
            {
                if (g.Checked || g.Created == null) continue;
                g.Checked = true;
                try { g.PlayableAtFeed = ss.CanPlayCard(g.Created, active); } catch { }
            }
            Server.MarkDirty();
            Debug.Log("[HexRevival] [Gauntlet] cards given up to index " + index + " to " + active.m_PlayerId);
        }

        /// <summary>Diagnostics: every second where a holder has priority (main phase,
        /// empty chain), records whether the engine considers its card playable.</summary>
        private static LocalPlayer s_ClientPlayer;
        private static bool s_ForcedThisTick;
        private static bool ForceMode() { return File.Exists(Path.Combine(Root, "HexRevival.gauntlet.force")); }

        /// <summary>
        /// Forced mode: makes the holder's AI play the card without going through its decision
        /// (private AITactical.PlayCard(card): same activation data construction as the AI).
        /// Used to check that the card WORKS, independently of the AI's choices.
        /// </summary>
        private static void ForcePlay(StandaloneSession ss, Player prio, Given g)
        {
            LocalPlayer owner = null;
            if (s_ClientPlayer != null && (s_ClientPlayer.m_PlayerId == prio.m_PlayerId || g.Player == s_ClientPlayer.m_PlayerId)) owner = s_ClientPlayer;
            else owner = prio as LocalPlayer;
            if (owner == null || owner.AITactical == null) return;
            object ai = owner.AITactical;
            FieldInfo ff = ai.GetType().GetField("m_Functions", BindingFlags.Instance | BindingFlags.NonPublic);
            Game.Shared.AI.AIFunctions fn = ff != null ? ff.GetValue(ai) as Game.Shared.AI.AIFunctions : null;
            if (fn == null) return;
            CardRepresentation cr = fn.GetCard(g.Id);
            if (cr == null) return;
            // Wait until the client has caught up with the server (empty queue, "play" option received).
            string q = ClientQueue(owner);
            ClientSessionBase csb = owner.m_Session as ClientSessionBase;
            bool clientPrio = csb == null || (csb.PriorityPlayer != null && csb.PriorityPlayer.m_PlayerId == owner.m_PlayerId);
            if (!clientPrio || (cr.Usage & ECardUsage.Play) == 0)
            {
                if (g.Skips++ < 4)
                {
                    ClientSessionBase cs = owner.m_Session as ClientSessionBase;
                    Debug.Log("[HexRevival] [Gauntlet] waiting for " + g.Name + " : file=" + q + " usage=" + cr.Usage
                        + " optionsClient=" + (cs != null ? cs.Cache.GetUsageFor(g.Id).ToString() : "?")
                        + " prioClient=" + (cs != null && cs.PriorityPlayer != null ? cs.PriorityPlayer.m_PlayerId.ToString() : "?")
                        + " phaseClient=" + (cs != null ? cs.CurrentPhase.ToString() : "?") + " phaseSrv=" + ss.CurrentTurnPhase);
                }
                return;
            }
            HashSet<SessionCardId> last;
            bool wasFor;
            Server.LastPlayOffer.TryGetValue(prio.m_PlayerId, out last);
            Server.LastOfferWasFor.TryGetValue(prio.m_PlayerId, out wasFor);
            Debug.Log("[HexRevival] [Gauntlet] before forced play of " + g.Name + ": client usage=" + cr.Usage
                + " client zone=" + cr.Collection + " last server list=" + (last == null ? "none" : (last.Contains(g.Id) ? "with the card" : "without the card (" + last.Count + ")"))
                + (wasFor ? " [SendPlayerOptionsFor list]" : "") + " chain=" + ss.Chain.IsEmpty + " phase=" + ss.CurrentTurnPhase
                + " fileClient=" + ClientQueue(owner) + " usagesClient=" + ClientUsages(fn, owner));
            MethodInfo m = ai.GetType().GetMethod("PlayCard", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CardRepresentation) }, null);
            if (m == null) return;
            g.Forced++;
            object r = m.Invoke(ai, new object[] { cr });
            Debug.Log("[HexRevival] [Gauntlet] forced play of " + g.Name + " by " + prio.m_PlayerId + " -> " + r);
        }

        private static string ClientQueue(LocalPlayer owner)
        {
            try
            {
                FieldInfo q = typeof(Game.Client.SessionClient).GetField("m_SyncEventsQueue", BindingFlags.Instance | BindingFlags.NonPublic);
                System.Collections.ICollection c = (q != null && owner.m_Session is Game.Client.SessionClient) ? q.GetValue(owner.m_Session) as System.Collections.ICollection : null;
                return c == null ? "?" : c.Count.ToString();
            }
            catch { return "?"; }
        }

        private static string ClientUsages(Game.Shared.AI.AIFunctions fn, LocalPlayer owner)
        {
            try
            {
                int n = 0;
                foreach (KeyValuePair<SessionCardId, CardRepresentation> kv in ((ClientSessionBase)owner.m_Session).Cache.Cards)
                    if (kv.Value != null && kv.Value.Usage != ECardUsage.None) n++;
                return n.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>
        /// Forced mode, every frame: when the CLIENT (behind the server, packet queue)
        /// sees priority on its player in the main phase with the "play" option on a bench
        /// card, we count the window; after 2 windows left to the AI, we force the play.
        /// </summary>
        public static void FrameCheck(StandaloneSession ss, LocalPlayer client)
        {
            if (ss == null || client == null || s_Given.Count == 0 || !ForceMode())
                return;
            s_ClientPlayer = client;
            ClientSessionBase cs = client.m_Session as ClientSessionBase;
            if (cs == null || cs.PriorityPlayer == null || cs.PriorityPlayer.m_PlayerId != client.m_PlayerId)
                return;
            if (!cs.CurrentPhase.IsMainPhasePriorityWindow())
                return;
            // Client and server agree (otherwise the transaction is built on a stale state and fails).
            Player srvPrio = ss.GetPriorityPlayer();
            if (srvPrio == null || srvPrio.m_PlayerId != client.m_PlayerId || ClientQueue(client) != "0" || !ss.Chain.IsEmpty)
                return;
            string key = s_Game + "|" + ss.m_TotalTurnsTaken + "|" + cs.CurrentPhase;
            foreach (Given g in s_Given)
            {
                if (g.Created == null || g.Player != client.m_PlayerId || g.Forced >= 2) continue;
                if (g.Created.m_CurrentCardCollection != ECardCollections.Hand) continue;
                CardRepresentation cr;
                if (!cs.Cache.Cards.TryGetValue(g.Id, out cr) || cr == null || (cr.Usage & ECardUsage.Play) == 0) continue;
                if (g.LastWindowKey != key) { g.LastWindowKey = key; g.ClientWindows++; }
                if (g.ClientWindows <= 2 || g.LastForcedKey == key) continue;
                g.LastForcedKey = key;
                Player prio = ss.GetPriorityPlayer();
                try { ForcePlay(ss, prio ?? client, g); }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Gauntlet] ForcePlay " + g.Name + ": " + e); }
                return; // only one attempt per frame
            }
        }

        private static void SampleWindows(StandaloneSession ss)
        {
            try
            {
                s_ForcedThisTick = false;
                Player prio = ss.GetPriorityPlayer();
                if (prio == null || !ss.CurrentTurnPhase.IsMainPhasePriorityWindow() || !ss.Chain.IsEmpty)
                    return;
                foreach (Given g in s_Given)
                {
                    if (g.Created == null || g.Player != prio.m_PlayerId) continue;
                    if (g.Created.m_CurrentCardCollection != ECardCollections.Hand) continue;
                    g.Windows++;
                    if (ss.CanPlayCard(g.Created, prio))
                    {
                        g.CanPlay++;
                    }
                }
            }
            catch { }
        }

        /// <summary>Writes the cards whose fate is known (left the hand), or all of them at the end of the game.</summary>
        private static void Resolve(bool all)
        {
            if (s_Given.Count == 0)
                return;
            StandaloneSession ss = Server.CurrentSession;
            List<string> lines = new List<string>();
            for (int i = s_Given.Count - 1; i >= 0; i--)
            {
                Given g = s_Given[i];
                Card c = ss != null ? ss.ResourceCache.GetCard(g.Id) : null;
                string zone = c != null ? c.m_CurrentCardCollection.ToString() : "disparue";
                bool played = c == null || c.m_CurrentCardCollection != ECardCollections.Hand;
                if (!played && !all)
                    continue;
                int fails;
                Server.FailuresByCard.TryGetValue(g.Id, out fails);
                lines.Add(string.Join(";", new[] { g.Name.Replace(";", ","), g.Game.ToString(), g.Turn.ToString(), g.Player.ToString(), played ? "played" : "NOT_PLAYED", zone,
                    g.PlayableAtFeed ? "jouable" : "injouable", fails.ToString(),
                    g.Windows.ToString(), g.CanPlay.ToString(), Server.OfferedToPlay.Contains(g.Id) ? "proposee" : "jamais_proposee", g.Forced.ToString() }));
                s_Given.RemoveAt(i);
            }
            if (lines.Count > 0)
            {
                try { File.AppendAllText(CsvPath, string.Join("\n", lines.ToArray()) + "\n"); } catch { }
            }
        }
    }
}
