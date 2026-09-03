using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Relative directory in UserData used exclusively by mapper map transfers.
    /// </summary>
    public static readonly CVarDef<string>
        MapTransferRoot = CVarDef.Create("mapping.transfer.root", "Mapping", CVar.SERVERONLY);

    /// <summary>
    ///     Allows a mapper to upload a validated map to the configured root.
    /// </summary>
    public static readonly CVarDef<bool>
        MapTransferUploadEnabled = CVarDef.Create("mapping.transfer.upload_enabled", true, CVar.SERVERONLY);

    /// <summary>
    ///     Allows a mapper to download validated server maps.
    /// </summary>
    public static readonly CVarDef<bool>
        MapTransferDownloadEnabled = CVarDef.Create("mapping.transfer.download_enabled", true, CVar.SERVERONLY);
}
