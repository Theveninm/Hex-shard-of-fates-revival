using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexOffline
{
    /// <summary>
    /// Injecteur de triche POUR LES TESTS, volontairement separe de la logique du mod.
    /// Inactif sauf si le fichier "HexOffline.cheats" existe a la racine du jeu.
    /// Agit cote "serveur" (session autoritative) : le client recoit les changements par le
    /// flux normal (deltas d'etat + options), ce qui teste aussi ce flux.
    ///
    /// Commandes (canal HexOffline.cmd) :
    ///   cheat find &lt;texte&gt;               cartes dont le nom contient le texte
    ///   cheat give &lt;j|ia&gt; &lt;nom de carte&gt;   ajoute la carte dans la main
    ///   cheat res &lt;j|ia&gt; &lt;n&gt;               +n ressources (total, courant, seuils de toutes couleurs)
    /// </summary>
    public static class Cheats
    {
        public static bool Enabled()
        {
            return File.Exists(Path.Combine(Path.GetFullPath(Application.dataPath + "/../"), "HexOffline.cheats"));
        }

        public static string Execute(StandaloneGameClient gc, string[] parts)
        {
            if (!Enabled())
                return "triches desactivees (creer HexOffline.cheats)";
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
                return "pas de partie en cours";
            if (parts.Length < 4)
                return "usage: cheat " + sub + " <j|ia> ...";
            Player p = FindPlayer(ss, parts[2]);
            if (p == null)
                return "joueur introuvable (j ou ia)";

            switch (sub)
            {
                case "give":
                {
                    CardTemplate t = FindCard(string.Join(" ", parts, 3, parts.Length - 3));
                    if (t == null)
                        return "carte introuvable";
                    GiveToHand(ss, p, t);
                    Server.MarkDirty();
                    return "donne " + t.m_Name + " a " + p.m_PlayerId;
                }
                case "res":
                {
                    int n;
                    if (!int.TryParse(parts[3], out n))
                        return "nombre invalide";
                    ss.IncrementPlayerTotalResourcePool(p, n, false);
                    ss.IncrementPlayerCurrentResourcePool(p, n, false);
                    foreach (ECardShards c in new[] { ECardShards.Blood, ECardShards.Ruby, ECardShards.Sapphire, ECardShards.Wild, ECardShards.Diamond })
                        ss.IncrementPlayerResourceThreshold(p, c, n, false);
                    Server.MarkDirty();
                    return "ressources +" + n + " pour " + p.m_PlayerId;
                }
            }
            return "sous-commande inconnue";
        }

        /// <summary>
        /// Met une carte neuve dans la main, comme la triche de debogage du jeu
        /// (DebugCheatTransaction) : creation, dessus de la pioche, capacites de creation,
        /// capacites PERMANENTES seulement, puis pioche. CreateEncounterCardsInZone(Main) creait
        /// TOUTES les capacites automatiques des la creation : elles etaient recreees a l'entree
        /// en jeu, d'ou des effets statiques doubles (ex. Briar Legion +4/+4 au lieu de +2/+2).
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

        private static StandaloneSession GetServer(StandaloneGameClient gc)
        {
            FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(gc) as StandaloneSession;
        }

        private static Player FindPlayer(StandaloneSession ss, string who)
        {
            bool wantAI = who.ToLower().StartsWith("ia") || who.ToLower().StartsWith("ai");
            foreach (Player p in ss.GetAllPlayers())
            {
                if (p == null) continue;
                bool isAI = p.m_PlayerId.GetUIDType() == UID.Type.AIPlayer;
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

        /// <summary>Texte complet d'une carte (texte de la carte + textes de ses capacites).</summary>
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

        /// <summary>cheat text &lt;mots&gt; : cartes (une par nom) dont le texte contient les mots.</summary>
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
                res.Add(t.m_Name + " [" + t.m_CardType + ", " + t.m_ResourceCost + "]");
                if (res.Count >= 40) break;
            }
            return res.Count + " carte(s)\n" + string.Join("\n", res.ToArray());
        }

        /// <summary>cheat cost &lt;min&gt; [type] : cartes de cout &gt;= min (40 max).</summary>
        private static string FindCost(string min, string type)
        {
            int m;
            if (!int.TryParse(min, out m)) return "cout invalide";
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
            return res.Count + " carte(s)\n" + string.Join("\n", res.GetRange(0, Math.Min(60, res.Count)).ToArray());
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

namespace HexOffline
{
    /// <summary>
    /// Banc d'essai des cartes (outil de test, separe du mod). Actif si "HexOffline.gauntlet" existe
    /// (et HexOffline.cheats). A chaque debut de phase principale, le joueur actif (joue par l'IA)
    /// recoit les K cartes suivantes de la liste des cartes possedables + des ressources ;
    /// on note ensuite si chaque carte a quitte la main (= jouee). Resultats :
    /// HexOffline.gauntlet.csv (carte;partie;tour;joueur;resultat;zone). Position : HexOffline.gauntlet.
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
            public int Windows;   // fenetres ou le detenteur avait la priorite (phase principale, chaine vide)
            public int CanPlay;   // ... dont celles ou le moteur disait la carte jouable
            public int Forced;
            public int Skips;
            public int ClientWindows;      // fenetres (vues du client) ou l'option "jouer" etait presente
            public string LastWindowKey, LastForcedKey;    // tentatives de jeu forcees (mode HexOffline.gauntlet.force)
        }

        private static List<CardTemplate> s_Cards;
        private static readonly List<Given> s_Given = new List<Given>();
        private static string s_LastFeed;
        private static int s_Game;

        private static string Root { get { return Path.GetFullPath(Application.dataPath + "/../"); } }
        /// <summary>Activation : fichier HexOffline.gauntlet (commun). Position : par instance.</summary>
        private static string MarkerPath { get { return Path.Combine(Root, "HexOffline.gauntlet"); } }
        private static string StatePath { get { return Path.Combine(Root, Instance.Id == 0 ? "HexOffline.gauntlet" : "HexOffline.gauntlet.pos" + Instance.Suffix); } }
        private static string CsvPath { get { return Path.Combine(Root, Instance.Name("HexOffline.gauntlet.csv")); } }

        public static bool Enabled() { return Cheats.Enabled() && File.Exists(MarkerPath); }

        /// <summary>Mode "cible" (fichier HexOffline.gauntlet.focus) : moins de cartes par tour et un
        /// plateau prepare a chaque distribution (troupes en jeu et au cimetiere des deux cotes,
        /// un artefact chacun) pour que les sorts aient toujours des cibles.</summary>
        private static bool Focus() { return File.Exists(Path.Combine(Root, "HexOffline.gauntlet.focus")); }

        private static CardTemplate s_FillerTroop, s_FillerArtifact;
        private static bool s_FillersSearched;

        /// <summary>Cartes "neutres" pour garnir le plateau : sans capacite, troupe 2-3 / 2-4.</summary>
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
            Debug.Log("[HexOffline] [Gauntlet] cartes de remplissage : troupe=" + (s_FillerTroop != null ? s_FillerTroop.m_Name : "-")
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
                catch (Exception e) { Debug.LogWarning("[HexOffline] [Gauntlet] PrepareBoard: " + e.Message); }
            }
        }

        /// <summary>Resume pour le fichier d'etat : position, total, cartes jouees / non jouees.</summary>
        public static string Summary()
        {
            int total = Cards().Count, pos = ReadIndex(), played = 0, notPlayed = 0;
            try
            {
                if (File.Exists(CsvPath))
                    foreach (string l in File.ReadAllLines(CsvPath))
                    {
                        if (l.Contains(";jouee;")) played++;
                        else if (l.Contains(";NON_JOUEE;")) notPlayed++;
                    }
            }
            catch { }
            int done = Math.Min(pos, total);
            return "Banc d'essai : carte " + done + " / " + total + " (" + (total > 0 ? (100 * done / total) : 0) + " %)"
                + (pos >= total ? "  -> TERMINE" : "")
                + "\nCartes jouees : " + played + "   non jouees : " + notPlayed;
        }

        /// <summary>
        /// Toutes les cartes du jeu jouables en partie (y compris PvE / IA uniquement), une par nom :
        /// hors champions, ressources et jetons ("Land"). Triees par nom pour un ordre stable.
        /// </summary>
        private static List<CardTemplate> Cards()
        {
            // Ne pas figer la liste avant la fin du chargement des cartes (le fichier d'etat
            // interroge le banc des l'ecran de connexion).
            if (s_Cards == null && !Singleton<TemplateManager>.Instance.LoadCompleted)
                return new List<CardTemplate>();
            string listPath = Path.Combine(Root, "HexOffline.gauntlet.list");
            if (s_Cards == null && File.Exists(listPath))
            {
                // Liste restreinte (un nom de carte par ligne), ex. pour retester les cartes non jouees.
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
        /// Position dans la liste. Avec N instances, l'instance k traite les cartes k, k+N, k+2N...
        /// (fichier de position propre a chaque instance ; l'instance 0 seule reprend HexOffline.gauntlet).
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

        /// <summary>Appele chaque seconde pendant un combat.</summary>
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
            // Mode force : seules les cartes du joueur "client" peuvent etre forcees ; ne donner qu'a lui.
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
                    Debug.Log("[HexOffline] [Gauntlet] liste terminee");
                    break;
                }
                CardTemplate t = cards[index];
                index += Instance.Count;
                Card created = null;
                try { created = Cheats.GiveToHand(ss, active, t); }
                catch (Exception e) { Debug.LogWarning("[HexOffline] [Gauntlet] GiveToHand " + t.m_Name + ": " + e.Message); }
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
            Debug.Log("[HexOffline] [Gauntlet] cartes donnees jusqu'a l'index " + index + " a " + active.m_PlayerId);
        }

        /// <summary>Diagnostic : a chaque seconde ou un detenteur a la priorite (phase principale,
        /// chaine vide), note si le moteur juge sa carte jouable.</summary>
        private static LocalPlayer s_ClientPlayer;
        private static bool s_ForcedThisTick;
        private static bool ForceMode() { return File.Exists(Path.Combine(Root, "HexOffline.gauntlet.force")); }

        /// <summary>
        /// Mode force : fait jouer la carte par l'IA de son detenteur sans passer par sa decision
        /// (AITactical.PlayCard(carte) prive : meme construction des donnees d'activation que l'IA).
        /// Sert a verifier que la carte FONCTIONNE, independamment des choix de l'IA.
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
            // Attendre que le client ait rattrape le serveur (file vide, option "jouer" recue).
            string q = ClientQueue(owner);
            ClientSessionBase csb = owner.m_Session as ClientSessionBase;
            bool clientPrio = csb == null || (csb.PriorityPlayer != null && csb.PriorityPlayer.m_PlayerId == owner.m_PlayerId);
            if (!clientPrio || (cr.Usage & ECardUsage.Play) == 0)
            {
                if (g.Skips++ < 4)
                {
                    ClientSessionBase cs = owner.m_Session as ClientSessionBase;
                    Debug.Log("[HexOffline] [Gauntlet] attente " + g.Name + " : file=" + q + " usage=" + cr.Usage
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
            Debug.Log("[HexOffline] [Gauntlet] avant jeu force de " + g.Name + " : usage client=" + cr.Usage
                + " zone client=" + cr.Collection + " derniere liste serveur=" + (last == null ? "aucune" : (last.Contains(g.Id) ? "avec la carte" : "sans la carte (" + last.Count + ")"))
                + (wasFor ? " [liste SendPlayerOptionsFor]" : "") + " chaine=" + ss.Chain.IsEmpty + " phase=" + ss.CurrentTurnPhase
                + " fileClient=" + ClientQueue(owner) + " usagesClient=" + ClientUsages(fn, owner));
            MethodInfo m = ai.GetType().GetMethod("PlayCard", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(CardRepresentation) }, null);
            if (m == null) return;
            g.Forced++;
            object r = m.Invoke(ai, new object[] { cr });
            Debug.Log("[HexOffline] [Gauntlet] jeu force de " + g.Name + " par " + prio.m_PlayerId + " -> " + r);
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
        /// Mode force, a chaque image : quand le CLIENT (en retard sur le serveur, file de paquets)
        /// voit la priorite chez son joueur en phase principale avec l'option "jouer" sur une carte
        /// du banc, on compte la fenetre ; apres 2 fenetres laissees a l'IA, on force le jeu.
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
            // Client et serveur d'accord (sinon la transaction part sur un etat perime et echoue).
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
                catch (Exception e) { Debug.LogWarning("[HexOffline] [Gauntlet] ForcePlay " + g.Name + ": " + e); }
                return; // une seule tentative par image
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

        /// <summary>Ecrit les cartes dont le sort est connu (sorties de main), ou toutes si fin de partie.</summary>
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
                lines.Add(string.Join(";", new[] { g.Name.Replace(";", ","), g.Game.ToString(), g.Turn.ToString(), g.Player.ToString(), played ? "jouee" : "NON_JOUEE", zone,
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
