using Robust.Shared.Network;

namespace Content.Server._WH40K.DeathTransition;

public sealed partial class GhostPermissionSystem
{
    public void ForgetPlayer(NetUserId userId)
    {
        _permissions.Remove(userId);
        _loadedPermissions.Remove(userId);
        _reservedPermissionUses.Remove(userId);
        _staffObserverAuthorizations.Remove(userId);
        _pendingTransitions.Remove(userId);
        _deathScreenEligible.Remove(userId);
        _persistentGhostTransitions.Remove(userId);
        _authorizedPersistentGhostAttempts.Remove(userId);
    }
}
