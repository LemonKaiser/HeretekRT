using System.Numerics;
using Content.Client._WH40K.RadarLabels;
using Content.Shared._WH40K.RadarLabels;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Collects radar labels across grids, beacons, docks, and waypoints, then renders them cleanly with
/// non-overlapping stacking, hover details, and smooth fade transitions.
/// </summary>
public partial class ShuttleNavControl
{
    private static readonly Color Wh40kStackMoreColor = Color.FromHex("#AD9F86");
    private const float Wh40kTagHoverRadius = 32f;
    private const float Wh40kTagHoverSwitchMargin = 4f;
    private const float Wh40kCrowdRadius = 24f;
    private const float Wh40kTagCloseRange = 750f;
    private const float Wh40kTagFade = 0.15f;

    private IConfigurationManager? _wh40kRadarCfg;
    private IConfigurationManager Wh40kRadarCfg => _wh40kRadarCfg ??= IoCManager.Resolve<IConfigurationManager>();

    private readonly Wh40kRadarTagLayout _wh40kTagLayout = new();
    private readonly List<Wh40kRadarTagLayout.Tag> _wh40kTags = new();
    private readonly List<Wh40kTagDraw> _wh40kTagDraws = new();
    private readonly List<Wh40kTagText> _wh40kTagLines = new();
    private readonly List<Wh40kTagText> _wh40kTagAbove = new();
    private readonly Dictionary<string, int> _wh40kTagKeyCounts = new();

    private readonly List<Wh40kRadarTagLayout.Tag> _wh40kShownTags = new();
    private readonly List<int> _wh40kShownIndex = new();
    private readonly List<int> _wh40kQuietIndex = new();
    private readonly Dictionary<int, float> _wh40kTagAlpha = new();
    private readonly HashSet<int> _wh40kTagSeen = new();
    private readonly List<int> _wh40kTagGone = new();
    private TimeSpan _wh40kLastTagFrame;
    private int? _wh40kFocusedTagKey;

    private readonly record struct Wh40kTagText(string Text, float Scale, Color Color, Vector2 Size);

    private struct Wh40kTagDraw
    {
        public int FirstLine;
        public int LineCount;
        public int FirstAbove;
        public int AboveCount;
        public float HeadTextWidth;
        public bool Always;
        public bool Keyed;
        public bool Full;
        public float Alpha;
        public Action<DrawingHandleScreen, Vector2>? DrawHeadExtra;
        public Action<DrawingHandleScreen, Vector2>? DrawAlone;
    }

    private void Wh40kBeginTag(Vector2 anchor, Vector2 position, int priority = 0, bool always = false, int? key = null)
    {
        _wh40kTags.Add(new Wh40kRadarTagLayout.Tag
        {
            Anchor = anchor,
            Position = position,
            Priority = priority,
            Key = HashCode.Combine(1, key ?? 0),
        });
        _wh40kTagDraws.Add(new Wh40kTagDraw
        {
            FirstLine = _wh40kTagLines.Count,
            FirstAbove = _wh40kTagAbove.Count,
            Always = always,
            Keyed = key != null,
            Full = true,
            Alpha = 1f,
        });
    }

    private void Wh40kTagLine(DrawingHandleScreen handle, string text, float scale, Color color)
    {
        var size = handle.GetDimensions(Font, text, scale);
        _wh40kTagLines.Add(new Wh40kTagText(text, scale, color, size));
        var index = _wh40kTags.Count - 1;
        var tag = _wh40kTags[index];
        var draw = _wh40kTagDraws[index];
        if (draw.LineCount == 0)
        {
            draw.HeadTextWidth = size.X;
            tag.HeadSize += size;
        }

        draw.LineCount++;
        tag.Size = new Vector2(MathF.Max(tag.Size.X, size.X), tag.Size.Y + size.Y);
        _wh40kTags[index] = tag;
        _wh40kTagDraws[index] = draw;
    }

    private Vector2? Wh40kTagCursor => _isMouseInside && Wh40kHoverReveal
        ? (UserInterfaceManager.MousePositionScaled.Position - GlobalPosition) * UIScale
        : null;

    private void Wh40kDrawTags(DrawingHandleScreen handle, Vector2? cursor)
    {
        var now = _timing.RealTime;
        var step = Math.Clamp((float) (now - _wh40kLastTagFrame).TotalSeconds, 0f, 0.1f) / Wh40kTagFade;
        _wh40kLastTagFrame = now;

        Wh40kDropTagsOffScope();
        if (_wh40kTags.Count == 0)
        {
            _wh40kFocusedTagKey = null;
            Wh40kClearTags();
            return;
        }

        var iconsOnly = Wh40kRadarCfg.GetCVar(Wh40kRadarCVars.RadarIconsOnly);
        if (!iconsOnly && !Wh40kRadarCfg.GetCVar(Wh40kRadarCVars.RadarSmartTags))
        {
            _wh40kFocusedTagKey = null;
            for (var i = 0; i < _wh40kTags.Count; i++)
                Wh40kDrawTag(handle, i);

            Wh40kClearTags();
            return;
        }

        Wh40kKeyTags();
        var allNames = _wh40kAllNames;

        var radius = Wh40kTagHoverRadius * UIScale;
        var focus = cursor is { } point
            ? Wh40kRadarTagLayout.FindHoverTarget(_wh40kTags, point, radius, _wh40kFocusedTagKey, Wh40kTagHoverSwitchMargin * UIScale)
            : -1;
        _wh40kFocusedTagKey = focus >= 0 ? _wh40kTags[focus].Key : null;

        _wh40kShownTags.Clear();
        _wh40kShownIndex.Clear();
        _wh40kQuietIndex.Clear();
        _wh40kTagSeen.Clear();
        for (var i = 0; i < _wh40kTags.Count; i++)
        {
            var tag = _wh40kTags[i];
            var draw = _wh40kTagDraws[i];
            var shown = allNames || i == focus
                || !iconsOnly && (draw.Always || cursor is { } at && Vector2.DistanceSquared(tag.Anchor, at) <= radius * radius);
            draw.Full = i == focus;
            if (draw.Full)
            {
                tag.Priority = int.MaxValue;
            }
            else
            {
                tag.Size = tag.HeadSize;
                tag.Rise = 0f;
            }
            _wh40kTags[i] = tag;

            var alpha = _wh40kTagAlpha.GetValueOrDefault(tag.Key);
            alpha = shown ? MathF.Min(1f, alpha + step) : MathF.Max(0f, alpha - step);
            _wh40kTagAlpha[tag.Key] = alpha;
            _wh40kTagSeen.Add(tag.Key);
            draw.Alpha = alpha;
            _wh40kTagDraws[i] = draw;

            if (shown)
            {
                _wh40kShownTags.Add(tag);
                _wh40kShownIndex.Add(i);
            }
            else
            {
                _wh40kQuietIndex.Add(i);
            }
        }

        _wh40kTagGone.Clear();
        foreach (var key in _wh40kTagAlpha.Keys)
        {
            if (!_wh40kTagSeen.Contains(key))
                _wh40kTagGone.Add(key);
        }

        foreach (var key in _wh40kTagGone)
            _wh40kTagAlpha.Remove(key);

        Wh40kDrawQuietTags(handle, !iconsOnly);
        Wh40kDrawShownTags(handle);
        Wh40kClearTags();
    }

    private void Wh40kKeyTags()
    {
        _wh40kTagKeyCounts.Clear();
        for (var i = 0; i < _wh40kTags.Count; i++)
        {
            var draw = _wh40kTagDraws[i];
            if (draw.Keyed)
                continue;

            var name = draw.LineCount > 0 ? _wh40kTagLines[draw.FirstLine].Text : string.Empty;
            var seen = _wh40kTagKeyCounts.GetValueOrDefault(name);
            _wh40kTagKeyCounts[name] = seen + 1;
            var tag = _wh40kTags[i];
            tag.Key = HashCode.Combine(name, seen);
            _wh40kTags[i] = tag;
        }
    }

    private void Wh40kDrawQuietTags(DrawingHandleScreen handle, bool counts)
    {
        foreach (var index in _wh40kQuietIndex)
        {
            if (_wh40kTagDraws[index].Alpha > 0f)
                Wh40kDrawTag(handle, index);
        }

        if (!counts)
            return;

        var crowd = Wh40kCrowdRadius * UIScale;
        var scale = UIScale * 0.7f;
        for (var i = 0; i < _wh40kQuietIndex.Count; i++)
        {
            var seed = _wh40kQuietIndex[i];
            if (seed < 0)
                continue;

            var sum = _wh40kTags[seed].Anchor;
            var count = 1;
            for (var j = i + 1; j < _wh40kQuietIndex.Count; j++)
            {
                var other = _wh40kQuietIndex[j];
                if (other < 0 || (_wh40kTags[other].Anchor - _wh40kTags[seed].Anchor).LengthSquared() > crowd * crowd)
                    continue;

                sum += _wh40kTags[other].Anchor;
                count++;
                _wh40kQuietIndex[j] = -1;
            }

            if (count < 2)
                continue;

            var middle = sum / count;
            handle.DrawString(
                Font,
                middle + new Vector2(6f, -14f) * UIScale,
                Loc.GetString("wh40k-radar-tags-crowd", ("count", count)),
                scale,
                Wh40kStackMoreColor);
        }
    }

    private void Wh40kDrawShownTags(DrawingHandleScreen handle)
    {
        if (_wh40kShownTags.Count == 0)
            return;

        var moreScale = UIScale * 0.7f;
        _wh40kTagLayout.Gap = 3f * UIScale;
        _wh40kTagLayout.StickyGap = 10f * UIScale;
        _wh40kTagLayout.SwapMargin = 8f * UIScale;
        _wh40kTagLayout.MoreHeight = handle.GetDimensions(Font, "+", moreScale).Y;
        _wh40kTagLayout.Solve(_wh40kShownTags, PixelSize);

        foreach (var group in _wh40kTagLayout.Groups)
        {
            if (!group.Stacked)
            {
                Wh40kDrawTag(handle, _wh40kShownIndex[group.All[0]], group.Origin);
                continue;
            }

            var box = group.Box;
            var backing = 0f;
            foreach (var row in group.Rows)
                backing = MathF.Max(backing, _wh40kTagDraws[_wh40kShownIndex[row]].Alpha);

            var y = box.Bottom;
            foreach (var row in group.Rows)
            {
                var index = _wh40kShownIndex[row];
                var tag = _wh40kTags[index];
                Wh40kDrawTag(handle, index, new Vector2(box.Left, y + tag.Rise));
                y += tag.Size.Y + tag.Rise;
            }

            if (group.Hidden > 0)
            {
                handle.DrawString(
                    Font,
                    new Vector2(box.Left, y),
                    Loc.GetString("wh40k-radar-tags-more", ("count", group.Hidden)),
                    moreScale,
                    Wh40kFade(Wh40kStackMoreColor, backing));
            }
        }
    }

    private bool Wh40kHoverReveal => Wh40kRadarCfg.GetCVar(Wh40kRadarCVars.RadarHoverReveal);

    private static Color Wh40kFade(Color color, float alpha) => color.WithAlpha(color.A * alpha);

    private void Wh40kClearTags()
    {
        _wh40kTags.Clear();
        _wh40kTagDraws.Clear();
        _wh40kTagLines.Clear();
        _wh40kTagAbove.Clear();
    }

    private bool Wh40kOnScope(Vector2 point)
    {
        var half = new Vector2(PixelSize.X, PixelSize.Y) / 2f;
        if (half.X <= 0f || half.Y <= 0f)
            return false;

        var offset = (point - half) / half;
        return offset.LengthSquared() <= 1f;
    }

    private void Wh40kDropTagsOffScope()
    {
        var kept = 0;
        for (var i = 0; i < _wh40kTags.Count; i++)
        {
            if (!Wh40kOnScope(_wh40kTags[i].Anchor))
                continue;

            _wh40kTags[kept] = _wh40kTags[i];
            _wh40kTagDraws[kept] = _wh40kTagDraws[i];
            kept++;
        }

        _wh40kTags.RemoveRange(kept, _wh40kTags.Count - kept);
        _wh40kTagDraws.RemoveRange(kept, _wh40kTagDraws.Count - kept);
    }

    private void Wh40kDrawTag(DrawingHandleScreen handle, int index, Vector2? position = null)
    {
        var tag = _wh40kTags[index];
        var draw = _wh40kTagDraws[index];
        var origin = position ?? tag.Position;
        var at = origin;
        var lines = draw.Full ? draw.LineCount : Math.Min(1, draw.LineCount);
        for (var i = 0; i < lines; i++)
        {
            var line = _wh40kTagLines[draw.FirstLine + i];
            handle.DrawString(Font, at, line.Text, line.Scale, Wh40kFade(line.Color, draw.Alpha));
            if (i == 0)
                draw.DrawHeadExtra?.Invoke(handle, at + new Vector2(draw.HeadTextWidth, 0f));

            at.Y += line.Size.Y;
        }

        if (draw.Full)
        {
            var above = origin;
            for (var i = 0; i < draw.AboveCount; i++)
            {
                var line = _wh40kTagAbove[draw.FirstAbove + i];
                above.Y -= line.Size.Y;
                handle.DrawString(Font, above, line.Text, line.Scale, Wh40kFade(line.Color, draw.Alpha));
            }
        }

        draw.DrawAlone?.Invoke(handle, origin);
    }
}
