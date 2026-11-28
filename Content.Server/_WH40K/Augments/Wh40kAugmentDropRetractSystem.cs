using Content.Server._WH40K.Augments.Combat;
using Content.Shared._Goobstation.MantisBlades;
using Content.Shared._WH40K.Augments;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Interaction.Components;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WH40K.Augments;

/// <summary>
/// Intercepts the Drop key (Q) ahead of <see cref="SharedHandsSystem"/> when the active hand holds
/// a deployed augment tool, deployable combat implant (arm laser, hand shield, finger lighter), or mantis blade,
/// retracting it back into the limb instead of failing to drop.
/// </summary>
public sealed partial class Wh40kAugmentDropRetractSystem : EntitySystem
{
    [Dependency] private AugmentToolPanelSystem _toolPanel = default!;
    [Dependency] private Wh40kDeployableImplantSystem _deployables = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.Drop, new PointerInputCmdHandler(OnDropPressed), typeof(SharedHandsSystem))
            .Register<Wh40kAugmentDropRetractSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<Wh40kAugmentDropRetractSystem>();
    }

    private bool OnDropPressed(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (session?.AttachedEntity is not { } user ||
            !_hands.TryGetActiveItem(user, out var item))
        {
            return false;
        }

        return TryRetract(user, item.Value);
    }

    /// <summary>
    /// Retracts the held item if it was deployed from an arm tool panel or mantis blade arm.
    /// </summary>
    public bool TryRetract(EntityUid body, EntityUid item)
    {
        if (HasComp<AugmentToolPanelActiveItemComponent>(item) &&
            _toolPanel.TryRetractActiveItem(body, item))
        {
            return true;
        }

        if (_deployables.TryStowBodyItem(body, item))
            return true;

        if (!HasComp<UnremoveableComponent>(item))
            return false;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (TryComp<MantisBladeArmComponent>(part, out var mantis) && mantis.BladeUid == item)
            {
                RaiseLocalEvent(part, new ToggleMantisBladeEvent());
                return true;
            }
        }

        return false;
    }
}
