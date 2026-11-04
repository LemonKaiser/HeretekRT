using Robust.Shared.Configuration;

namespace Content.Shared._WH40K.ShuttleStorage;

[RegisterComponent]
public sealed partial class ShuttleStorageComponent : Component;

public static class ShuttleStorageLimits
{
    public const int ShortSide = 64;
    public const int LongSide = 96;
    public const int PreviewCells = ShortSide * LongSide;
    public const int Bytes = 1_000_000;
    public const int Schema = 1;

    public static bool FitsDimensions(long width, long height) => width > 0 && height > 0 &&
        Math.Min(width, height) <= ShortSide && Math.Max(width, height) <= LongSide;
}

[CVarDefs]
public static class ShuttleStorageCVars
{
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wh40k.shuttle_storage.enabled", true, CVar.SERVERONLY);
    public static readonly CVarDef<int> MaxEntities =
        CVarDef.Create("wh40k.shuttle_storage.max_entities", 2000, CVar.SERVERONLY);
    public static readonly CVarDef<int> MaxDepth =
        CVarDef.Create("wh40k.shuttle_storage.max_depth", 16, CVar.SERVERONLY);
    public static readonly CVarDef<float> Cooldown =
        CVarDef.Create("wh40k.shuttle_storage.cooldown", 10f, CVar.SERVERONLY);
}
