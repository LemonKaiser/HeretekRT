using System;
using Content.Shared._WH40K.DeathTransition;

namespace Content.Client._WH40K.DeathTransition;

/// <summary>
/// Holds the single server-authoritative bit needed by the lobby's observe button.
/// Permission details and remaining uses deliberately stay on the server.
/// </summary>
public sealed class GhostPermissionStatusSystem : EntitySystem
{
    public bool CanObserve { get; private set; }

    public event Action? StatusUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GhostPermissionStatusEvent>(OnPermissionStatus);
    }

    private void OnPermissionStatus(GhostPermissionStatusEvent ev)
    {
        if (CanObserve == ev.CanObserve)
            return;

        CanObserve = ev.CanObserve;
        StatusUpdated?.Invoke();
    }
}
