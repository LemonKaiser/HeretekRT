using Content.Shared._CorvaxNext.Silicons.Borgs.Components;

namespace Content.Server._WH40K.Cyborgs;

/// <summary>
/// Allows a Station AI to reach any networked borg on its core's grid plus any borg it previously controlled
/// anywhere on the same map until another AI links it or its remote controller is removed.
/// </summary>
public sealed class Wh40kAiRemoteLinkSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AiRemoteControllerComponent, ComponentRemove>(OnControllerRemoved);
    }

    private void OnControllerRemoved(EntityUid uid, AiRemoteControllerComponent component, ComponentRemove args)
    {
        RemCompDeferred<Wh40kAiRemoteLinkComponent>(uid);
    }

    public bool CanReach(EntityUid ai, EntityUid device)
    {
        var aiXform = Transform(ai);
        var deviceXform = Transform(device);

        if (aiXform.GridUid != null && aiXform.GridUid == deviceXform.GridUid)
            return true;

        return aiXform.MapID == deviceXform.MapID
            && TryComp<Wh40kAiRemoteLinkComponent>(device, out var link)
            && link.Ai == ai;
    }

    public void Link(EntityUid ai, EntityUid device)
    {
        EnsureComp<Wh40kAiRemoteLinkComponent>(device).Ai = ai;
    }
}
