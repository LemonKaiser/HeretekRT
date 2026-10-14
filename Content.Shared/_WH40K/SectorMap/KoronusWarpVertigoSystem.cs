using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.StatusEffect;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>
/// Temporary distortion composed with ordinary eye damage and vision correction.
/// The existing vision overlay also honors the player's reduced-motion setting.
/// </summary>
public sealed class KoronusWarpVertigoSystem : EntitySystem
{
    [ValidatePrototypeId<StatusEffectPrototype>]
    public const string EffectId = "KoronusWarpVertigo";

    [Dependency] private BlurryVisionSystem _vision = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KoronusWarpVertigoComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KoronusWarpVertigoComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<KoronusWarpVertigoComponent, GetBlurEvent>(OnGetBlur);
    }

    private void OnStartup(EntityUid uid, KoronusWarpVertigoComponent component, ComponentStartup args)
        => _vision.UpdateBlurMagnitude(uid);

    private void OnShutdown(EntityUid uid, KoronusWarpVertigoComponent component, ComponentShutdown args)
        => _vision.UpdateBlurMagnitude(uid);

    private void OnGetBlur(EntityUid uid, KoronusWarpVertigoComponent component, GetBlurEvent args)
    {
        if (component.LifeStage <= ComponentLifeStage.Running)
            args.Blur += 3.5f;
    }
}

[RegisterComponent, NetworkedComponent]
public sealed partial class KoronusWarpVertigoComponent : Component;
