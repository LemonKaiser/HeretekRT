using System;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Administration.Managers;

/// <summary>
/// A single authorization boundary for administrative operations that act on a player account.
/// Console commands, EUIs and external entry points must use this service instead of resolving
/// hierarchy rules independently.
/// </summary>
public interface IAdminAuthorizationManager
{
    ValueTask<AdminAuthorizationDecision> AuthorizeTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        AdminOperation operation,
        CancellationToken cancel = default);

    ValueTask<AdminAuthorizationDecision> AuthorizeTargetAsync(
        AdminHierarchyInfo actorHierarchy,
        NetUserId targetUserId,
        AdminOperation operation,
        CancellationToken cancel = default);

    ValueTask<bool> TryDenyTargetAsync(
        ICommonSession? actor,
        NetUserId targetUserId,
        AdminOperation operation,
        string? targetName = null,
        Action<string>? notify = null,
        CancellationToken cancel = default);
}

public readonly record struct AdminAuthorizationDecision(
    bool Allowed,
    AdminOperation Operation,
    AdminHierarchyDenyReason Reason)
{
    public static AdminAuthorizationDecision Allow(AdminOperation operation) =>
        new(true, operation, AdminHierarchyDenyReason.None);

    public static AdminAuthorizationDecision Deny(AdminOperation operation, AdminHierarchyDenyReason reason) =>
        new(false, operation, reason);
}

/// <summary>
/// Semantic names for sensitive operations.  This intentionally describes the business action,
/// not a console command, so a new UI/API/command cannot create a bypass by using a different
/// transport.
/// </summary>
public enum AdminOperation : byte
{
    GenericTarget,
    Ban,
    RoleBan,
    PermissionsAdmin,
    PermissionsRank,
    Wh40kRpgProgression,
    Wh40kClassProgression,
    Wh40kProfileRead,
    Wh40kScreenCheck,
    GhostPermission,
}

internal static class AdminOperationExtensions
{
    public static string GetLocalizationKey(this AdminOperation operation)
    {
        return operation switch
        {
            AdminOperation.Ban => "admin-authorization-operation-ban",
            AdminOperation.RoleBan => "admin-authorization-operation-role-ban",
            AdminOperation.PermissionsAdmin => "admin-authorization-operation-permissions-admin",
            AdminOperation.PermissionsRank => "admin-authorization-operation-permissions-rank",
            AdminOperation.Wh40kRpgProgression => "admin-authorization-operation-wh40k-rpg",
            AdminOperation.Wh40kClassProgression => "admin-authorization-operation-wh40k-class",
            AdminOperation.Wh40kProfileRead => "admin-authorization-operation-wh40k-profile",
            AdminOperation.Wh40kScreenCheck => "admin-authorization-operation-wh40k-screencheck",
            AdminOperation.GhostPermission => "admin-authorization-operation-ghost-permission",
            _ => "admin-authorization-operation-generic",
        };
    }
}
