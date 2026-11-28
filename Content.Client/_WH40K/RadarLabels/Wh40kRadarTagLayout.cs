using System.Numerics;

namespace Content.Client._WH40K.RadarLabels;

/// <summary>
/// Arranges radar contact tags so none overlap: isolated tags sit beside their contact, while colliding tags
/// merge into a stable vertical stack that splits apart again once room opens up.
/// </summary>
public sealed class Wh40kRadarTagLayout
{
    public struct Tag
    {
        public Vector2 Anchor;
        public Vector2 Position;
        public Vector2 Size;
        public float Rise;
        public Vector2 HeadSize;
        public int Priority;
        public int Key;
    }

    public sealed class Group
    {
        public readonly List<int> All = new();
        public readonly List<int> Rows = new();
        public int Hidden;
        public Vector2 Origin;
        public Box2 Box;
        public bool Alive;
        public readonly HashSet<int> WasIn = new();
        public bool Stacked => All.Count > 1;
    }

    public float Gap = 3f;
    public int MaxRows = 10;
    public float MoreHeight = 12f;
    public float StickyGap = 10f;
    public float SwapMargin = 8f;

    public readonly List<Group> Groups = new();
    private readonly List<Group> _pool = new();
    private readonly Stack<Group> _pending = new();
    private IReadOnlyList<Tag> _tags = Array.Empty<Tag>();
    private Vector2 _bounds;
    private Dictionary<int, int> _lastStack = new();
    private Dictionary<int, int> _lastRow = new();
    private Dictionary<int, int> _nextStack = new();
    private Dictionary<int, int> _nextRow = new();

    public static int FindHoverTarget(
        IReadOnlyList<Tag> tags, Vector2 cursor, float radius, int? previousKey, float switchMargin)
    {
        var radiusSquared = radius * radius;
        var nearest = -1;
        var nearestDistance = radiusSquared;
        var previous = -1;
        var previousDistance = 0f;

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];
            var distance = Vector2.DistanceSquared(tag.Anchor, cursor);
            if (distance > radiusSquared)
                continue;

            if (tag.Key == previousKey)
            {
                previous = i;
                previousDistance = distance;
            }

            if (nearest < 0 || distance < nearestDistance ||
                distance == nearestDistance && tag.Priority > tags[nearest].Priority)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        return previous >= 0 && MathF.Sqrt(previousDistance) <= MathF.Sqrt(nearestDistance) + switchMargin
            ? previous
            : nearest;
    }

    public void Solve(IReadOnlyList<Tag> tags, Vector2 bounds)
    {
        _tags = tags;
        _bounds = bounds;
        foreach (var group in Groups)
            _pool.Add(group);

        Groups.Clear();
        _pending.Clear();
        for (var i = 0; i < tags.Count; i++)
        {
            var group = Rent();
            group.All.Add(i);
            group.Rows.Add(i);
            var rise = new Vector2(0f, tags[i].Rise);
            var size = tags[i].Size + rise;
            var origin = ClampOrigin(tags[i].Position - rise, size);
            group.Origin = origin + rise;
            group.Box = Box2.FromDimensions(origin, size);
            if (_lastStack.TryGetValue(tags[i].Key, out var stack))
                group.WasIn.Add(stack);
            Groups.Add(group);
            _pending.Push(group);
        }

        while (_pending.TryPop(out var group))
        {
            if (!group.Alive)
                continue;

            var merged = false;
            foreach (var other in Groups)
            {
                if (other == group || !other.Alive || !Overlaps(group.Box, other.Box, group.WasIn.Overlaps(other.WasIn) ? Gap + StickyGap : Gap))
                    continue;

                group.All.AddRange(other.All);
                group.WasIn.UnionWith(other.WasIn);
                other.Alive = false;
                merged = true;
            }

            if (!merged)
                continue;

            Arrange(group);
            _pending.Push(group);
        }

        for (var i = Groups.Count - 1; i >= 0; i--)
        {
            if (Groups[i].Alive)
                continue;

            _pool.Add(Groups[i]);
            Groups.RemoveAt(i);
        }

        _nextStack.Clear();
        _nextRow.Clear();
        for (var g = 0; g < Groups.Count; g++)
        {
            if (!Groups[g].Stacked)
                continue;

            foreach (var index in Groups[g].All)
                _nextStack[tags[index].Key] = g;
            for (var row = 0; row < Groups[g].Rows.Count; row++)
                _nextRow[tags[Groups[g].Rows[row]].Key] = row;
        }

        (_lastStack, _nextStack) = (_nextStack, _lastStack);
        (_lastRow, _nextRow) = (_nextRow, _lastRow);
    }

    private Group Rent()
    {
        Group group;
        if (_pool.Count > 0)
        {
            group = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            group = new Group();
        }

        group.All.Clear();
        group.Rows.Clear();
        group.WasIn.Clear();
        group.Hidden = 0;
        group.Alive = true;
        return group;
    }

    private static bool Overlaps(Box2 a, Box2 b, float gap)
    {
        return a.Left < b.Right + gap && b.Left < a.Right + gap && a.Bottom < b.Top + gap && b.Bottom < a.Top + gap;
    }

    private Vector2 ClampOrigin(Vector2 origin, Vector2 size)
    {
        return Vector2.Clamp(origin, Vector2.Zero, Vector2.Max(Vector2.Zero, _bounds - size));
    }

    private void Arrange(Group group)
    {
        var tags = _tags;
        group.Rows.Clear();
        group.Rows.AddRange(group.All);
        if (group.Rows.Count > MaxRows)
        {
            group.Rows.Sort((a, b) => tags[b].Priority.CompareTo(tags[a].Priority));
            group.Rows.RemoveRange(MaxRows, group.Rows.Count - MaxRows);
        }

        group.Hidden = group.All.Count - group.Rows.Count;
        var lastRow = _lastRow;
        group.Rows.Sort((a, b) =>
        {
            var knownA = lastRow.TryGetValue(tags[a].Key, out var rowA);
            var knownB = lastRow.TryGetValue(tags[b].Key, out var rowB);
            if (knownA != knownB)
                return knownA ? -1 : 1;
            if (knownA && rowA != rowB)
                return rowA.CompareTo(rowB);
            var y = tags[a].Anchor.Y.CompareTo(tags[b].Anchor.Y);
            return y != 0 ? y : tags[a].Key.CompareTo(tags[b].Key);
        });

        for (var i = 1; i < group.Rows.Count; i++)
        {
            for (var j = i; j > 0 && tags[group.Rows[j - 1]].Anchor.Y > tags[group.Rows[j]].Anchor.Y + SwapMargin; j--)
                (group.Rows[j - 1], group.Rows[j]) = (group.Rows[j], group.Rows[j - 1]);
        }

        var origin = new Vector2(float.MaxValue);
        foreach (var index in group.All)
            origin = Vector2.Min(origin, tags[index].Position);

        var size = Vector2.Zero;
        foreach (var index in group.Rows)
        {
            size.X = MathF.Max(size.X, tags[index].Size.X);
            size.Y += tags[index].Size.Y + tags[index].Rise;
        }

        if (group.Hidden > 0)
            size.Y += MoreHeight;

        origin = ClampOrigin(origin, size);
        group.Origin = origin;
        group.Box = Box2.FromDimensions(origin, size);
    }
}
