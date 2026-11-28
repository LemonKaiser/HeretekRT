using Content.Shared._WH40K.WolfChem.Narcotics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.WolfChem.Narcotics;

/// <summary>
/// Client half of intoxication: keeps predicted movement speed in step with the networked levels and draws the
/// screen overlay.
/// </summary>
public sealed partial class Wh40kIntoxicationSystem : SharedWh40kIntoxicationSystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float NodCloseSeconds = 0.6f;
    private const float OverdoseRamp = 0.5f;

    private Wh40kIntoxicationOverlay _overlay = default!;
    private Wh40kNodOverlay _nodOverlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new Wh40kIntoxicationOverlay(this);
        _overlays.AddOverlay(_overlay);
        _nodOverlay = new Wh40kNodOverlay(this);
        _overlays.AddOverlay(_nodOverlay);
        SubscribeLocalEvent<Wh40kIntoxicationComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<Wh40kIntoxicationComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_overlay);
        _overlays.RemoveOverlay(_nodOverlay);
    }

    /// <summary>How far a nod has shut the local player's eyes: 0 awake, 1 shut.</summary>
    public float NodClosure()
    {
        if (_player.LocalEntity is not { } local
            || !TryComp<Wh40kIntoxicationComponent>(local, out var comp)
            || !IsNodding(comp))
            return 0f;

        return Math.Clamp((float) (_timing.CurTime - comp.NodStart).TotalSeconds / NodCloseSeconds, 0f, 1f);
    }

    /// <summary>The high that sets the look: the furthest phase, then the strongest within it.</summary>
    public Wh40kDominantHigh? DominantHigh(Wh40kIntoxicationComponent comp)
    {
        Wh40kDominantHigh? best = null;
        foreach (var (profile, level) in comp.Levels)
        {
            if (!Proto.TryIndex(profile, out var proto))
                continue;

            var tier = proto.TierAt(level);
            var phase = proto.PhaseOf(tier);
            if (phase == Wh40kDrugPhase.None || proto.Tiers[tier].Overlay <= 0f)
                continue;

            var strength = Strength(proto, tier, level);
            if (best is { } current && (phase < current.Phase || phase == current.Phase && strength <= current.Strength))
                continue;

            best = new Wh40kDominantHigh(proto.Family, phase, strength, tier);
        }

        return best;
    }

    private static float Strength(Wh40kDrugProfilePrototype proto, int tier, float level)
    {
        var current = proto.Tiers[tier];
        float next, span;
        if (tier + 1 < proto.Tiers.Count)
        {
            next = proto.Tiers[tier + 1].Overlay;
            span = proto.Tiers[tier + 1].Min - current.Min;
        }
        else
        {
            next = 1f;
            span = current.Min * OverdoseRamp;
        }

        var progress = span > 0f ? Math.Clamp((level - current.Min) / span, 0f, 1f) : 1f;
        return Math.Clamp(current.Overlay + (Math.Max(next, current.Overlay) - current.Overlay) * progress, 0f, 1f);
    }

    private void OnState(Entity<Wh40kIntoxicationComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRemove(Entity<Wh40kIntoxicationComponent> ent, ref ComponentRemove args)
    {
        ent.Comp.Levels.Clear();
        ent.Comp.Crashes.Clear();
        if (!TerminatingOrDeleted(ent))
            Movement.RefreshMovementSpeedModifiers(ent);
    }
}

public readonly record struct Wh40kDominantHigh(Wh40kDrugFamily Family, Wh40kDrugPhase Phase, float Strength, int Tier);
