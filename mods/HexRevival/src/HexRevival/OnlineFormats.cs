using System;
using System.Collections.Generic;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The formats of online games (a game in the lobby is played in one of them; the server knows the same list):
    /// the game's own constructed formats, checked with the game's deck validator (DeckValidator, every card owned
    /// online), and "free" (any deck).
    /// - immortal: every set (the game's Immortal);
    /// - standard: the recent sets (the game's Standard);
    /// - pve: PvE cards and campaign champions allowed (the game's PvE deck format);
    /// - free: no check.
    /// </summary>
    public static class OnlineFormats
    {
        public static readonly string[] All = { "immortal", "standard", "pve", "free" };
        public const string Default = "immortal";

        public static string Name(string format) { return string.IsNullOrEmpty(format) ? Loc.Get("format_all") : Loc.Get("format_" + format); }

        /// <summary>Validity per deck and format; cleared when the decks may have changed (the lobby opening).</summary>
        private static readonly Dictionary<string, bool> s_Cache = new Dictionary<string, bool>();

        public static void ClearCache() { s_Cache.Clear(); }

        /// <summary>Can this deck be played in this format ("" or null: any format)?</summary>
        public static bool Accepts(string format, UID deck)
        {
            if (string.IsNullOrEmpty(format) || format == "free") return deck.IsValid();
            string key = deck.GetInstanceId() + "|" + format;
            bool ok;
            if (s_Cache.TryGetValue(key, out ok)) return ok;
            ok = Validate(format, deck, null);
            s_Cache[key] = ok;
            return ok;
        }

        /// <summary>Runs the game's validator; errors (if not null) gets its messages.</summary>
        public static bool Validate(string format, UID deck, List<string> errors)
        {
            if (format == "free") return deck.IsValid();
            try
            {
                champion_bits champion;
                deck_bits bits = Decks.BitsForValidation(deck, out champion);
                if (bits == null) return false;
                EDeckFormat f = format == "standard" ? EDeckFormat.Standard : format == "pve" ? EDeckFormat.PvE : EDeckFormat.Immortal;
                // Online, every card is owned.
                DeckValidator v = new DeckValidator(bits, DeckValidator.DeckFormatToNormalFormat(f), champion, (id, n) => true);
                if (errors != null) foreach (string k in v.Errors.Keys) errors.Add(k);
                return v.Valid;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HexRevival] [Online] format " + format + ", deck " + deck + ": " + e.Message);
                if (errors != null) errors.Add(e.Message);
                return false;
            }
        }
    }
}
