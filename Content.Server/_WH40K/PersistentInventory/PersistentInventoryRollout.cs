using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server._WH40K.PersistentInventory;

public enum PersistentInventoryRolloutMode
{
    Disabled = 0,
    DryRun = 1,
    Validated = 2,
    Production = 3,
}

public enum PersistentInventoryRolloutDecision
{
    Disabled = 0,
    DryRun = 1,
    Full = 2,
    Excluded = 3,
}

/// <summary>
/// Unified server-side gradual rollout policy. It holds no state and therefore
/// safely rereads CVars before irreversible save/restore phases.
/// </summary>
public static class PersistentInventoryRollout
{
    public static PersistentInventoryRolloutMode GetMode(IConfigurationManager configuration)
    {
        if (!configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled))
            return PersistentInventoryRolloutMode.Disabled;

        return ParseMode(configuration.GetCVar(CCVars.Wh40kPersistentInventoryRolloutMode));
    }

    public static PersistentInventoryRolloutDecision GetDecision(
        IConfigurationManager configuration,
        NetUserId userId)
    {
        return GetMode(configuration) switch
        {
            PersistentInventoryRolloutMode.Disabled => PersistentInventoryRolloutDecision.Disabled,
            PersistentInventoryRolloutMode.DryRun => PersistentInventoryRolloutDecision.DryRun,
            PersistentInventoryRolloutMode.Validated => PersistentInventoryRolloutDecision.Full,
            PersistentInventoryRolloutMode.Production =>
                IsInProductionBucket(
                    userId,
                    configuration.GetCVar(CCVars.Wh40kPersistentInventoryProductionPercentage))
                    ? PersistentInventoryRolloutDecision.Full
                    : PersistentInventoryRolloutDecision.Excluded,
            _ => PersistentInventoryRolloutDecision.Disabled,
        };
    }

    public static PersistentInventoryRolloutMode ParseMode(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "dry-run" or "dryrun" => PersistentInventoryRolloutMode.DryRun,
            "validated" => PersistentInventoryRolloutMode.Validated,
            "production" => PersistentInventoryRolloutMode.Production,
            _ => PersistentInventoryRolloutMode.Disabled,
        };
    }

    public static bool IsInProductionBucket(NetUserId userId, int percentage)
    {
        if (percentage <= 0)
            return false;
        if (percentage >= 100)
            return true;

        var bytes = userId.UserId.ToByteArray();
        var bucket = BitConverter.ToUInt32(bytes, 0) % 100;
        return bucket < percentage;
    }
}
