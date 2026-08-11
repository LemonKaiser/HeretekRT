using Robust.Shared.Network;

namespace Content.Server._WH40K.ClassProgression;

/// <summary>
/// Extension seam for audited server-owned point sources. Version 1 deliberately returns zero.
/// </summary>
public interface IWh40kAdditionalSkillPointSource
{
    int GetAdditionalSkillPoints(NetUserId userId);
}

public sealed class Wh40kNoAdditionalSkillPointSource : IWh40kAdditionalSkillPointSource
{
    public int GetAdditionalSkillPoints(NetUserId userId)
    {
        return 0;
    }
}
