using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.OfferItem;

/// <summary>
/// Displays Arcane's offer reticle while the local player has an active item offer.
/// </summary>
public sealed class OfferItemCursorOverlay : Overlay
{
    private readonly OfferItemSystem _offerItem;

    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEyeManager _eye = default!;

    private readonly Texture _reticle;

    private static readonly Color ReticleColor = Color.White.WithAlpha(0.3f);
    private static readonly Color StrokeColor = Color.Black.WithAlpha(0.5f);
    private const float ReticleScale = 0.6f;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public OfferItemCursorOverlay(OfferItemSystem offerItem)
    {
        _offerItem = offerItem;
        IoCManager.InjectDependencies(this);

        var sprite = _entityManager.EntitySysManager.GetEntitySystem<SpriteSystem>();
        _reticle = sprite.Frame0(new SpriteSpecifier.Rsi(
            new ResPath("/Textures/_WH40K/Interface/give_item.rsi"),
            "give_item"));
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _offerItem.IsLocalOfferPending() && base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var mousePosition = _input.MouseScreenPosition;
        if (_eye.PixelToMap(mousePosition).MapId != args.MapId)
            return;

        var uiScale = (args.ViewportControl as Control)?.UIScale ?? 1f;
        var center = mousePosition.Position;
        var size = _reticle.Size * (Math.Min(1.25f, uiScale) * ReticleScale);
        var borderSize = size + new Vector2(7f);

        args.ScreenHandle.DrawTextureRect(
            _reticle,
            UIBox2.FromDimensions(center - size * 0.5f, size),
            StrokeColor);
        args.ScreenHandle.DrawTextureRect(
            _reticle,
            UIBox2.FromDimensions(center - borderSize * 0.5f, borderSize),
            ReticleColor);
    }
}
