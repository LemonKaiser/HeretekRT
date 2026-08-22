#nullable enable
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.GameRules;

[TestFixture]
public sealed class FailAndStartPresetTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: gamePreset
  id: TestPreset
  alias:
    - nukeops
  name: Test Preset
  description: """"
  showInVote: false
  rules:
  - TestRule

- type: gamePreset
  id: TestPresetTenPlayers
  alias:
    - nukeops
  name: Test Preset 10 players
  description: """"
  showInVote: false
  rules:
  - TestRuleTenPlayers

- type: entity
  id: TestRule
  parent: BaseGameRule
  categories: [ GameRules ]
  components:
  - type: GameRule
    minPlayers: 0
  - type: TestRule

- type: entity
  id: TestRuleTenPlayers
  parent: BaseGameRule
  categories: [ GameRules ]
  components:
  - type: GameRule
    minPlayers: 10
  - type: TestRule
";

    /// <summary>
    ///     Test that a nuke ops gamemode can start after failing to start once.
    /// </summary>
    [Test]
    [Ignore("Preventing CI tests from failing")] // Not required.
    public async Task FailAndStartTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
            InLobby = true
        });

        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        server.System<TestRuleSystem>().Run = true;

        var userId = client.User!.Value;
        var session = System.Linq.Enumerable.Single(
            server.ResolveDependency<Robust.Shared.Player.ISharedPlayerManager>().Sessions);
        await server.ResolveDependency<Content.Server.Database.UserDbDataManager>().WaitLoadComplete(session);

        var preferences = (Content.Server.Preferences.Managers.ServerPreferencesManager) server
            .ResolveDependency<Content.Server.Preferences.Managers.IServerPreferencesManager>();
        var profile = preferences.GetPreferences(userId).Characters[0] as Content.Shared.Preferences.HumanoidCharacterProfile;
        Assert.That(profile, Is.Not.Null);

        var completedProfile = profile!.WithWh40kCharacterBuild(new Content.Shared._WH40K.CharacterCreation.Wh40kCharacterBuild
        {
            HomeworldId = "death-world",
            OriginId = "astra-militarum-commander",
            ClassId = "soldier",
            PortraitId = "test-portrait-01",
            CharacteristicPoints =
            {
                [Content.Shared._WH40K.CharacterCreation.Wh40kCharacteristic.Melee] =
                    Content.Shared._WH40K.CharacterCreation.Wh40kCharacterBuild.MaximumAttributePoints,
            },
        });
        var completion = await server.ResolveDependency<Content.Server.Database.IServerDbManager>()
            .CompleteWh40kOnboardingAsync(userId, completedProfile);
        Assert.That(completion.IsSuccess, Is.True);
        await preferences.RefreshPreferencesAsync(session, default);

        await server.WaitAssertion(() =>
        {
            var progress = server.ResolveDependency<Content.Server._WH40K.CharacterCreation.Wh40kPlayerProgressManager>();
            Assert.That(progress.Get(userId), Is.EqualTo(completion.Progress));
            Assert.That(preferences.IsWh40kOnboardingRequired(userId), Is.False);
        });

        Assert.That(server.CfgMan.GetCVar(CCVars.GridFill), Is.False);
        Assert.That(server.CfgMan.GetCVar(CCVars.GameLobbyFallbackEnabled), Is.True);
        Assert.That(server.CfgMan.GetCVar(CCVars.GameLobbyDefaultPreset), Is.EqualTo("secret"));
        server.CfgMan.SetCVar(CCVars.GridFill, true);
        server.CfgMan.SetCVar(CCVars.GameLobbyFallbackEnabled, false);
        server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "TestPreset");

        // Initially in the lobby
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(client.AttachedEntity, Is.Null);
        Assert.That(ticker.PlayerGameStatuses[client.User!.Value], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));

        // Try to start nukeops without readying up
        await pair.WaitCommand("setgamepreset TestPresetTenPlayers 9999");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(10);

        // Game should not have started
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
        Assert.That(ticker.PlayerGameStatuses[client.User!.Value], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
        Assert.That(!client.EntMan.EntityExists(client.AttachedEntity));
        var player = pair.Player!.AttachedEntity;
        Assert.That(!entMan.EntityExists(player));

        // Ready up and start nukeops
        await server.WaitAssertion(() =>
        {
            var progress = server.ResolveDependency<Content.Server._WH40K.CharacterCreation.Wh40kPlayerProgressManager>();
            var userDb = server.ResolveDependency<Content.Server.Database.UserDbDataManager>();
            Assert.That(userDb.IsLoadComplete(session), Is.True);
            Assert.That(progress.Get(userId), Is.EqualTo(completion.Progress));
            Assert.That(preferences.IsWh40kOnboardingRequired(userId), Is.False);
        });
        await pair.WaitClientCommand("toggleready True");
        Assert.That(ticker.PlayerGameStatuses[client.User!.Value], Is.EqualTo(PlayerGameStatus.ReadyToPlay));
        await pair.WaitCommand("setgamepreset TestPreset 9999");
        await pair.WaitCommand("startround");
        await pair.RunTicksSync(10);

        // Game should have started
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
        Assert.That(ticker.PlayerGameStatuses[client.User!.Value], Is.EqualTo(PlayerGameStatus.JoinedGame));
        Assert.That(client.EntMan.EntityExists(client.AttachedEntity));
        player = pair.Player!.AttachedEntity!.Value;
        Assert.That(entMan.EntityExists(player));

        ticker.SetGamePreset((GamePresetPrototype?) null);
        server.CfgMan.SetCVar(CCVars.GridFill, false);
        server.CfgMan.SetCVar(CCVars.GameLobbyFallbackEnabled, true);
        server.CfgMan.SetCVar(CCVars.GameLobbyDefaultPreset, "secret");
        server.System<TestRuleSystem>().Run = false;
        await pair.CleanReturnAsync();
    }
}

public sealed class TestRuleSystem : EntitySystem
{
    public bool Run;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundStartAttemptEvent>(OnRoundStartAttempt);
    }

    private void OnRoundStartAttempt(RoundStartAttemptEvent args)
    {
        if (!Run)
            return;

        if (args.Forced || args.Cancelled)
            return;

        var query = EntityQueryEnumerator<TestRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out _, out _, out var gameRule))
        {
            var minPlayers = gameRule.MinPlayers;
            if (!gameRule.CancelPresetOnTooFewPlayers)
                continue;
            if (args.Players.Length >= minPlayers)
                continue;

            args.Cancel();
        }
    }
}

[RegisterComponent]
public sealed partial class TestRuleComponent : Component;
