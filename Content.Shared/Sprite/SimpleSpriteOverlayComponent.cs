using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Sprite;

/// <summary>
/// Adds a simple sprite overlay layer to an entity while present.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class SimpleSpriteOverlayComponent : Component
{
    /// <summary>
    /// Sprite specifier to overlay onto the entity.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier OverlaySprite = SpriteSpecifier.Invalid;

    /// <summary>
    /// Layer map key on the target entity's SpriteComponent.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string LayerMap = "simple_visual_overlay";

    /// <summary>
    /// Optional shader applied to the overlay layer.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Shader;
}
