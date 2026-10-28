using System.Linq;
using Content.Shared._WH40K.SectorMap.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.SectorMap;

internal static class KoronusArrivalPolicy
{
    public static bool IsEligible(KoronusSystemPrototype system, string startSystem) =>
        system.Enabled && system.ID != "Footfall" && system.ID != startSystem;

    public static bool IsValid(KoronusArrivalPrototype settings) =>
        float.IsFinite(settings.MinInterval) && settings.MinInterval > 0f &&
        float.IsFinite(settings.MaxInterval) && settings.MaxInterval >= settings.MinInterval && settings.MaxInterval <= 604800f &&
        float.IsFinite(settings.MinLifetime) && settings.MinLifetime > 0f &&
        float.IsFinite(settings.MaxLifetime) && settings.MaxLifetime >= settings.MinLifetime && settings.MaxLifetime <= 604800f &&
        float.IsFinite(settings.WarningTime) && settings.WarningTime >= 0f && settings.WarningTime <= 3600f;

    public static bool TryGetMapPath(string input, out ResPath path)
    {
        path = default;
        var value = input.Trim().Replace('\\', '/').TrimStart('/');
        var prefix = value.StartsWith("Maps/", StringComparison.OrdinalIgnoreCase) ? "Maps/" :
            value.StartsWith("SharedMaps/", StringComparison.OrdinalIgnoreCase) ? "SharedMaps/" : null;
        if (prefix == null)
            return false;

        value = prefix + value[prefix.Length..];
        if (value.Split('/').Any(part => part is "" or "." or ".."))
            return false;
        if (!value.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            value += ".yml";

        path = new ResPath('/' + value);
        return true;
    }
}
