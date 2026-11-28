using Robust.Client.Graphics;

namespace Content.Client._WH40K.SectorMap;

[RegisterComponent]
public sealed partial class KoronusQuicksandVisualsComponent : Component
{
    public ShaderInstance Shader = default!;
    public float Progress;
}
