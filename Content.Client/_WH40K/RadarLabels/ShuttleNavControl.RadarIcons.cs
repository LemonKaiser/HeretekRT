using System.Numerics;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WH40K.RadarLabels;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Tiles;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using SpaceDebrisComponent = Content.Server._NF.Worldgen.Components.Debris.SpaceDebrisComponent;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Draws on-scope contacts by classification: ships as heading chevrons, stations as hollow squares,
/// and wrecks/debris as crosses; rim and unknown signatures keep standard triangles.
/// </summary>
public partial class ShuttleNavControl
{
    private enum Wh40kContactKind : byte
    {
        Ship,
        Station,
        Wreck,
    }

    private readonly record struct Wh40kContactIcon(Wh40kContactKind Kind, Vector2 Position, Vector2 Heading, Color Color);

    private readonly List<Wh40kContactIcon> _wh40kContactIcons = new();
    private readonly Vector2[] _wh40kIconVertices = new Vector2[4];

    private RadarModeButton? _wh40kAllNamesButton;
    private bool _wh40kAllNames;

    private bool Wh40kQueueContactIcon(EntityUid grid, Vector2 uiPosition, Matrix3x2 gridToView, Color color, bool unknown)
    {
        if (unknown)
            return false;

        var kind = Wh40kContactKindOf(grid);
        var heading = Vector2.TransformNormal(new Vector2(0f, 1f), gridToView);
        heading = heading.LengthSquared() > 0f ? Vector2.Normalize(heading) : new Vector2(0f, -1f);

        if (EntManager.TryGetComponent(grid, out CompanyComponent? company) &&
            !string.IsNullOrEmpty(company.CompanyName) &&
            _prototypeManager.TryIndex<CompanyPrototype>(company.CompanyName, out var prototype))
        {
            color = prototype.Color;
        }

        _wh40kContactIcons.Add(new Wh40kContactIcon(kind, uiPosition * UIScale, heading, color));
        return true;
    }

    private Wh40kContactKind Wh40kContactKindOf(EntityUid grid)
    {
        if (EntManager.HasComponent<SpaceDebrisComponent>(grid))
            return Wh40kContactKind.Wreck;

        var playerShip = EntManager.HasComponent<ShuttleDeedComponent>(grid)
                         || EntManager.TryGetComponent(grid, out IFFComponent? iff) && (iff.Flags & IFFFlags.IsPlayerShuttle) != 0;
        if (playerShip)
            return Wh40kContactKind.Ship;

        if (EntManager.HasComponent<StationMemberComponent>(grid) || EntManager.HasComponent<ProtectedGridComponent>(grid))
            return Wh40kContactKind.Station;

        return EntManager.TryGetComponent(grid, out PhysicsComponent? body) && body.BodyType == BodyType.Static
            ? Wh40kContactKind.Station
            : Wh40kContactKind.Ship;
    }

    private void Wh40kDrawContactIcons(DrawingHandleScreen handle, bool draw)
    {
        if (draw)
        {
            var size = RadarBlipSize * 0.45f * UIScale;
            foreach (var icon in _wh40kContactIcons)
                Wh40kDrawContactIcon(handle, icon, size);
        }

        _wh40kContactIcons.Clear();
    }

    private void Wh40kDrawContactIcon(DrawingHandleScreen handle, Wh40kContactIcon icon, float size)
    {
        var p = icon.Position;
        switch (icon.Kind)
        {
            case Wh40kContactKind.Ship:
            {
                var forward = icon.Heading * size;
                var side = new Vector2(-icon.Heading.Y, icon.Heading.X) * size * 0.75f;
                _wh40kIconVertices[0] = p + forward;
                _wh40kIconVertices[1] = p - forward * 0.8f + side;
                _wh40kIconVertices[2] = p - forward * 0.35f;
                _wh40kIconVertices[3] = p - forward * 0.8f - side;
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _wh40kIconVertices, icon.Color);
                break;
            }
            case Wh40kContactKind.Station:
            {
                var half = size * 0.75f;
                handle.DrawRect(new UIBox2(p.X - half, p.Y - half, p.X + half, p.Y + half), icon.Color, false);
                handle.DrawRect(new UIBox2(p.X - half * 0.3f, p.Y - half * 0.3f, p.X + half * 0.3f, p.Y + half * 0.3f), icon.Color);
                break;
            }
            case Wh40kContactKind.Wreck:
            {
                var half = size * 0.65f;
                handle.DrawLine(p + new Vector2(-half, -half), p + new Vector2(half, half), icon.Color);
                handle.DrawLine(p + new Vector2(-half, half), p + new Vector2(half, -half), icon.Color);
                break;
            }
        }
    }

    private void AddWh40kAllNamesButton(BoxContainer buttons)
    {
        _wh40kAllNames = Wh40kRadarCfg.GetCVar(Wh40kRadarCVars.RadarAllNames);
        var button = _wh40kAllNamesButton = CreateRadarModeButton(RadarModeButtonIcon.Wh40kAllNames);
        button.ToolTip = Loc.GetString("wh40k-radar-all-names-tooltip");
        button.ToggleMode = true;
        button.Pressed = _wh40kAllNames;
        button.OnToggled += args =>
        {
            _wh40kAllNames = args.Pressed;
            Wh40kRadarCfg.SetCVar(Wh40kRadarCVars.RadarAllNames, args.Pressed);
            Wh40kRadarCfg.SaveToFile();
        };
        buttons.AddChild(button);
    }

    public sealed partial class RadarModeButton
    {
        private static void Wh40kDrawAllNames(DrawingHandleScreen handle, Vector2 center, float radius, Color ink)
        {
            for (var row = -1; row <= 1; row++)
            {
                var y = center.Y + row * radius * 0.55f;
                handle.DrawRect(new UIBox2(center.X - radius * 0.8f, y - radius * 0.12f, center.X - radius * 0.56f, y + radius * 0.12f), ink);
                handle.DrawLine(new Vector2(center.X - radius * 0.35f, y), new Vector2(center.X + radius * 0.8f, y), ink);
            }
        }
    }
}
