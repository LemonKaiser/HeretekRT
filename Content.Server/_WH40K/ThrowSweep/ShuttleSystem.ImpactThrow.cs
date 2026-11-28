using System.Numerics;
using Content.Shared._WH40K.ThrowSweep;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private Vector2 Wh40kCapImpactThrow(Vector2 throwVector)
    {
        var max = _cfg.GetCVar(Wh40kThrowSweepCVars.ImpactThrowMax);
        var length = throwVector.Length();
        if (max <= 0f || length <= max)
            return throwVector;

        return throwVector / length * max;
    }
}
