using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

public sealed partial class AdminAuthorizationManager : IAdminAuthorizationManager
{
    [Dependency] private IAdminHierarchyManager _hierarchy = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill Sawmill => _logManager.GetSawmill("admin.authorization");

    public async ValueTask<AdminAuthorizationDecision> AuthorizeTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        AdminOperation operation,
        CancellationToken cancel = default)
    {
        // The server console has no player principal and is deliberately the explicit
        // break-glass entry point. Every player-originated action is checked.
        if (actor == null)
            return AdminAuthorizationDecision.Allow(operation);

        var hierarchy = await _hierarchy.CanManageAdminAsync(actor, targetUserId, cancel);
        return hierarchy.Allowed
            ? AdminAuthorizationDecision.Allow(operation)
            : AdminAuthorizationDecision.Deny(operation, hierarchy.Reason);
    }

    public async ValueTask<AdminAuthorizationDecision> AuthorizeTargetAsync(
        AdminHierarchyInfo actorHierarchy,
        NetUserId targetUserId,
        AdminOperation operation,
        CancellationToken cancel = default)
    {
        if (!actorHierarchy.Exists)
            return AdminAuthorizationDecision.Deny(operation, AdminHierarchyDenyReason.ActorNotAdmin);

        AdminHierarchyInfo targetHierarchy;
        if (_playerManager.TryGetSessionById(targetUserId, out var targetSession))
        {
            targetHierarchy = _hierarchy.GetAdminHierarchy(targetSession, includeDeAdmin: true);
        }
        else
        {
            var targetAdmin = await _db.GetAdminDataForAsync(targetUserId, cancel);
            targetHierarchy = targetAdmin == null
                ? AdminHierarchyInfo.Missing
                : _hierarchy.GetAdminHierarchy(targetAdmin);
        }

        if (!targetHierarchy.Exists)
            return AdminAuthorizationDecision.Allow(operation);

        var hierarchy = AdminHierarchyManager.CanManageTarget(actorHierarchy, targetHierarchy);
        return hierarchy.Allowed
            ? AdminAuthorizationDecision.Allow(operation)
            : AdminAuthorizationDecision.Deny(operation, hierarchy.Reason);
    }

    public async ValueTask<bool> TryDenyTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        AdminOperation operation,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default)
    {
        var decision = await AuthorizeTargetAsync(actor, targetUserId, operation, cancel);
        if (decision.Allowed || actor == null)
            return false;

        targetName = await ResolveTargetNameAsync(targetUserId, targetName, cancel);
        var action = Loc.GetString(operation.GetLocalizationKey());
        notify?.Invoke(Loc.GetString(
            "admin-hierarchy-action-denied",
            ("action", action),
            ("target", targetName)));

        var actorName = $"{actor.Name} ({actor.UserId})";
        Sawmill.Warning(
            "{0} was denied {1} on protected admin {2} ({3}): {4}",
            actorName,
            operation,
            targetName,
            targetUserId,
            decision.Reason);
        _chat.SendAdminAlert(Loc.GetString(
            "admin-hierarchy-action-denied-alert",
            ("actor", actorName),
            ("action", action),
            ("target", targetName)));
        return true;
    }

    private async ValueTask<string> ResolveTargetNameAsync(
        NetUserId targetUserId,
        string? targetName,
        CancellationToken cancel)
    {
        if (!string.IsNullOrWhiteSpace(targetName))
            return targetName;

        var playerRecord = await _db.GetPlayerRecordByUserId(targetUserId, cancel);
        return playerRecord?.LastSeenUserName ?? targetUserId.ToString();
    }
}
