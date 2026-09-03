using Content.Server._Mono.Company; // Mono
using Content.Server._Mono.MonoCoins; // Mono
  using Content.Server._WH40K.Administration.ScreenCheck;
  using Content.Server._WH40K.Administration;
using Content.Server._WH40K.CharacterCreation;
using Content.Server._WH40K.ClassProgression;
using Content.Server._WH40K.PersistentInventory;
using Content.Server._WH40K.Progression;
using Content.Server._NF.Auth;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Administration.Notes;
using Content.Server.Afk;
using Content.Server.Chat.Managers;
using Content.Server.Connection;
using Content.Server.Database;
using Content.Server.Discord;
using Content.Server.Discord.DiscordLink;
using Content.Server.Discord.WebhookMessages;
using Content.Server.EUI;
using Content.Server.GhostKick;
using Content.Server.Info;
using Content.Server.Mapping;
using Content.Server.Maps;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.Players.JobWhitelist;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Players.RateLimiting;
using Content.Server.Preferences.Managers;
using Content.Server.ServerInfo;
using Content.Server.ServerUpdates;
using Content.Server.TTS;
using Content.Server.Voting.Managers;
using Content.Server.Worldgen.Tools;
using Content.Shared.Administration.Logs;
using Content.Shared.Administration.Managers;
using Content.Shared.Chat;
using Content.Shared.Kitchen;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Players.RateLimiting;

namespace Content.Server.IoC
{
    internal static class ServerContentIoC
    {
        public static void Register()
        {
            IoCManager.Register<IChatManager, ChatManager>();
            IoCManager.Register<ISharedChatManager, ChatManager>();
            IoCManager.Register<IChatSanitizationManager, ChatSanitizationManager>();
            IoCManager.Register<IServerPreferencesManager, ServerPreferencesManager>();
            IoCManager.Register<IServerDbManager, ServerDbManager>();
            IoCManager.Register<RecipeManager, RecipeManager>();
            IoCManager.Register<INodeGroupFactory, NodeGroupFactory>();
            IoCManager.Register<IConnectionManager, ConnectionManager>();
            IoCManager.Register<ServerUpdateManager>();
            IoCManager.Register<IAdminManager, AdminManager>();
            IoCManager.Register<IAdminHierarchyManager, AdminHierarchyManager>();
            IoCManager.Register<IAdminAuthorizationManager, AdminAuthorizationManager>();
            IoCManager.Register<IAdminActionGuard, AdminActionGuard>();
            IoCManager.Register<ISharedAdminManager, AdminManager>();
            IoCManager.Register<EuiManager, EuiManager>();
            IoCManager.Register<IVoteManager, VoteManager>();
            IoCManager.Register<IPlayerLocator, PlayerLocator>();
            IoCManager.Register<IAfkManager, AfkManager>();
            IoCManager.Register<IGameMapManager, GameMapManager>();
            IoCManager.Register<RulesManager, RulesManager>();
            IoCManager.Register<IBanManager, BanManager>();
            IoCManager.Register<ContentNetworkResourceManager>();
            IoCManager.Register<IAdminNotesManager, AdminNotesManager>();
            IoCManager.Register<GhostKickManager>();
            IoCManager.Register<ScreenCheckManager>();
            IoCManager.Register<Wh40kPlayerProgressManager>();
            IoCManager.Register<Wh40kAccountRpgManager>();
            IoCManager.Register<PersistentInventoryManager>();
            IoCManager.Register<Wh40kProgressManager>();
            IoCManager.Register<Wh40kClassProgressManager>();
            IoCManager.Register<IWh40kAdditionalSkillPointSource, Wh40kNoAdditionalSkillPointSource>();
            IoCManager.Register<Wh40kPartyManager>();
            IoCManager.Register<Wh40kExperienceService>();
            IoCManager.Register<Wh40kLevelRewardCatalog>();
            IoCManager.Register<Wh40kRpgAdminService>();
            IoCManager.Register<Wh40kCharacterStatResolver>();
            IoCManager.Register<ISharedAdminLogManager, AdminLogManager>();
            IoCManager.Register<IAdminLogManager, AdminLogManager>();
            IoCManager.Register<PlayTimeTrackingManager>();
            IoCManager.Register<UserDbDataManager>();
            IoCManager.Register<ServerInfoManager>();
            IoCManager.Register<PoissonDiskSampler>();
            IoCManager.Register<DiscordWebhook>();
            IoCManager.Register<VoteWebhooks>();
            IoCManager.Register<ServerDbEntryManager>();
            IoCManager.Register<ISharedPlaytimeManager, PlayTimeTrackingManager>();
            IoCManager.Register<ServerApi>();
            IoCManager.Register<JobWhitelistManager>();
            IoCManager.Register<PlayerRateLimitManager>();
            IoCManager.Register<SharedPlayerRateLimitManager, PlayerRateLimitManager>();
            IoCManager.Register<MappingManager>();
            IoCManager.Register<MapTransferManager>();
            IoCManager.Register<IWatchlistWebhookManager, WatchlistWebhookManager>();
            IoCManager.Register<ConnectionManager>();
            IoCManager.Register<MultiServerKickManager>();
            IoCManager.Register<CVarControlManager>();
            IoCManager.Register<MiniAuthManager>(); //Frontier
            IoCManager.Register<CompanyManager>(); // Mono
            IoCManager.Register<MonoCoinsManager>(); // Mono
            IoCManager.Register<TTSManager>();

            IoCManager.Register<DiscordLink>();
            IoCManager.Register<DiscordChatLink>();
        }
    }
}
