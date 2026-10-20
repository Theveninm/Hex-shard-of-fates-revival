using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Client;
using Game.Shared;
using Game.Shared.Network;
using Game.Shared.Profile;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The profile's deck templates: decks that belong to a sub-champion (the AZ2 boat) instead of being
    /// player decks. The original server kept them in the profile ("pdecktsave" request of
    /// Game.Shared.Profile.Network); here they are saved in decktemplates.txt (id|base64 of the game's own encoding).
    /// </summary>
    public static class DeckTemplates
    {
        private static Dictionary<ulong, ProfileDeckTemplate> s_Templates;
        private static string s_LoadedFrom;

        private static string FilePath { get { return Saves.File("decktemplates.txt"); } }

        private static Dictionary<ulong, ProfileDeckTemplate> All
        {
            get
            {
                // Reloaded when the account (save folder) changes.
                if (s_Templates != null && s_LoadedFrom == FilePath) return s_Templates;
                s_LoadedFrom = FilePath;
                s_Templates = new Dictionary<ulong, ProfileDeckTemplate>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            int bar = line.IndexOf('|');
                            ulong id;
                            ProfileDeckTemplate t;
                            string error;
                            if (bar > 0 && ulong.TryParse(line.Substring(0, bar), out id) && ProfileDeckTemplate.FromBase64(line.Substring(bar + 1), out t, out error))
                                s_Templates[id] = t;
                        }
                }
                catch (Exception e) { Debug.LogError("[HexRevival] [DeckTemplates] read: " + e); }
                return s_Templates;
            }
        }

        public static ProfileDeckTemplate Get(ulong id)
        {
            ProfileDeckTemplate t;
            return id != 0 && All.TryGetValue(id, out t) ? t : null;
        }

        /// <summary>Gives the client the saved templates (at login, as the profile stream did).</summary>
        public static void LoadInto(PlayerProfile profile)
        {
            profile.DeckTemplateList.Clear();
            foreach (KeyValuePair<ulong, ProfileDeckTemplate> kv in All) profile.DeckTemplateList[kv.Key] = kv.Value;
        }

        /// <summary>Saves a template (new when id is 0) and returns its id. The client's list is updated too.</summary>
        public static ulong Put(ProfileDeckTemplate t, ulong id)
        {
            if (id == 0) { id = 1; foreach (ulong k in All.Keys) if (k >= id) id = k + 1; }
            All[id] = t;
            Save();
            try { GameClient.Instance.PlayerProfile.DeckTemplateList[id] = t; } catch { }
            return id;
        }

        /// <summary>
        /// Game.Shared.Profile.Network.DeckTemplateSave (Harmony prefix): the deck editor saving a sub-champion's
        /// deck. Answers like the original server: a SavedProfileDeckTemplate in the response envelope.
        /// </summary>
        public static void OnSaveRequest(ProfileDeckTemplate template, ulong? deckTemplateId, Action<CustomNetworkResponse<Game.Shared.Profile.Network.Response>> notify)
        {
            // The editor sends "ERROR" when the deck has no name of its own: the saved deck keeps its name.
            ProfileDeckTemplate old = Get(deckTemplateId ?? 0);
            if ((string.IsNullOrEmpty(template.Name) || template.Name == "ERROR") && old != null) template.Name = old.Name;
            ulong id = Put(template, deckTemplateId ?? 0);
            Debug.Log("[HexRevival] [DeckTemplates] saved " + id + " '" + template.Name + "'");
            if (notify == null) return;
            SavedProfileDeckTemplate saved = new SavedProfileDeckTemplate { Id = id, Name = template.Name, Comp = false, Data = template.ToBytes() };
            CustomNetworkResponse<Game.Shared.Profile.Network.Response> rsp = new CustomNetworkResponse<Game.Shared.Profile.Network.Response>();
            rsp.Message = new Game.Shared.Profile.Network.Response { Envelope = EncData.Encode(saved) };
            Campaign.Enqueue(() => notify(rsp));
        }

        private static void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<ulong, ProfileDeckTemplate> kv in All) sb.Append(kv.Key).Append('|').Append(kv.Value.ToBase64()).Append('\n');
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [DeckTemplates] save: " + e); }
        }
    }
}
