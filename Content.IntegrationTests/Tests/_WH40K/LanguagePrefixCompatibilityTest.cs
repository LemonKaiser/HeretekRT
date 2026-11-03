using Content.Server._EinsteinEngines.Language;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared.Radio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class LanguagePrefixCompatibilityTest
{
    private static readonly ProtoId<RadioChannelPrototype> Channel = "Common";

    [Test]
    public async Task PrefixUsesKnownLanguageWithoutChangingSelectedLanguage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var speaker = server.EntMan.SpawnEntity(null, map.GridCoords);
            var languages = server.System<LanguageSystem>();
            languages.AddLanguage(speaker, "SolCommon");
            languages.AddLanguage(speaker, "Classical");
            languages.SetLanguage(speaker, "SolCommon");

            var message = "^CLShello";
            var language = languages.GetLanguageFromPrefix(speaker, ref message, out var parsed, true);
            Assert.That(parsed, Is.True);
            Assert.That(language.ID, Is.EqualTo("Classical"));
            Assert.That(message, Is.EqualTo("hello"));
            Assert.That(languages.GetLanguage(speaker).ID, Is.EqualTo("SolCommon"));

            message = "^clsread only";
            language = languages.GetLanguageFromPrefix(speaker, ref message, out parsed);
            Assert.That(parsed, Is.True);
            Assert.That(language.ID, Is.EqualTo("Classical"));
            Assert.That(message, Is.EqualTo("^clsread only"));

            message = "^felunknown language";
            language = languages.GetLanguageFromPrefix(speaker, ref message, out parsed, true);
            Assert.That(parsed, Is.False);
            Assert.That(language.ID, Is.EqualTo("SolCommon"));
            Assert.That(message, Is.EqualTo("^felunknown language"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadioChecksPermissionForLanguageSelectedByPrefix()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var speaker = entities.SpawnEntity(null, map.GridCoords);
            var languages = server.System<LanguageSystem>();
            languages.AddLanguage(speaker, "SolCommon");
            languages.AddLanguage(speaker, "Sign");
            languages.SetLanguage(speaker, "SolCommon");

            var radio = server.System<RadioSystem>();
            var channel = server.ResolveDependency<IPrototypeManager>().Index(Channel);
            var observer = server.System<LanguagePrefixTestSystem>();
            observer.Source = speaker;
            observer.Sends = 0;
            try
            {
                radio.SendRadioMessage(speaker, "^solspoken control message", channel, speaker);
                var sent = observer.Sends;
                Assert.That(sent, Is.GreaterThan(0));

                radio.SendRadioMessage(speaker, "^sigsign language must stay local", channel, speaker);
                Assert.That(observer.Sends, Is.EqualTo(sent));

                languages.SetLanguage(speaker, "Sign");
                radio.SendRadioMessage(speaker, "^solspoken override of selected sign language", channel, speaker);
                Assert.That(observer.Sends, Is.GreaterThan(sent));
                Assert.That(languages.GetLanguage(speaker).ID, Is.EqualTo("Sign"));
            }
            finally
            {
                observer.Source = null;
            }
        });

        await pair.CleanReturnAsync();
    }

}

[Reflect(false)]
public sealed class LanguagePrefixTestSystem : EntitySystem
{
    public EntityUid? Source;
    public int Sends;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnSend);
    }

    private void OnSend(ref RadioSendAttemptEvent args)
    {
        if (args.RadioSource != Source)
            return;

        Sends++;
        args.Cancelled = true;
    }
}
