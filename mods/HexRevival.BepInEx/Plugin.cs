using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Game.Client;
using Game.Client.Campaign;
using Game.Shared.Campaign.Messages;
using Game.Shared;
using HarmonyLib;
using Game.Shared.Mechanics;

namespace HexRevival
{
    /// <summary>
    /// BepInEx 5 entry point of the HexRevival mod.
    ///
    /// Replaces the Mono.Cecil patcher (tools/HexRevival.Patcher): the same
    /// redirections are applied at runtime through Harmony, and the game installation stays
    /// untouched (BepInEx only adds files next to it).
    ///
    /// The logic itself is unchanged and shared with the old plugin:
    /// mods/HexRevival/src/HexRevival/{Bootstrap,Patches,Server,Repl,Test}.cs.
    /// </summary>
    [BepInPlugin("hexrevival", "HexRevival", Community.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            HexRevival.Crafting.Enabled = Config.Bind("Crafting", "Enabled", true,
                "Surplus cards (above 4) and equipment (above 1) break down into gems, ores and parts; missing ones are crafted from them and stardust.").Value;
            HexRevival.Crafting.GemsPerCard = Math.Max(1, Config.Bind("Crafting", "GemsPerCard", 2, "Gems (or Magical Ore) of the card's rarity and color to craft a card.").Value);
            HexRevival.Crafting.PartsPerEquipment = Math.Max(1, Config.Bind("Crafting", "PartsPerEquipment", 1, "Parts of the equipment's slot and rarity to craft an equipment.").Value);
            {
                string dust = Config.Bind("Crafting", "DustCost", "1,1,1,1", "Stardust to craft, for Common, Uncommon, Rare, Legendary (epic and promo cards: Legendary).").Value;
                string[] d = dust.Split(',');
                for (int i = 0; i < 4 && i < d.Length; i++) { int v; if (int.TryParse(d[i].Trim(), out v) && v >= 0) HexRevival.Crafting.DustCost[i] = v; }
            }
            Hooks.HideAuctionHouse = Config.Bind("UI", "HideAuctionHouse", true,
                "No Auction House button in the top bar (offline there is nobody to trade with).").Value;
            Harmony harmony = new Harmony("hexrevival");
            int ok = 0, failed = 0;

            Action<string, Action> apply = (label, patch) =>
            {
                try { patch(); ok++; }
                catch (Exception e) { failed++; Log.LogError("Patch KO " + label + " : " + e); }
            };

            Type standaloneSession = typeof(StandaloneSession);

            // Equivalent of the Cecil injection at the start of Main.FinializeInit:
            // config.ini has already been read at this point, so standalone mode can be forced.
            apply("Main.FinializeInit", () => Prefix(harmony, typeof(Main), "FinializeInit", Type.EmptyTypes, "Main_FinializeInit"));

            apply("Steamy.IsRunningSteam", () => Prefix(harmony, typeof(Steamy), "IsRunningSteam", Type.EmptyTypes, "Steamy_IsRunningSteam"));
            apply("PlayerProfile.Login", () => Prefix(harmony, typeof(PlayerProfile), "Login",
                new[] { typeof(string), typeof(string), typeof(string), typeof(string).MakeByRefType(), typeof(Dictionary<string, object>).MakeByRefType() },
                "PlayerProfile_Login"));
            apply("StandaloneGameClient.SendStartSession (Practice)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartSession",
                null, "StandaloneGameClient_SendStartSession"));
            apply("Session.AddPlayer (player deck)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(StandaloneSession), "AddPlayer", new[] { typeof(Player) });
                if (m == null) throw new MissingMethodException("AddPlayer");
                // Declared by Session (StandaloneSession does not override it): patched there, as HarmonyX asks.
                m = AccessTools.Method(m.DeclaringType, "AddPlayer", new[] { typeof(Player) });
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Session_AddPlayer"));
            });
            // The local AI has an AIPlayer-type id: its "server"-side copy (RemotePlayer)
            // is therefore not recognized as an AI (IsAI tests ServiceAI) and received the
            // player-side deck of the encounter. We recognize it as an AI.
            apply("Player.IsAI (local AI)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Player), "IsAI", Type.EmptyTypes);
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Player_IsAI"));
            });
            // Faster startup: the intro video (HEX Entertainment logo) is skipped
            // automatically, as if the player pressed Space.
            apply("LevelLoader.Update (skip intro)", () => Prefix(harmony, typeof(LevelLoader), "Update", Type.EmptyTypes, "LevelLoader_SkipIntro"));
            apply("StandaloneGameClient.SendStartEncounter", () => Prefix(harmony, typeof(StandaloneGameClient), "SendStartEncounter",
                new[] { typeof(string), typeof(SessionStateEncounterData) }, "StandaloneGameClient_SendStartEncounter"));

            // Online guest: joins the host's game, its actions go to the host (see OnlineMatch).
            apply("StandaloneGameClient.SendJoinSession (online)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendJoinSession", null, "Online_JoinSession"));
            apply("StandaloneGameClient.SendReadyToStartGame (online)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendReadyToStartGame", null, "Online_ReadyToStart"));
            apply("SessionClient.PumpSyncEventQueue", () => Prefix(harmony, typeof(Game.Client.SessionClient), "PumpSyncEventQueue", Type.EmptyTypes, "Session_PumpSync"));
            apply("StandaloneGameClient.SendSessionTransaction (online)", () => Prefix(harmony, typeof(StandaloneGameClient), "SendSessionTransaction", null, "Online_SessionTransaction"));
            apply("UILandingPageContext.OnClickChallengeAI", () => Prefix(harmony, typeof(UILandingPageContext), "OnClickChallengeAI", Type.EmptyTypes, "UILandingPage_OnClickChallengeAI"));

            // Local campaign service: the client's campaign requests are answered by Campaign.
            Type ccm = typeof(ClientCampaignManager);
            foreach (string m in new[] { "QueryCurrentCampaign", "GetSummary", "GetActiveStatus", "QueryState", "Start", "Create",
                                         "Forfeit", "SendEvent", "StartLocation", "FinishLocation", "GetTemplateTypeInfo" })
            {
                string name = m;
                apply("ClientCampaignManager." + name, () => Prefix(harmony, ccm, name, null, "Campaign_" + name));
            }
            // Campaign mercenary parties (see Mercenaries.cs).
            apply("ClientCampaignManager.SaveChampionParty", () => Prefix(harmony, typeof(ClientCampaignManager), "SaveChampionParty", null, "Campaign_SaveChampionParty"));
            // Profile deck templates (the AZ2 boat's deck, see DeckTemplates.cs).
            // Account level rewards claimed on the account panel (see AccountRewards).
            apply("Profile.Network.ProfileAccountXpState", () => Prefix(harmony, typeof(Game.Shared.Profile.Network), "ProfileAccountXpState", null, "Profile_AccountXpState"));
            apply("Profile.Network.ProfileAccountXpClaim", () => Prefix(harmony, typeof(Game.Shared.Profile.Network), "ProfileAccountXpClaim", null, "Profile_AccountXpClaim"));
            apply("Profile.Network.DeckTemplateSave", () => Prefix(harmony, typeof(Game.Shared.Profile.Network), "DeckTemplateSave", null, "Profile_DeckTemplateSave"));
            // Local store (see Store.cs).
            apply("EscrowService.GetStoreItems.Request", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.GetStoreItems), "Request",
                new[] { typeof(Game.Client.Network.Escrow.EscrowService.GetStoreItems.ResponseHandler), typeof(string), typeof(int), typeof(Game.Shared.Network.SFS.IService) }, "Store_GetStoreItems"));
            // Overloads without a handler (the store screen uses these: the answer goes through OnResponse).
            apply("EscrowService.GetStoreItems.Request(event)", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.GetStoreItems), "Request",
                new[] { typeof(string), typeof(Game.Shared.Network.SFS.IService) }, "Store_GetStoreItemsEvent"));
            apply("EscrowService.PurchaseItem.Request(event)", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.PurchaseItem), "Request",
                new[] { typeof(int), typeof(ulong), typeof(bool), typeof(string), typeof(Game.Shared.Network.SFS.IService) }, "Store_PurchaseItemEvent"));
            // "Unlock extended art" (ExtendedArt): answered here, it waited for the dead server.
            apply("ProfileService.UpgradeExtendedArtOnCard.Request", () => Prefix(harmony, typeof(Game.Client.Network.Profile.ProfileService.UpgradeExtendedArtOnCard), "Request",
                new[] { typeof(ulong), typeof(ulong), typeof(Game.Client.Network.Profile.ProfileService.UpgradeExtendedArtOnCard.ResponseHandler), typeof(string), typeof(int), typeof(Game.Shared.Network.SFS.IService) }, "ExtendedArt_Upgrade"));
            // Redeem codes of the store (Redeem).
            apply("EscrowService.RedeemCode.Request", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.RedeemCode), "Request",
                new[] { typeof(string), typeof(Game.Client.Network.Escrow.EscrowService.RedeemCode.ResponseHandler), typeof(string), typeof(int), typeof(Game.Shared.Network.SFS.IService) }, "Redeem_Request"));
            apply("EscrowService.RedeemCode.Request(event)", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.RedeemCode), "Request",
                new[] { typeof(string), typeof(string), typeof(Game.Shared.Network.SFS.IService) }, "Redeem_RequestEvent"));
            apply("EscrowService.PurchaseItem.Request", () => Prefix(harmony, typeof(Game.Client.Network.Escrow.EscrowService.PurchaseItem), "Request",
                new[] { typeof(int), typeof(ulong), typeof(bool), typeof(Game.Client.Network.Escrow.EscrowService.PurchaseItem.ResponseHandler), typeof(string), typeof(int), typeof(Game.Shared.Network.SFS.IService) }, "Store_PurchaseItem"));
            // End-of-battle campaign events of the game's rules (princess saved, creatures tamed...).
            apply("AuthoritativeSessionBase.PerformTutorialBoardSetup", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.AuthoritativeSessionBase), "PerformTutorialBoardSetup"), postfix: new HarmonyMethod(typeof(Hooks), "Tutorial_BoardSetup")));
            apply("TutorialManager..ctor", () => harmony.Patch(AccessTools.Constructor(typeof(Game.Shared.Tutorial.TutorialManager), new[] { typeof(Game.Shared.ClientSessionBase), typeof(Game.Shared.Tutorial.EPlayerType) }),
                prefix: new HarmonyMethod(typeof(Hooks), "TutorialManager_CtorPre"), postfix: new HarmonyMethod(typeof(Hooks), "TutorialManager_CtorPost")));
            apply("UITutorialRewards.Launch", () => harmony.Patch(AccessTools.Method(typeof(UITutorialRewards), "Launch"), prefix: new HarmonyMethod(typeof(Hooks), "TutorialRewards_Launch")));
            apply("AITactical.AIUpdate", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.AI.AITactical), "AIUpdate", new[] { typeof(Game.Shared.Mechanics.ETurnPhases) }), prefix: new HarmonyMethod(typeof(Hooks), "AITactical_AIUpdate")));
            apply("UISystemMenuViewModel.Awake", () => harmony.Patch(AccessTools.Method(typeof(UISystemMenuViewModel), "Awake"), postfix: new HarmonyMethod(typeof(Hooks), "SystemMenu_Awake")));
            apply("LocalPlayer.PassPriority", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.LocalPlayer), "PassPriority"), prefix: new HarmonyMethod(typeof(Hooks), "Trace_LocalPass")));
            apply("LocalPlayer.SendAutoPass", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.LocalPlayer), "SendAutoPass"), prefix: new HarmonyMethod(typeof(Hooks), "Trace_AutoPass")));
            apply("Player.HasStop", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.Player), "HasStop"), postfix: new HarmonyMethod(typeof(Hooks), "Player_HasStop")));
            // Windowed mode: the mouse wheel over the unfocused window is ignored (Windows sends it to the window
            // under the cursor): NGUI's wheel (map zoom, scroll lists) and the two direct reads (battle: wheel up
            // examines the hovered card; drop-down lists).
            apply("UICamera.GetAxis (focus)", () =>
            {
                UICamera.GetAxisFunc orig = UICamera.GetAxis;
                UICamera.GetAxis = axis => { float v = orig(axis); return v == 0f || HexRevival.WindowFocus.IsActive() ? v : 0f; };
            });
            apply("UIBattle.LateUpdate (focus)", () => harmony.Patch(AccessTools.Method(typeof(UIBattle), "LateUpdate"), transpiler: new HarmonyMethod(typeof(Hooks), "FocusedAxisTranspiler")));
            apply("UIPopupList.LateUpdate (focus)", () => harmony.Patch(AccessTools.Method(typeof(UIPopupList), "LateUpdate"), transpiler: new HarmonyMethod(typeof(Hooks), "FocusedAxisTranspiler")));
            apply("UICoinToss.TurnOffCoinTossEffects", () => harmony.Patch(AccessTools.Method(typeof(UICoinToss), "TurnOffCoinTossEffects"), postfix: new HarmonyMethod(typeof(Hooks), "CoinToss_TurnOff")));
            apply("Siege Execution.IssueRequest", () =>
            {
                MethodInfo m = null;
                foreach (MethodInfo x in typeof(Game.Shared.Campaign.Siege.Execution).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
                    if (x.Name == "IssueRequest" && x.GetParameters().Length == 8) m = x;
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "Siege_IssueRequest"));
            });
            // Battlegrounds: the gauntlets and the ladder against the AI (see Battlegrounds.cs).
            apply("GameStateTournament.OnEntry", () => harmony.Patch(AccessTools.Method(typeof(GameStateTournament), "OnEntry"), postfix: new HarmonyMethod(typeof(Hooks), "Tournament_OnEntry")));
            {
                Type svc = typeof(Game.Shared.Network.SFS.IService);
                Type ts = typeof(Game.Client.Network.Tournaments.TournamentsService);
                Func<string, Type> handler = n => ts.GetNestedType(n).GetNestedType("ResponseHandler");
                Action<string, Type[], string> request = (n, args, hook) =>
                    apply("TournamentsService." + n + ".Request", () => Prefix(harmony, ts.GetNestedType(n), "Request", args, hook));
                request("TestTournamentEntry", new[] { typeof(ulong), typeof(bool), handler("TestTournamentEntry"), typeof(string), typeof(int), svc }, "Tournament_TestEntry");
                request("EnterTournament", new[] { typeof(ulong), typeof(UID), typeof(UID), typeof(int), typeof(string), typeof(bool), handler("EnterTournament"), typeof(string), typeof(int), svc }, "Tournament_Enter");
                request("SaveTournamentDeck", new[] { typeof(Game.Shared.Domain.deck_bits), typeof(ResourceId), typeof(ulong), handler("SaveTournamentDeck"), typeof(string), typeof(int), svc }, "Tournament_SaveDeck");
                request("GameEntrance", new[] { typeof(ulong), typeof(Game.Shared.Domain.deck_bits), typeof(bool), handler("GameEntrance"), typeof(string), typeof(int), svc }, "Tournament_GameEntrance");
                request("LeaveTournament", new[] { typeof(ulong), handler("LeaveTournament"), typeof(string), typeof(int), svc }, "Tournament_Leave");
            }
            apply("TournamentManager.SendTryReconnectionToDisconnectedTournament", () => Prefix(harmony, typeof(Game.Client.Tournament.TournamentManager), "SendTryReconnectionToDisconnectedTournament", new[] { typeof(ulong) }, "Tournament_Reconnect"));
            apply("TournamentManager.RequestSelectDraftCard", () => Prefix(harmony, typeof(Game.Client.Tournament.TournamentManager), "RequestSelectDraftCard", new[] { typeof(ulong), typeof(ulong), typeof(ulong) }, "Tournament_SelectDraftCard"));
            apply("TournamentManager.RequestHilightDraftCard", () => Prefix(harmony, typeof(Game.Client.Tournament.TournamentManager), "RequestHilightDraftCard", new[] { typeof(ulong), typeof(ulong) }, "Skip"));
            {
                Type tsm = typeof(Game.Shared.Tournaments.TournamentSharedMessaging);
                apply("TournamentSharedMessaging.Send_LadderRecord_Request", () => Prefix(harmony, tsm, "Send_LadderRecord_Request", null, "Ladder_Record"));
                apply("TournamentSharedMessaging.Send_Ladder_Tier_Rewards_List_Request", () => Prefix(harmony, tsm, "Send_Ladder_Tier_Rewards_List_Request", null, "Ladder_TierRewards"));
                apply("TournamentSharedMessaging.Send_Ladder_Top_100_Request", () => Prefix(harmony, tsm, "Send_Ladder_Top_100_Request", null, "Ladder_Top100"));
                apply("TournamentSharedMessaging.Send_LadderFindMatch_Request", () => Prefix(harmony, tsm, "Send_LadderFindMatch_Request",
                    new[] { typeof(ulong), typeof(Game.Shared.Domain.deck_bits), typeof(bool), typeof(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderFindMatch.Response>>) }, "Ladder_FindMatch"));
            }
            // Frost Ring Arena on the game's own screens (see Arena.cs).
            {
                Type svc = typeof(Game.Shared.Network.SFS.IService);
                Type cs = typeof(Game.Client.Network.Campaign.CampaignService);
                Func<string, Type> handler = n => cs.GetNestedType(n).GetNestedType("ResponseHandler");
                Type ad = typeof(Reckoning.Campaign.Messages.Arena.ArenaData), af = typeof(Reckoning.Campaign.Messages.Arena.ArenaFight), ac = typeof(Reckoning.Campaign.Messages.Arena.ArenaChallenger);
                Action<string, Type[], string> request = (n, args, hook) =>
                    apply("CampaignService." + n + ".Request", () => Prefix(harmony, cs.GetNestedType(n), "Request", args, hook));
                request("JoinCampaignArena", new[] { typeof(ulong), handler("JoinCampaignArena"), typeof(string), typeof(int), svc }, "Arena_Join");
                request("GetMasterListOfChallengers", new[] { typeof(ECampaignDifficulty), handler("GetMasterListOfChallengers"), typeof(string), typeof(int), svc }, "Arena_MasterList");
                request("AssignArenaDeck", new[] { typeof(ulong), typeof(ECampaignDifficulty), handler("AssignArenaDeck"), typeof(string), typeof(int), svc }, "Arena_AssignDeck");
                request("UpdateMCChallenge", new[] { ad, af, handler("UpdateMCChallenge"), typeof(string), typeof(int), svc }, "Skip");
                request("DoArenaCashOut", new[] { ad, handler("DoArenaCashOut"), typeof(string), typeof(int), svc }, "Arena_CashOut");
                request("DestroyArenaData", new[] { typeof(ulong), handler("DestroyArenaData"), typeof(string), typeof(int), svc }, "Arena_Destroy");
                request("RefreshArenaInfo", new[] { ad, af, ac, handler("RefreshArenaInfo"), typeof(string), typeof(int), svc }, "Arena_Refresh");
                apply("AssetBundleManager.GetAsset", () => harmony.Patch(AccessTools.Method(typeof(AssetBundleManager), "GetAsset", new[] { typeof(AssetBundleFile), typeof(string), typeof(string), typeof(AssetType) }), postfix: new HarmonyMethod(typeof(Hooks), "Icons_GetAsset")));
                apply("UIArenaCampaignContext.AssignChampionHUD", () => harmony.Patch(AccessTools.Method(typeof(UIArenaCampaignContext), "AssignChampionHUD"), postfix: new HarmonyMethod(typeof(Hooks), "Arena_ChampionHud")));
                apply("UIArenaCampaignViewModel.Start", () => harmony.Patch(AccessTools.Method(typeof(UIArenaCampaignViewModel), "Start"), postfix: new HarmonyMethod(typeof(Hooks), "Arena_Start")));
                apply("UIArenaCampaignViewModel.BattleChampion", () => Prefix(harmony, typeof(UIArenaCampaignViewModel), "BattleChampion", Type.EmptyTypes, "Arena_Battle"));
                // The Dueling Pit's Versus AI (see DuelingPit.cs).
                apply("UISocketCardViewModel.UpdateAvailableGems", () => harmony.Patch(AccessTools.Method(typeof(UISocketCardViewModel), "UpdateAvailableGems"), prefix: new HarmonyMethod(typeof(Hooks), "Gems_Available")));
                apply("Localization.Get", () => harmony.Patch(AccessTools.Method(typeof(Localization), "Get", new[] { typeof(string) }), postfix: new HarmonyMethod(typeof(Hooks), "Localization_Get")));
                apply("UITournamentPendingViewModel.OnJoinTournamentClicked", () => Prefix(harmony, typeof(UITournamentPendingViewModel), "OnJoinTournamentClicked", Type.EmptyTypes, "Tournament_Join"));
                apply("UITournamentPendingViewModel.OnTournamentDoubleClicked", () => Prefix(harmony, typeof(UITournamentPendingViewModel), "OnTournamentDoubleClicked", new[] { typeof(UITournamentDescContext) }, "Tournament_DoubleClick"));
                apply("UILandingPageViewModel.Awake", () => harmony.Patch(AccessTools.Method(typeof(UILandingPageViewModel), "Awake"), postfix: new HarmonyMethod(typeof(Hooks), "Landing_Awake")));
                apply("UIPremiumStoreItemContext..ctor(pack)", () => harmony.Patch(AccessTools.Constructor(typeof(UIPremiumStoreItemContext), new[] { typeof(Reckoning.Game.InventoryCardPack), typeof(Game.Shared.StoreItem) }), postfix: new HarmonyMethod(typeof(Hooks), "Store_PackItem")));
                apply("UIPremiumStoreItemContext..ctor(chest)", () => harmony.Patch(AccessTools.Constructor(typeof(UIPremiumStoreItemContext), new[] { typeof(Reckoning.Game.InventoryTreasureChest), typeof(Game.Shared.StoreItem) }), postfix: new HarmonyMethod(typeof(Hooks), "Store_ChestItem")));
                apply("StoreItem3DController.OnAssetBundleObjectLoaded", () => harmony.Patch(AccessTools.Method(typeof(StoreItem3DController), "OnAssetBundleObjectLoaded"), postfix: new HarmonyMethod(typeof(Hooks), "Store_3DLoaded")));
                apply("DuelingPitVM.FindMatch", () => Prefix(harmony, typeof(DuelingPitVM), "FindMatch", Type.EmptyTypes, "DuelingPit_FindMatch"));
                // The buffs' conversations were the server's: clicking a buff shows nothing (its text is in its tooltip).
                apply("UIArenaCampaignViewModel.OnBuff1Click", () => Prefix(harmony, typeof(UIArenaCampaignViewModel), "OnBuff1Click", Type.EmptyTypes, "Skip"));
                apply("UIArenaCampaignViewModel.OnBuff2Click", () => Prefix(harmony, typeof(UIArenaCampaignViewModel), "OnBuff2Click", Type.EmptyTypes, "Skip"));
            }
            apply("StandaloneSession.PublishEvent", () => harmony.Patch(AccessTools.Method(typeof(Game.Shared.StandaloneSession), "PublishEvent"), prefix: new HarmonyMethod(typeof(Hooks), "Session_PublishEvent")));
            apply("AuthoritativeSessionBase.EndGame (campaign)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(AuthoritativeSessionBase), "EndGame", new[] { typeof(List<UID>), typeof(List<UID>), typeof(bool) });
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Session_EndGame"));
            });

            // Login screen: offline, no credentials are needed (button always enabled,
            // empty name => "Offline" profile).
            apply("UILoginDialogViewModel.CheckForLoginButtonEnable", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(UILoginDialogViewModel), "CheckForLoginButtonEnable");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "Login_AlwaysEnabled"));
            });

            // "Authoritative server" layer (StandaloneSession stubs).
            apply("StandaloneSession.SendPlayerOptions", () => Prefix(harmony, standaloneSession, "SendPlayerOptions", new[] { typeof(Player) }, "Session_SendPlayerOptions"));
            apply("StandaloneSession.Update", () =>
            {
                MethodInfo m = AccessTools.Method(standaloneSession, "Update", Type.EmptyTypes);
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "Session_Update"), postfix: new HarmonyMethod(typeof(Hooks), "Session_UpdatePost"));
            });
            apply("StandaloneSession.SubmitTransaction", () => Prefix(harmony, standaloneSession, "SubmitTransaction", new[] { typeof(Game.Shared.Mechanics.Transactions.Transaction) }, "Session_SubmitTransaction"));
            apply("StandaloneSession.DispatchSessionEvent(args)", () => Prefix(harmony, standaloneSession, "DispatchSessionEvent", new[] { typeof(SessionEventArgs) }, "Session_DispatchSessionEvent"));
            apply("StandaloneSession.DispatchSessionEvent(player,args)", () => Prefix(harmony, standaloneSession, "DispatchSessionEvent", new[] { typeof(Player), typeof(SessionEventArgs) }, "Session_DispatchSessionEventPlayer"));
            apply("StandaloneSession.SendPlayerInformationUpdates", () => Prefix(harmony, standaloneSession, "SendPlayerInformationUpdates", new[] { typeof(Game.Shared.Mechanics.ETurnPhases) }, "Session_SendPlayerInformationUpdates"));
            apply("StandaloneSession.SendPlayerOptionsFor(card)", () => Prefix(harmony, standaloneSession, "SendPlayerOptionsFor",
                new[] { typeof(Player), typeof(Card), typeof(Reckoning.Game.AbilityTemplate), typeof(Game.Shared.Mechanics.Abilities.AbilityInstance) }, "Session_SendPlayerOptionsFor"));
            apply("StandaloneSession.SendPlayerOptionsFor(list)", () => Prefix(harmony, standaloneSession, "SendPlayerOptionsFor",
                new[] { typeof(Player), typeof(List<Card>), typeof(List<Reckoning.Game.AbilityTemplate>), typeof(List<Game.Shared.Mechanics.Abilities.AbilityInstance>) }, "Session_SendPlayerOptionsForList"));
            apply("StandaloneSession.SendRevealedCards", () => Prefix(harmony, standaloneSession, "SendRevealedCards", new[] { typeof(Player), typeof(List<SessionCardId>) }, "Session_SendRevealedCards"));

            // Known UI NREs: the exception is swallowed (replaces WrapVoidInTryCatch).
            apply("GoChampionAbilityButton.SetStateFromAbilityTemplate", () => Finalizer(harmony, typeof(GoChampionAbilityButton), "SetStateFromAbilityTemplate"));
            apply("GoHUDPlayer.UpdateAbilityButtons", () => Finalizer(harmony, typeof(GoHUDPlayer), "UpdateAbilityButtons"));
            // Equipment tray of the battle HUD: the session sets the equipment while it loads the decks, before the battle
            // screen listens to EquipmentSet, so the tray stayed closed (the equipment itself works). Shown at game start.
            // Battleboard: the battle's scene (campaign, dungeons) or the arena's frozen lake, before the decks' boards.
            apply("UIBattle.Start (battleboard)", () =>
                harmony.Patch(AccessTools.Method(typeof(UIBattle), "Start"), postfix: new HarmonyMethod(typeof(Hooks), "UIBattle_Start")));
            apply("UIBattle.OnGameStarted (equipment tray)", () =>
                harmony.Patch(AccessTools.Method(typeof(UIBattle), "OnGameStarted"), postfix: new HarmonyMethod(typeof(Hooks), "UIBattle_OnGameStarted")));

            // Decks: a deck holding cards or equipment the account does not own is invalid (the original server's rule).
            apply("DeckValidator.RunValidation (ownership)", () =>
                harmony.Patch(AccessTools.Method(typeof(Game.Shared.Mechanics.DeckValidator), "RunValidation"), postfix: new HarmonyMethod(typeof(Hooks), "DeckValidator_Ownership")));

            // Battle: the developer ability the game gives every human player at the start of a battle when
            // GameConfig.ConnDevMode is on (Session.RunScenarioSetup), which the mod turns on for its local engine:
            // "You can see the cards in each opposing champion's hand". Not given.
            apply("AuthoritativeSessionBase.ActivateAbilityForPlayer (dev mode ability)", () =>
                harmony.Patch(AccessTools.Method(typeof(AuthoritativeSessionBase), "ActivateAbilityForPlayer"), prefix: new HarmonyMethod(typeof(Hooks), "Session_ActivateAbilityForPlayer")));

            // Campaign battles: the champion's party passives (the party list the campaign server gave the session).
            apply("AuthoritativeSessionBase.InstantiateChampionCard / InstantiateMercenaryCard (party passives)", () =>
            {
                Type[] sig = { typeof(Game.Shared.Domain.champion_bits), typeof(Dictionary<string, int>), typeof(List<ResourceId>), typeof(Game.Shared.Player) };
                harmony.Patch(AccessTools.Method(typeof(AuthoritativeSessionBase), "InstantiateChampionCard", sig), prefix: new HarmonyMethod(typeof(Hooks), "Session_PartyMembers"));
                harmony.Patch(AccessTools.Method(typeof(AuthoritativeSessionBase), "InstantiateMercenaryCard", sig), prefix: new HarmonyMethod(typeof(Hooks), "Session_PartyMembers"));
            });

            // Campaign battles: cards added to the champion's deck (the gnomes in tow in the desert).
            apply("AuthoritativeSessionBase.InstantiateDeck (campaign deck cards)", () =>
                harmony.Patch(AccessTools.Method(typeof(AuthoritativeSessionBase), "InstantiateDeck", new[] { typeof(Game.Shared.Domain.deck_bits), typeof(Game.Shared.Player) }),
                    postfix: new HarmonyMethod(typeof(Hooks), "Session_DeckExtras")));

            // Battle: a reveal window closed: the cards it showed from a deck go back to face down.
            apply("BattleStateCoverflow.OnHide (deck reveals)", () =>
                harmony.Patch(AccessTools.Method(typeof(BattleStateCoverflow), "OnHide"),
                    prefix: new HarmonyMethod(typeof(Hooks), "Coverflow_OnHidePre"), postfix: new HarmonyMethod(typeof(Hooks), "Coverflow_OnHidePost")));

            // Campaign map: the location window of the faction nodes (Node023 Saramago Crater, Node026 Johannes Bog).
            apply("UIDungeonZoneViewModel.CollectItemData (faction nodes)", () =>
                harmony.Patch(AccessTools.Method(typeof(UIDungeonZoneViewModel), "CollectItemData"), postfix: new HarmonyMethod(typeof(Hooks), "Zone_CollectItemData")));

            // Diagnostics: the Taming Sphere's rolls ("You have a 50% chance to Tame") and every Tame, so a player's
            // report ("the creature is voided but not counted") can be checked in the log.
            apply("AbilityVariableCondition.IsValid (taming log)", () =>
                harmony.Patch(AccessTools.Method(typeof(Game.Shared.Mechanics.Abilities.Conditions.AbilityVariableCondition), "IsValid", new[] { typeof(Game.Shared.Mechanics.Abilities.AbilityEffectInstance) }),
                    postfix: new HarmonyMethod(typeof(Hooks), "TameRoll_IsValid")));
            apply("TACAbilityEffectTemplate.Tame.Apply (taming log)", () =>
                harmony.Patch(AccessTools.Method(typeof(Game.Shared.Mechanics.Abilities.TACAbilityEffectTemplate.Tame), "Apply"),
                    postfix: new HarmonyMethod(typeof(Hooks), "Tame_Apply")));

            // Diagnostics: every rejected transaction (except priority passes) is logged with its cards.
            apply("Transaction.Validate (log)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Mechanics.Transactions.Transaction), "Validate");
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Hooks), "LogFailedTransaction"));
            });

            // Offline solo: no loss on the chess clock or for inactivity (45 min against the AI).
            apply("Player.IsChessTimerExpired", () => Prefix(harmony, typeof(Player), "IsChessTimerExpired", Type.EmptyTypes, "NeverExpired"));
            apply("Player.IsInactiveTimerExpired", () => Prefix(harmony, typeof(Player), "IsInactiveTimerExpired", Type.EmptyTypes, "NeverExpired"));

            // Network services (SFS) without a connection: Produce() calls pub(m_Publish) with null -> NRE.
            // Worst case: AuthoritativeSessionBase.EndGame calls MonitorService.SendBIInfo,
            // and the exception aborts the end of the game on every tick (champion dead but endless game).
            apply("Service<T>.Produce (offline)", () =>
            {
                int n = 0;
                foreach (Type t in typeof(Game.Client.Network.Monitor.MonitorService).Assembly.GetTypes())
                {
                    Type b = t.BaseType;
                    if (b == null || !b.IsGenericType || b.GetGenericTypeDefinition() != typeof(Game.Shared.Network.SFS.Service<>))
                        continue;
                    MethodInfo m = AccessTools.Method(b, "Produce");
                    if (m == null) continue;
                    try { harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "Service_Produce")); n++; }
                    catch (Exception e) { Log.LogWarning("Produce " + t.Name + " : " + e.Message); }
                }
                Log.LogInfo("Service.Produce disabled on " + n + " service(s)");
            });

            // Network probe: logs what the client tries to send to the server (HexRevival.net.log).
            apply("ClientConnection.SendCommand (probe)", () =>
            {
                MethodInfo m = AccessTools.Method(typeof(Game.Shared.Network.SFS.ClientConnection), "SendCommand",
                    new[] { typeof(byte[]), typeof(string), typeof(string), typeof(long), typeof(Game.Shared.Network.MessageHandler), typeof(System.Collections.Generic.IDictionary<string, object>) });
                if (m == null) throw new MissingMethodException("ClientConnection.SendCommand");
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Hooks), "ClientConnection_SendCommand"));
            });

            // Logging out (Switch account): the game's network session, never connected offline, terminates its
            // connector again and again (Session.onConnectorState in LOGOUT -> Connector.Terminate -> DISCONNECTED ->
            // onConnectorState...) until a stack overflow, which left the logout half done: dim screen and an endless
            // loading wheel. A Terminate already running is not started again.
            // The game's connection to the official servers (closed since 2019) retries every 30 seconds: its messages
            // made most of the log (see LogFilter).
            // Cards without art (choices such as "Pain" and "Suffering") drawn with the art of the card linking to them.
            apply("GoCardBuilder.UpdateArtOverride", () =>
                harmony.Patch(AccessTools.Method(typeof(GoCardBuilder), "UpdateArtOverride"), postfix: new HarmonyMethod(typeof(Hooks), "GoCardBuilder_UpdateArtOverride")));
            apply("LogBase.Debug/Error (dead server noise)", () =>
            {
                harmony.Patch(AccessTools.Method(typeof(LogBase), "Debug", new[] { typeof(string), typeof(string), typeof(object[]) }),
                    prefix: new HarmonyMethod(typeof(Hooks), "LogBase_Debug"));
                harmony.Patch(AccessTools.Method(typeof(LogBase), "Error", new[] { typeof(string), typeof(string), typeof(object[]) }),
                    prefix: new HarmonyMethod(typeof(Hooks), "LogBase_Error"));
            });
            apply("Connector.Terminate (logout recursion)", () =>
                harmony.Patch(AccessTools.Method(typeof(Game.Shared.Network.HConnect.Connector), "Terminate"),
                    prefix: new HarmonyMethod(typeof(Hooks), "Connector_TerminateEnter"),
                    finalizer: new HarmonyMethod(typeof(Hooks), "Connector_TerminateExit")));

            // Online: depth limit on the opponent's data (see NetCodec.DecodeEnter).
            apply("ObjFmt.Decoder.Decode (depth limit)", () =>
                harmony.Patch(AccessTools.Method(typeof(Game.Shared.Network.ObjFmt.Decoder), "Decode", new[] { typeof(System.IO.Stream), typeof(object), typeof(bool), typeof(Type), typeof(object) }),
                    prefix: new HarmonyMethod(typeof(Hooks), "Decoder_DecodeEnter"),
                    finalizer: new HarmonyMethod(typeof(Hooks), "Decoder_DecodeExit")));

            // Online: the players' names in the battle instead of their champions' (see OnlineMatch).
            apply("UIBattle.OnChampionCardPlayed", () =>
                harmony.Patch(AccessTools.Method(typeof(UIBattle), "OnChampionCardPlayed"), postfix: new HarmonyMethod(typeof(Hooks), "Battle_ChampionCardPlayed")));
            // Battlegrounds draft: the RECORD boxes in the order the matches were played (see Battlegrounds.ShowRecord).
            apply("UIDeckBuilderVM.SetupTournamentUI", () =>
                harmony.Patch(AccessTools.Method(typeof(DeckBuilder.UIDeckBuilderVM), "SetupTournamentUI"), postfix: new HarmonyMethod(typeof(Hooks), "DeckBuilder_Record")));
            apply("UIDeckBuilderVM.OnAsynchPlayerUpdated", () =>
                harmony.Patch(AccessTools.Method(typeof(DeckBuilder.UIDeckBuilderVM), "OnAsynchPlayerUpdated"), postfix: new HarmonyMethod(typeof(Hooks), "DeckBuilder_Record")));
            apply("UITournamentDeckEditorViewModel.Initialize", () =>
                harmony.Patch(AccessTools.Method(typeof(UITournamentDeckEditorViewModel), "Initialize"), postfix: new HarmonyMethod(typeof(Hooks), "DeckEditor_Record")));
            apply("UITournamentDeckEditorViewModel.OnAsynchPlayerUpdated", () =>
                harmony.Patch(AccessTools.Method(typeof(UITournamentDeckEditorViewModel), "OnAsynchPlayerUpdated"), postfix: new HarmonyMethod(typeof(Hooks), "DeckEditor_Record")));
            // Breaking down and crafting on the zoomed card, next to "Unlock extended art" (see CraftUi).
            apply("UIExtendedArtViewModel.ShowExtendedArtUI", () =>
                harmony.Patch(AccessTools.Method(typeof(UIExtendedArtViewModel), "ShowExtendedArtUI"), postfix: new HarmonyMethod(typeof(Hooks), "ExtendedArt_Show")));
            apply("UIExtendedArtViewModel.HideExtendedArtUI", () =>
                harmony.Patch(AccessTools.Method(typeof(UIExtendedArtViewModel), "HideExtendedArtUI"), postfix: new HarmonyMethod(typeof(Hooks), "ExtendedArt_Hide")));
            // Shards (not the basic ones): the game shows no extended art panel for resources; ours, without "Unlock".
            apply("UICardStackPagingViewModel.SetSelectedStack", () =>
                harmony.Patch(AccessTools.Method(typeof(UICardStackPagingViewModel), "SetSelectedStack",
                    new[] { typeof(UICardContainerContext), typeof(DeckCache), typeof(bool), typeof(bool), typeof(ulong) }),
                    postfix: new HarmonyMethod(typeof(Hooks), "CardStack_Shown")));
            apply("UICardStackPagingViewModel.SetCurrentCard", () =>
                harmony.Patch(AccessTools.Method(typeof(UICardStackPagingViewModel), "SetCurrentCard"), postfix: new HarmonyMethod(typeof(Hooks), "CardStack_Shown")));
            apply("GoInventoryIcon.SetIconFromData", () =>
                harmony.Patch(AccessTools.Method(typeof(GoInventoryIcon), "SetIconFromData"), postfix: new HarmonyMethod(typeof(Hooks), "InventoryIcon_Set")));
            apply("UIPlayerEquipmentViewModel.OnItemRightClick", () =>
                harmony.Patch(AccessTools.Method(typeof(UIPlayerEquipmentViewModel), "OnItemRightClick"), postfix: new HarmonyMethod(typeof(Hooks), "Equipment_RightClick")));
            // The top bar's Auction House button ([UI] HideAuctionHouse): after the game sets its visibility.
            apply("UIGameMenu.UpdateButtonVisibility", () =>
                harmony.Patch(AccessTools.Method(typeof(UIGameMenu), "UpdateButtonVisibility"), postfix: new HarmonyMethod(typeof(Hooks), "GameMenu_Buttons")));
            // Featured Content of the main menu: the mod's panels instead of the HEX website's news (see Community).
            apply("UINewsEventsViewModel.Start", () =>
                harmony.Patch(AccessTools.Method(typeof(UINewsEventsViewModel), "Start"), prefix: new HarmonyMethod(typeof(Hooks), "News_Start")));
            apply("UINewsEventsViewModel.GetNewsAndEvents", () => Prefix(harmony, typeof(UINewsEventsViewModel), "GetNewsAndEvents", Type.EmptyTypes, "News_Get"));

            Log.LogInfo("HexRevival loaded: " + ok + " patches OK, " + failed + " failed.");
        }

        private static void Prefix(Harmony h, Type type, string method, Type[] args, string hook)
        {
            MethodInfo target = AccessTools.Method(type, method, args);
            if (target == null)
                throw new MissingMethodException(type.FullName + "." + method);
            h.Patch(target, prefix: new HarmonyMethod(typeof(Hooks), hook));
        }

        private static void Finalizer(Harmony h, Type type, string method)
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null)
                throw new MissingMethodException(type.FullName + "." + method);
            h.Patch(target, finalizer: new HarmonyMethod(typeof(Hooks), "SwallowException"));
        }
    }

    /// <summary>
    /// Harmony prefixes. Convention: returning false = the original is not executed,
    /// the existing replacement method (Patches / Server) runs instead.
    /// Arguments are taken by position (__0, __1…) so as not to depend on names.
    /// </summary>
    internal static class Hooks
    {
        private static bool s_Initialized;

        public static void Main_FinializeInit()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            Bootstrap.Init();
        }

        public static bool Online_JoinSession(StandaloneGameClient __instance)
        {
            if (!OnlineMatch.IsGuest) return true;
            try { OnlineMatch.GuestJoinSession(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Online join: " + e); }
            return false;
        }

        private static FieldInfo s_SyncQueue, s_Responded;

        /// <summary>
        /// A packet from the server answers the last transaction as soon as the game starts handling it. The game only
        /// marked it after all the packet's events, so an AI answering one of them (a triggered ability's targets) was
        /// refused as "still waiting for the server" and the game stopped.
        /// </summary>
        public static void Session_PumpSync(Game.Client.SessionClient __instance)
        {
            if (s_SyncQueue == null)
            {
                s_SyncQueue = AccessTools.Field(typeof(Game.Client.SessionClient), "m_SyncEventsQueue");
                s_Responded = AccessTools.Field(typeof(ClientSessionBase), "m_HasPreviousTransactionBeenRespondedByServer");
            }
            System.Collections.ICollection q = s_SyncQueue.GetValue(__instance) as System.Collections.ICollection;
            if (q != null && q.Count > 0) s_Responded.SetValue(__instance, true);
        }

        public static bool Online_ReadyToStart() { return !OnlineMatch.IsGuest; }

        private static FieldInfo s_SessionServer;

        public static bool Online_SessionTransaction(StandaloneGameClient __instance, Game.Shared.Mechanics.Transactions.Transaction __0)
        {
            if (!OnlineMatch.IsGuest)
            {
                // Right after an online game the guest has no session to send to.
                if (s_SessionServer == null) s_SessionServer = AccessTools.Field(typeof(StandaloneGameClient), "m_SessionServer");
                return s_SessionServer.GetValue(__instance) != null;
            }
            try { OnlineMatch.SendTransaction(__0); }
            catch (Exception e) { Plugin.Log.LogError("Online transaction: " + e); }
            return false;
        }

        public static bool Steamy_IsRunningSteam(ref bool __result)
        {
            __result = Patches.Steamy_IsRunningSteam();
            return false;
        }

        public static bool PlayerProfile_Login(PlayerProfile __instance, string __0, string __1, string __2,
            ref string __3, ref Dictionary<string, object> __4, ref bool __result)
        {
            __result = Patches.PlayerProfile_Login(__instance, __0, __1, __2, ref __3, ref __4);
            return false;
        }

        private static bool s_IntroSkipped;

        public static bool LevelLoader_SkipIntro(LevelLoader __instance)
        {
            if (s_IntroSkipped) return true;
            s_IntroSkipped = true;
            try
            {
                if (__instance.m_mediaPlayer != null && __instance.m_mediaPlayer.Control != null)
                    __instance.m_mediaPlayer.Control.Stop();
                MethodInfo m = typeof(LevelLoader).GetMethod("StartLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                __instance.StartCoroutine((System.Collections.IEnumerator)m.Invoke(__instance, null));
                Plugin.Log.LogInfo("Intro skipped");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Intro : " + e.Message); }
            return false;
        }

        public static void Player_IsAI(Player __instance, ref bool __result)
        {
            if (!__result && __instance.m_PlayerId.GetUIDType() == UID.Type.AIPlayer) __result = true;
        }

        public static void GoCardBuilder_UpdateArtOverride(GoCardBuilder __instance) { ChoiceArt.AfterUpdate(__instance); }
        public static bool LogBase_Debug(string __0, string __1, object[] __2) { return !LogFilter.DropDebug(__1, __2); }
        public static bool LogBase_Error(string __0, string __1) { return !LogFilter.DropError(__0, __1); }

        public static void Session_AddPlayer(Session __instance, Player __0)
        {
            Patches.AuthoritativeSession_AddPlayer(__instance, __0);
        }

        public static bool StandaloneGameClient_SendStartSession(StandaloneGameClient __instance, string __0, int __3, int __5)
        {
            return Patches.StandaloneGameClient_SendStartSession(__instance, __0, __3, __5);
        }

        public static bool StandaloneGameClient_SendStartEncounter(StandaloneGameClient __instance, string __0, SessionStateEncounterData __1)
        {
            Patches.StandaloneGameClient_SendStartEncounter(__instance, __0, __1);
            return false;
        }

        // --- Local campaign service (see Campaign.cs) ---
        /// <summary>
        /// Runs a campaign request locally and never lets the original (network) method run; an exception
        /// is logged with the request's name (the client then keeps waiting for that answer).
        /// </summary>
        private static bool CampaignGuard(string name, Action a)
        {
            try { a(); }
            catch (Exception e) { UnityEngine.Debug.LogError("[HexRevival] [Campaign] " + name + ": " + e); }
            return false;
        }

        public static bool Campaign_QueryCurrentCampaign(ulong __0, ClientCampaignManager.StateCallback __1) { return CampaignGuard("QueryCurrentCampaign", () => Campaign.QueryCurrentCampaign(__0, __1)); }
        public static bool Campaign_GetSummary(IEnumerable<ulong> __0, ClientCampaignManager.GetSummaryCallback __1) { return CampaignGuard("GetSummary", () => Campaign.GetSummary(__0, __1)); }
        public static bool Campaign_GetActiveStatus(ulong __0, CampaignType __1, string __2, ClientCampaignManager.GetActiveStatusCallback __3) { return CampaignGuard("GetActiveStatus", () => Campaign.GetActiveStatus(__0, __1, __2, __3)); }
        public static bool Campaign_QueryState(ulong __0, ClientCampaignManager.StateCallback __1) { return CampaignGuard("QueryState", () => Campaign.QueryState(__0, __1)); }
        public static bool Campaign_Start(ulong __0, ClientCampaignManager.StateCallback __3) { return CampaignGuard("Start", () => Campaign.Start(__0, __3)); }
        public static bool Campaign_Create(ulong __0, string __1, ClientCampaignManager.StateCallback __2) { return CampaignGuard("Create", () => Campaign.Create(__0, __1, __2)); }
        public static bool Campaign_Forfeit(ulong __0, ClientCampaignManager.StateCallback __1) { return CampaignGuard("Forfeit", () => Campaign.Forfeit(__0, __1)); }
        public static bool Campaign_SendEvent(ulong __0, string __1, object[] __2, ClientCampaignManager.StateCallback __3) { return CampaignGuard("SendEvent", () => Campaign.SendEvent(__0, __1, __2, __3)); }
        public static bool Campaign_StartLocation(ulong __0, string __1, ClientCampaignManager.StateCallback __3) { return CampaignGuard("StartLocation", () => Campaign.StartLocation(__0, __1, __3)); }
        public static bool Campaign_FinishLocation(ulong __0, string __1, ClientCampaignManager.StateCallback __3) { return CampaignGuard("FinishLocation", () => Campaign.FinishLocation(__0, __1, __3)); }
        public static bool Campaign_GetTemplateTypeInfo(ClientCampaignManager.GetTemplateTypeInfoCallback __0) { if (__0 != null) __0(new TemplateTypeInfo[0]); return false; }
        public static bool Store_GetStoreItems(Game.Client.Network.Escrow.EscrowService.GetStoreItems.ResponseHandler __0) { Store.GetStoreItems(__0); return false; }
        public static bool Store_GetStoreItemsEvent() { Store.GetStoreItems(null); return false; }
        public static bool Store_PurchaseItemEvent(int __0, ulong __1) { Store.PurchaseItem(__0, __1, null); return false; }
        public static bool ExtendedArt_Upgrade(ulong __1, Game.Client.Network.Profile.ProfileService.UpgradeExtendedArtOnCard.ResponseHandler __2) { ExtendedArt.Upgrade(__1, __2); return false; }
        public static bool Redeem_Request(string __0, Game.Client.Network.Escrow.EscrowService.RedeemCode.ResponseHandler __1) { HexRevival.Redeem.Request(__0, __1); return false; }
        public static bool Redeem_RequestEvent(string __0) { HexRevival.Redeem.Request(__0, null); return false; }
        public static bool Store_PurchaseItem(int __0, ulong __1, Game.Client.Network.Escrow.EscrowService.PurchaseItem.ResponseHandler __3) { Store.PurchaseItem(__0, __1, __3); return false; }
        public static void Tournament_OnEntry() { Battlegrounds.OnHubEntered(); }
        public static bool Tournament_TestEntry(ulong __0, Game.Client.Network.Tournaments.TournamentsService.TestTournamentEntry.ResponseHandler __2) { Battlegrounds.TestEntry(__0, __2); return false; }
        public static bool Tournament_Enter(ulong __0, UID __1, Game.Client.Network.Tournaments.TournamentsService.EnterTournament.ResponseHandler __6) { Battlegrounds.Enter(__0, __1, __6); return false; }
        public static bool Tournament_SaveDeck(Game.Shared.Domain.deck_bits __0, ulong __2, Game.Client.Network.Tournaments.TournamentsService.SaveTournamentDeck.ResponseHandler __3) { Battlegrounds.SaveDeck(__0, __2, __3); return false; }
        public static bool Tournament_GameEntrance(ulong __0, Game.Shared.Domain.deck_bits __1, bool __2, Game.Client.Network.Tournaments.TournamentsService.GameEntrance.ResponseHandler __3) { Battlegrounds.GameEntrance(__0, __1, __2, __3); return false; }
        public static bool Tournament_Leave(ulong __0, Game.Client.Network.Tournaments.TournamentsService.LeaveTournament.ResponseHandler __1) { Battlegrounds.Leave(__0, __1); return false; }
        public static bool Tournament_Reconnect(ulong __0) { Battlegrounds.Resume(__0); return false; }
        public static bool Tournament_SelectDraftCard(ulong __0, ulong __1, ulong __2) { Battlegrounds.SelectDraftCard(__0, __1, __2); return false; }
        public static bool Skip() { return false; }
        public static bool Arena_Join(Game.Client.Network.Campaign.CampaignService.JoinCampaignArena.ResponseHandler __1) { Arena.Join(__1); return false; }
        public static bool Arena_MasterList(Game.Client.Network.Campaign.CampaignService.GetMasterListOfChallengers.ResponseHandler __1) { Arena.MasterList(__1); return false; }
        public static bool Arena_AssignDeck(ulong __0, Game.Client.Network.Campaign.CampaignService.AssignArenaDeck.ResponseHandler __2) { Arena.AssignDeck(__0, __2); return false; }
        public static bool Arena_CashOut(Game.Client.Network.Campaign.CampaignService.DoArenaCashOut.ResponseHandler __1) { Arena.CashOut(__1); return false; }
        public static bool Arena_Destroy(Game.Client.Network.Campaign.CampaignService.DestroyArenaData.ResponseHandler __1) { Arena.Destroy(__1); return false; }
        public static bool Arena_Refresh(Game.Client.Network.Campaign.CampaignService.RefreshArenaInfo.ResponseHandler __3) { Arena.Refresh(__3); return false; }
        public static void Arena_Start(UIArenaCampaignViewModel __instance) { Arena.OnScreenStart(__instance); }
        public static void Icons_GetAsset(AssetBundleManager __instance, AssetBundleFile __0, string __1, string __2, AssetType __3, ref UnityEngine.Object __result) { if (__result == null || StoreIcons.IsPlaceholder(__0, __2)) { UnityEngine.Object r = StoreIcons.Missing(__instance, __0, __1, __2, __3); if (r != null) __result = r; } }
        public static void Arena_ChampionHud(UIArenaCampaignContext __instance, DeckCache __0) { Arena.OnAssignChampionHud(__instance, __0); }
        public static bool Arena_Battle() { Arena.Battle(); return false; }
        public static void Gems_Available(UISocketCardViewModel __instance) { Gems.BeforeAvailableGems(__instance); }
        public static void Localization_Get(string key, ref string __result) { if (key != null && key.StartsWith("hexrevival-")) { string t = DuelingPit.Localize(key); if (t != null) __result = t; } }
        public static bool Tournament_Join(UITournamentPendingViewModel __instance) { return DuelingPit.OnJoin(__instance, null); }
        public static bool Tournament_DoubleClick(UITournamentPendingViewModel __instance, UITournamentDescContext __0) { return DuelingPit.OnJoin(__instance, __0); }
        public static void Landing_Awake(UILandingPageViewModel __instance) { Community.OnLandingAwake(__instance); }
        public static void Store_PackItem(UIPremiumStoreItemContext __instance, Reckoning.Game.InventoryCardPack __0) { StoreIcons.OnPackItem(__instance, __0); }
        public static void Store_ChestItem(UIPremiumStoreItemContext __instance, Reckoning.Game.InventoryTreasureChest __0) { StoreIcons.OnChestItem(__instance, __0); }
        public static void Store_3DLoaded(StoreItem3DController __instance) { StoreIcons.On3DItemLoaded(__instance); }
        public static bool DuelingPit_FindMatch(DuelingPitVM __instance) { return DuelingPit.FindMatch(__instance); }

        public static bool Ladder_Record(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderRecord.Response>> __0)
        {
            Campaign.Enqueue(() => __0(new Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderRecord.Response> { Message = Battlegrounds.LadderRecordResponse() }));
            return false;
        }
        public static bool Ladder_TierRewards(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderTierRewardsList.Response>> __0)
        {
            Campaign.Enqueue(() => __0(new Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderTierRewardsList.Response>
                { Message = new Game.Shared.Tournaments.Messages.Tournament.LadderTierRewardsList.Response { tier_rewards = new Dictionary<uint, Game.Shared.Tournaments.TournamentRewardInfo>() } }));
            return false;
        }
        public static bool Ladder_Top100(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderRankingList.Response>> __0)
        {
            Campaign.Enqueue(() => __0(new Game.Shared.Network.CustomNetworkResponse<Game.Shared.Tournaments.Messages.Tournament.LadderRankingList.Response>
                { Message = new Game.Shared.Tournaments.Messages.Tournament.LadderRankingList.Response { ConstructedLadderRanking = new List<string>(), LimitedLadderRanking = new List<string>() } }));
            return false;
        }
        public static bool Ladder_FindMatch(ulong __0, Game.Shared.Domain.deck_bits __1, bool __2) { Battlegrounds.LadderFindMatch(__0, __1, __2); return false; }
        /// <summary>Input.GetAxis, 0 while the game window does not have the focus.</summary>
        public static float FocusedAxis(string axis) { float v = UnityEngine.Input.GetAxis(axis); return v == 0f || HexRevival.WindowFocus.IsActive() ? v : 0f; }
        public static IEnumerable<CodeInstruction> FocusedAxisTranspiler(IEnumerable<CodeInstruction> code)
        {
            MethodInfo getAxis = AccessTools.Method(typeof(UnityEngine.Input), "GetAxis", new[] { typeof(string) });
            MethodInfo focused = AccessTools.Method(typeof(Hooks), "FocusedAxis");
            foreach (CodeInstruction ci in code)
            {
                if ((ci.opcode == System.Reflection.Emit.OpCodes.Call) && Equals(ci.operand, getAxis)) ci.operand = focused;
                yield return ci;
            }
        }
        public static void Session_PublishEvent(string __0, object __1) { Campaign.OnPublishEvent(__0, __1); }
        public static bool Campaign_SaveChampionParty(Game.Shared.Campaign.Messages.CampSysGeneral.Party.ChampionParty __0, ClientCampaignManager.PartyMembersCallback __1)
        { Mercenaries.OnSaveParty(__0, __1); return false; }
        public static bool Profile_DeckTemplateSave(Game.Shared.Profile.ProfileDeckTemplate __2, ulong? __3,
            Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Profile.Network.Response>> __4, ref IAsyncResult __result)
        { __result = null; DeckTemplates.OnSaveRequest(__2, __3, __4); return false; }
        public static bool Profile_AccountXpState(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Profile.Network.Response>> __2, ref IAsyncResult __result)
        { __result = null; AccountRewards.OnState(__2); return false; }
        public static bool Profile_AccountXpClaim(Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Profile.Network.Response>> __2, ref IAsyncResult __result)
        { __result = null; AccountRewards.OnClaim(__2); return false; }
        public static bool Siege_IssueRequest(byte[] __3, Action<Game.Shared.Network.CustomNetworkResponse<Game.Shared.Campaign.Siege.Messaging.Response>> __5, ref IAsyncResult __result)
        { __result = null; Siege.OnRequest(__3, __5); return false; }
        public static void CoinToss_TurnOff() { Tutorial.OnRoundStarted(); }
        public static void SystemMenu_Awake(UISystemMenuViewModel __instance) { Tutorial.OnSystemMenu(__instance); }
        public static bool AITactical_AIUpdate(Game.Shared.AI.AITactical __instance, Game.Shared.Mechanics.ETurnPhases __0, ref bool __result) { if (Tutorial.DevStallAI) { Tutorial.DevStallAI = false; UnityEngine.Debug.Log("[HexRevival] [Tutorial] dev: AI update swallowed"); __result = true; return false; } Tutorial.SkipImpossiblePlays(__instance); return !Tutorial.AIDamageOrder(__instance, __0, ref __result); }
        public static void TutorialRewards_Launch(System.Collections.Generic.IEnumerable<Game.Shared.ResourceId> __0) { Tutorial.OnRewards(__0); }
        public static void TutorialManager_CtorPre(Game.Shared.ClientSessionBase __0) { Tutorial.BeforeScriptCopy(__0); }
        public static void TutorialManager_CtorPost(Game.Shared.Tutorial.TutorialManager __instance) { Tutorial.AfterScriptCopy(__instance); }
        public static void Player_HasStop(Game.Shared.Player __instance, Game.Shared.Player __0, Game.Shared.Mechanics.ETurnPhases __1, ref bool __result) { if (!__result) __result = Tutorial.ScriptStop(__instance, __0, __1); }
        public static void Trace_LocalPass(Game.Shared.LocalPlayer __instance) { if (Tutorial.TracePass) Tutorial.OnPass("PassPriority", __instance.m_PlayerId); }
        public static void Trace_AutoPass(Game.Shared.LocalPlayer __instance, Game.Shared.Mechanics.EPassingState __0) { if (Tutorial.TracePass) Tutorial.OnPass("SendAutoPass " + __0, __instance.m_PlayerId); }
        public static void Tutorial_BoardSetup(bool __result) { if (__result) Tutorial.OnBoardReset(); }
        public static void Session_EndGame(List<UID> __0) { Progression.OnMatchEnded(__0); }

        [ThreadStatic] private static int s_Terminating;

        public static bool Connector_TerminateEnter(ref bool __state)
        {
            __state = s_Terminating > 0;
            if (__state) return false;
            s_Terminating++;
            return true;
        }

        public static void Decoder_DecodeEnter(Game.Shared.Network.ObjFmt.Decoder __instance, ref bool __state) { HexRevival.NetCodec.DecodeEnter(__instance, ref __state); }
        public static Exception Decoder_DecodeExit(bool __state, Exception __exception) { HexRevival.NetCodec.DecodeExit(__state); return __exception; }

        public static void DeckValidator_Ownership(Game.Shared.Mechanics.DeckValidator __instance)
        {
            try
            {
                Game.Shared.Domain.deck_bits deck = AccessTools.Field(typeof(Game.Shared.Mechanics.DeckValidator), "m_Deck").GetValue(__instance) as Game.Shared.Domain.deck_bits;
                System.Collections.Generic.List<string> missing = HexRevival.Decks.NotOwned(deck);
                if (missing.Count == 0) return;
                AccessTools.Method(typeof(Game.Shared.Mechanics.DeckValidator), "AddError").Invoke(__instance, new object[] { HexRevival.Loc.Get("deck_not_owned", string.Join(", ", missing.ToArray())), null, false });
                AccessTools.Field(typeof(Game.Shared.Mechanics.DeckValidator), "m_IsValid").SetValue(__instance, false);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] deck ownership: " + e.Message); }
        }

        private static readonly ResourceId s_DevSeeHandAbility = new ResourceId("9642c879-a112-adae-1c2c-66aa9fcc63dc");

        public static void Session_PartyMembers(Game.Shared.Domain.champion_bits championBits, ref List<ResourceId> partyMembers, Game.Shared.Player player)
        {
            try
            {
                if (partyMembers != null || championBits == null || player == null || player.IsAI()) return;
                if (HexRevival.Campaign.BattleChampion == 0 || HexRevival.Campaign.BattleChampion != championBits.Id) return;
                partyMembers = HexRevival.Mercenaries.PartyTemplateIds(championBits.Id);
                if (partyMembers != null) UnityEngine.Debug.Log("[HexRevival] [Mercenaries] party passives of " + partyMembers.Count + " member(s)");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] party passives: " + e.Message); }
        }

        public static void Session_DeckExtras(AuthoritativeSessionBase __instance, Game.Shared.Player player, List<Game.Shared.Mechanics.Card> __result)
        {
            try
            {
                if (__result == null || player == null || player.IsAI() || HexRevival.Campaign.BattleChampion == 0) return;
                List<string> extras = HexRevival.Campaign.TakeDeckExtras();
                foreach (string name in extras)
                {
                    Reckoning.Game.CardTemplate t = HexRevival.Opponents.CardByName(name);
                    Game.Shared.Mechanics.Card c = t != null ? __instance.InternalCreateCard(t, false, player) : null;
                    if (c != null) __result.Add(c);
                    else UnityEngine.Debug.LogWarning("[HexRevival] campaign deck card not created: " + name);
                }
                if (extras.Count > 0) UnityEngine.Debug.Log("[HexRevival] [Campaign] " + extras.Count + " card(s) added to the deck");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] campaign deck cards: " + e.Message); }
        }

        public static bool Session_ActivateAbilityForPlayer(ResourceId ability)
        {
            return ability != s_DevSeeHandAbility;
        }

        public static void Coverflow_OnHidePre(BattleStateCoverflow __instance, ref System.Collections.Generic.List<Game.Shared.SessionCardId> __state)
        {
            try
            {
                if ((bool)AccessTools.Field(typeof(BattleStateCoverflow), "m_UserInitiated").GetValue(__instance)) return;
                System.Collections.Generic.List<GoCardView> views = AccessTools.Field(typeof(BattleStateCoverflow), "m_CardViews").GetValue(__instance) as System.Collections.Generic.List<GoCardView>;
                if (views == null) return;
                __state = new System.Collections.Generic.List<Game.Shared.SessionCardId>();
                foreach (GoCardView v in views)
                    if (v != null && v.Card != null) __state.Add(v.Card.SessionId);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] reveal window: " + e.Message); }
        }

        public static void Coverflow_OnHidePost(System.Collections.Generic.List<Game.Shared.SessionCardId> __state)
        {
            try
            {
                if (__state == null || __state.Count == 0 || UIBattle.Instance == null || UIBattle.Instance.Player == null) return;
                if (HexRevival.OnlineMatch.IsGuest) HexRevival.OnlineMatch.SendRevealClosed(__state);
                else HexRevival.Server.RevealWindowClosed(__state, UIBattle.Instance.Player.m_PlayerId);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] reveal window: " + e.Message); }
        }

        public static void Zone_CollectItemData(UIDungeonZoneViewModel __instance)
        {
            try
            {
                FieldInfo f = AccessTools.Field(typeof(UIDungeonZoneViewModel), "m_CampaignItemData");
                HexRevival.Campaign.FactionItemData(f != null ? f.GetValue(__instance) as System.Collections.Generic.Dictionary<string, Reckoning.Game.DungeonItemData> : null);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] faction nodes: " + e.Message); }
        }

        public static void TameRoll_IsValid(Game.Shared.Mechanics.Abilities.Conditions.AbilityVariableCondition __instance, Game.Shared.Mechanics.Abilities.AbilityEffectInstance effectInstance, bool __result)
        {
            try
            {
                Game.Shared.Mechanics.Abilities.AbilityInstance ai = effectInstance != null ? effectInstance.m_AbilityInstance : null;
                if (ai == null || ai.m_AbilityTemplate == null || __instance.m_Lhs != "RandomNumber" || ai.m_AbilityTemplate.Name.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) < 0) return;
                int v; ai.TryGetAbilityVariable("RandomNumber", out v, false);
                UnityEngine.Debug.Log("[HexRevival] [Taming] roll " + v + " " + __instance.m_ComparisonOp + " " + __instance.m_Rhs + " -> " + (__result ? "tame" : "no tame") + " (ability " + ai.m_AbilityInstanceId + ")");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] [Taming] roll: " + e.Message); }
        }

        public static void Tame_Apply(Game.Shared.Mechanics.Card targetCard, Game.Shared.Mechanics.Abilities.AbilityEffectInstance effectInstance)
        {
            try
            {
                Game.Shared.Player p = effectInstance != null && effectInstance.m_AbilityInstance != null ? effectInstance.m_AbilityInstance.m_ResponsiblePlayer : null;
                Game.Shared.Mechanics.TACList l = p != null && p.m_ChampionCard != null ? p.m_ChampionCard.Get(Game.Shared.Mechanics.TACAttrs.PermanentData, Game.Shared.Mechanics.ListAttrs.TamedCards) : null;
                UnityEngine.Debug.Log("[HexRevival] [Taming] Tame " + (targetCard != null ? targetCard.m_SessionCardId + " " + targetCard.m_CardTemplate.m_Name + " now in " + targetCard.m_CurrentCardCollection : "null")
                    + ", tamed list " + (l != null ? l.Count : 0) + " (ability " + (effectInstance != null && effectInstance.m_AbilityInstance != null ? effectInstance.m_AbilityInstance.m_AbilityInstanceId : 0) + ")");
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] [Taming] tame: " + e.Message); }
        }

        public static void UIBattle_Start(UIBattle __instance) { Boards.OnBattleStart(__instance); }

        public static void ExtendedArt_Show(UIExtendedArtViewModel __instance, ICard __0) { CraftUi.OnShow(__instance, __0); }
        public static void ExtendedArt_Hide() { CraftUi.OnHide(); }
        public static void CardStack_Shown(UICardStackPagingViewModel __instance) { CraftUi.OnCardShown(__instance); }
        public static void Equipment_RightClick(object __0) { CraftUi.OnEquipmentRightClick(__0); }
        public static void InventoryIcon_Set(GoInventoryIcon __instance, Reckoning.Game.InventoryItemData __0) { Crafting.OnIcon(__instance, __0); }

        /// <summary>BepInEx config [UI] HideAuctionHouse.</summary>
        public static bool HideAuctionHouse = true;
        public static void GameMenu_Buttons(UIGameMenu __instance)
        {
            if (HideAuctionHouse && __instance != null && __instance.Context != null) __instance.Context.AuctionButtonVisible = false;
        }
        public static void UIBattle_OnGameStarted(UIBattle __instance)
        {
            try
            {
                UnityEngine.Debug.Log("[HexRevival] equipment tray: game started, " + (__instance.Players != null ? __instance.Players.Length : 0) + " player(s)");
                if (__instance.Players == null) return;
                foreach (Game.Shared.Player p in __instance.Players)
                {
                    if (p == null || p.m_Equipment == null || p.m_Equipment.Count == 0) continue;
                    BattleScenePlayerObjects o = __instance.GetPlayerSceneObjects(p);
                    if (o != null && o.PlayerHUD != null) o.PlayerHUD.SetActiveEquipmentForPlayer(p);
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[HexRevival] equipment tray: " + e.Message); }
        }

        public static Exception Connector_TerminateExit(bool __state, Exception __exception)
        {
            if (!__state) s_Terminating--;
            return __exception;
        }

        public static void Battle_ChampionCardPlayed(UIBattle __instance, ChampionCardPlayedSessionEventArgs __0) { OnlineMatch.ShowPlayerNames(__instance, __0); }

        public static void DeckEditor_Record(UITournamentDeckEditorViewModel __instance)
        {
            try { HexRevival.Battlegrounds.ShowRecord(Traverse.Create(__instance).Field("m_Context").GetValue<UITournamentDeckEditorContext>()); }
            catch (Exception e) { Plugin.Log.LogError("Gauntlet record: " + e); }
        }

        public static void DeckBuilder_Record(DeckBuilder.UIDeckBuilderVM __instance)
        {
            try { Battlegrounds.ShowRecord(Traverse.Create(__instance).Field("m_Context").Property("Tournament").GetValue<DeckBuilder.UITournamentContext>()); }
            catch (Exception e) { Plugin.Log.LogError("Draft record: " + e); }
        }

        /// <summary>A URL is needed for the carousel to ask for its panels (the game's config has none offline).</summary>
        public static void News_Start(UINewsEventsViewModel __instance)
        {
            if (string.IsNullOrEmpty(__instance.m_NewsEventsURL)) __instance.m_NewsEventsURL = Community.RepoUrl;
        }

        public static bool News_Get(UINewsEventsViewModel __instance)
        {
            Community.FillNews(__instance);
            return false;
        }

        public static bool UILandingPage_OnClickChallengeAI(UILandingPageContext __instance)
        {
            Patches.UILandingPage_OnClickChallengeAI(__instance);
            return false;
        }


        public static void Login_AlwaysEnabled(UILoginDialogViewModel __instance)
        {
            if (__instance.Context != null)
                __instance.Context.LoginButtonEnabled = true;
        }

        public static bool Session_SendPlayerOptions(StandaloneSession __instance, Player __0, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptions(__instance, __0);
            return false;
        }

        public static void Session_Update(StandaloneSession __instance)
        {
            Server.Pump(__instance);
            Server.BeginTick();
        }

        public static void Session_UpdatePost(StandaloneSession __instance)
        {
            Server.EndTick();
            Server.Flush(__instance);
        }

        public static bool Session_SubmitTransaction(StandaloneSession __instance, Game.Shared.Mechanics.Transactions.Transaction __0, ref bool __result)
        {
            __result = Server.Server_SubmitTransaction(__instance, __0);
            return false;
        }

        public static bool Session_DispatchSessionEvent(StandaloneSession __instance, SessionEventArgs __0)
        {
            Server.Server_DispatchSessionEvent(__instance, __0);
            return false;
        }

        public static bool Session_DispatchSessionEventPlayer(StandaloneSession __instance, Player __0, SessionEventArgs __1)
        {
            Server.Server_DispatchSessionEventPlayer(__instance, __0, __1);
            return false;
        }

        public static bool Session_SendPlayerInformationUpdates(StandaloneSession __instance, Game.Shared.Mechanics.ETurnPhases __0, ref bool __result)
        {
            __result = Server.Server_SendPlayerInformationUpdates(__instance, __0);
            return false;
        }

        public static bool Session_SendPlayerOptionsFor(StandaloneSession __instance, Player __0, Card __1, Reckoning.Game.AbilityTemplate __2, Game.Shared.Mechanics.Abilities.AbilityInstance __3, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptionsFor(__instance, __0, __1, __2, __3);
            return false;
        }

        public static bool Session_SendPlayerOptionsForList(StandaloneSession __instance, Player __0, List<Card> __1, List<Reckoning.Game.AbilityTemplate> __2, List<Game.Shared.Mechanics.Abilities.AbilityInstance> __3, ref bool __result)
        {
            __result = Server.Server_SendPlayerOptionsForList(__instance, __0, __1, __2, __3);
            return false;
        }

        public static bool Session_SendRevealedCards(StandaloneSession __instance, Player __0, List<SessionCardId> __1, ref bool __result)
        {
            __result = Server.Server_SendRevealedCards(__instance, __0, __1);
            return false;
        }

        /// <summary>Why the engine refuses to play the card (sub-conditions of Session.CanPlayCard).</summary>
        private static string PlayDiag(object req, Session session, Player p)
        {
            Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement cr = req as Game.Shared.Mechanics.Transactions.Requirements.CardCanBePlayedRequirement;
            if (cr == null) return "";
            try
            {
                System.Reflection.FieldInfo f = cr.GetType().GetField("m_SessionCardId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                SessionCardId id = (SessionCardId)f.GetValue(cr);
                Card c = session.ResourceCache.GetCard(id);
                if (c == null) return " (card not found)";
                Player prio = session.GetPriorityPlayer();
                Player act = session.GetActivePlayer();
                System.Text.StringBuilder sb = new System.Text.StringBuilder(" (");
                sb.Append("zone=").Append(c.m_CurrentCardCollection);
                sb.Append(" prio=").Append(prio != null ? prio.m_PlayerId.ToString() : "-");
                sb.Append(" active=").Append(act != null ? act.m_PlayerId.ToString() : "-");
                sb.Append(" phase=").Append(session.CurrentTurnPhase);
                sb.Append(" chain=").Append(session.Chain.IsEmpty ? "empty" : "not empty");
                sb.Append(" quick=").Append(c.IsQuickSpeed());
                sb.Append(" cout=").Append(c.CastingCost).Append("/res=").Append(p.m_CurrentResourcePool);
                sb.Append(" seuils=").Append(p.HasResourceThresholds(c.Thresholds));
                sb.Append(" condition=").Append(c.m_CardTemplate.m_PlayCondition == null ? "-" : c.m_CardTemplate.m_PlayCondition.IsValid(c, session).ToString());
                if (c.IsSpell())
                {
                    System.Reflection.MethodInfo m = typeof(Session).GetMethod("HasEnoughTargetsToCast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (m != null) sb.Append(" ciblesSuffisantes=").Append(m.Invoke(session, new object[] { c, p }));
                }
                sb.Append(" mobilize=").Append(session.CheckMobilize(c, p));
                sb.Append(")");
                return sb.ToString();
            }
            catch (Exception e) { return " (diag: " + e.Message + ")"; }
        }

        /// <summary>First unmet requirement (re-evaluated: IsValid has no side effect).</summary>
        private static string FailedRequirement(Game.Shared.Mechanics.Transactions.Transaction tx, Session session)
        {
            try
            {
                System.Reflection.FieldInfo f = typeof(Game.Shared.Mechanics.Transactions.Transaction).GetField("m_RequirementList",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                System.Collections.IEnumerable reqs = f != null ? f.GetValue(tx) as System.Collections.IEnumerable : null;
                if (reqs == null) return "?";
                Player p = session.GetPlayer(tx.m_PlayerId);
                foreach (object o in reqs)
                {
                    Game.Shared.Mechanics.Transactions.Requirements.TransactionRequirement r = o as Game.Shared.Mechanics.Transactions.Requirements.TransactionRequirement;
                    if (r != null && !r.IsValid(session, p))
                        return r.GetType().Name + PlayDiag(r, session, p);
                }
                return "(none on re-evaluation)";
            }
            catch (Exception e) { return "? " + e.Message; }
        }

        public static void LogFailedTransaction(Game.Shared.Mechanics.Transactions.Transaction __instance, Session __0, bool __result)
        {
            if (__result || !Dev.Enabled || __instance is Game.Shared.Mechanics.Transactions.PassPriorityTransaction)
                return;
            string line;
            try { line = __instance.GetLogLine(__0); }
            catch (Exception e) { line = __instance.GetType().Name + " (GetLogLine: " + e.Message + ")"; }
            line = "FAILED " + FailedRequirement(__instance, __0) + " | " + line;
            UnityEngine.Debug.Log("[HexRevival] [FailTx] " + line.Replace("\n", " | "));
            try
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(HexRevival.LocalServer.Root, Instance.Name("HexRevival.failtx.log")),
                    DateTime.Now.ToString("HH:mm:ss") + " " + line.Replace("\n", " | ") + "\r\n");
            }
            catch { }
            Server.ReportFailedTransaction(__instance, line.Replace("\n", " | "));
        }

        public static bool NeverExpired(ref bool __result)
        {
            __result = false;
            return false;
        }

        private static readonly Dictionary<Type, FieldInfo> s_PublishFields = new Dictionary<Type, FieldInfo>();

        /// <summary>Skips sending if the service has no connection (offline mode).</summary>
        public static bool Service_Produce(object __instance, Delegate __0, string __1)
        {
            if (__1 != null)
                return true; // the original already handles m_Connections == null
            Type t = __instance.GetType();
            FieldInfo f;
            if (!s_PublishFields.TryGetValue(t, out f))
            {
                f = AccessTools.Field(t, "m_Publish");
                s_PublishFields[t] = f;
            }
            bool send = f == null || f.GetValue(__instance) != null;
            if (!send)
                NetTrace.Log("Produce " + t.Name, __0);
            return send;
        }

        /// <summary>Trace (offline): direct send on the SFS connection, without a session.</summary>
        public static bool ClientConnection_SendCommand(byte[] eventArgsBytes, string toexc, string routeto, Game.Shared.Network.MessageHandler responseHandler)
        {
            bool local = LocalServer.TryHandle(eventArgsBytes, responseHandler);
            NetTrace.Log((local ? "LOCAL " : "SendCommand ") + toexc + "/" + routeto + " " + NetTrace.Peek(eventArgsBytes), null);
            return !local;
        }

        public static Exception SwallowException(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception != null && Plugin.Log != null)
                Plugin.Log.LogDebug("Exception swallowed in " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": " + __exception.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Offline network probe: for each blocked send, records the service, the calling method
    /// (stack) and the closure content (captured request objects). File HexRevival.net.log.
    /// </summary>
    public static class NetTrace
    {
        private static readonly System.Collections.Generic.Dictionary<string, int> s_Seen = new System.Collections.Generic.Dictionary<string, int>();

        public static void Log(string what, Delegate pub)
        {
            if (!Dev.Enabled) return;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(what);
                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(2, false);
                sb.Append(" | appel:");
                int n = 0;
                for (int i = 0; i < st.FrameCount && n < 6; i++)
                {
                    MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    string dn = m.DeclaringType.FullName ?? m.DeclaringType.ToString();
                    dn = dn.Replace("Game.Shared.Network.", "").Replace("Game.Shared.", "").Replace("Game.Client.", "");
                    if (dn.Contains("ClientConnection") || dn.Contains("NetworkConnection") || dn.StartsWith("Service`") || dn == "NetTrace" || dn.EndsWith("Hooks") || dn.Contains("DMD")) continue;
                    sb.Append(' ').Append(dn).Append('.').Append(m.Name);
                    n++;
                }
                string key = sb.ToString().Substring(9);
                int count;
                s_Seen.TryGetValue(key, out count);
                s_Seen[key] = count + 1;
                if (count >= 3) return; // no flooding for periodic sends
                if (pub != null && pub.Target != null)
                {
                    foreach (FieldInfo f in pub.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object v = null;
                        try { v = f.GetValue(pub.Target); } catch { }
                        sb.Append("\n    ").Append(f.Name).Append(" = ").Append(Describe(v, 0));
                    }
                }
                System.IO.File.AppendAllText(System.IO.Path.Combine(HexRevival.LocalServer.Root, "HexRevival.net.log"),
                    sb.ToString() + "\r\n");
            }
            catch { }
        }

        /// <summary>Readable start of a serialized message (the type name often appears in it).</summary>
        public static string Peek(byte[] b)
        {
            if (b == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder("[" + b.Length + "o] ");
            for (int i = 0; i < b.Length && i < 300; i++)
                sb.Append(b[i] >= 32 && b[i] < 127 ? (char)b[i] : '.');
            return sb.ToString();
        }

        private static string Describe(object v, int depth)
        {
            if (v == null) return "null";
            Type t = v.GetType();
            if (t.IsPrimitive || v is string || t.IsEnum) return v.ToString();
            if (depth >= 2) return t.Name;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(t.Name).Append(" {");
            int k = 0;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (k++ >= 12) { sb.Append(" ..."); break; }
                object fv = null;
                try { fv = f.GetValue(v); } catch { }
                sb.Append(' ').Append(f.Name).Append('=');
                if (fv is System.Collections.ICollection && !(fv is string))
                    sb.Append(fv.GetType().Name).Append("[").Append(((System.Collections.ICollection)fv).Count).Append("]");
                else sb.Append(Describe(fv, depth + 1));
            }
            return sb.Append(" }").ToString();
        }
    }
}
