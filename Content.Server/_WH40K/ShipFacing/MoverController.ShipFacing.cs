using System.Numerics;
using Content.Server._WH40K.ShipFacing;
using Content.Server.Shuttles.Components;
using Content.Shared._WH40K.ShipFacing;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private Wh40kShipFacingSystem _wh40kShipFacing = default!;

    /// <summary>
    /// Turns the ship toward the pilot's cursor while they hold the facing key; manual turn keys take priority.
    /// </summary>
    private void Wh40kFaceCursor(Entity<PilotComponent> pilot, EntityUid shuttle, float frameTime, ref ShuttleInput input)
    {
        if (input.Rotation != 0f
            || !_wh40kShipFacing.TryGetTarget(pilot, out var target)
            || !_shuttleQuery.TryComp(shuttle, out var shuttleComp)
            || !PhysicsQuery.TryComp(shuttle, out var body))
            return;

        var xform = Transform(shuttle);
        if (xform.MapID != target.MapId)
            return;

        var center = Vector2.Transform(body.LocalCenter, _transform.GetWorldMatrix(xform));
        var rotation = Wh40kShipFacingSteering.TurnInput(
            _transform.GetWorldRotation(xform),
            body.AngularVelocity,
            center,
            target.Position,
            GetAngularAcceleration(shuttleComp, body),
            ShuttleComponent.MaxAngularVelocity,
            frameTime);

        input = input with { Rotation = rotation };
    }
}
