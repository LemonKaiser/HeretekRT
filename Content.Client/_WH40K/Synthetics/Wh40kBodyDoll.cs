using System.Numerics;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.Synthetics;

/// <summary>
/// Interactive 96x96 body status doll for the Mechanicus chassis panel, reusing the Shitmed targeting
/// status sprites and per-limb click hitboxes via crisp TextureRect layers. Clicking a limb highlights
/// it and notifies listeners.
/// </summary>
public sealed class Wh40kBodyDoll : PanelContainer
{
    private static readonly (TargetBodyPart Part, string Style, Vector2 Size, Vector2 Offset)[] Layout =
    {
        (TargetBodyPart.Head, "TargetDollButtonHead", new Vector2(27, 27), new Vector2(33, 6)),
        (TargetBodyPart.Torso, "TargetDollButtonChest", new Vector2(33, 27), new Vector2(30, 27)),
        (TargetBodyPart.Groin, "TargetDollButtonGroin", new Vector2(33, 21), new Vector2(30, 48)),
        (TargetBodyPart.RightArm, "TargetDollButtonRightArm", new Vector2(21, 24), new Vector2(18, 27)),
        (TargetBodyPart.RightHand, "TargetDollButtonRightHand", new Vector2(18, 18), new Vector2(18, 45)),
        (TargetBodyPart.LeftArm, "TargetDollButtonLeftArm", new Vector2(21, 24), new Vector2(54, 27)),
        (TargetBodyPart.LeftHand, "TargetDollButtonLeftHand", new Vector2(18, 18), new Vector2(57, 45)),
        (TargetBodyPart.RightLeg, "TargetDollButtonRightLeg", new Vector2(18, 24), new Vector2(30, 57)),
        (TargetBodyPart.RightFoot, "TargetDollButtonRightFoot", new Vector2(24, 15), new Vector2(24, 75)),
        (TargetBodyPart.LeftLeg, "TargetDollButtonLeftLeg", new Vector2(18, 24), new Vector2(45, 57)),
        (TargetBodyPart.LeftFoot, "TargetDollButtonLeftFoot", new Vector2(24, 15), new Vector2(45, 75)),
    };

    private readonly SpriteSystem _sprites;
    private readonly Dictionary<TargetBodyPart, TextureRect> _partLayers = new();
    private readonly Dictionary<TargetBodyPart, TextureRect> _selectionOverlays = new();

    public event Action<TargetBodyPart>? OnPartSelected;

    public Wh40kBodyDoll()
    {
        _sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();

        SetSize = new Vector2(96, 96);
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Center;

        var buttons = new PanelContainer { SetSize = new Vector2(96, 96) };

        foreach (var (part, style, size, offset) in Layout)
        {
            var name = part.ToString().ToLowerInvariant();
            var partLayer = new TextureRect
            {
                Texture = _sprites.Frame0(new SpriteSpecifier.Rsi(
                    new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"), $"{name}_0")),
                Stretch = TextureRect.StretchMode.Scale,
                SetSize = new Vector2(96, 96),
                Visible = true,
                MouseFilter = MouseFilterMode.Ignore,
            };
            AddChild(partLayer);
            _partLayers[part] = partLayer;

            var overlay = new TextureRect
            {
                Texture = _sprites.Frame0(new SpriteSpecifier.Rsi(
                    new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"), $"{name}_0")),
                Stretch = TextureRect.StretchMode.Scale,
                SetSize = new Vector2(96, 96),
                Visible = false,
                MouseFilter = MouseFilterMode.Ignore,
                Modulate = Color.FromHex("#ffcf6b"),
            };
            AddChild(overlay);
            _selectionOverlays[part] = overlay;

            var button = new TextureButton
            {
                SetSize = size,
                Margin = new Thickness(offset.X, offset.Y, 0, 0),
                HorizontalAlignment = HAlignment.Left,
                VerticalAlignment = VAlignment.Top,
                MouseFilter = MouseFilterMode.Stop,
            };
            button.AddStyleClass(style);
            var captured = part;
            button.OnPressed += _ => OnPartSelected?.Invoke(captured);
            buttons.AddChild(button);
        }

        AddChild(buttons);
    }

    public void SetBody(Dictionary<TargetBodyPart, TargetIntegrity>? body)
    {
        if (body == null)
        {
            foreach (var layer in _partLayers.Values)
                layer.Visible = false;
            return;
        }

        foreach (var (part, layer) in _partLayers)
        {
            if (!body.TryGetValue(part, out var integrity))
            {
                layer.Visible = false;
                continue;
            }

            var name = part.ToString().ToLowerInvariant();
            var rsi = new SpriteSpecifier.Rsi(
                new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"),
                $"{name}_{(int) integrity}");
            layer.Texture = _sprites.Frame0(rsi);
            layer.Visible = true;
        }
    }

    public void SetSelected(TargetBodyPart? part)
    {
        foreach (var (key, overlay) in _selectionOverlays)
            overlay.Visible = key == part;
    }
}
