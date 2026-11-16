using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HexRevival
{
    /// <summary>
    /// Memory monitoring. HEX is a 32-bit game: after a few hours players' games crashed with "out of memory"
    /// while Windows still had free memory, because the address space was full or too fragmented to hold one
    /// more large block. This writes one line to the log after each level load (the game frees unused assets
    /// then), so a player's log shows the trend: memory used, Unity objects, and the largest free block of
    /// address space (what a large allocation needs). The dev command "mem census" counts the loaded objects
    /// by type and compares with the previous count, to find what accumulates.
    /// </summary>
    public class MemoryAudit : MonoBehaviour
    {
        private float m_LogAt = -1f;
        private string m_Scene;
        private static int s_Census;
        private static Dictionary<string, int> s_Previous;
        private static readonly string s_Run = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        private void OnEnable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; }
        private void OnDisable() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            m_Scene = scene.name;
            m_LogAt = Time.realtimeSinceStartup + 3f;   // after the game's own clean-up of the previous level
        }

        private void Update()
        {
            if (m_LogAt < 0f || Time.realtimeSinceStartup < m_LogAt) return;
            m_LogAt = -1f;
            try
            {
                Debug.Log("[HexRevival] [Memory] " + m_Scene + ": " + Line());
                // The crashes reported came when a large block was asked for and none was left: the player is told
                // to restart before that happens (once per session).
                Space sp;
                if (!s_Warned && AddressSpace(out sp) && sp.Largest < LowBlock)
                {
                    s_Warned = true;
                    Debug.Log("[HexRevival] [Memory] largest free block " + Mb(sp.Largest) + ": the player is told to restart");
                    Notices.Show(Loc.Get("memory_low"));
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Memory] " + e.Message); }
        }

        /// <summary>
        /// Below this largest free block of address space, a restart is advised. PLACEHOLDER: the crashes reported
        /// failed to get 32 MB and 4.6 MB blocks; 128 MB leaves room for a level load (the game asks for blocks of
        /// up to 32 MB then).
        /// </summary>
        private const long LowBlock = 128L * 1024 * 1024;
        private static bool s_Warned;

        /// <summary>
        /// One line: managed objects alive and the Mono heap holding them, Unity's allocations, objects loaded, and
        /// the address space: free (and its largest block), then what uses the rest (private memory committed and
        /// only reserved, mapped files, program and DLL images).
        /// </summary>
        public static string Line()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("managed ").Append(Mb(GC.GetTotalMemory(false)));
            long heap = MonoHeap();
            if (heap > 0) sb.Append(" in a mono heap of ").Append(Mb(heap));
            sb.Append(", unity ").Append(Mb(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemory()))
              .Append(" of ").Append(Mb(UnityEngine.Profiling.Profiler.GetTotalReservedMemory()));
            sb.Append(", objects ").Append(Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object)).Length);
            Space sp;
            if (AddressSpace(out sp))
                sb.Append(", address space free ").Append(Mb(sp.Free)).Append(" (largest block ").Append(Mb(sp.Largest))
                  .Append("), private ").Append(Mb(sp.Committed)).Append(" + reserved ").Append(Mb(sp.Reserved))
                  .Append(", mapped ").Append(Mb(sp.Mapped)).Append(", images ").Append(Mb(sp.Images));
            return sb.ToString();
        }

        [DllImport("mono.dll")]
        private static extern long mono_gc_get_heap_size();

        /// <summary>Size of the Mono heap (Unity's Boehm collector never gives it back), 0 when unknown.</summary>
        private static long MonoHeap()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer) return 0;
            try { return mono_gc_get_heap_size(); }
            catch { return 0; }
        }

        private static string Mb(long bytes) { return (bytes / (1024 * 1024)) + " MB"; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryBasicInformation info, UIntPtr length);

        private const uint MemCommit = 0x1000, MemReserve = 0x2000, MemFree = 0x10000;
        private const uint MemMapped = 0x40000, MemImage = 0x1000000;

        public struct Space { public long Free, Largest, Committed, Reserved, Mapped, Images; }

        /// <summary>The process's address space by kind (Windows and Wine only).</summary>
        public static bool AddressSpace(out Space sp)
        {
            sp = new Space();
            if (Application.platform != RuntimePlatform.WindowsPlayer) return false;
            try
            {
                MemoryBasicInformation info;
                UIntPtr size = (UIntPtr)Marshal.SizeOf(typeof(MemoryBasicInformation));
                ulong address = 0x10000;
                ulong end = IntPtr.Size == 4 ? 0x100000000UL : 0x7FFFFFFFFFFFUL;
                while (address < end)
                {
                    // A 32-bit pointer above 2 GB (large address aware game) does not fit a signed int: unchecked casts.
                    IntPtr at = IntPtr.Size == 4 ? new IntPtr(unchecked((int)(uint)address)) : new IntPtr((long)address);
                    if (VirtualQuery(at, out info, size) == UIntPtr.Zero) break;
                    long region = (long)(ulong)info.RegionSize;
                    if (region == 0) break;
                    if (info.State == MemFree) { sp.Free += region; if (region > sp.Largest) sp.Largest = region; }
                    else if (info.Type == MemImage) sp.Images += region;
                    else if (info.Type == MemMapped) sp.Mapped += region;
                    else if (info.State == MemCommit) sp.Committed += region;
                    else if (info.State == MemReserve) sp.Reserved += region;
                    ulong start = IntPtr.Size == 4 ? (uint)info.BaseAddress.ToInt32() : (ulong)info.BaseAddress.ToInt64();
                    address = start + (ulong)region;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Dev: counts the loaded objects by type (and the most frequent names of the main types), writes them to
        /// HexRevivalMem/census-N.txt and returns what changed since the previous count.
        /// </summary>
        public static string Census(string label)
        {
            GC.Collect();
            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));
            Dictionary<string, int> byType = new Dictionary<string, int>();
            Dictionary<string, int> byName = new Dictionary<string, int>();
            foreach (UnityEngine.Object o in all)
            {
                if (o == null) continue;
                string t = o.GetType().Name;
                int n;
                byType.TryGetValue(t, out n); byType[t] = n + 1;
                string key = t + " " + Short(o.name);
                byName.TryGetValue(key, out n); byName[key] = n + 1;
            }
            Dictionary<string, int> counts = new Dictionary<string, int>(byType);
            foreach (KeyValuePair<string, int> kv in byName) counts["  " + kv.Key] = kv.Value;

            StringBuilder file = new StringBuilder();
            file.Append("# ").Append(label).Append(' ').Append(DateTime.Now.ToString("HH:mm:ss")).Append(" - ").Append(Line()).Append('\n');
            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(counts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (KeyValuePair<string, int> kv in sorted) file.Append(kv.Value).Append('\t').Append(kv.Key).Append('\n');
            string dir = Path.Combine(Path.Combine(LocalServer.Root, "HexRevivalMem"), s_Run);
            Directory.CreateDirectory(dir);
            s_Census++;
            File.WriteAllText(Path.Combine(dir, "census-" + s_Census.ToString("000") + ".txt"), file.ToString());

            StringBuilder diff = new StringBuilder("census " + s_Census + " (" + label + "): " + all.Length + " objects");
            if (s_Previous != null)
            {
                List<KeyValuePair<string, int>> changes = new List<KeyValuePair<string, int>>();
                foreach (KeyValuePair<string, int> kv in counts)
                {
                    int before;
                    s_Previous.TryGetValue(kv.Key, out before);
                    if (kv.Value != before) changes.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - before));
                }
                foreach (KeyValuePair<string, int> kv in s_Previous)
                    if (!counts.ContainsKey(kv.Key)) changes.Add(new KeyValuePair<string, int>(kv.Key, -kv.Value));
                changes.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
                for (int i = 0; i < changes.Count && i < 40; i++)
                    diff.Append('\n').Append(changes[i].Value > 0 ? "+" : "").Append(changes[i].Value).Append('\t').Append(changes[i].Key);
            }
            s_Previous = counts;
            return diff.ToString();
        }

        /// <summary>
        /// Dev: the asset bundles the game keeps loaded and, for each, its assets with their reference counts. The game
        /// unloads a bundle after a level load only when no asset of it is still referenced (counted by GetAsset and
        /// ReleaseAsset): a bundle left here game after game is held by a GetAsset never released.
        /// </summary>
        public static string Bundles()
        {
            AssetBundleManager m = AssetBundleManager.Instance;
            if (m == null) return "no AssetBundleManager";
            System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            Dictionary<string, AssetBundleContainer> bundles = typeof(AssetBundleManager).GetField("assetBundles", bf).GetValue(m) as Dictionary<string, AssetBundleContainer>;
            if (bundles == null) return "no bundle list";
            System.Reflection.FieldInfo listField = typeof(AssetBundleContainer).GetField("assetList", bf);
            System.Reflection.FieldInfo persistField = typeof(AssetBundleContainer).GetField("persist", bf);
            StringBuilder sb = new StringBuilder(bundles.Count + " bundles");
            List<string> keys = new List<string>(bundles.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys)
            {
                AssetBundleContainer c = bundles[k];
                sb.Append('\n').Append(k).Append((bool)persistField.GetValue(c) ? " (persistent)" : "").Append(':');
                List<AssetContainer> list = listField.GetValue(c) as List<AssetContainer>;
                if (list == null) continue;
                foreach (AssetContainer a in list)
                    sb.Append(' ').Append(a.AssetName).Append(" x").Append(a.ReferenceCount).Append(a.Asset == null ? " (gone)" : "");
            }
            return sb.ToString();
        }

        private static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "(no name)";
            name = name.Replace('\n', ' ');
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }
}
