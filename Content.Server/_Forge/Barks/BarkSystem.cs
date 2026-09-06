using Content.Server.Chat.Systems;
using Content.Shared._Forge.Barks;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Barks;

/// <summary>
///     Sends a selected bark to clients when an entity speaks.
/// </summary>
public sealed partial class BarkSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;

    private bool _enabled;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CCVars.BarksEnabled, enabled => _enabled = enabled, true);

        SubscribeLocalEvent<SpeechSynthesisComponent, EntitySpokeEvent>(OnEntitySpoke);
        SubscribeNetworkEvent<RequestPreviewBarkEvent>(OnRequestPreviewBark);
    }

    private void OnEntitySpoke(EntityUid uid, SpeechSynthesisComponent component, EntitySpokeEvent args)
    {
        if (!_enabled ||
            string.IsNullOrWhiteSpace(component.VoicePrototypeId) ||
            !_prototype.TryIndex<BarkPrototype>(component.VoicePrototypeId, out var bark) ||
            bark.SoundFiles.Count == 0)
        {
            return;
        }

        var barks = ExpressiveBarkPlanner.CreatePlan(
            args.Message,
            component.PlaybackSpeed,
            component.Pitch,
            component.Expression,
            bark.SoundFiles.Count,
            _random.Next());

        if (barks.Length == 0)
            return;

        var barkEvent = new PlayBarkEvent(
            bark.SoundFiles.ToArray(),
            GetNetEntity(uid),
            barks,
            args.IsWhisper,
            component.PlaybackSpeed);

        RaiseNetworkEvent(barkEvent, Filter.Pvs(uid));
    }

    private void OnRequestPreviewBark(RequestPreviewBarkEvent ev, EntitySessionEventArgs args)
    {
        if (!_enabled ||
            !_prototype.TryIndex<BarkPrototype>(ev.BarkVoiceId, out var bark) ||
            !bark.RoundStart ||
            bark.SoundFiles.Count == 0)
        {
            return;
        }

        RaiseNetworkEvent(new PlayBarkPreviewEvent(_random.Pick(bark.SoundFiles)), args.SenderSession);
    }
}
