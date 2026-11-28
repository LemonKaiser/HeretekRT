using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WH40K.WolfChem.Narcotics;

/// <summary>
/// Eyelids for an opioid nod: the local player's view closes from the top and bottom, with soft edges, and snaps open
/// when the nod ends. A neuroware host dims into standby instead.
/// </summary>
public sealed partial class Wh40kNodOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    private const float Edge = 0.12f;
    private const int EdgeBands = 12;
    private const float MaxClosure = 0.97f;
    private const float HostDim = 0.55f;

    private readonly Wh40kIntoxicationSystem _system;
    private float _closure;
    private bool _host;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public Wh40kNodOverlay(Wh40kIntoxicationSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _closure = _system.NodClosure() * MaxClosure;
        if (_closure <= 0f)
            return false;

        _host = _player.LocalEntity is { } local && _system.IsNeurowareHost(local);

        return _entities.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var box = args.ViewportBounds;
        if (_host)
        {
            handle.DrawRect(box, Color.Black.WithAlpha(_closure * HostDim));
            return;
        }

        float top = box.Top;
        float bottom = box.Bottom;
        var height = bottom - top;
        var edge = height * Edge;

        var reach = _closure * (height / 2f + edge);
        var solid = reach - edge;
        if (solid > 0f)
        {
            handle.DrawRect(new UIBox2(box.Left, top, box.Right, top + solid), Color.Black);
            handle.DrawRect(new UIBox2(box.Left, bottom - solid, box.Right, bottom), Color.Black);
        }

        var band = edge / EdgeBands;
        for (var i = 0; i < EdgeBands; i++)
        {
            var from = solid + band * i;
            if (from + band <= 0f)
                continue;

            var color = Color.Black.WithAlpha(1f - (i + 0.5f) / EdgeBands);
            var start = Math.Max(0f, from);
            handle.DrawRect(new UIBox2(box.Left, top + start, box.Right, top + from + band), color);
            handle.DrawRect(new UIBox2(box.Left, bottom - from - band, box.Right, bottom - start), color);
        }
    }
}
