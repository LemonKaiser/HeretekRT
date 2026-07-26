using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.PDA;

public sealed class PdaTerminalCrtShaderContainer : Control
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "GadOsPdaCrt";

    private readonly IClyde _clyde;
    private readonly IPrototypeManager _prototypeManager;
    private readonly ShaderInstance _shader;

    private IRenderTexture? _target;

    public PdaTerminalCrtShaderContainer()
    {
        IoCManager.InjectDependencies(this);

        _clyde = IoCManager.Resolve<IClyde>();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _shader = _prototypeManager.Index(ShaderId).InstanceUnique();

        MouseFilter = MouseFilterMode.Ignore;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    protected override void RenderChildOverride(ref ControlRenderArguments args, int childIndex, Vector2i position)
    {
        // All child elements are rendered to a single texture in PostRenderChildren.
    }

    protected override void PostRenderChildren(ref ControlRenderArguments args)
    {
        var size = PixelSize;
        if (size.X <= 0 || size.Y <= 0)
            return;

        EnsureTarget(size);

        var renderHandle = args.Handle;
        var screenHandle = renderHandle.DrawingHandleScreen;
        var oldTransform = screenHandle.GetTransform();
        var oldShader = screenHandle.GetShader();
        var coordinateTransform = args.CoordinateTransform;

        renderHandle.RenderInRenderTarget(_target!, () =>
        {
            screenHandle.SetTransform(Matrix3x2.Identity);
            screenHandle.UseShader(null);

            for (var index = 0; index < ChildCount; index++)
            {
                var child = GetChild(index);
                var childPosition = (Vector2i) Vector2.Transform(child.PixelPosition, coordinateTransform);
                UserInterfaceManager.RenderControl(renderHandle, child, childPosition);
            }

            screenHandle.SetTransform(Matrix3x2.Identity);
            screenHandle.UseShader(null);
        }, Color.Transparent);

        screenHandle.SetTransform(Matrix3x2.Identity);
        screenHandle.UseShader(_shader);
        _shader.SetParameter("RenderSize", (Vector2) size);
        screenHandle.DrawTextureRect(_target!.Texture, UIBox2.FromDimensions(GlobalPixelPosition, size));
        screenHandle.UseShader(oldShader);
        screenHandle.SetTransform(oldTransform);
    }

    [Obsolete("Controls should only be removed from UI tree instead of being disposed")]
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _target?.Dispose();

        base.Dispose(disposing);
    }

    private void EnsureTarget(Vector2i size)
    {
        if (_target != null && _target.Size == size)
            return;

        _target?.Dispose();
        _target = _clyde.CreateRenderTarget(
            size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            nameof(PdaTerminalCrtShaderContainer));
    }
}
