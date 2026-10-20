using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Bosses;

public sealed class Wh40kBossHudControl : Control
{
    private readonly Texture _frame;
    private readonly Texture _fill;
    private readonly Texture _pointer;
    private readonly Font _titleFont;
    private readonly Font _smallFont;
    private float _trail;
    private NetEntity? _boss;

    public Wh40kBossHudData? State;
    public Vector2 ScreenDirection;
    public float Distance;
    public bool ShowPointer;
    public UIBox2 PointerBounds;

    public Wh40kBossHudControl()
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        _frame = resources.GetResource<TextureResource>("/Textures/_WH40K/UI/Bosses/boss-bar-frame.png").Texture;
        _fill = resources.GetResource<TextureResource>("/Textures/_WH40K/UI/Bosses/boss-bar-fill.png").Texture;
        _pointer = resources.GetResource<TextureResource>("/Textures/_WH40K/UI/Bosses/boss-pointer.png").Texture;
        var font = resources.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Regular.ttf");
        var titleFont = resources.GetResource<FontResource>("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf");
        _titleFont = new VectorFont(titleFont, 14);
        _smallFont = new VectorFont(font, 12);
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (State == null)
            return;
        var ratio = Math.Clamp(State.Health / MathF.Max(State.MaxHealth, 1), 0, 1);
        if (_boss != State.Boss)
        {
            _boss = State.Boss;
            _trail = ratio;
        }
        _trail = MathF.Max(ratio, _trail - args.DeltaSeconds * 0.5f);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (State == null || PixelSize.X < 120 || PixelSize.Y < 120)
            return;

        var width = MathF.Min(640f * UIScale, PixelSize.X * 0.7f);
        var scale = width / 720f;
        var left = (PixelSize.X - width) / 2;
        var top = 52f * UIScale;
        var ratio = Math.Clamp(State.Health / MathF.Max(State.MaxHealth, 1), 0, 1);
        var inner = new Vector2(left + 36f * scale, top + 13f * scale);
        var innerWidth = 648f * scale;
        var innerHeight = 12f * scale;
        handle.DrawRect(UIBox2.FromDimensions(inner, new Vector2(innerWidth, innerHeight)), Color.FromHex("#090b11"));
        handle.DrawRect(UIBox2.FromDimensions(inner, new Vector2(innerWidth * _trail, innerHeight)), Color.FromHex("#8c6c46"));
        if (ratio > 0)
            handle.DrawTextureRect(_fill, UIBox2.FromDimensions(inner, new Vector2(innerWidth * ratio, innerHeight)));
        handle.DrawTextureRect(_frame, UIBox2.FromDimensions(new Vector2(left, top), new Vector2(width, 40f * scale)));

        var title = Loc.GetString(State.Title);
        var titleTop = top - _titleFont.GetLineHeight(UIScale) - 6f * UIScale;
        DrawCentered(handle, _titleFont, title, new Vector2(PixelSize.X / 2, titleTop), Color.FromHex("#ead7b3"));
        var health = Loc.GetString("wh40k-boss-health", ("current", MathF.Ceiling(State.Health)), ("max", State.MaxHealth));
        if (State.Protected)
            health += " · " + Loc.GetString("wh40k-marine-protected");
        DrawCentered(handle, _smallFont, health, new Vector2(PixelSize.X / 2, top + 40f * scale + 4f * UIScale),
            Color.FromHex("#ddd6c9"));

        if (!ShowPointer || ScreenDirection.LengthSquared() < 0.001f)
            return;
        var direction = Vector2.Normalize(ScreenDirection);
        var centre = PointerBounds.Center;
        var half = Vector2.Max(PointerBounds.Size / 2 - new Vector2(62f, 76f) * UIScale, Vector2.One);
        var tx = MathF.Abs(direction.X) > 0.001f ? half.X / MathF.Abs(direction.X) : float.MaxValue;
        var ty = MathF.Abs(direction.Y) > 0.001f ? half.Y / MathF.Abs(direction.Y) : float.MaxValue;
        var point = centre + direction * MathF.Min(tx, ty);
        var angle = MathF.Atan2(direction.Y, direction.X) - MathF.PI / 2;
        var size = 36f * UIScale;
        var previousTransform = handle.GetTransform();
        handle.SetTransform(Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(point) * previousTransform);
        handle.DrawTextureRect(_pointer, UIBox2.FromDimensions(new Vector2(-size / 2), new Vector2(size)));
        handle.SetTransform(previousTransform);
        var distance = Loc.GetString("wh40k-boss-distance", ("distance", MathF.Round(Distance)));
        DrawCentered(handle, _smallFont, distance, point + new Vector2(0, 32f * UIScale), Color.FromHex("#e1c895"));
    }

    private void DrawCentered(DrawingHandleScreen handle, Font font, string text, Vector2 centreTop, Color color)
    {
        var dimensions = handle.GetDimensions(font, text, UIScale);
        var position = centreTop - new Vector2(dimensions.X / 2, 0);
        var outline = MathF.Max(1f, UIScale);
        var shadow = new Color(0f, 0f, 0f, 0.95f);
        handle.DrawString(font, position + new Vector2(-outline, 0), text, UIScale, shadow);
        handle.DrawString(font, position + new Vector2(outline, 0), text, UIScale, shadow);
        handle.DrawString(font, position + new Vector2(0, -outline), text, UIScale, shadow);
        handle.DrawString(font, position + new Vector2(0, outline), text, UIScale, shadow);
        handle.DrawString(font, position, text, UIScale, color);
    }
}
