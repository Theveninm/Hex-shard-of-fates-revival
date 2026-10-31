using System;
using Game.Client;
using Game.Client.Network.Profile;
using Game.Shared;
using Game.Shared.Domain;
using Game.Shared.Mechanics;
using Game.Shared.Network.Profile;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Extended art (ProfileService.UpgradeExtendedArtOnCard, replaced here): the zoomed card's "Unlock extended art"
    /// spends one stardust of the card's rarity and shows that copy with its extended art from then on. Before, the
    /// request went to the dead server and the button kept waiting.
    ///
    /// Src.Game: the stardust of a card (UIExtendedArtViewModel.ShowExtendedArtUI, PlayerProfile.HasStardustToUpgradeCard):
    /// its rarity's, Promo stardust for epic and promo cards and for the cards with an alternate art; the errors
    /// NotOwnedCard, InsufficientStardust, AlreadyUpgraded. The copies with their extended art are saved by Collection.
    /// </summary>
    public static class ExtendedArt
    {
        /// <summary>The stardust that upgrades (or, see Crafting, makes) a card of this template.</summary>
        public static ResourceId StardustFor(CardTemplate t)
        {
            if (t == null) return ResourceId.Invalid;
            if (t.HasAlternateArt) return BuiltInResources.PromoStardust;
            switch (t.m_CardRarity)
            {
                case ERarity.Common: return BuiltInResources.CommonStardust;
                case ERarity.Uncommon: return BuiltInResources.UncommonStardust;
                case ERarity.Rare: return BuiltInResources.RareStardust;
                case ERarity.Legendary: return BuiltInResources.LegendaryStardust;
                case ERarity.Epic:
                case ERarity.Promo: return BuiltInResources.PromoStardust;
                default: return ResourceId.Invalid;
            }
        }

        /// <summary>
        /// Takes n of an item (stardust, crafting materials) from the save and the client's inventory; returns how many.
        /// With keepLast, the client keeps the last one taken: its instance id goes to lastId, for an answer whose
        /// handler removes it (UpgradeExtendedArtOnCardResponse.StardustInventoryId).
        /// </summary>
        public static int Spend(ResourceId item, int n, bool keepLast, out ulong lastId)
        {
            lastId = 0;
            int taken = Inventory.Consume(item, n), left = taken;
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            foreach (PlayerInventoryItem i in profile.InventoryItems.ToArray())
            {
                if (left <= 0) break;
                if (i.TemplateId != item) continue;
                left--;
                if (keepLast && left == 0) { lastId = i.ItemUid.GetInstanceId(); break; }
                profile.RemoveInventoryItem(i.ItemUid);
            }
            return taken;
        }

        /// <summary>ProfileService.UpgradeExtendedArtOnCard.Request.</summary>
        public static void Upgrade(ulong cardInstanceId, ProfileService.UpgradeExtendedArtOnCard.ResponseHandler handler)
        {
            UpgradeExtendedArtOnCardResponse r = LocalServer.Ok<UpgradeExtendedArtOnCardResponse>();
            r.CardInstanceId = cardInstanceId;
            try
            {
                card_instance_bits c = Collection.Instance(cardInstanceId);
                ResourceId dust = c != null ? StardustFor(c.Template) : ResourceId.Invalid;
                if (c == null) r.Error = EUpgradeExtendedArtOnCardError.NotOwnedCard;
                else if (c.IsExtended) r.Error = EUpgradeExtendedArtOnCardError.AlreadyUpgraded;
                else if (!dust.IsValid || Inventory.Count(dust) <= 0) r.Error = EUpgradeExtendedArtOnCardError.InsufficientStardust;
                else
                {
                    ulong spent;
                    Spend(dust, 1, true, out spent);
                    r.StardustInventoryId = spent;
                    Collection.MarkExtended(cardInstanceId);
                    Debug.Log("[HexRevival] [ExtendedArt] " + c.Template.m_Name + " (copy " + cardInstanceId + ")");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[HexRevival] [ExtendedArt] " + e);
                r.Error = EUpgradeExtendedArtOnCardError.InternalServerError;
            }
            if (r.Error != EUpgradeExtendedArtOnCardError.Ok) Debug.Log("[HexRevival] [ExtendedArt] copy " + cardInstanceId + " refused: " + r.Error);
            LocalServer.Answer(typeof(ProfileService.UpgradeExtendedArtOnCard), r, handler);
        }
    }
}
