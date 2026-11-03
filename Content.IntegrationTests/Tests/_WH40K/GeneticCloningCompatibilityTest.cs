using System.Linq;
using Content.Server._EinsteinEngines.Language;
using Content.Server._Mono.Cloning;
using Content.Server.Materials;
using Content.Shared._Forge.Barks;
using Content.Shared._Mono.Cloning;
using Content.Shared.Cloning;
using Content.Shared.DetailExaminable;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.TTS;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class GeneticCloningCompatibilityTest
{
    [Test]
    public async Task CloningPreservesCharacterDataAndChargesOnlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var mindSystem = server.System<SharedMindSystem>();
            var source = entities.SpawnEntity("MobHuman", map.GridCoords);
            var mind = mindSystem.CreateMind(pair.Player!.UserId);
            mindSystem.TransferTo(mind, source);

            var tts = entities.EnsureComponent<TTSComponent>(source);
            tts.VoicePrototypeId = "TtsTechpriest";
            var bark = entities.EnsureComponent<SpeechSynthesisComponent>(source);
            bark.VoicePrototypeId = server.ProtoMan.EnumeratePrototypes<BarkPrototype>().First().ID;
            bark.PlaybackSpeed = 1.2f;
            var description = entities.EnsureComponent<DetailExaminableComponent>(source);
            description.CharacterContent = "[bold]Character history[/bold]";
            description.OocContent = "Private notes";
            description.ShareOocContent = false;
            description.ShareLinksContent = false;
            description.SharePreferencesContent = false;
            var languages = server.System<LanguageSystem>();
            languages.AddLanguage(source, "Classical");
            languages.SetLanguage(source, "Classical");

            var cloning = server.System<GeneticCloningSystem>();
            var database = server.System<GeneticDatabaseSystem>();
            var record = cloning.CreateRecord(source, out var reason);
            Assert.That(record, Is.Not.Null, reason);
            Assert.That(database.GetStatus(record!), Is.EqualTo(GeneticRecordStatus.Alive));
            var pod = entities.SpawnEntity("CloningPod", map.GridCoords);
            var material = server.System<MaterialStorageSystem>();
            var podComponent = entities.GetComponent<CloningPodComponent>(pod);
            Assert.That(cloning.TryCloning(pod, record!), Is.False);
            Assert.That(material.GetMaterialAmount(pod, podComponent.RequiredMaterial), Is.EqualTo(0));
            server.System<MobStateSystem>().ChangeMobState(source, MobState.Dead);
            Assert.That(database.GetStatus(record!), Is.EqualTo(GeneticRecordStatus.Cloneable));
            Assert.That(cloning.TryCloning(pod, record!), Is.False);
            Assert.That(podComponent.BodyContainer.ContainedEntity, Is.Null);
            Assert.That(material.TryChangeMaterialAmount(pod, podComponent.RequiredMaterial, 1000), Is.True);
            description.CharacterContent = "Changed after scan";

            Assert.That(cloning.TryCloning(pod, record!), Is.True);
            Assert.That(podComponent.BodyContainer.ContainedEntity, Is.Not.Null);
            var clone = podComponent.BodyContainer.ContainedEntity!.Value;
            Assert.That(podComponent.UsedBiomass, Is.GreaterThan(0));
            var remaining = material.GetMaterialAmount(pod, podComponent.RequiredMaterial);
            Assert.That(remaining, Is.EqualTo(1000 - podComponent.UsedBiomass));
            Assert.That(entities.GetComponent<TTSComponent>(clone).VoicePrototypeId, Is.EqualTo("TtsTechpriest"));
            Assert.That(entities.GetComponent<SpeechSynthesisComponent>(clone).VoicePrototypeId, Is.EqualTo(bark.VoicePrototypeId));
            Assert.That(entities.GetComponent<SpeechSynthesisComponent>(clone).PlaybackSpeed, Is.EqualTo(1.2f));
            var clonedDescription = entities.GetComponent<DetailExaminableComponent>(clone);
            Assert.That(clonedDescription.CharacterContent, Is.EqualTo("[bold]Character history[/bold]"));
            Assert.That(clonedDescription.OocContent, Is.EqualTo("Private notes"));
            Assert.That(clonedDescription.ShareOocContent, Is.False);
            Assert.That(clonedDescription.ShareLinksContent, Is.False);
            Assert.That(clonedDescription.SharePreferencesContent, Is.False);
            Assert.That(languages.CanSpeak(clone, "Classical"), Is.True);
            Assert.That(languages.GetLanguage(clone).ID, Is.EqualTo("Classical"));

            Assert.That(cloning.TryCloning(pod, record!), Is.False);
            Assert.That(material.GetMaterialAmount(pod, podComponent.RequiredMaterial), Is.EqualTo(remaining));
            var secondPod = entities.SpawnEntity("CloningPod", map.GridCoords);
            Assert.That(material.TryChangeMaterialAmount(secondPod, podComponent.RequiredMaterial, 1000), Is.True);
            Assert.That(cloning.TryCloning(secondPod, record!), Is.False);
            Assert.That(material.GetMaterialAmount(secondPod, podComponent.RequiredMaterial), Is.EqualTo(1000));
            mindSystem.WipeMind(mind);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DatabaseRetainsTerminalFlagsAndReusesRecordIds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var database = server.System<GeneticDatabaseSystem>();
            var holder = entities.SpawnEntity(null, map.GridCoords);
            var component = entities.AddComponent<GeneticDatabaseComponent>(holder);
            var mind = server.System<SharedMindSystem>().CreateMind(null);
            var record = new GeneticRecord { Mind = mind };
            database.Upsert((holder, component), record);
            var firstId = record.Id;
            var updated = new GeneticRecord { Mind = mind };
            database.Upsert((holder, component), updated);
            Assert.That(updated.Id, Is.EqualTo(firstId));
            Assert.That(component.Records, Has.Count.EqualTo(1));
            database.MarkUncloneable(mind);
            Assert.That(database.GetStatus(updated), Is.EqualTo(GeneticRecordStatus.Lost));
            var rescanned = new GeneticRecord { Mind = mind };
            database.Upsert((holder, component), rescanned);
            Assert.That(database.GetStatus(rescanned), Is.EqualTo(GeneticRecordStatus.Lost));
            Assert.That(database.TryGetRecord((holder, component), firstId, out var found), Is.True);
            Assert.That(found, Is.SameAs(rescanned));
        });
        await pair.CleanReturnAsync();
    }
}
