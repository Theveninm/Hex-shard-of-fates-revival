using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
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

        // --- Game data at start-up --------------------------------------------------------------------------------------
        // The game data (HexData, gzip, about 100 MB of JSON once decompressed) was loaded in a way that peaked at several
        // copies of it at once: a MemoryStream doubling its buffer while decompressing, then one 200 MB string, then that
        // string split into its sections (a second copy of all of it) and each section into its templates (a third copy).
        // Mono's collector never gives its heap back: that peak is why the game's managed heap was about 1 GB from the
        // start, in a 32-bit game (2026-11-20, user's PC: 1008-1175 MB at the main menu, 417-449 MB with these fixes; the
        // largest free block of address space went from about 1.5 GB to 2 GB).
        // Now: the bytes are decompressed into one array of the right size (Decompress), kept as bytes (DecompressBall),
        // and handed to TemplateManager.LoadAbilitiesFromBall in small pieces of the same form
        // ("Type$$--$$template$$--$$template...", LoadBall), which it parses the same way: every case of its switch only
        // adds the templates it parses (FastParseTemplates), so one section in several pieces gives the same result
        // (compared with "mem templates": the same counts in every collection). A HexRevival.wholeball file next to Hex.exe
        // turns the last two steps off (to compare).

        [DllImport("mono.dll")]
        private static extern long mono_gc_get_heap_size();

        [ThreadStatic] private static bool t_InBall;
        private static Type s_GZip;
        private static MethodInfo s_LoadBall;
        private const string SectionMark = "$$$---$$$", TemplateMark = "$$--$$";
        private const int PieceChars = 1 << 20;   // about 2 MB per piece

        private static string Heap()
        {
            long h = 0;
            try { h = mono_gc_get_heap_size(); } catch { }
            return "managed " + (GC.GetTotalMemory(false) >> 20) + " MB in a heap of " + (h >> 20) + " MB";
        }

        /// <summary>
        /// Before Compressor.Decompress (the game data, gzip): the game copies the decompressed bytes into a MemoryStream,
        /// which doubles its buffer as it grows (4 KB ... 128 MB for the 100 MB of data), then copies it all once more
        /// (ToArray): about 450 MB allocated for 100 MB of result, so Mono's heap grew for good. The data is now read
        /// straight into one array of the size the gzip trailer gives. False when done here.
        /// </summary>
        public static bool Decompress(byte[] b, ref byte[] result)
        {
            try
            {
                if (b == null || b.Length < 1 << 20 || !Game.Shared.Utils.Compressor.IsCompressed(b)) return true;
                // ISIZE: the uncompressed size modulo 2^32, in the last 4 bytes (RFC 1952).
                long size = (uint)(b[b.Length - 4] | b[b.Length - 3] << 8 | b[b.Length - 2] << 16 | b[b.Length - 1] << 24);
                if (size < b.Length || size > 768L << 20) return true;
                GC.Collect();   // what was allocated before is freed before the large block is asked for
                byte[] outBytes = new byte[size];
                int got = 0;
                // The game's own gzip reader (SharpZipLib, a game assembly the mod is not built against).
                if (s_GZip == null) s_GZip = Type.GetType("ICSharpCode.SharpZipLib.GZip.GZipInputStream, ICSharpCode.SharpZipLib");
                if (s_GZip == null) return true;
                using (MemoryStream ms = new MemoryStream(b))
                using (Stream gz = (Stream)Activator.CreateInstance(s_GZip, ms))
                {
                    int n;
                    while (got < outBytes.Length && (n = gz.Read(outBytes, got, outBytes.Length - got)) > 0) got += n;
                    if (got != outBytes.Length || gz.ReadByte() != -1) return true;   // not a single gzip member of that size: the game's way
                }
                result = outBytes;
                Debug.Log("[HexRevival] [Memory] game data decompressed in place (" + (size >> 20) + " MB), " + Heap());
                return false;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Memory] decompress: " + e.Message); return true; }
        }

        // TemplateManager.Decompress (gzip bytes → one string) is only used for the two data files, whose strings go to
        // LoadClientBall or LoadAbilitiesFromBall. For a large one, it now returns a marker and keeps the bytes: the
        // ability ball is parsed from the bytes in pieces (never one 200 MB string), the client ball gets its string.
        private static readonly string BallMarker = new string(new[] { '\u0001', 'H', 'R', 'b', 'a', 'l', 'l' });
        [ThreadStatic] private static byte[] t_BallBytes;
        private const int LargeBall = 8 << 20;

        /// <summary>Before TemplateManager.Decompress(byte[]): false with the marker (large data) or the string.</summary>
        public static bool DecompressBall(byte[] raw, ref string result)
        {
            if (File.Exists(Path.Combine(LocalServer.Root, "HexRevival.wholeball"))) return true;
            byte[] bytes = Game.Shared.Utils.Compressor.Decompress(raw);
            if (bytes != null && bytes.Length >= LargeBall) { t_BallBytes = bytes; result = BallMarker; }
            else result = bytes != null ? Encoding.UTF8.GetString(bytes) : null;
            return false;
        }

        /// <summary>Before TemplateManager.LoadClientBall: the marker becomes the string.</summary>
        public static void ClientBall(ref string data)
        {
            if (!ReferenceEquals(data, BallMarker)) return;
            data = Encoding.UTF8.GetString(t_BallBytes);
            t_BallBytes = null;
        }

        private static readonly byte[] SectionBytes = Encoding.ASCII.GetBytes(SectionMark), TemplateBytes = Encoding.ASCII.GetBytes(TemplateMark);

        private static int IndexOf(byte[] d, byte[] mark, int from, int to)
        {
            for (int i = from; i <= to - mark.Length; i++)
            {
                if (d[i] != mark[0]) continue;
                int k = 1;
                while (k < mark.Length && d[i + k] == mark[k]) k++;
                if (k == mark.Length) return i;
            }
            return -1;
        }

        /// <summary>Before TemplateManager.LoadAbilitiesFromBall: false when the data was handed over in pieces.</summary>
        public static bool LoadBall(object manager, ref string data, ref bool result)
        {
            if (t_InBall || !ReferenceEquals(data, BallMarker)) return true;
            byte[] d = t_BallBytes;
            t_BallBytes = null;
            if (s_LoadBall == null) s_LoadBall = manager.GetType().GetMethod("LoadAbilitiesFromBall", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();   // maybe not the main thread: no Time
            Debug.Log("[HexRevival] [Memory] game data (" + (d.Length >> 20) + " MB) handed over in pieces, before: " + Heap());
            int pieces = 0;
            t_InBall = true;
            try
            {
                // The markers are ASCII: a piece's bytes always end between two whole UTF-8 characters.
                StringBuilder piece = new StringBuilder(PieceChars + 4096);
                int pos = 0;
                while (pos < d.Length)
                {
                    int end = IndexOf(d, SectionBytes, pos, d.Length);
                    if (end < 0) end = d.Length;
                    int head = IndexOf(d, TemplateBytes, pos, end);
                    if (head < 0)
                    {
                        // A section without templates: as it is (the game logs an unknown type or skips it).
                        if (end > pos) { s_LoadBall.Invoke(manager, new object[] { Encoding.UTF8.GetString(d, pos, end - pos) }); pieces++; }
                    }
                    else
                    {
                        string header = Encoding.UTF8.GetString(d, pos, head - pos);
                        int t = head + TemplateBytes.Length;
                        piece.Length = 0;
                        while (t <= end)
                        {
                            int next = IndexOf(d, TemplateBytes, t, end);
                            int stop = next < 0 ? end : next;
                            if (stop > t)   // empty templates are dropped, as by the game's Split (RemoveEmptyEntries)
                            {
                                if (piece.Length == 0) piece.Append(header);
                                piece.Append(TemplateMark).Append(Encoding.UTF8.GetString(d, t, stop - t));
                                if (piece.Length >= PieceChars) { s_LoadBall.Invoke(manager, new object[] { piece.ToString() }); pieces++; piece.Length = 0; }
                            }
                            if (next < 0) break;
                            t = next + TemplateBytes.Length;
                        }
                        if (piece.Length > 0) { s_LoadBall.Invoke(manager, new object[] { piece.ToString() }); pieces++; piece.Length = 0; }
                    }
                    pos = end + SectionBytes.Length;
                }
                result = true;
            }
            finally { t_InBall = false; }
            Debug.Log("[HexRevival] [Memory] game data parsed in " + pieces + " pieces (" + (sw.ElapsedMilliseconds / 1000f).ToString("0.0") + " s), after: " + Heap());
            return false;
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
