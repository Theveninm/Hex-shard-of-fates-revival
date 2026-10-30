using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The store list shows each product's icon as "&lt;texture&gt;_Thumb" (the list's binding adds the suffix), and the
    /// game's icon bundle has no thumbnail for some products the mod sells: the Starter Trials packs, the Standard
    /// Format Gauntlet Pack, the gift boxes, the Random Previous Kismet Pack (Src.Game: icons.ab). For those the list
    /// gets the product's full icon instead (the same picture, 128 pixels instead of 256).
    /// </summary>
    public static class StoreIcons
    {
        private const string Suffix = "_Thumb";

        /// <summary>AssetBundleManager.GetAsset (postfix, when it found nothing): an icon's missing thumbnail.</summary>
        public static Object MissingThumb(AssetBundleManager m, AssetBundleFile bundle, string subBundle, string name, AssetType type)
        {
            if (bundle != AssetBundleFile.Icons || type != AssetType.Texture2D || name == null || !name.EndsWith(Suffix)) return null;
            return m.GetAsset(bundle, subBundle, name.Substring(0, name.Length - Suffix.Length), type);
        }
    }
}
