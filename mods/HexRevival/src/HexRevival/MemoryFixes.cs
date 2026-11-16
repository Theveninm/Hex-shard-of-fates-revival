using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Fixes for the game's own memory leaks, found with MemoryAudit (HEX is a 32-bit game: what leaks in a long
    /// session ends in an "out of memory" crash).
    /// </summary>
    public static class MemoryFixes
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // --- Card sounds ---------------------------------------------------------------------------------------------
        // In battle, each card's sound comes from its asset bundle (the one holding its art): SoundGroupManager
        // .OnSoundEffectLoaded takes the clip with AssetBundleManager.GetAsset, which counts a reference, and never
        // gives it back; at the end of the battle CleanUpFileGroup destroys the sound groups but does not release the
        // clips. A bundle with a referenced asset is never unloaded, so every card seen in a battle kept its bundle
        // (picture, sound) loaded until the game was closed. The clips taken during the battle are released when the
        // battle's sound groups are destroyed; the game then unloads the unused bundles at the next level load.

        private static readonly List<AssetRequestItem> s_Sounds = new List<AssetRequestItem>();

        /// <summary>After SoundGroupManager.OnSoundEffectLoaded: the clip it took, if it took one.</summary>
        public static void SoundLoaded(AssetRequestItem ari)
        {
            if (ari != null && RefCount(ari) > 0) s_Sounds.Add(ari);
        }

        /// <summary>After SoundGroupManager.CleanUpFileGroup (end of a battle): releases the clips taken in it.</summary>
        public static void ReleaseSounds()
        {
            AssetBundleManager m = AssetBundleManager.Instance;
            if (m == null) { s_Sounds.Clear(); return; }
            int released = 0;
            foreach (AssetRequestItem ari in s_Sounds)
            {
                if (RefCount(ari) <= 0) continue;   // never below zero, whatever happened to it meanwhile
                try { m.ReleaseAsset(ari); released++; }
                catch (Exception e) { Debug.LogWarning("[HexRevival] [Memory] release " + ari.Name + ": " + e.Message); }
            }
            if (s_Sounds.Count > 0) Debug.Log("[HexRevival] [Memory] battle sounds released: " + released + " of " + s_Sounds.Count);
            s_Sounds.Clear();
        }

        private static FieldInfo s_Bundles, s_AssetList;
        private static MethodInfo s_BundlePath;

        /// <summary>References the bundle manager counts on this asset (0 when it is not loaded).</summary>
        private static int RefCount(AssetRequestItem ari)
        {
            try
            {
                AssetBundleManager m = AssetBundleManager.Instance;
                if (m == null) return 0;
                if (s_Bundles == null)
                {
                    s_Bundles = typeof(AssetBundleManager).GetField("assetBundles", Any);
                    s_AssetList = typeof(AssetBundleContainer).GetField("assetList", Any);
                    s_BundlePath = typeof(AssetBundleManager).GetMethod("GetBundlePath", Any, null, new[] { typeof(AssetRequestItem) }, null);
                }
                Dictionary<string, AssetBundleContainer> bundles = s_Bundles.GetValue(m) as Dictionary<string, AssetBundleContainer>;
                string path = s_BundlePath.Invoke(m, new object[] { ari }) as string;
                AssetBundleContainer c;
                if (bundles == null || path == null || !bundles.TryGetValue(path, out c) || c == null) return 0;
                List<AssetContainer> list = s_AssetList.GetValue(c) as List<AssetContainer>;
                if (list == null) return 0;
                foreach (AssetContainer a in list)
                    if (a.Type == ari.Type && a.AssetName == ari.Name) return a.ReferenceCount;
            }
            catch { }
            return 0;
        }

        // --- Map paths -----------------------------------------------------------------------------------------------
        // The paths between the nodes of a campaign or dungeon map are Vectrosity lines, whose object the game hides and
        // keeps across levels (HideAndDontSave); UIDungeonMapPath.OnDestroy is empty, so every visit of a map added a
        // copy of all its paths (object, mesh) that nothing ever destroyed. They are destroyed with their map path.

        private static FieldInfo s_PathLine;
        private static MethodInfo s_DestroyLine;

        /// <summary>After UIDungeonMapPath.OnDestroy: destroys its line.</summary>
        public static void PathDestroyed(object path)
        {
            try
            {
                if (s_PathLine == null)
                {
                    s_PathLine = path.GetType().GetField("m_Path", Any);
                    Type line = s_PathLine.FieldType;
                    s_DestroyLine = line.GetMethod("Destroy", BindingFlags.Static | BindingFlags.Public, null, new[] { line.MakeByRefType() }, null);
                }
                object l = s_PathLine.GetValue(path);
                if (l == null) return;
                object[] args = { l };
                s_DestroyLine.Invoke(null, args);
                s_PathLine.SetValue(path, null);
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Memory] map path: " + e.Message); }
        }
    }
}
