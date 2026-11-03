using System.Linq;
using Content.Shared.Preferences;

namespace Content.IntegrationTests.Tests._WH40K;

[TestFixture]
public sealed class ProfileDescriptionCompatibilityTest
{
    [Test]
    public async Task LongerDescriptionPreservesHeretekFieldsAndHandlesMarkup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player, Is.Not.Null);
            var original = HumanoidCharacterProfile.DefaultWithSpecies();
            original.OocFlavorText = "Notes";
            original.CharacterFlavorText = "[bold]History[/bold]";
            original.ShareOocFlavorText = false;
            original.ShareLinksFlavorText = false;
            original.SharePreferencesFlavorText = false;

            var plain = original.WithFlavorText(new string('x', 2500));
            plain.EnsureValid(pair.Player!, server.InstanceDependencyCollection);
            Assert.That(plain.FlavorText, Has.Length.EqualTo(2048));
            Assert.That(plain.OocFlavorText, Is.EqualTo("Notes"));
            Assert.That(plain.CharacterFlavorText, Is.EqualTo("[bold]History[/bold]"));
            Assert.That(plain.ShareOocFlavorText, Is.False);
            Assert.That(plain.ShareLinksFlavorText, Is.False);
            Assert.That(plain.SharePreferencesFlavorText, Is.False);

            var markup = original.WithFlavorText(string.Concat(Enumerable.Repeat("[bold]x[/bold]", 300)));
            markup.EnsureValid(pair.Player!, server.InstanceDependencyCollection);
            Assert.That(markup.FlavorText, Is.EqualTo(new string('x', 300)));
            Assert.That(markup.CharacterFlavorText, Is.EqualTo(original.CharacterFlavorText));
        });
        await pair.CleanReturnAsync();
    }
}
