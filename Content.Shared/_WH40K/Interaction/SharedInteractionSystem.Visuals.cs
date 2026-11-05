using Content.Shared._WH40K.Interaction;
using Content.Shared.Ghost;
using Content.Shared.Inventory.VirtualItem;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Shared.Interaction;

public abstract partial class SharedInteractionSystem
{
    [Dependency] private IConfigurationManager _interactionConfig = default!;

    private bool _interactionParticlesSuppressed;
    private bool _interactionParticlesEnabled = true;

    private void InitializeInteractionParticles()
    {
        Subs.CVar(_interactionConfig, Wh40kInteractionCVars.InteractionParticlesEnabled,
            value => _interactionParticlesEnabled = value, true);
    }

    public bool SetInteractionParticlesSuppressed(bool suppressed)
    {
        var previous = _interactionParticlesSuppressed;
        _interactionParticlesSuppressed = suppressed;
        return previous;
    }

    public void DoContactInteraction(EntityUid performer, EntityUid? target, EntityUid? used,
        bool predicted, HandledEntityEventArgs? args = null, bool interactionParticles = true,
        Wh40kInteractionParticleType interactionParticleType = Wh40kInteractionParticleType.Use)
    {
        DoContactInteraction(performer, target, args);

        if (!interactionParticles || !_interactionParticlesEnabled || _interactionParticlesSuppressed ||
            target is not { } targetUid || args?.Handled == false || performer == targetUid ||
            HasComp<GhostComponent>(performer) || HasComp<VirtualItemComponent>(targetUid))
            return;

        if (!TryComp<MetaDataComponent>(performer, out var performerMeta) || performerMeta.EntityPaused ||
            !TryComp<MetaDataComponent>(targetUid, out var targetMeta) || targetMeta.EntityPaused ||
            performerMeta.VisibilityMask != targetMeta.VisibilityMask ||
            performerMeta.CreationTick == _gameTiming.CurTick || targetMeta.CreationTick == _gameTiming.CurTick)
            return;

        if (_net.IsServer)
        {
            RaiseNetworkEvent(new Wh40kInteractionParticleEvent(GetNetEntity(performer), GetNetEntity(used),
                GetNetEntity(targetUid), false, interactionParticleType), Filter.Pvs(performer, entityManager: EntityManager));
        }
        else if (predicted && _gameTiming.IsFirstTimePredicted)
        {
            RaiseLocalEvent(new Wh40kInteractionParticleEvent(GetNetEntity(performer), GetNetEntity(used),
                GetNetEntity(targetUid), true, interactionParticleType));
        }
    }
}
