using System;
using System.Collections;
using System.Collections.Generic;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// Store pictures the game's icon bundle (icons.ab, Src.Game) lacks for products the mod sells; the store asks the
    /// bundle for them by the product's texture name:
    /// - "&lt;texture&gt;_Thumb", the list's small picture: missing for the Starter Trials packs, the Standard Format
    ///   Gauntlet Pack, the gift boxes, the Random Previous Kismet Pack. They get the product's full icon (the same
    ///   picture, 128 pixels instead of 256).
    /// - for a battleboard, "&lt;texture&gt;_Wallpaper", the banner behind its row, and "&lt;texture&gt;", the 3D board shown
    ///   on the right: missing for Frostlock and Dream Forest, which only have their picture. The banner is the middle of
    ///   the picture (the banners are 4:1, the pictures 2:1); the board is another board's model (Badlands) with this
    ///   board's picture.
    /// - "&lt;texture&gt;_Wallpaper" of the other products without one (most primal packs, Kismet's Draft Pack, the Starter
    ///   Trials packs, the Gauntlet pack, the Random Previous Kismet Pack), and Dead of Winter Primal Pack's, which is a
    ///   "PLACEHOLDER" picture: a related product's banner (see Related); the Random Previous Kismet Pack's missing model
    ///   too.
    /// The gift boxes (treasure chests in the game data, shown as the campaign packs) lose the campaign packs' "Bonus
    /// chance!", and the outline the store draws around its 3D item is put on their chest.
    /// </summary>
    public static class StoreIcons
    {
        private const string Thumb = "_Thumb", Wallpaper = "_Wallpaper";
        /// <summary>The board whose model the boards without one borrow: Badlands (Src.Game, icons.ab).</summary>
        private const string ModelBoard = "a0008050";

        /// <summary>
        /// Banners the game ships as a "PLACEHOLDER" picture (Src.Game, icons.ab): Dead of Winter Primal Pack's. Replaced
        /// like a missing one.
        /// </summary>
        private static readonly HashSet<string> PlaceholderBanners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a0008437_Wallpaper" };

        /// <summary>Whether a picture the bundle has must be replaced all the same.</summary>
        public static bool IsPlaceholder(AssetBundleFile bundle, string name)
        {
            return bundle == AssetBundleFile.Icons && name != null && PlaceholderBanners.Contains(name);
        }

        private static int s_Depth;

        /// <summary>AssetBundleManager.GetAsset (postfix, when it found nothing or a placeholder): a picture or model the bundle lacks.</summary>
        public static UnityEngine.Object Missing(AssetBundleManager m, AssetBundleFile bundle, string subBundle, string name, AssetType type)
        {
            if (bundle != AssetBundleFile.Icons || string.IsNullOrEmpty(name) || s_Depth > 0) return null;
            s_Depth++;
            try
            {
                if (type == AssetType.Texture2D && name.EndsWith(Thumb))
                    return m.GetAsset(bundle, subBundle, name.Substring(0, name.Length - Thumb.Length), type);
                if (type == AssetType.Texture2D && name.EndsWith(Wallpaper))
                {
                    string tex = name.Substring(0, name.Length - Wallpaper.Length);
                    if (IsBoard(tex)) return Banner(m.GetAsset(bundle, subBundle, tex, type) as Texture2D, tex);
                    Texture2D own = PackBanner(m, bundle, subBundle, tex);
                    if (own != null) return own;
                    string from = Related(tex);
                    return from != null ? m.GetAsset(bundle, subBundle, from + Wallpaper, type) : null;
                }
                if (type == AssetType.Prefab && IsBoard(name)) return BoardModel(m, bundle, subBundle, name);
                if (type == AssetType.Prefab)
                {
                    string from = Related(name);
                    return from != null ? m.GetAsset(bundle, subBundle, from, type) : null;
                }
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Store] icon " + name + ": " + e.Message); }
            finally { s_Depth--; }
            return null;
        }

        // ---- Banners made from a pack's own art ----

        /// <summary>
        /// A pack's art on its wrapper (Src.Game, icons.ab: the texture of the pack's 3D model) and the band of it that
        /// shows its figure, as fractions of the texture from its top left: x0, x1, y0 (the band is 2:1, as tall as half
        /// its width). Four are card illustrations (matched with the card database: Bertram Cragraven, Prophet of
        /// Lodegan, Zoltog, Strength in Numbers); the others were painted for the pack. Which band: a mod choice.
        /// </summary>
        private static readonly Dictionary<string, KeyValuePair<string, Vector3>> PackArt = new Dictionary<string, KeyValuePair<string, Vector3>>(StringComparer.OrdinalIgnoreCase)
        {
            { "a0005474", Art("BoosterPack_Set01_Dwarf", 0f, 0.85f, 0.05f) },      // Dwarf Starter Trials Pack
            { "a0005476", Art("BoosterPack_Set01_Human", 0f, 0.85f, 0f) },         // Human
            { "a0005478", Art("BoosterPack_Set01_Orc", 0.05f, 0.85f, 0.02f) },     // Orc
            { "a0005482", Art("BoosterPack_Set01_Shinhare", 0f, 0.85f, 0.02f) },   // Shin'hare
            { "a0005473", Art("BoosterPack_Set03_Coyotle", 0.24f, 0.74f, 0.36f) }, // Coyotle
            { "a0005475", Art("BoosterPack_Set03_Elf", 0.24f, 0.74f, 0.36f) },     // Elf
            { "a0005477", Art("BoosterPack_Set03_Necrotic", 0.24f, 0.74f, 0.36f) },// Necrotic
            { "a0005483", Art("BoosterPack_Set03_Vennen", 0.24f, 0.74f, 0.36f) },  // Vennen
            { "a0005479", Art("BoosterPack_Set01_Primal", 0.08f, 0.86f, 0.02f) },  // Set 001 Primal Pack
            { "a0005480", Art("BoosterPack_Set02_Primal", 0.1f, 0.75f, 0.24f) },   // Set 002
            { "a0005481", Art("BoosterPack_Set03_Primal", 0.15f, 0.85f, 0.22f) },  // Set 003
            { "a0006242", Art("BoosterPack_Set04_Primal", 0.14f, 0.86f, 0.18f) },  // Set 004 (Primal Dawn)
            { "a0006829", Art("BoosterPack_Set05_Primal", 0.14f, 0.86f, 0.18f) },  // Herofall
            { "a0007598", Art("BoosterPack_Set06_Primal", 0.14f, 0.86f, 0.18f) },  // Scars of War
            { "a0008022", Art("BoosterPack_Set07_Primal", 0.14f, 0.86f, 0.18f) },  // Frostheart
            { "a0008437", Art("BoosterPack_Set08_Primal", 0.14f, 0.86f, 0.22f) },  // Dead of Winter
            { "a0008803", Art("BoosterPack_Set09_Primal", 0.14f, 0.86f, 0.22f) },  // Doombringer
            { "a0008362", Art("BoosterPack_KismetDraft", 0f, 0.8f, 0.02f) },       // Kismet's Draft Pack
        };

        private static KeyValuePair<string, Vector3> Art(string wrapper, float x0, float x1, float y0) { return new KeyValuePair<string, Vector3>(wrapper, new Vector3(x0, x1, y0)); }

        private static readonly Dictionary<string, Texture2D> s_PackBanners = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A banner like the game's (1024x256, the figure on the right fading into black on the left) from the pack's
        /// art; null when the pack is not in PackArt or its art is not loaded.
        /// </summary>
        private static Texture2D PackBanner(AssetBundleManager m, AssetBundleFile bundle, string subBundle, string tex)
        {
            KeyValuePair<string, Vector3> art;
            if (!PackArt.TryGetValue(tex, out art)) return null;
            Texture2D b;
            if (s_PackBanners.TryGetValue(tex, out b) && b != null) return b;
            Texture2D src = m.GetAsset(bundle, subBundle, art.Key, AssetType.Texture2D) as Texture2D;
            if (src == null) return null;
            float x0 = art.Value.x, x1 = art.Value.y, y0 = art.Value.z, y1 = y0 + (x1 - x0) / 2f;
            const int W = 1024, H = 256, ArtW = 512, Right = 30;
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, W, H, 0);
                // The source rect in uv (origin at the bottom left).
                Graphics.DrawTexture(new Rect(W - Right - ArtW, 0, ArtW, H), src, new Rect(x0, 1f - y1, x1 - x0, y1 - y0), 0, 0, 0, 0);
                GL.PopMatrix();
                b = new Texture2D(W, H, TextureFormat.RGB24, false);
                b.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                // Its left half fades into black.
                Color32[] px = b.GetPixels32();
                int fadeFrom = W - Right - ArtW, fadeW = ArtW / 2;
                for (int y = 0; y < H; y++)
                    for (int x = fadeFrom; x < fadeFrom + fadeW; x++)
                    {
                        float f = (x - fadeFrom) / (float)fadeW;
                        int i = y * W + x;
                        px[i] = new Color32((byte)(px[i].r * f), (byte)(px[i].g * f), (byte)(px[i].b * f), 255);
                    }
                b.SetPixels32(px);
                b.Apply();
                b.name = tex + Wallpaper;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
            s_PackBanners[tex] = b;
            return b;
        }

        // ---- 3D models with the wrong texture ----

        /// <summary>
        /// Packs whose 3D model wears another texture (Src.Game, icons.ab): the Vennen Starter Trials Pack's model uses
        /// the Vennen starter deck's box (Starter_Vennen); its own wrapper is BoosterPack_Set03_Vennen, as the seven other
        /// races' packs use theirs.
        /// </summary>
        private static readonly Dictionary<string, string> ModelTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "a0005483", "BoosterPack_Set03_Vennen" },
        };

        private static void FixModelTexture(GameObject go, string tex)
        {
            string right;
            if (go == null || tex == null || !ModelTextures.TryGetValue(tex, out right) || AssetBundleManager.Instance == null) return;
            Texture2D t = AssetBundleManager.Instance.GetAsset(AssetBundleFile.Icons, right, AssetType.Texture2D) as Texture2D;
            if (t == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material mat in r.materials)
                {
                    mat.mainTexture = t;
                    if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", t);
                }
        }

        // ---- Products without a banner or model of their own ----

        private static Dictionary<string, string> s_Related;

        /// <summary>
        /// The product whose banner (and model) a product without one shows (PLACEHOLDER, a mod choice: the original
        /// store's rows for these are not known): a pack, its set's booster's; Kismet's Draft Pack and the Random Previous
        /// Kismet Pack, the Kismet's Reserve Pack's. By texture name; null if none.
        /// </summary>
        private static string Related(string texture)
        {
            if (s_Related == null)
            {
                s_Related = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Dictionary<ResourceId, string> boosters = new Dictionary<ResourceId, string>();
                string kismet = null;
                List<InventoryItemData> all = new List<InventoryItemData>(Singleton<TemplateManager>.Instance.InventoryItems.Values);
                foreach (InventoryItemData i in all)
                {
                    InventoryCardPack p = i as InventoryCardPack;
                    if (p != null && p.m_CardPackType == ECardPackType.BoosterPack && p.m_SetId.IsValid && !string.IsNullOrEmpty(i.m_TexturePath)
                        && i.m_Name != null && i.m_Name.EndsWith("Booster Pack") && !boosters.ContainsKey(p.m_SetId))
                        boosters[p.m_SetId] = Tex(i);
                    if (i is InventoryTreasureChest && i.m_Name != null && i.m_Name.Trim() == "Kismet's Reserve Pack") kismet = Tex(i);
                }
                foreach (InventoryItemData i in all)
                {
                    InventoryCardPack p = i as InventoryCardPack;
                    if (p == null || string.IsNullOrEmpty(i.m_TexturePath)) continue;
                    string tex = Tex(i), from = null;
                    if (p.m_CardPackType == ECardPackType.KismetPack || (i.m_Name ?? "").Trim() == "Random Previous Kismet Pack") from = kismet;
                    else boosters.TryGetValue(p.m_SetId, out from);
                    if (from != null && !string.Equals(from, tex, StringComparison.OrdinalIgnoreCase) && !s_Related.ContainsKey(tex)) s_Related[tex] = from;
                }
            }
            string r;
            return s_Related.TryGetValue(texture, out r) ? r : null;
        }

        private static string Tex(InventoryItemData i) { return FolderUtils.GetFileNameWithoutPathOrExtension(i.m_TexturePath); }

        // ---- Battleboards ----

        private static HashSet<string> s_Boards;

        /// <summary>Whether a texture name is a battleboard's picture.</summary>
        private static bool IsBoard(string texture)
        {
            if (s_Boards == null)
            {
                s_Boards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (InventoryItemData i in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                    if (i is InventoryGameboardData && !string.IsNullOrEmpty(i.m_TexturePath))
                        s_Boards.Add(FolderUtils.GetFileNameWithoutPathOrExtension(i.m_TexturePath));
            }
            return s_Boards.Contains(texture);
        }

        private static readonly Dictionary<string, Texture2D> s_Banners = new Dictionary<string, Texture2D>();

        private static Texture2D Banner(Texture2D picture, string board)
        {
            Texture2D b;
            if (s_Banners.TryGetValue(board, out b) && b != null) return b;
            if (picture == null) return null;
            b = Community.Crop(picture, new Rect(0f, 0.25f, 1f, 0.5f));
            b.name = board + Wallpaper;
            s_Banners[board] = b;
            return b;
        }

        private static GameObject s_Holder;
        private static readonly Dictionary<string, GameObject> s_Models = new Dictionary<string, GameObject>();

        /// <summary>
        /// The Badlands board's model with this board's picture, kept under an inactive object (the store instantiates
        /// it: its copy is active). Made again if the bundle was unloaded since (its mesh or picture gone).
        /// </summary>
        private static GameObject BoardModel(AssetBundleManager m, AssetBundleFile bundle, string subBundle, string board)
        {
            GameObject model;
            if (s_Models.TryGetValue(board, out model) && Usable(model)) return model;
            if (model != null) UnityEngine.Object.Destroy(model);
            Texture2D picture = m.GetAsset(bundle, subBundle, board, AssetType.Texture2D) as Texture2D;
            GameObject source = m.GetAsset(bundle, subBundle, ModelBoard, AssetType.Prefab) as GameObject;
            if (picture == null || source == null) return null;
            if (s_Holder == null)
            {
                s_Holder = new GameObject("HexRevival_BoardModels");
                s_Holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(s_Holder);
            }
            model = UnityEngine.Object.Instantiate(source, s_Holder.transform);
            model.name = board;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null) { mats[i] = new Material(mats[i]); mats[i].mainTexture = picture; }
                r.sharedMaterials = mats;
            }
            s_Models[board] = model;
            return model;
        }

        private static bool Usable(GameObject model)
        {
            if (model == null) return false;
            foreach (MeshFilter f in model.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh == null) return false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                foreach (Material mat in r.sharedMaterials) if (mat == null || mat.mainTexture == null) return false;
            return true;
        }

        // ---- Gift boxes ----

        /// <summary>UIPremiumStoreItemContext(InventoryTreasureChest, StoreItem) (postfix): a gift box is no campaign pack.</summary>
        public static void OnChestItem(UIPremiumStoreItemContext ctx, InventoryTreasureChest chest)
        {
            if (ctx == null || SpecialPacks.KindOf(chest) != "giftbox") return;
            ctx.IsCampaignPack = false;
            ctx.Quantity = 1;
        }

        /// <summary>
        /// UIPremiumStoreItemContext(InventoryCardPack, StoreItem) (postfix): the Random Previous Kismet Pack is a booster
        /// in the game data, but holds a Kismet's Reserve pack: no "Primal chance!" and no collection count of a set.
        /// </summary>
        public static void OnPackItem(UIPremiumStoreItemContext ctx, InventoryCardPack pack)
        {
            if (ctx == null || SpecialPacks.KindOf(pack) != "randomkismet") return;
            ctx.IsBoosterPack = false;
            ctx.HasDetails = false;
            ctx.StarterPackContentTooltip = string.Empty;
        }

        /// <summary>StoreItem3DController.OnAssetBundleObjectLoaded (postfix): the outline put on a gift box's chest.</summary>
        public static void On3DItemLoaded(StoreItem3DController c)
        {
            if (c != null) c.StartCoroutine(PlaceOutline(c));
        }

        /// <summary>
        /// The store outlines its 3D item with the model's first mesh, drawn unrotated at the item's origin (Outline and
        /// Glow, next to Item under RotPivot; Src.Game: StoreItem3DController). That fits the packs, whose mesh is their
        /// model's root. The chest's model is turned (270° about x) and its body mesh is a scaled, moved child of it: the
        /// outline showed the body from another side, away from the chest. For a gift box the outline and the glow take
        /// the body's place, rotation and scale; for the other items they are put back as the store has them.
        /// </summary>
        private static IEnumerator PlaceOutline(StoreItem3DController c)
        {
            // After the controller's own SetHighlightMeshes (end of this frame).
            yield return new WaitForEndOfFrame();
            yield return null;
            if (c == null) yield break;
            HarmonyLib.Traverse t = HarmonyLib.Traverse.Create(c);
            MeshFilter[] outlines = { t.Field("m_OutlineMesh").GetValue<MeshFilter>(), t.Field("m_GlowMesh").GetValue<MeshFilter>() };
            AssetBundlePrefab holder = t.Field("m_AssetBundleGameObject").GetValue<AssetBundlePrefab>();
            GameObject go = holder != null ? holder.LoadedPrefab : null;
            UIPremiumStoreViewModel vm = UnityEngine.Object.FindObjectOfType<UIPremiumStoreViewModel>();
            UIPremiumStoreItemContext sel = vm != null && vm.Context != null ? vm.Context.Catalog.SelectedItem : null;
            if (sel != null && sel.ItemData != null && !string.IsNullOrEmpty(sel.ItemData.m_TexturePath)) FixModelTexture(go, Tex(sel.ItemData));
            // The mesh the controller took (the first one under the model).
            MeshFilter mf = holder != null ? holder.GetComponentInChildren<MeshFilter>() : null;
            bool chest = sel != null && go != null && mf != null && mf.transform != go.transform && mf.transform.IsChildOf(go.transform)
                && SpecialPacks.KindOf(sel.ItemData) == "giftbox";
            // The body's place in the item's space (the model sits at the origin of Item, which is where Outline is).
            Vector3 pos = Vector3.zero, scale = Vector3.one;
            Quaternion rot = Quaternion.identity;
            if (chest)
                for (Transform tr = mf.transform; tr != null && tr != holder.transform; tr = tr.parent)
                {
                    pos = tr.localRotation * Vector3.Scale(tr.localScale, pos) + tr.localPosition;
                    rot = tr.localRotation * rot;
                    scale = Vector3.Scale(tr.localScale, scale);
                }
            foreach (MeshFilter o in outlines)
            {
                if (o == null) continue;
                o.transform.localPosition = pos;
                o.transform.localRotation = rot;
                o.transform.localScale = scale;
            }
        }

        /// <summary>Dev "store3d": the store's 3D item (objects, layers, positions) and the cameras.</summary>
        public static string Dump()
        {
            StoreItem3DController c = UnityEngine.Object.FindObjectOfType<StoreItem3DController>();
            if (c == null) return "no 3D item";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Transform root = c.transform;
            for (Transform p = c.transform; p != null; p = p.parent) sb.Append(p.name).Append(p.parent != null ? " < " : "\n");
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                Renderer r = t.GetComponent<Renderer>();
                MeshFilter mf = t.GetComponent<MeshFilter>();
                sb.Append(t.name).Append(" act=").Append(t.gameObject.activeInHierarchy).Append(" layer=").Append(t.gameObject.layer)
                  .Append(" pos=").Append(t.position.ToString("F3")).Append(" lpos=").Append(t.localPosition.ToString("F3"))
                  .Append(" lrot=").Append(t.localEulerAngles.ToString("F1")).Append(" lscale=").Append(t.localScale.ToString("F3"));
                if (mf != null) sb.Append(" mesh=").Append(mf.sharedMesh != null ? mf.sharedMesh.name + mf.sharedMesh.bounds.ToString() : "null");
                if (r != null) sb.Append(" bounds=").Append(r.bounds.center.ToString("F3")).Append(r.bounds.size.ToString("F3")).Append(" mat=").Append(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null");
                sb.Append('\n');
            }
            foreach (Camera cam in Camera.allCameras)
                sb.Append("CAM ").Append(cam.name).Append(" mask=").Append(cam.cullingMask).Append(" pos=").Append(cam.transform.position.ToString("F3"))
                  .Append(" ortho=").Append(cam.orthographic).Append(" fov=").Append(cam.fieldOfView).Append(" depth=").Append(cam.depth).Append(" rt=").Append(cam.targetTexture != null).Append('\n');
            return sb.ToString();
        }
    }
}
