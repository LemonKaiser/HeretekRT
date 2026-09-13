using Content.Client.UserInterface.Systems.Chat.RichText;
using Content.Shared.Chat;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Client.UserInterface.Systems.Chat.RichText;

[TestFixture]
public sealed class ChatJobIconMarkupTest
{
    [Test]
    public void InsertsIconImmediatelyBeforeMarkedName()
    {
        const string message = "[BubbleHeader][bold][Name]Mechanicus[/Name][/bold][/BubbleHeader] says, hi";

        var result = ChatJobIconMarkup.Inject(message, "JobIconWanderer");

        Assert.That(result, Is.EqualTo(
            "[BubbleHeader][bold][chatjobicon icon=\"JobIconWanderer\"/] [Name]Mechanicus[/Name][/bold][/BubbleHeader] says, hi"));
    }

    [Test]
    public void LeavesAnonymousMessageUntouched()
    {
        const string message = "[BubbleHeader]Someone[/BubbleHeader] whispers, hi";

        Assert.That(ChatJobIconMarkup.Inject(message, "JobIconWanderer"), Is.EqualTo(message));
    }

    [Test]
    public void DoesNotDuplicateExistingIcon()
    {
        const string message = "[chatjobicon icon=\"JobIconWanderer\"/] [Name]Mechanicus[/Name]";

        Assert.That(ChatJobIconMarkup.Inject(message, "JobIconWanderer"), Is.EqualTo(message));
    }

    [Test]
    public void ReservesOutputHeightOnlyForMessagesWithAnIcon()
    {
        const string withIcon = "[chatjobicon icon=\"JobIconWanderer\"/] [Name]Mechanicus[/Name]";
        const string withoutIcon = "[Name]Mechanicus[/Name]";
        const string spacer = "[font size=3]\n[/font]";

        var reserved = FormattedMessage.FromMarkupOrThrow(withIcon);
        ChatJobIconMarkup.ReserveOutputLineHeight(reserved);

        Assert.That(reserved.ToMarkup(), Does.EndWith(spacer));
        var reservedMarkup = reserved.ToMarkup();
        ChatJobIconMarkup.ReserveOutputLineHeight(reserved);
        Assert.That(reserved.ToMarkup(), Is.EqualTo(reservedMarkup));

        var plain = FormattedMessage.FromMarkupOrThrow(withoutIcon);
        ChatJobIconMarkup.ReserveOutputLineHeight(plain);
        Assert.That(plain.ToMarkup(), Is.EqualTo(withoutIcon));
    }

    [TestCase(ChatChannel.Local, true)]
    [TestCase(ChatChannel.Radio, true)]
    [TestCase(ChatChannel.Whisper, false)]
    [TestCase(ChatChannel.Emotes, false)]
    [TestCase(ChatChannel.LOOC, false)]
    [TestCase(ChatChannel.OOC, false)]
    [TestCase(ChatChannel.Dead, false)]
    [TestCase(ChatChannel.AdminChat, false)]
    public void InjectsOnlyForOrdinarySpeechAndRadio(ChatChannel channel, bool expected)
    {
        Assert.That(ChatJobIconMarkup.ShouldInjectForChannel(channel), Is.EqualTo(expected));
    }
}
