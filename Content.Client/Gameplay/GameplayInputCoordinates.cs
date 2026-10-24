using System;
using Robust.Shared.Map;

namespace Content.Client.Gameplay;

internal static class GameplayInputCoordinates
{
    public static bool IsKnownMap(MapCoordinates coordinates, Func<MapId?, bool> mapExists)
    {
        return coordinates.MapId != MapId.Nullspace && mapExists(coordinates.MapId);
    }
}
