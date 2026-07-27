using Content.Client.Lobby;
using Content.Server.Preferences.Managers;
using Content.Shared.Preferences;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests.Lobby
{
    [TestFixture]
    [TestOf(typeof(ClientPreferencesManager))]
    [TestOf(typeof(ServerPreferencesManager))]
    public sealed class CharacterCreationTest
    {
        [Test]
        public async Task CreateDeleteCreateTest()
        {
            await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
            var server = pair.Server;
            var client = pair.Client;

            var clientNetManager = client.ResolveDependency<IClientNetManager>();
            var clientStateManager = client.ResolveDependency<IStateManager>();
            var clientPrefManager = client.ResolveDependency<IClientPreferencesManager>();

            var serverPrefManager = server.ResolveDependency<IServerPreferencesManager>();


            // Need to run them in sync to receive the messages.
            await pair.RunTicksSync(1);

            await PoolManager.WaitUntil(client, () => clientStateManager.CurrentState is LobbyState, 600);

            Assert.That(clientNetManager.ServerChannel, Is.Not.Null);

            var clientNetId = clientNetManager.ServerChannel.UserId;
            HumanoidCharacterProfile profile = null;

            var session = System.Linq.Enumerable.Single(
                server.ResolveDependency<Robust.Shared.Player.ISharedPlayerManager>().Sessions);
            await server.ResolveDependency<Content.Server.Database.UserDbDataManager>().WaitLoadComplete(session);

            var onboardingProfile = serverPrefManager.GetPreferences(clientNetId).Characters[0]
                as HumanoidCharacterProfile;
            Assert.That(onboardingProfile, Is.Not.Null);

            var completedProfile = onboardingProfile!.WithWh40kCharacterBuild(
                new Content.Shared._WH40K.CharacterCreation.Wh40kCharacterBuild
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
                .CompleteWh40kOnboardingAsync(clientNetId, completedProfile);
            Assert.That(completion.IsSuccess, Is.True);
            await serverPrefManager.RefreshPreferencesAsync(session, default);
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(
                client,
                () => clientPrefManager.Wh40kProgress == completion.Progress &&
                      clientPrefManager.Wh40kProgress.CanUseLegacyPersonalization,
                60);

            await client.WaitAssertion(() =>
            {
                clientPrefManager.SelectCharacter(0);

                var clientCharacters = clientPrefManager.Preferences?.Characters;
                Assert.That(clientCharacters, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(clientCharacters, Has.Count.EqualTo(1));

                    Assert.That(clientStateManager.CurrentState, Is.TypeOf<LobbyState>());
                });

                profile = HumanoidCharacterProfile.Random().WithWh40kCharacterBuild(completedProfile.Wh40kBuild);
                clientPrefManager.CreateCharacter(profile);

                clientCharacters = clientPrefManager.Preferences?.Characters;

                Assert.That(clientCharacters, Is.Not.Null);
                Assert.That(clientCharacters, Has.Count.EqualTo(2));
                Assert.That(clientCharacters[1].MemberwiseEquals(profile));
            });

            await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(clientNetId).Characters.Count == 2, maxTicks: 60);

            await server.WaitAssertion(() =>
            {
                var serverCharacters = serverPrefManager.GetPreferences(clientNetId).Characters;

                Assert.That(serverCharacters, Has.Count.EqualTo(2));
                Assert.That(serverCharacters[1].MemberwiseEquals(profile));
            });

            await client.WaitAssertion(() =>
            {
                clientPrefManager.DeleteCharacter(1);

                var clientCharacters = clientPrefManager.Preferences?.Characters.Count;
                Assert.That(clientCharacters, Is.EqualTo(1));
            });

            await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(clientNetId).Characters.Count == 1, maxTicks: 60);

            await server.WaitAssertion(() =>
            {
                var serverCharacters = serverPrefManager.GetPreferences(clientNetId).Characters.Count;
                Assert.That(serverCharacters, Is.EqualTo(1));
            });

            await client.WaitIdleAsync();

            await client.WaitAssertion(() =>
            {
                profile = HumanoidCharacterProfile.Random().WithWh40kCharacterBuild(completedProfile.Wh40kBuild);

                clientPrefManager.CreateCharacter(profile);

                var clientCharacters = clientPrefManager.Preferences?.Characters;

                Assert.That(clientCharacters, Is.Not.Null);
                Assert.That(clientCharacters, Has.Count.EqualTo(2));
                Assert.That(clientCharacters[1].MemberwiseEquals(profile));
            });

            await PoolManager.WaitUntil(server, () => serverPrefManager.GetPreferences(clientNetId).Characters.Count == 2, maxTicks: 120); //60->120 - Mono

            await server.WaitAssertion(() =>
            {
                var serverCharacters = serverPrefManager.GetPreferences(clientNetId).Characters;

                Assert.That(serverCharacters, Has.Count.EqualTo(2));
                Assert.That(serverCharacters[1].MemberwiseEquals(profile));
            });
            await pair.CleanReturnAsync();
        }
    }
}
