using Content.Server.Administration.Managers;
using Content.Shared.Administration;

namespace Content.Server._WH40K.Administration;

internal static class WH40KStaffProtection
{
    public static bool CanUseMuteTools(AdminData? adminData)
    {
        return adminData?.HasFlag(AdminFlags.Admin) == true ||
               adminData?.HasFlag(AdminFlags.Moderator) == true;
    }

    public static bool HasHostBypass(AdminData? adminData, bool isPromotedHost)
    {
        return isPromotedHost || adminData?.IsHost == true;
    }

    public static bool ShouldBypassChatRateLimits(
        AdminData? activeAdminData,
        AdminData? anyAdminData,
        bool isPromotedHost)
    {
        return CanUseMuteTools(activeAdminData) || HasHostBypass(anyAdminData, isPromotedHost);
    }

    public static bool CanOverrideStaffAction(AdminHierarchyInfo actorHierarchy, AdminHierarchyInfo sourceHierarchy)
    {
        return !sourceHierarchy.Exists ||
               AdminHierarchyManager.CanManageTarget(actorHierarchy, sourceHierarchy).Allowed;
    }
}
