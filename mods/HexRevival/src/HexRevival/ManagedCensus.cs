using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Dev: the managed objects reachable from the static fields, counted by class with their size ("mem live"),
    /// written to HexRevivalMem and compared with the previous count. Uses the liveness walk Unity's own memory
    /// profiler uses (mono.dll's mono_unity_liveness_*, Unity's mono fork, unity/unity_liveness.c). It stops the other
    /// threads during the walk and marks each object by setting the low bit of its vtable pointer: the callback must
    /// not allocate managed memory, so it only adds to preallocated tables. Thread stacks and corlib's statics are not
    /// walked. Dev only: a crash here is possible if the layout differs (32-bit Windows player only).
    /// </summary>
    public static class ManagedCensus
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ObjectsFound(IntPtr objects, int count, IntPtr userData);

        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mono_unity_liveness_calculation_begin(IntPtr filter, uint maxCount, ObjectsFound callback, IntPtr userData);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void mono_unity_liveness_calculation_from_statics(IntPtr state);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void mono_unity_liveness_calculation_end(IntPtr state);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void mono_unity_liveness_calculation_from_root(IntPtr root, IntPtr state);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mono_gchandle_get_target(uint handle);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mono_class_get_name(IntPtr klass);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mono_class_get_namespace(IntPtr klass);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mono_class_instance_size(IntPtr klass);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int mono_class_array_element_size(IntPtr klass);
        [DllImport("mono.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mono_class_get_element_class(IntPtr klass);

        // One slot per vtable (one per class): objects, and the sums of the words at offsets 8 (a string's length) and
        // 12 (an array's length), read for every object since the class is not known during the walk.
        private const int Size = 1 << 16, Mask = Size - 1;
        private static readonly IntPtr[] s_Vtables = new IntPtr[Size];
        private static readonly long[] s_Count = new long[Size], s_At8 = new long[Size], s_At12 = new long[Size];
        private static int s_Overflow;
        private static readonly ObjectsFound s_Callback = Found;
        private static Dictionary<string, long> s_Previous;
        private static int s_Run;

        private static void Found(IntPtr objects, int count, IntPtr userData)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr o = Marshal.ReadIntPtr(objects, i * IntPtr.Size);
                if (o == IntPtr.Zero) continue;
                // The walk marks an object by setting its vtable pointer's low bit.
                IntPtr vt = new IntPtr(Marshal.ReadIntPtr(o).ToInt64() & ~1L);
                int h = (int)((((ulong)vt.ToInt64() >> 3) * 2654435761UL) >> 8) & Mask;
                for (int probe = 0; probe < Size; probe++, h = (h + 1) & Mask)
                {
                    if (s_Vtables[h] == vt) break;
                    if (s_Vtables[h] == IntPtr.Zero) { s_Vtables[h] = vt; break; }
                }
                if (s_Vtables[h] != vt) { s_Overflow++; continue; }
                s_Count[h]++;
                s_At8[h] += Marshal.ReadInt32(o, 8);
                s_At12[h] += Marshal.ReadInt32(o, 12);
            }
        }

        /// <param name="unityRoots">Also walk from every loaded Unity object (scene objects, assets): what they reference
        /// is held through Unity's own handles, not through static fields.</param>
        public static string Run(string label, int top, bool unityRoots)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer || IntPtr.Size != 4) return "32-bit Windows player only";
            Array.Clear(s_Vtables, 0, Size); Array.Clear(s_Count, 0, Size); Array.Clear(s_At8, 0, Size); Array.Clear(s_At12, 0, Size);
            s_Overflow = 0;
            GC.Collect();
            Found(IntPtr.Zero, 0, IntPtr.Zero);   // compiled now: nothing may be allocated once the other threads are stopped
            float t0 = Time.realtimeSinceStartup;
            List<GCHandle> handles = new List<GCHandle>();
            IntPtr[] roots = new IntPtr[0];
            if (unityRoots)
            {
                UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(typeof(UnityEngine.Object));
                roots = new IntPtr[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    GCHandle gh = GCHandle.Alloc(all[i]);
                    handles.Add(gh);
                    roots[i] = mono_gchandle_get_target((uint)GCHandle.ToIntPtr(gh).ToInt32());
                }
            }
            IntPtr state = mono_unity_liveness_calculation_begin(IntPtr.Zero, 1000000, s_Callback, IntPtr.Zero);
            mono_unity_liveness_calculation_from_statics(state);
            for (int i = 0; i < roots.Length; i++)
                if (roots[i] != IntPtr.Zero) mono_unity_liveness_calculation_from_root(roots[i], state);
            mono_unity_liveness_calculation_end(state);
            foreach (GCHandle gh in handles) gh.Free();
            float secs = Time.realtimeSinceStartup - t0;

            Dictionary<string, long> bytes = new Dictionary<string, long>(), counts = new Dictionary<string, long>();
            long total = 0, objects = 0;
            for (int h = 0; h < Size; h++)
            {
                if (s_Vtables[h] == IntPtr.Zero) continue;
                IntPtr klass = Marshal.ReadIntPtr(s_Vtables[h]);
                string name = Marshal.PtrToStringAnsi(mono_class_get_name(klass)) ?? "?";
                string ns = Marshal.PtrToStringAnsi(mono_class_get_namespace(klass)) ?? "";
                string full = ns.Length > 0 ? ns + "." + name : name;
                long n = s_Count[h], size;
                if (full == "System.String") size = n * 14 + s_At8[h] * 2;
                else if (name.EndsWith("]")) size = n * 16 + s_At12[h] * Math.Max(1, mono_class_array_element_size(klass));
                else size = n * Math.Max(8, mono_class_instance_size(klass));
                long b; bytes.TryGetValue(full, out b); bytes[full] = b + size;
                long c; counts.TryGetValue(full, out c); counts[full] = c + n;
                total += size; objects += n;
            }
            List<KeyValuePair<string, long>> sorted = new List<KeyValuePair<string, long>>(bytes);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            StringBuilder file = new StringBuilder();
            file.Append("# ").Append(label).Append(' ').Append(DateTime.Now.ToString("HH:mm:ss")).Append(": ").Append(objects).Append(" objects, ")
                .Append(total / 1048576).Append(unityRoots ? " MB reachable from statics and Unity objects, " : " MB reachable from statics, ").Append(secs.ToString("0.0")).Append(" s").Append(s_Overflow > 0 ? ", table overflow " + s_Overflow : "").Append('\n');
            foreach (KeyValuePair<string, long> kv in sorted) file.Append(kv.Value).Append('\t').Append(counts[kv.Key]).Append('\t').Append(kv.Key).Append('\n');
            string dir = Path.Combine(LocalServer.Root, "HexRevivalMem");
            Directory.CreateDirectory(dir);
            s_Run++;
            File.WriteAllText(Path.Combine(dir, "live-" + DateTime.Now.ToString("HHmmss") + "-" + label + ".txt"), file.ToString());

            StringBuilder res = new StringBuilder();
            res.Append(objects).Append(" objects, ").Append(total / 1048576).Append(unityRoots ? " MB reachable from statics and Unity objects (" : " MB reachable from statics (").Append(secs.ToString("0.0")).Append(" s)");
            if (s_Previous == null)
                for (int i = 0; i < sorted.Count && i < top; i++) res.Append('\n').Append(sorted[i].Value / 1024).Append(" KB\t").Append(counts[sorted[i].Key]).Append('\t').Append(sorted[i].Key);
            else
            {
                List<KeyValuePair<string, long>> diff = new List<KeyValuePair<string, long>>();
                foreach (KeyValuePair<string, long> kv in bytes) { long p; s_Previous.TryGetValue(kv.Key, out p); if (kv.Value != p) diff.Add(new KeyValuePair<string, long>(kv.Key, kv.Value - p)); }
                foreach (KeyValuePair<string, long> kv in s_Previous) if (!bytes.ContainsKey(kv.Key)) diff.Add(new KeyValuePair<string, long>(kv.Key, -kv.Value));
                diff.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
                for (int i = 0; i < diff.Count && i < top; i++) res.Append('\n').Append(diff[i].Value > 0 ? "+" : "").Append(diff[i].Value / 1024).Append(" KB\t").Append(diff[i].Key);
            }
            s_Previous = bytes;
            return res.ToString();
        }
    }
}
