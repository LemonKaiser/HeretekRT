using Content.Shared._WH40K.BorgModules;
using Content.Shared.Whitelist;

namespace Content.Shared.Silicons.Borgs.Components;

/// <summary>
/// Used for borg modules that require the presence or absence of specific module categories/tags.
/// </summary>
[RegisterComponent, Access(typeof(SharedBorgSystem), typeof(BorgModuleExtrasSystem))]
public sealed partial class BorgModuleWhitelistComponent : Component
{
    /// <summary>
    /// Examine text informing the player of what category this module belongs to and its compatibility rules.
    /// </summary>
    [DataField]
    public LocId? WhitelistInfo;

    /// <summary>
    /// Whitelist matching modules this module is incompatible with.
    /// </summary>
    [DataField]
    public EntityWhitelist? ModuleBlacklist;

    /// <summary>
    /// Whitelist matching prerequisite modules required for this module to be installed.
    /// </summary>
    [DataField]
    public EntityWhitelist? ModuleWhitelist;
}
