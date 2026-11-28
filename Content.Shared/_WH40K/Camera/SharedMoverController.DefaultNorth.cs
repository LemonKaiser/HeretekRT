using Content.Shared._WH40K.Camera;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private Wh40kDefaultNorthSystem _wh40kDefaultNorth = default!;
}
