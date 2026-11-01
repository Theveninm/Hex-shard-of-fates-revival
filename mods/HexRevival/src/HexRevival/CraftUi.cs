using System;
using System.Collections.Generic;
using DarkTonic.MasterAudio;
using Game.Client;
using Game.Shared;
using Game.Shared.Mechanics;
using Game.Shared.Resources;
using Reckoning.Game;
using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// The "Break down" and "Craft" buttons of the zoomed card (the collection's right-click on a card, also on cards not
    /// owned with "show cards not owned"): copies of the game's "Unlock extended art" button next to it, shown with the
    /// extended art panel (UIExtendedArtViewModel.ShowExtendedArtUI / HideExtendedArtUI, postfixed). Each asks first
    /// (UISystem.DisplayConfirm), then plays the extended art's stardust animation, sound and card effect
    /// (UICardStackPagingViewModel.m_ExtendedArtFX, as DelayedCardUpgradedToExtendedArt does, without marking the card).
    /// See Crafting for the rules.
    /// </summary>
    public static class CraftUi
    {
        private static UIExtendedArtViewModel s_Vm;
        private static ICard s_Card;
        private static GameObject s_Break, s_Make, s_Info;
        /// <summary>The equipment shown (its details popup), instead of a card.</summary>
        private static InventoryEquipmentData s_Equip;

        public static void OnShow(UIExtendedArtViewModel vm, ICard card)
        {
            try
            {
                s_Vm = vm;
                s_Card = card;
                s_Equip = null;
                RestoreDepths();
                RestorePanel();
                GameObject unlock = Unlock(vm);
                if (unlock != null) unlock.SetActive(true);
                if (!Crafting.Active || card == null || card.Template == null || !Crafting.IsCraftable(card.Template)) { SetVisible(false); return; }
                if (!Ensure(vm)) return;
                ForcePanel(vm);
                // The button panel shows with "Unlock extended art" (Context.UnlockExtendedArtVisible): kept for our buttons,
                // only "Unlock" hidden where there is nothing to unlock (a card already extended, a shard).
                if (card.IsExtended || card.Template.m_CardType.HasAnyFlags(ECardTypes.Resource))
                {
                    vm.Context.UnlockExtendedArtVisible = true;
                    if (unlock != null) unlock.SetActive(false);
                }
                Refresh();
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Crafting] UI: " + e); }
        }

        /// <summary>
        /// UICardStackPagingViewModel.SetSelectedStack / SetCurrentCard (postfixed): the game shows the extended art panel
        /// only for cards that are not resources. A shard that can be crafted (not a basic one) gets it here too, for the
        /// buttons, without "Unlock extended art"; any other resource hides it (else the previous card's panel stayed).
        /// </summary>
        public static void OnCardShown(UICardStackPagingViewModel pager)
        {
            try
            {
                UIExtendedArtViewModel vm = pager.m_ExtendedArtVM;
                ICard card = pager.Context != null ? pager.Context.Card : null;
                if (vm == null || card == null || card.Template == null || !card.Template.m_CardType.HasAnyFlags(ECardTypes.Resource)) return;
                if (Crafting.Active && Crafting.IsCraftable(card.Template)) vm.ShowExtendedArtUI(card);   // OnShow hides "Unlock"
                else if (vm.Context.ExtendedArtVisible) vm.HideExtendedArtUI();
            }
            catch (Exception e) { Debug.LogError("[HexRevival] [Crafting] shard: " + e); }
        }

        public static void OnHide()
        {
            s_Card = null;
            s_Equip = null;
            SetVisible(false);
            RestorePanel();
        }

        /// <summary>
        /// The game's button panel (Unlock, the stardust picture) our buttons sit in. The campaign deck editor's prefab has
        /// it off (no extended art there, the game's choice; nothing in its code touches it): switched on there for the
        /// crafting buttons, without Unlock, and off again when the card goes (user 2026-10-31: crafting right where the
        /// campaign deck is built).
        /// </summary>
        private static GameObject s_ForcedPanel;

        private static void ForcePanel(UIExtendedArtViewModel vm)
        {
            Transform p = vm.transform.Find("VisibilityBinding/Anchor/ExtendedArtButtonPanel");
            if (p == null || p.gameObject.activeSelf) return;
            GameObject panel = p.gameObject;
            panel.SetActive(true);
            s_ForcedPanel = panel;
            GameObject unlock = Unlock(vm);
            if (unlock == null) return;
            unlock.SetActive(false);
            // The panel's own binding starts with it (next frame) and shows its children, Unlock among them: off again then.
            vm.gameObject.AddComponent<NextFrame>().Do = () => { if (s_ForcedPanel == panel) unlock.SetActive(false); };
        }

        private static void RestorePanel()
        {
            if (s_ForcedPanel != null) s_ForcedPanel.SetActive(false);
            s_ForcedPanel = null;
        }

        private class NextFrame : MonoBehaviour
        {
            public Action Do;
            private void Update() { Action a = Do; Destroy(this); if (a != null) a(); }
        }

        private static void SetVisible(bool v)
        {
            if (s_Break != null) s_Break.SetActive(v);
            if (s_Make != null) s_Make.SetActive(v);
            if (s_Info != null) s_Info.SetActive(v);
        }

        /// <summary>The game's "Unlock extended art" button of the panel.</summary>
        private static GameObject Unlock(UIExtendedArtViewModel vm)
        {
            foreach (UIButton b in vm.GetComponentsInChildren<UIButton>(true))
                if (b.name.IndexOf("Unlock", StringComparison.OrdinalIgnoreCase) >= 0) return b.gameObject;
            return null;
        }

        private static bool Ensure(UIExtendedArtViewModel vm)
        {
            if (s_Break != null && s_Make != null && s_Info != null && s_Break.transform.parent != null) return true;
            GameObject src = Unlock(vm);
            if (src == null) { Debug.LogWarning("[HexRevival] [Crafting] no Unlock button: " + Buttons(vm)); return false; }
            // Craft right of "Unlock extended art", Break down left of the panel's round stardust picture.
            float w = NGUIMath.CalculateRelativeWidgetBounds(src.transform).size.x * src.transform.localScale.x;
            if (w < 50f) w = 180f;
            s_Break = Clone(src, "HexRevival_CraftBreak", -w - 110f, OnBreak);
            s_Make = Clone(src, "HexRevival_CraftMake", w + 12f, OnMake);
            // Info right of the stardust bar's BUY button, a copy of it.
            Transform buy = vm.transform.Find("VisibilityBinding/Anchor/Stardust_Store_Button");
            if (buy != null)
            {
                float bw = NGUIMath.CalculateRelativeWidgetBounds(buy).size.x * buy.localScale.x;
                if (bw < 20f) bw = 90f;
                s_Info = Clone(buy.gameObject, "HexRevival_CraftInfo", bw + 8f, OnInfo);
            }
            else Debug.LogWarning("[HexRevival] [Crafting] no BUY button: " + Buttons(vm));
            Debug.Log("[HexRevival] [Crafting] buttons next to " + Path(src.transform, vm.transform) + ", width " + w);
            return true;
        }

        private static GameObject Clone(GameObject src, string name, float dx, EventDelegate.Callback action)
        {
            GameObject b = UiClone.Copy(src, name);
            UiClone.Strip(b);
            ShiftX(b, dx);
            // Once per object: the game's buttons carry two UIButton components (one for the look), each would run it
            // (one click crafted twice, Criesener, 2026-10-31).
            HashSet<GameObject> wired = new HashSet<GameObject>();
            foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true))
            {
                ub.onClick.Clear();
                if (wired.Add(ub.gameObject)) ub.onClick.Add(new EventDelegate(action));
            }
            // The mouse hits colliders whose click was the game's binding (removed by Strip): ours instead. The actions check
            // themselves whether there is something to do (a disabled button still gets the click there).
            foreach (Collider c in b.GetComponentsInChildren<Collider>(true))
                if (c.GetComponent<UIButton>() == null) UIEventListener.Get(c.gameObject).onClick = go => action();
            b.SetActive(true);
            return b;
        }

        /// <summary>Moves a copy sideways: through its anchors when it is an anchored widget (they would put it back).</summary>
        private static void ShiftX(GameObject g, float dx)
        {
            UIRect r = g.GetComponent<UIRect>();
            if (r != null && r.isAnchored)
            {
                foreach (UIRect.AnchorPoint a in new[] { r.leftAnchor, r.rightAnchor }) if (a.target != null) a.absolute += Mathf.RoundToInt(dx);
                r.ResetAndUpdateAnchors();
            }
            else g.transform.localPosition += new Vector3(dx, 0f, 0f);
        }

        private static void SetButton(GameObject b, string text, bool enabled)
        {
            foreach (UILabel l in b.GetComponentsInChildren<UILabel>(true)) l.text = text;
            foreach (UIButton ub in b.GetComponentsInChildren<UIButton>(true)) ub.isEnabled = enabled;
            b.SetActive(true);
        }

        private static void Refresh()
        {
            if (s_Break == null) return;
            if (s_Info != null) SetButton(s_Info, Loc.Get("craft_btn_info"), true);
            if (s_Equip != null)
            {
                int spare = Crafting.EquipmentSurplus(s_Equip.m_Id), owned = Inventory.Count(s_Equip.m_Id);
                SetButton(s_Break, Loc.Get("craft_btn_break", spare), spare > 0);
                SetButton(s_Make, Loc.Get("craft_btn_make", owned, 1), owned == 0);
                return;
            }
            if (s_Card == null) return;
            ResourceId t = s_Card.Template.m_Id;
            int surplus = Crafting.Surplus(t), all = Crafting.TotalSurplus(), missing = Crafting.Missing(t);
            SetButton(s_Break, surplus > 0 || all == 0 ? Loc.Get("craft_btn_break", surplus) : Loc.Get("craft_btn_break_all", all), all > 0);
            SetButton(s_Make, Loc.Get("craft_btn_make", Collection.Count(t), Collection.CopiesPerCard), missing > 0);
        }

        private static void OnInfo()
        {
            UISystem.DisplayAlert(Crafting.InfoText(), Loc.Get("craft_info_title"), null);
        }

        /// <summary>This card's surplus, every surplus of the account, or both to choose from.</summary>
        private static void OnBreak()
        {
            if (s_Equip != null) { EquipmentBreak(s_Equip); return; }
            if (s_Card == null) return;
            CardTemplate t = s_Card.Template;
            int n = Crafting.Surplus(t.m_Id), all = Crafting.TotalSurplus();
            if (all <= 0) return;
            UIModalPopupViewModel.ClickHandler one = () => BreakOne(t, n), every = BreakEvery;
            string title = Loc.Get("craft_title_break");
            if (n <= 0) UISystem.DisplayConfirm(Loc.Get("craft_confirm_break_all", all), title, null, every);
            else if (all == n) UISystem.DisplayConfirm(Loc.Get("craft_confirm_break", n, t.m_Name.Trim(), Crafting.Yield(t)), title, null, one);
            else UISystem.Display3ButtonPopup(Loc.Get("craft_confirm_break", n, t.m_Name.Trim(), Crafting.Yield(t)) + "\n\n" + Loc.Get("craft_confirm_break_or_all", all), title,
                null, one, every, Localization.Get("Cancel"), Loc.Get("craft_this_card", n), Loc.Get("craft_all", all));
        }

        private static void BreakOne(CardTemplate t, int n)
        {
            Dictionary<ResourceId, int> got = Crafting.BreakCard(t.m_Id, n);
            if (got.Count == 0) return;
            PlayEffect();
            Notices.Show(Loc.Get("craft_done_break", n, t.m_Name.Trim(), Crafting.Describe(got)));
            Again();
        }

        private static void BreakEvery()
        {
            int cards, items;
            Dictionary<ResourceId, int> got = Crafting.BreakAll(out cards, out items);
            if (got.Count == 0) return;
            PlayEffect();
            Notices.Show(Loc.Get("craft_done_break_all", cards, items, Crafting.Describe(got)));
            Again();
        }

        // ------------------------------------------------------------------
        // Equipment: right-click in the collection's equipment list (UIPlayerEquipmentViewModel.OnItemRightClick, postfixed)
        // ------------------------------------------------------------------

        /// <summary>
        /// The game shows the equipment's details popup (UISystem.DisplayLinkedEquipment); ours is the extended art panel
        /// under it, without "Unlock", with the stardust of the equipment's rarity and the buttons. It goes with the popup.
        /// </summary>
        public static void OnEquipmentRightClick(object sender)
        {
            try
            {
                UIEquipmentItemContext ctx = sender as UIEquipmentItemContext;
                InventoryEquipmentData e = ctx != null ? ctx.GenericItemData as InventoryEquipmentData : null;
                if (e == null || !Crafting.Active || Crafting.EquipmentCost(e) == null) return;
                UIExtendedArtViewModel vm = Panel();
                if (vm == null) { Debug.LogWarning("[HexRevival] [Crafting] equipment: no extended art panel"); return; }
                ShowForEquipment(vm, e);
            }
            catch (Exception ex) { Debug.LogError("[HexRevival] [Crafting] equipment: " + ex); }
        }

        /// <summary>The collection's extended art panel (inactive until a card was zoomed).</summary>
        private static UIExtendedArtViewModel Panel()
        {
            if (s_Vm != null) return s_Vm;
            foreach (UIExtendedArtViewModel vm in Resources.FindObjectsOfTypeAll<UIExtendedArtViewModel>())
                if (vm != null && vm.gameObject.scene.IsValid() && vm.Context != null) return vm;
            return null;
        }

        private static void ShowForEquipment(UIExtendedArtViewModel vm, InventoryEquipmentData e)
        {
            s_Vm = vm;
            s_Card = null;
            s_Equip = e;
            UIExtendedArtContext c = vm.Context;
            ERarity[] dust = { ERarity.Common, ERarity.Uncommon, ERarity.Rare, ERarity.Legendary };
            c.StarDustRarity = (int)dust[Mathf.Clamp(Crafting.Tier(e.m_Rarity), 0, 3)];
            foreach (UISpriteAnimationExt a in vm.StarDustSprites) if (a != null) a.Reset();
            // As UIExtendedArtViewModel.UpdateBindings, which needs a card.
            PlayerProfile profile = GameClient.Instance.PlayerProfile;
            c.CommonStardustCount = profile.GetInventoryItemsOfType(BuiltInResources.CommonStardust).Count.ToString();
            c.UncommonStardustCount = profile.GetInventoryItemsOfType(BuiltInResources.UncommonStardust).Count.ToString();
            c.RareStardustCount = profile.GetInventoryItemsOfType(BuiltInResources.RareStardust).Count.ToString();
            c.LegendaryStardustCount = profile.GetInventoryItemsOfType(BuiltInResources.LegendaryStardust).Count.ToString();
            c.PromoStardustCount = profile.GetInventoryItemsOfType(BuiltInResources.PromoStardust).Count.ToString();
            c.Gold = profile.Gold;
            c.UnlockExtendedArtVisible = true;
            c.UnlockExtendedArtButtonEnabled = true;   // the button panel's look (enabled or greyed) goes with it
            c.ExtendedArtVisible = true;
            c.StardustStoreVisible = false;
            if (!Ensure(vm)) return;
            ForcePanel(vm);
            GameObject unlock = Unlock(vm);
            if (unlock != null) unlock.SetActive(false);
            if (vm.GetComponent<EquipmentWatch>() == null) vm.gameObject.AddComponent<EquipmentWatch>();
            RaiseOverDetails(vm);
            Refresh();
            s_RefreshNext = true;
        }

        /// <summary>The panel's NGUI panels and their own depths, while raised over the equipment's details popup.</summary>
        private static readonly Dictionary<UIPanel, int> s_Depths = new Dictionary<UIPanel, int>();
        /// <summary>The panel's content, moved down under the details popup's card while raised.</summary>
        private static Transform s_Moved;
        private const float EquipmentDrop = 130f;
        /// <summary>The buttons' state again on the next frame (set while the panel was still hidden, they look disabled).</summary>
        private static bool s_RefreshNext;

        /// <summary>The details popup is drawn on top of everything (and darkens it): the panel goes above it.</summary>
        private static void RaiseOverDetails(UIExtendedArtViewModel vm)
        {
            if (s_Depths.Count > 0) return;
            UIEquipmentDetailsViewModel details = UnityEngine.Object.FindObjectOfType<UIEquipmentDetailsViewModel>();
            if (details == null) return;
            int top = 0;
            foreach (UIPanel p in details.GetComponentsInChildren<UIPanel>(true)) top = Mathf.Max(top, p.depth);
            foreach (UIPanel p in details.GetComponentsInParent<UIPanel>(true)) top = Mathf.Max(top, p.depth);
            UIPanel[] mine = vm.GetComponentsInChildren<UIPanel>(true);
            int low = int.MaxValue;
            foreach (UIPanel p in mine) low = Mathf.Min(low, p.depth);
            foreach (UIPanel p in mine)
            {
                s_Depths[p] = p.depth;
                p.depth = top + 10 + (p.depth - low);
            }
            s_Moved = vm.transform.Find("VisibilityBinding");
            if (s_Moved != null) s_Moved.localPosition -= new Vector3(0f, EquipmentDrop, 0f);
        }

        private static void RestoreDepths()
        {
            if (s_Depths.Count == 0) return;
            foreach (KeyValuePair<UIPanel, int> d in s_Depths) if (d.Key != null) d.Key.depth = d.Value;
            s_Depths.Clear();
            if (s_Moved != null) s_Moved.localPosition += new Vector3(0f, EquipmentDrop, 0f);
            s_Moved = null;
        }

        /// <summary>Closes the panel with the equipment's details popup.</summary>
        private class EquipmentWatch : MonoBehaviour
        {
            private void Update()
            {
                if (s_RefreshNext && s_Equip != null) { s_RefreshNext = false; Refresh(); }
                if (s_Equip == null || UISystem.IsTaggedUIElementVisible("EquipmentDetails")) return;
                UIExtendedArtViewModel vm = s_Vm;
                s_Equip = null;
                RestoreDepths();
                if (vm != null) vm.HideExtendedArtUI();
            }
        }

        private static void EquipmentBreak(InventoryEquipmentData e)
        {
            string name = e.m_Name.Trim();
            int surplus = Crafting.EquipmentSurplus(e.m_Id);
            if (surplus <= 0) return;
            UISystem.DisplayConfirm(Loc.Get("craft_confirm_ebreak", surplus, name), Loc.Get("craft_title_break"), null, () =>
            {
                Dictionary<ResourceId, int> got = Crafting.BreakEquipment(e.m_Id, surplus);
                if (got.Count == 0) return;
                PlayEffect();
                Notices.Show(Loc.Get("craft_done_break", surplus, name, Crafting.Describe(got)));
                Again();
            });
        }

        private static void EquipmentMake(InventoryEquipmentData e)
        {
            string name = e.m_Name.Trim();
            if (Inventory.Count(e.m_Id) > 0) return;
            Crafting.Cost c = Crafting.EquipmentCost(e);
            if (c == null) return;
            if (!c.Affordable()) { UISystem.DisplayAlert(Loc.Get("craft_cannot", name, c.Text()), Loc.Get("craft_title_make"), null); return; }
            UISystem.DisplayConfirm(Loc.Get("craft_confirm_make", name, c.Text()), Loc.Get("craft_title_make"), null, () =>
            {
                if (!Crafting.CraftEquipment(e.m_Id)) return;
                PlayEffect();
                Notices.Show(Loc.Get("craft_done_make", name, 1));
                Again();
            });
        }

        private static void OnMake()
        {
            if (s_Equip != null) { EquipmentMake(s_Equip); return; }
            if (s_Card == null) return;
            CardTemplate t = s_Card.Template;
            Crafting.Cost c = Crafting.CardCost(t);
            if (c == null || Crafting.Missing(t.m_Id) <= 0) return;
            if (!c.Affordable()) { UISystem.DisplayAlert(Loc.Get("craft_cannot", t.m_Name.Trim(), c.Text()), Loc.Get("craft_title_make"), null); return; }
            UISystem.DisplayConfirm(Loc.Get("craft_confirm_make", t.m_Name.Trim(), c.Text()), Loc.Get("craft_title_make"), null, () =>
            {
                if (!Crafting.CraftCard(t.m_Id)) return;
                PlayEffect();
                Notices.Show(Loc.Get("craft_done_make", t.m_Name.Trim(), Collection.Count(t.m_Id)));
                Again();
            });
        }

        /// <summary>The panel shown again: the game's stardust counts and our buttons.</summary>
        private static void Again()
        {
            if (s_Vm != null && s_Equip != null) ShowForEquipment(s_Vm, s_Equip);
            else if (s_Vm != null && s_Card != null) s_Vm.ShowExtendedArtUI(s_Card);
        }

        /// <summary>The extended art's animation: stardust, sound and (for a card) the card effect of its rarity.</summary>
        private static void PlayEffect()
        {
            if (s_Vm == null) return;
            try
            {
                foreach (UISpriteAnimationExt a in s_Vm.StarDustSprites) if (a != null && a.gameObject.activeInHierarchy) a.Play();
                if (!string.IsNullOrEmpty(s_Vm.UnlockSound)) MasterAudio.PlaySound(s_Vm.UnlockSound);
                if (s_Card == null) return;
                UICardStackPagingViewModel stack = s_Vm.GetComponentInParent<UICardStackPagingViewModel>();
                if (stack == null) stack = UnityEngine.Object.FindObjectOfType<UICardStackPagingViewModel>();
                GoCardBuilder builder = stack != null ? stack.GetComponentInChildren<GoCardBuilder>() : null;
                if (builder == null || stack.m_ExtendedArtFX == null) return;
                int fx = Mathf.Max(0, Crafting.Tier(s_Card.Template.m_CardRarity));
                if (s_Card.Template.HasAlternateArt) fx = 4;
                if (fx >= stack.m_ExtendedArtFX.Length || stack.m_ExtendedArtFX[fx] == null) return;
                GameObject go = UnityEngine.Object.Instantiate(stack.m_ExtendedArtFX[fx]);
                builder.AttachEffect(go);
                go.transform.parent = null;
            }
            catch (Exception e) { Debug.LogWarning("[HexRevival] [Crafting] effect: " + e.Message); }
        }

        private static string Buttons(Component root)
        {
            List<string> l = new List<string>();
            foreach (UIButton b in root.GetComponentsInChildren<UIButton>(true)) l.Add(Path(b.transform, root.transform) + (b.gameObject.activeInHierarchy ? "" : " (hidden)"));
            return string.Join(", ", l.ToArray());
        }

        private static string Path(Transform t, Transform root)
        {
            string p = t.name;
            while (t.parent != null && t != root) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        /// <summary>Dev: "craft show &lt;card&gt;" = the collection's right-click on that card, "craft eshow &lt;equipment&gt;" on that
        /// equipment; "craft ui" = the panel's buttons.</summary>
        public static string Command(string sub, string arg)
        {
            if (sub == "ui")
            {
                UIExtendedArtViewModel vm = Panel();
                return vm == null ? "no extended art panel" : Buttons(vm);
            }
            if (sub == "eshow")
            {
                foreach (InventoryItemData d in Singleton<TemplateManager>.Instance.InventoryItems.Values)
                {
                    InventoryEquipmentData e = d as InventoryEquipmentData;
                    if (e == null || e.m_Name == null || !e.m_Name.Trim().Equals(arg, StringComparison.OrdinalIgnoreCase)) continue;
                    UISystem.DisplayLinkedEquipment(e);
                    UIExtendedArtViewModel vm = Panel();
                    if (vm == null) return "no extended art panel";
                    ShowForEquipment(vm, e);
                    return "showing " + e.m_Name;
                }
                return "no equipment " + arg;
            }
            CardCollectionManagerViewModelBase coll = UnityEngine.Object.FindObjectOfType<CardCollectionManagerViewModelBase>();
            if (coll == null) return "not on the collection screen";
            foreach (CardTemplate t in Singleton<TemplateManager>.Instance.Cards.Values)
            {
                if (t == null || t.m_Name == null || !t.m_Name.Trim().Equals(arg, StringComparison.OrdinalIgnoreCase)) continue;
                UICardTemplateContext stack = coll.TopCollection.GetStack(t);
                if (stack == null) continue;
                coll.OnTopCollectionCardRightClick(stack);
                return "showing " + t.m_Name;
            }
            return "no stack for " + arg + " (not owned? turn on \"show cards not owned\")";
        }
    }
}
