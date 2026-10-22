using System;
using System.Collections.Generic;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The AI configuration of each mode (see AiConfig): its tiers, the decks drawn in each tier, and the decks that can
    /// be added, by category: the shared decks made for players, those made for the AI (see DeckAiPanel), the game's own
    /// AI decks and its encounters. Each deck of a tier has a frequency and, in the arena and Siege, a role (common,
    /// elite, boss, rare). A game deck or an encounter can be copied into a shared deck to change it; a shared deck
    /// opens in the deck editor or is deleted. The account's settings, or the default settings (every account that kept
    /// them), are edited; changes are saved when the target or the window changes.
    /// Opened on one mode by the "Configure the AIs" button at the bottom right of that mode's screen (see AiButton).
    /// </summary>
    public class AiDecksWindow : MonoBehaviour
    {
        private static AiDecksWindow s_Instance;
        private bool m_Open;
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();

        private string m_Mode = AiConfig.Ladder;
        private bool m_EditDefaults, m_Dirty;
        private AiConfig.ModeConfig m_Cfg;
        private int m_Tier;
        private int m_Category;
        private string m_Search = "";
        private Vector2 m_ScrollTiers, m_ScrollDecks, m_ScrollCat;
        private ulong m_ConfirmDelete;
        private string m_Message;

        private class Entry
        {
            public string Kind, Key, Name, Detail;
            public ulong DeckId;
            public DeckTemplate Template;
            public ChampionTemplate Champion;
        }

        private List<Entry>[] m_Lists;
        private static List<Entry> s_GameDecks, s_Encounters;
        private readonly Dictionary<string, Entry> m_Info = new Dictionary<string, Entry>();

        private void Awake() { s_Instance = this; }

        public static bool IsOpen { get { return s_Instance != null && s_Instance.m_Open; } }

        /// <summary>Opens the window on a mode (its own configuration if the account has one).</summary>
        public static void Open(string mode)
        {
            if (s_Instance == null) return;
            s_Instance.m_Open = true;
            s_Instance.m_EditDefaults = false;
            s_Instance.m_Lists = null;
            s_Instance.Load(mode);
        }

        private void Close()
        {
            SaveIfDirty();
            m_Open = false;
            m_Block.Release();
        }

        private void Load(string mode)
        {
            m_Mode = mode;
            m_Cfg = AiConfig.Copy(m_EditDefaults ? AiConfig.GetDefault(mode) : AiConfig.Get(mode));
            m_Cfg.Mode = mode;
            m_Tier = m_Cfg.Tiers.Count > 0 ? 0 : -1;
            m_Dirty = false;
            m_ScrollTiers = m_ScrollDecks = Vector2.zero;
            m_Message = null;
        }

        private void SaveIfDirty()
        {
            if (!m_Dirty || m_Cfg == null) return;
            if (m_EditDefaults) AiConfig.SaveDefault(m_Cfg);
            else AiConfig.SaveForAccount(m_Cfg);
            m_Dirty = false;
        }

        private void Update()
        {
            if (!m_Open) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            m_Block.Update(true);
        }

        // ------------------------------------------------------------------
        // Deck catalogue
        // ------------------------------------------------------------------

        private const int CatPlayer = 0, CatAi = 1, CatGame = 2, CatEncounter = 3;

        private List<Entry> List(int cat)
        {
            if (m_Lists == null)
            {
                m_Lists = new List<Entry>[4];
                m_Lists[CatPlayer] = new List<Entry>();
                m_Lists[CatAi] = new List<Entry>();
                TemplateManager tm = Singleton<TemplateManager>.Instance;
                foreach (Decks.SharedDeck d in Decks.Shared())
                {
                    ChampionTemplate ch = tm.GetChampionTemplate(d.Champion);
                    m_Lists[d.Ai.ForAI ? CatAi : CatPlayer].Add(new Entry
                    {
                        Kind = AiConfig.Shared, Key = d.Id.ToString(), Name = d.Name, DeckId = d.Id, Champion = ch,
                        Detail = (ch != null ? ch.m_Name + " - " : "") + d.Count + " " + Loc.Get("cards_found"),
                    });
                }
                m_Lists[CatGame] = GameDecks();
                m_Lists[CatEncounter] = Encounters();
            }
            return m_Lists[cat];
        }

        /// <summary>The game's decks with a champion (prebuilt, arena, AI decks), one per name and champion.</summary>
        private static List<Entry> GameDecks()
        {
            if (s_GameDecks != null) return s_GameDecks;
            s_GameDecks = new List<Entry>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            HashSet<string> seen = new HashSet<string>();
            foreach (DeckTemplate t in tm.Decks.Values)
            {
                if (t == null || string.IsNullOrEmpty(t.m_DeckName) || !t.m_ChampionId.IsValid) continue;
                ChampionTemplate ch = tm.GetChampionTemplate(t.m_ChampionId);
                if (ch == null) continue;
                int n = 0;
                foreach (TemplateCardStack st in t.m_DeckResources) n += st.m_Count;
                string key = AiConfig.GameKey(t);
                if (n < 20 || !seen.Add(key)) continue;
                // Shown by champion (the arena's and the AI's deck names are internal: "Arena_...", "AI_...").
                s_GameDecks.Add(new Entry { Kind = AiConfig.GameDeck, Key = key, Name = ch.m_Name, Detail = t.m_DeckName + " - " + n + " " + Loc.Get("cards_found"), Template = t, Champion = ch });
            }
            s_GameDecks.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return s_GameDecks;
        }

        /// <summary>The game's battles against an AI with its own deck (campaign, dungeons, arena).</summary>
        private static List<Entry> Encounters()
        {
            if (s_Encounters != null) return s_Encounters;
            s_Encounters = new List<Entry>();
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            foreach (SceneData sc in tm.CampaignScenes.Values)
            {
                EncounterScene es = sc as EncounterScene;
                if (es == null) continue;
                EncounterPlayer ai = null;
                try { ai = es.GetEncounterPlayer(true); } catch { }
                if (ai == null || !ai.m_EncounterDeck.IsValid) continue;
                // The player's deck id names an encounter deck (itself naming a deck template) or a deck template.
                DeckTemplate d;
                EncounterDeck ed;
                ResourceId deckId = tm.EncounterDecks.TryGetValue(ai.m_EncounterDeck, out ed) && ed != null ? ed.m_DeckTemplateId : ai.m_EncounterDeck;
                if (!tm.Decks.TryGetValue(deckId, out d) || d == null) continue;
                ChampionTemplate ch = null;
                try { ch = es.GetChampionTemplate(true); } catch { }
                string title = null;
                try { title = es.Title; } catch { }
                if (string.IsNullOrEmpty(title)) title = es.m_Name;
                s_Encounters.Add(new Entry
                {
                    Kind = AiConfig.Encounter, Key = es.m_Id.m_Guid.ToString(), Name = title, Template = d, Champion = ch,
                    Detail = (ch != null ? ch.m_Name + " - " : "") + es.m_Name + (es.m_IsBossEncounter ? " (" + Loc.Get("boss") + ")" : ""),
                });
            }
            s_Encounters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return s_Encounters;
        }

        /// <summary>A tier's deck as shown in the lists (name, champion), from the catalogue or else resolved.</summary>
        private Entry InfoOf(AiConfig.DeckRef r)
        {
            string k = r.Kind + ":" + r.Key;
            Entry e;
            if (m_Info.TryGetValue(k, out e)) return e;
            for (int c = 0; c < 4 && e == null; c++) e = List(c).Find(x => x.Kind == r.Kind && x.Key == r.Key);
            if (e == null)
            {
                Opponents.Opponent o = AiConfig.Resolve(r, null);
                e = new Entry { Kind = r.Kind, Key = r.Key, Name = o != null ? o.Name : "? " + r.Key, Detail = o == null ? Loc.Get("ai_missing_deck") : "" };
            }
            m_Info[k] = e;
            return e;
        }

        /// <summary>A shared deck made for the AI from a game deck or an encounter's deck, to change it.</summary>
        private void CopyToShared(DeckTemplate deck, string name, ChampionTemplate champion)
        {
            Decks.AiSettings ai = new Decks.AiSettings { ForAI = true };
            ulong id = Decks.CopyFromTemplate(deck, name, ai, champion != null ? champion.m_Id : default(ResourceId));
            m_Lists = null;
            m_Info.Clear();
            m_Category = CatAi;
            m_Message = Loc.Get("ai_copied") + " " + name;
            Debug.Log("[HexRevival] [AiDecks] copy of " + name + " -> shared deck " + id);
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private static string ModeName(string mode) { return Loc.Get("mode_" + mode); }

        private void OnGUI()
        {
            if (!m_Open || m_Cfg == null) return;
            GUI.depth = -1150;
            Controls.Changed = false;
            // The Modifiers window opened from here is on top: this one waits.
            GUI.enabled = !ModifiersWindow.IsOpen;
            float S = GameSkin.Scale;
            Rect win = Controls.Window(1600, 900, Loc.Get("ai_settings") + " : " + ModeName(m_Mode));
            float pad = 30 * S, y = win.y + 60 * S;

            // Account or default settings
            bool own = !AiConfig.UsesDefault(m_Mode);
            bool edit = Controls.Toggle(new Rect(win.x + pad, y, 360 * S, 34 * S), Loc.Get("ai_edit_defaults"), m_EditDefaults);
            if (edit != m_EditDefaults) { SaveIfDirty(); m_EditDefaults = edit; Load(m_Mode); }
            Controls.Changed = false;   // choosing what to edit is not a change
            string status = m_EditDefaults ? Loc.Get("ai_defaults_warn") : own ? Loc.Get("ai_own") : Loc.Get("ai_uses_default");
            GUI.Label(new Rect(win.x + pad + 380 * S, y, win.width - 2 * pad - 700 * S, 34 * S), status, GameSkin.Style("body", 14, m_EditDefaults ? GameSkin.Gold : GameSkin.Muted, TextAnchor.MiddleLeft, true));
            if (!m_EditDefaults && own && GameSkin.GoldButton(new Rect(win.xMax - pad - 300 * S, y, 300 * S, 36 * S), Loc.Get("ai_reset"), true, 15))
            {
                AiConfig.UseDefault(m_Mode);
                Load(m_Mode);
            }
            y += 46 * S;

            float bottom = win.yMax - 84 * S, gap = 16 * S, total = win.width - 2 * pad - 2 * gap;
            float x0 = win.x + pad, h = bottom - y;
            switch (m_Mode)
            {
                case AiConfig.Limited:
                    {
                        Rect players = new Rect(x0, y, total * 0.38f, h);
                        DrawPlayers(players);
                        DrawRunDecks(new Rect(players.xMax + gap, y, win.xMax - pad - players.xMax - gap, h));
                        break;
                    }
                case AiConfig.SiegeKeeps:
                    {
                        Rect keeps = new Rect(x0, y, total * 0.2f, h), editor = new Rect(keeps.xMax + gap, y, total * 0.42f, h);
                        DrawKeeps(keeps);
                        DrawKeepEditor(editor);
                        // A defense holds decks: no encounters.
                        DrawCatalogue(new Rect(editor.xMax + gap, y, win.xMax - pad - editor.xMax - gap, h), InKeep, PutInKeep, TakeFromKeep, CurrentKeep != null, false);
                        break;
                    }
                default:
                    {
                        Rect tiers = new Rect(x0, y, total * 0.18f, h);
                        Rect decks = new Rect(tiers.xMax + gap, y, total * 0.46f, h);
                        Rect cat = new Rect(decks.xMax + gap, y, win.xMax - pad - decks.xMax - gap, h);
                        DrawTiers(tiers);
                        DrawTierDecks(decks);
                        AiConfig.Tier tier = CurrentTier;
                        DrawCatalogue(cat, e => tier != null && tier.Decks.Exists(d => d.Kind == e.Kind && d.Key == e.Key),
                            e => tier.Decks.Add(new AiConfig.DeckRef(e.Kind, e.Key) { Weight = m_Mode == AiConfig.Arena ? 10 : 1 }),
                            e => tier.Decks.RemoveAll(d => d.Kind == e.Kind && d.Key == e.Key), tier != null, m_Mode != AiConfig.SiegeAttackers);
                        break;
                    }
            }

            float by = win.yMax - 70 * S;
            GUI.Label(new Rect(win.x + pad, by, win.width - 2 * pad - 220 * S, 52 * S), m_Message ?? Loc.Get("ai_saved_auto"), GameSkin.Style("body", 14, m_Message != null ? GameSkin.Gold : GameSkin.Muted, TextAnchor.MiddleLeft, true));
            if (GameSkin.RedButton(new Rect(win.xMax - pad - 190 * S, by, 190 * S, 52 * S), Loc.Get("close")) && GUI.enabled) Close();
            if (Controls.Changed) m_Dirty = true;
            GUI.enabled = true;
        }

        private string FromLabel()
        {
            switch (m_Mode)
            {
                case AiConfig.Ladder: return Loc.Get("opp_from_stars");
                case AiConfig.Arena: return Loc.Get("opp_from_tier");
                case AiConfig.SiegeAttackers: return Loc.Get("opp_from_bounty");
                default: return Loc.Get("opp_from_wins");
            }
        }

        private AiConfig.Tier CurrentTier { get { return m_Tier >= 0 && m_Tier < m_Cfg.Tiers.Count ? m_Cfg.Tiers[m_Tier] : null; } }

        private void DrawTiers(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("opp_tiers"));
            float rowH = 54 * S, x = r.x + 10 * S, w = r.width - 20 * S;
            float editH = 130 * S;
            Rect area = new Rect(x, r.y + 44 * S, w, r.height - 44 * S - editH - 50 * S);
            List<AiConfig.Tier> tiers = m_Cfg.Tiers;
            m_ScrollTiers = Controls.List(area, m_ScrollTiers, tiers.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), tiers[i].Name, tiers[i].From + " - " + tiers[i].To + " (" + tiers[i].Decks.Count + ")", i == m_Tier),
                i => { m_Tier = i; m_ScrollDecks = Vector2.zero; });
            float by = area.yMax + 8 * S;
            if (GameSkin.GoldButton(new Rect(x, by, w / 2f - 4 * S, 36 * S), Loc.Get("opp_add"), true, 15))
            {
                AiConfig.Tier last = CurrentTier;
                tiers.Add(new AiConfig.Tier { Name = Loc.Get("ai_new_tier"), From = last != null ? last.To + 1 : 0, To = last != null ? last.To + 10 : 99 });
                m_Tier = tiers.Count - 1;
                m_Dirty = true;
            }
            if (GameSkin.RedButton(new Rect(x + w / 2f + 4 * S, by, w / 2f - 4 * S, 36 * S), Loc.Get("ai_remove"), 15) && CurrentTier != null)
            {
                tiers.RemoveAt(m_Tier);
                m_Tier = Math.Min(m_Tier, tiers.Count - 1);
                m_Dirty = true;
            }
            AiConfig.Tier tier = CurrentTier;
            if (tier == null) return;
            float y = r.yMax - editH, lh = 34 * S, step = 40 * S;
            bool siege = m_Mode == AiConfig.SiegeAttackers;
            int unit = siege ? 500 : 1, max = siege ? 999999999 : 9999;
            tier.Name = Controls.TextField(new Rect(x, y, w, lh), tier.Name, 40); y += step;
            tier.From = Controls.Stepper(new Rect(x, y, w, lh), FromLabel(), tier.From, unit, 0, max); y += step;
            tier.To = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("opp_to"), tier.To, unit, tier.From, max);
        }

        private void DrawTierDecks(Rect r)
        {
            float S = GameSkin.Scale;
            AiConfig.Tier tier = CurrentTier;
            Controls.Frame(r, Loc.Get("ai_tier_decks") + (tier != null ? " (" + tier.Decks.Count + ")" : ""));
            if (tier == null) return;
            string[] roles = AiConfig.RolesOf(m_Mode);
            string warning = Warning(tier);
            float rowH = 58 * S, freqW = 90 * S, roleW = roles.Length > 0 ? 140 * S : 0, xW = 32 * S, gap = 6 * S;
            float controls = freqW + (roleW > 0 ? roleW + gap : 0) + xW + 2 * gap;
            // Column titles
            float hy = r.y + 40 * S, listW = r.width - 20 * S - 20 * S;
            GUIStyle head = GameSkin.Style("body", 12, GameSkin.Muted, TextAnchor.MiddleCenter);
            float cx = r.x + 10 * S + listW - controls + gap;
            GUI.Label(new Rect(cx, hy, freqW, 20 * S), Loc.Get("opp_weight"), head);
            if (roleW > 0) GUI.Label(new Rect(cx + freqW + gap, hy, roleW, 20 * S), Loc.Get("ai_role"), head);
            Rect area = new Rect(r.x + 10 * S, r.y + 62 * S, r.width - 20 * S, r.height - 72 * S - (warning != null ? 46 * S : 0));
            int remove = -1;
            List<AiConfig.DeckRef> refs = tier.Decks;
            m_ScrollDecks = Controls.List(area, m_ScrollDecks, refs.Count, rowH, i =>
            {
                AiConfig.DeckRef d = refs[i];
                Entry e = InfoOf(d);
                Rect row = new Rect(0, i * (rowH + 4 * S), area.width - 20 * S - controls, rowH);
                GameSkin.Row(row, e.Name, KindName(d.Kind) + (string.IsNullOrEmpty(e.Detail) ? "" : " - " + e.Detail), false, e.Champion);
                float x = row.xMax + gap, cy = row.y + (rowH - 32 * S) / 2f;
                // Frequency: "- n +"
                if (Controls.Small(new Rect(x, cy, 32 * S, 32 * S), "-") && d.Weight > 1) { d.Weight--; Controls.Changed = true; }
                GameSkin.Label(new Rect(x + 32 * S, cy, freqW - 64 * S, 32 * S), "x" + d.Weight, GameSkin.Style("bold", 15, GameSkin.Text, TextAnchor.MiddleCenter));
                if (Controls.Small(new Rect(x + freqW - 32 * S, cy, 32 * S, 32 * S), "+") && d.Weight < AiConfig.MaxWeight) { d.Weight++; Controls.Changed = true; }
                x += freqW + gap;
                if (roleW > 0)
                {
                    if (Array.IndexOf(roles, d.Role) < 0) d.Role = AiConfig.Common;
                    d.Role = Controls.Cycle(new Rect(x, cy, roleW, 32 * S), "", d.Role, roles, v => Loc.Get("role_" + v));
                    x += roleW + gap;
                }
                return Controls.Small(new Rect(x, cy, xW, 32 * S), "x");
            }, i => remove = i);
            if (remove >= 0) { refs.RemoveAt(remove); m_Dirty = true; }
            if (warning != null)
            {
                Rect wr = new Rect(r.x + 14 * S, r.yMax - 52 * S, r.width - 28 * S, 44 * S);
                GUI.DrawTexture(wr, GameSkin.Solid(new Color(0.35f, 0.15f, 0.05f, 0.6f)));
                GUI.Label(new Rect(wr.x + 10 * S, wr.y, wr.width - 20 * S, wr.height), "⚠ " + warning, GameSkin.Style("body", 13, new Color(1f, 0.8f, 0.45f), TextAnchor.MiddleLeft, true));
            }
        }

        /// <summary>What a tier lacks for its mode (arena: a boss, elites from tier 2; Siege: boss and elite fights).</summary>
        private string Warning(AiConfig.Tier t)
        {
            if (t.Decks.Count == 0) return null;
            bool boss = t.Decks.Exists(d => d.Role == AiConfig.Boss), elite = t.Decks.Exists(d => d.Role == AiConfig.Elite);
            if (m_Mode == AiConfig.Arena)
            {
                if (!boss) return Loc.Get("warn_arena_boss");
                if (!elite && t.To >= 2) return Loc.Get("warn_arena_elite");
            }
            return null;
        }

        private static string KindName(string kind)
        {
            return kind == AiConfig.Shared ? Loc.Get("ai_kind_shared") : kind == AiConfig.Encounter ? Loc.Get("ai_kind_enc") : Loc.Get("ai_kind_game");
        }

        /// <summary>
        /// The decks that can be added, by category: "+" adds one (isIn tells whether it is already there; then "-" takes
        /// it out), copy / edit / delete as in the game's deck lists. Encounters only where a mode can play them.
        /// </summary>
        private void DrawCatalogue(Rect r, Func<Entry, bool> isIn, Action<Entry> add, Action<Entry> remove, bool canAdd, bool encounters)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, null);
            string[] cats = { Loc.Get("ai_cat_player"), Loc.Get("ai_cat_ai"), Loc.Get("ai_cat_game"), Loc.Get("ai_cat_enc") };
            int nCats = encounters ? 4 : 3;
            if (m_Category >= nCats) m_Category = CatPlayer;
            float x = r.x + 10 * S, w = r.width - 20 * S, cw = w / nCats, y = r.y + 10 * S;
            for (int i = 0; i < nCats; i++)
                if (GameSkin.Tab(new Rect(x + i * cw, y, cw - 4 * S, 38 * S), cats[i], i == m_Category, 14) && i != m_Category)
                {
                    m_Category = i; m_ScrollCat = Vector2.zero; m_ConfirmDelete = 0;
                }
            y += 46 * S;
            bool changed = Controls.Changed;
            string search = Controls.TextField(new Rect(x, y, w, 34 * S), m_Search, 40);
            Controls.Changed = changed;   // searching does not change the configuration
            if (search != m_Search) { m_Search = search; m_ScrollCat = Vector2.zero; }
            y += 42 * S;

            List<Entry> all = List(m_Category), shown = m_Search.Length == 0 ? all : all.FindAll(e => e.Name.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0 || e.Detail.IndexOf(m_Search, StringComparison.OrdinalIgnoreCase) >= 0);
            float rowH = 58 * S, btnW = 100 * S;
            bool custom = m_Category == CatPlayer || m_Category == CatAi;
            float reserve = custom ? 2 * btnW + 52 * S : btnW + 46 * S;
            Rect area = new Rect(x, y, w, r.yMax - 10 * S - y);
            Entry toAdd = null, toRemove = null, copy = null, editDeck = null, delete = null;
            bool enabled = GUI.enabled;
            m_ScrollCat = Controls.List(area, m_ScrollCat, shown.Count, rowH, i =>
            {
                Entry e = shown[i];
                Rect row = new Rect(0, i * (rowH + 4 * S), area.width - 20 * S - reserve, rowH);
                bool inside = isIn(e);
                GameSkin.Row(row, e.Name, e.Detail, inside, e.Champion);
                float bx = row.xMax + 6 * S, by = row.y + (rowH - 34 * S) / 2f;
                // "+" adds the deck, "-" (once added) takes it out.
                GUI.enabled = enabled && canAdd;
                if (Controls.Small(new Rect(bx, by, 34 * S, 34 * S), inside ? "-" : "+")) { if (inside) toRemove = e; else toAdd = e; }
                GUI.enabled = enabled;
                bx += 40 * S;
                if (custom)
                {
                    if (GameSkin.GoldButton(new Rect(bx, by, btnW, 34 * S), Loc.Get("ai_edit"), enabled, 14)) editDeck = e;
                    bool confirm = m_ConfirmDelete == e.DeckId;
                    if (GameSkin.RedButton(new Rect(bx + btnW + 6 * S, by, btnW, 34 * S), confirm ? Loc.Get("ai_confirm") : Loc.Get("ai_delete"), 14) && enabled) delete = e;
                }
                else if (GameSkin.GoldButton(new Rect(bx, by, btnW, 34 * S), Loc.Get("opp_copy"), enabled, 14)) copy = e;
                return false;
            }, i => { });
            if (toAdd != null) { add(toAdd); m_Dirty = true; }
            if (toRemove != null) { remove(toRemove); m_Dirty = true; }
            if (copy != null) CopyToShared(copy.Template, copy.Name, copy.Champion);
            if (editDeck != null)
            {
                Close();
                OfflineMenu.EditDeck(new UID(UID.Type.Deck, editDeck.DeckId));
            }
            if (delete != null)
            {
                if (m_ConfirmDelete != delete.DeckId) m_ConfirmDelete = delete.DeckId;
                else
                {
                    Decks.Delete(delete.DeckId);
                    m_ConfirmDelete = 0;
                    m_Lists = null;
                    m_Info.Clear();
                    m_Message = Loc.Get("ai_deleted") + " " + delete.Name;
                }
            }
        }

        // ------------------------------------------------------------------
        // Sealed and draft: the AI players, and the decks they play in the runs in progress
        // ------------------------------------------------------------------

        private int m_PlayerSel;
        private Vector2 m_ScrollPlayers, m_ScrollRun;

        private void DrawPlayers(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("ai_players"));
            List<AiConfig.AiPlayer> players = m_Cfg.Players;
            float x = r.x + 10 * S, w = r.width - 20 * S, rowH = 52 * S, editH = 210 * S;
            Rect area = new Rect(x, r.y + 44 * S, w, r.height - 44 * S - editH - 50 * S);
            m_ScrollPlayers = Controls.List(area, m_ScrollPlayers, players.Count, rowH, i =>
            {
                Decks.AiSettings a = players[i].Settings ?? new Decks.AiSettings();
                return GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), players[i].Name,
                    Controls.SkillName(a.Skill) + " - x" + players[i].Weight + (a.AI.IsEmpty && a.Player.IsEmpty ? "" : " - " + Loc.Get("modifiers")), i == m_PlayerSel);
            }, i => m_PlayerSel = i);
            float by = area.yMax + 8 * S;
            if (GameSkin.GoldButton(new Rect(x, by, w / 2f - 4 * S, 36 * S), Loc.Get("opp_add"), GUI.enabled, 15))
            {
                players.Add(new AiConfig.AiPlayer { Name = PlaceholderKeeps.RandomName(new System.Random()), Settings = new Decks.AiSettings() });
                m_PlayerSel = players.Count - 1;
                m_Dirty = true;
            }
            if (GameSkin.RedButton(new Rect(x + w / 2f + 4 * S, by, w / 2f - 4 * S, 36 * S), Loc.Get("ai_remove"), 15) && GUI.enabled && m_PlayerSel >= 0 && m_PlayerSel < players.Count)
            {
                players.RemoveAt(m_PlayerSel);
                m_PlayerSel = Math.Min(m_PlayerSel, players.Count - 1);
                m_Dirty = true;
            }
            if (m_PlayerSel < 0 || m_PlayerSel >= players.Count) return;
            AiConfig.AiPlayer p = players[m_PlayerSel];
            if (p.Settings == null) p.Settings = new Decks.AiSettings();
            float y = r.yMax - editH, lh = 34 * S, step = 40 * S;
            p.Name = Controls.TextField(new Rect(x, y, w, lh), p.Name, 24); y += step;
            p.Settings.Skill = Controls.Skill(new Rect(x, y, w, lh), Loc.Get("ai_level"), p.Settings.Skill); y += step;
            p.Settings.Noise = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("ai_noise"), p.Settings.Noise, 10, 0, 200); y += step;
            p.Weight = Controls.Stepper(new Rect(x, y, w / 2f - 6 * S, lh), Loc.Get("opp_weight"), p.Weight, 1, 0, AiConfig.MaxWeight);
            if (GameSkin.GoldButton(new Rect(x + w / 2f + 6 * S, y, w / 2f - 6 * S, lh), Loc.Get("modifiers"), GUI.enabled, 15))
                ModifiersWindow.Open(p.Settings.Clone(), a => { p.Settings = a; m_Dirty = true; SaveIfDirty(); });
        }

        private void DrawRunDecks(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("ai_run_decks"));
            List<Battlegrounds.LimitedOpponent> opps = Battlegrounds.LimitedOpponents();
            float x = r.x + 10 * S, w = r.width - 20 * S;
            if (opps.Count == 0)
            {
                GUI.Label(new Rect(x + 6 * S, r.y + 50 * S, w - 12 * S, 120 * S), Loc.Get("ai_limited_note"), GameSkin.Style("body", 14, GameSkin.Muted, TextAnchor.UpperLeft, true));
                return;
            }
            float rowH = 58 * S, btnW = 100 * S;
            Rect area = new Rect(x, r.y + 44 * S, w, r.height - 54 * S);
            Battlegrounds.LimitedOpponent copy = null;
            TemplateManager tm = Singleton<TemplateManager>.Instance;
            m_ScrollRun = Controls.List(area, m_ScrollRun, opps.Count, rowH, i =>
            {
                Battlegrounds.LimitedOpponent o = opps[i];
                Rect row = new Rect(0, i * (rowH + 4 * S), area.width - 20 * S - btnW - 8 * S, rowH);
                string state = o.Drafting ? Loc.Get("ai_drafting") : o.Next ? Loc.Get("ai_next") : o.Faced ? Loc.Get("ai_faced") : "";
                GameSkin.Row(row, o.Player, o.Event + (state != "" ? " - " + state : ""), o.Next, tm.GetChampionTemplate(o.Deck.m_ChampionId));
                if (GameSkin.GoldButton(new Rect(row.xMax + 8 * S, row.y + (rowH - 34 * S) / 2f, btnW, 34 * S), Loc.Get("opp_copy"), GUI.enabled, 14)) copy = o;
                return false;
            }, i => { });
            if (copy != null)
            {
                ChampionTemplate ch = tm.GetChampionTemplate(copy.Deck.m_ChampionId);
                CopyToShared(copy.Deck, copy.Player + " (" + copy.Event + ")", ch);
            }
        }

        // ------------------------------------------------------------------
        // Siege: the defenses to attack
        // ------------------------------------------------------------------

        private int m_KeepSel, m_SlotSel;
        private Vector2 m_ScrollKeeps;
        private static readonly string[] Styles = Enum.GetNames(typeof(EDeckPersonality));

        private AiConfig.KeepDef CurrentKeep { get { return m_KeepSel >= 0 && m_KeepSel < m_Cfg.Keeps.Count ? m_Cfg.Keeps[m_KeepSel] : null; } }

        private void DrawKeeps(Rect r)
        {
            float S = GameSkin.Scale;
            Controls.Frame(r, Loc.Get("ai_keeps") + " (" + m_Cfg.Keeps.Count + ")");
            List<AiConfig.KeepDef> keeps = m_Cfg.Keeps;
            float x = r.x + 10 * S, w = r.width - 20 * S, rowH = 50 * S;
            Rect area = new Rect(x, r.y + 44 * S, w, r.height - 44 * S - 50 * S);
            m_ScrollKeeps = Controls.List(area, m_ScrollKeeps, keeps.Count, rowH, i =>
                GameSkin.Row(new Rect(0, i * (rowH + 4 * S), area.width - 20 * S, rowH), keeps[i].Name,
                    keeps[i].Bounty + " " + Loc.Get(keeps[i].Platinum ? "ai_platinum" : "ai_gold") + " - " + keeps[i].Fights.Count + "/3", i == m_KeepSel),
                i => { m_KeepSel = i; m_SlotSel = 0; });
            float by = area.yMax + 8 * S;
            if (GameSkin.GoldButton(new Rect(x, by, w / 2f - 4 * S, 36 * S), Loc.Get("opp_add"), GUI.enabled, 15))
            {
                keeps.Add(new AiConfig.KeepDef { Name = PlaceholderKeeps.RandomName(new System.Random()), Bounty = Siege.Fees.MinPotGold });
                m_KeepSel = keeps.Count - 1;
                m_SlotSel = 0;
                m_Dirty = true;
            }
            if (GameSkin.RedButton(new Rect(x + w / 2f + 4 * S, by, w / 2f - 4 * S, 36 * S), Loc.Get("ai_remove"), 15) && GUI.enabled && CurrentKeep != null)
            {
                keeps.RemoveAt(m_KeepSel);
                m_KeepSel = Math.Min(m_KeepSel, keeps.Count - 1);
                m_Dirty = true;
            }
        }

        /// <summary>The selected defense, like the game's defense setup: name, currency and bounty, 3 decks with their style, message.</summary>
        private void DrawKeepEditor(Rect r)
        {
            float S = GameSkin.Scale;
            AiConfig.KeepDef k = CurrentKeep;
            Controls.Frame(r, Loc.Get("ai_keep"));
            if (k == null) return;
            float x = r.x + 16 * S, w = r.width - 32 * S, y = r.y + 48 * S, lh = 34 * S, step = 42 * S;
            k.Name = Controls.TextField(new Rect(x, y, w, lh), k.Name, 24); y += step;
            string cur = Controls.Cycle(new Rect(x, y, w, lh), Loc.Get("ai_currency"), k.Platinum ? "platinum" : "gold", new[] { "gold", "platinum" }, v => Loc.Get("ai_" + v));
            bool plat = cur == "platinum";
            int min = plat ? Siege.Fees.MinPotPlatinum : Siege.Fees.MinPotGold;
            if (plat != k.Platinum) { k.Platinum = plat; k.Bounty = min; }
            y += step;
            k.Bounty = Controls.Stepper(new Rect(x, y, w, lh), Loc.Get("ai_bounty"), Math.Max(min, k.Bounty), plat ? 10 : 100, min, 10000000); y += step + 6 * S;
            while (k.Fights.Count < 3) k.Fights.Add(new AiConfig.KeepFight());
            float rowH = 58 * S;
            for (int i = 0; i < 3; i++)
            {
                AiConfig.KeepFight f = k.Fights[i];
                Rect row = new Rect(x, y, w - 250 * S, rowH);
                Entry e = f.Deck != null ? InfoOf(f.Deck) : null;
                if (GameSkin.Row(row, (i + 1) + ". " + (e != null ? e.Name : Loc.Get("ai_choose_deck")), e != null ? e.Detail : Loc.Get("ai_slot_hint"), i == m_SlotSel, e != null ? e.Champion : null)) m_SlotSel = i;
                float cy = row.y + (rowH - 32 * S) / 2f;
                f.Personality = Controls.Cycle(new Rect(row.xMax + 6 * S, cy, 204 * S, 32 * S), "", string.IsNullOrEmpty(f.Personality) ? "Default" : f.Personality, Styles, Controls.Words);
                if (Controls.Small(new Rect(row.xMax + 216 * S, cy, 32 * S, 32 * S), "x") && f.Deck != null) { f.Deck = null; m_Dirty = true; }
                y += rowH + 6 * S;
            }
            y += 6 * S;
            GUI.Label(new Rect(x, y, w, 26 * S), Loc.Get("ai_message"), GameSkin.Style("body", 14, GameSkin.Text));
            y += 28 * S;
            k.Message = Controls.TextField(new Rect(x, y, w, lh), k.Message, 140);
            y += step;
            int decks = k.Fights.FindAll(f => f.Deck != null).Count;
            if (decks < 3)
                GUI.Label(new Rect(x, y, w, 44 * S), "⚠ " + Loc.Get("ai_keep_incomplete"), GameSkin.Style("body", 13, new Color(1f, 0.8f, 0.45f), TextAnchor.UpperLeft, true));
        }

        /// <summary>A catalogue deck into the selected slot of the defense (then the next slot).</summary>
        private void PutInKeep(Entry e)
        {
            AiConfig.KeepDef k = CurrentKeep;
            if (k == null) return;
            while (k.Fights.Count < 3) k.Fights.Add(new AiConfig.KeepFight());
            k.Fights[m_SlotSel].Deck = new AiConfig.DeckRef(e.Kind, e.Key);
            m_SlotSel = (m_SlotSel + 1) % 3;
        }

        private bool InKeep(Entry e)
        {
            AiConfig.KeepDef k = CurrentKeep;
            return k != null && k.Fights.Exists(f => f.Deck != null && f.Deck.Kind == e.Kind && f.Deck.Key == e.Key);
        }

        private void TakeFromKeep(Entry e)
        {
            AiConfig.KeepDef k = CurrentKeep;
            if (k != null) foreach (AiConfig.KeepFight f in k.Fights) if (f.Deck != null && f.Deck.Kind == e.Kind && f.Deck.Key == e.Key) f.Deck = null;
        }

        /// <summary>Dev: "aidecks open [mode] | close | state".</summary>
        public static string Command(string[] parts)
        {
            AiDecksWindow w = s_Instance;
            if (w == null) return "no window";
            string sub = parts.Length > 1 ? parts[1] : "state";
            switch (sub)
            {
                case "open": Open(parts.Length > 2 ? parts[2] : AiConfig.Ladder); return "open";
                case "close": w.Close(); return "closed";
                case "cat": w.m_Category = int.Parse(parts[2]); return "ok";
                case "copy": { Entry e = w.List(w.m_Category)[int.Parse(parts[2])]; w.CopyToShared(e.Template, e.Name, e.Champion); return w.m_Message; }
                case "delete": Decks.Delete(ulong.Parse(parts[2])); w.m_Lists = null; return "deleted";
                case "pick":   // dev: "aidecks pick <mode> <value>": 10 draws of the account's configuration
                    {
                        string res = "";
                        for (int i = 0; i < 10; i++) { Opponents.Opponent o = Opponents.Pick(parts[2], int.Parse(parts[3])); res += (o != null ? o.Name + " [" + o.Skill + (o.Boss ? ",boss" : o.Elite ? ",elite" : "") + "]" : "none") + "; "; }
                        return res;
                    }
                case "screen": return "button mode: " + (AiButton.ModeOfScreen() ?? "none");
                case "count": return w.List(w.m_Category).Count + " in category " + w.m_Category;
                case "search": w.m_Search = parts.Length > 2 ? parts[2] : ""; return "ok";
            }
            return w.m_Open ? w.m_Mode + " defaults=" + w.m_EditDefaults + " tiers=" + w.m_Cfg.Tiers.Count + " players=" + w.m_Cfg.Players.Count + " keeps=" + w.m_Cfg.Keeps.Count + " dirty=" + w.m_Dirty : "closed";
        }
    }

    /// <summary>
    /// "Configure the AIs" at the bottom right of the screens of the modes played against the AI, above the game's
    /// bottom bar: opens AiDecksWindow on that mode (Battlegrounds: the mode of the selected event).
    /// </summary>
    public class AiButton : MonoBehaviour
    {
        private readonly Controls.UiBlocker m_Block = new Controls.UiBlocker();
        private Rect m_Rect, m_AttackRect;
        private string m_Mode;

        /// <summary>The mode of the screen shown (Battlegrounds: the selected event), null on other screens.</summary>
        internal static string ModeOfScreen()
        {
            try
            {
                Main main = Singleton<Main>.Instance;
                if (main == null || main.m_GameClient == null || main.m_GameClient.StateManager == null || Saves.Current == null) return null;
                if (main.m_GameClient.PlayerProfile == null || !main.m_GameClient.PlayerProfile.IsInitialized()) return null;
                switch (main.m_GameClient.StateManager.CurrentGameState)
                {
                    case EGameState.Tournament: return BattlegroundsMode();
                    case EGameState.ArenaCampaign: return AiConfig.Arena;
                    case EGameState.Siege:
                        // Attacking: the defenses to attack; defending: the attackers of the player's defense.
                        global::Siege.UISiege ui = global::Siege.UISiege.Instance;
                        if (ui == null) return null;
                        global::Siege.ESiegeState st = ui.CurrentState;
                        return st == global::Siege.ESiegeState.SelectKeep || st == global::Siege.ESiegeState.Attack ? AiConfig.SiegeKeeps
                            : st == global::Siege.ESiegeState.Defend ? AiConfig.SiegeAttackers : null;
                }
            }
            catch { }
            return null;
        }

        private static string BattlegroundsMode()
        {
            UITournamentPendingViewModel vm = FindObjectOfType<UITournamentPendingViewModel>();
            UITournamentDescContext sel = vm != null && vm.m_Context != null ? vm.m_Context.SelectedTournament : null;
            if (sel == null || sel.m_TournamentDesc == null) return null;
            if (sel.TournamentStyle == Game.Shared.Tournaments.ETournamentStyle.Ladder) return AiConfig.Ladder;
            return Battlegrounds.ModeOf(sel.m_TournamentDesc.TournamentID);
        }

        private void Update()
        {
            m_Mode = AiDecksWindow.IsOpen || OfflineMenu.IsOpen ? null : ModeOfScreen();
            if (m_Mode == null) { m_Block.Release(); m_Rect = new Rect(); return; }
            m_Block.Update(false, m_Rect, m_AttackRect);
        }

        private void OnGUI()
        {
            if (m_Mode == null) return;
            float S = GameSkin.Scale;
            GUI.depth = -900;
            m_Rect = new Rect(Screen.width - 250 * S, Screen.height - 112 * S, 236 * S, 44 * S);
            if (GameSkin.GoldButton(m_Rect, Loc.Get("ai_settings"), true, 16)) AiDecksWindow.Open(m_Mode);
            // The defense screen: an attacker of the configuration attacks the player's defense now (AI against AI).
            if (m_Mode == AiConfig.SiegeAttackers && Siege.CanAttackDefense)
            {
                m_AttackRect = new Rect(m_Rect.x, m_Rect.y - 52 * S, m_Rect.width, 44 * S);
                if (GameSkin.GreenButton(m_AttackRect, Loc.Get("siege_attack_now"), true, 16)) Siege.AttackDefense();
            }
            else m_AttackRect = new Rect();
        }
    }
}
