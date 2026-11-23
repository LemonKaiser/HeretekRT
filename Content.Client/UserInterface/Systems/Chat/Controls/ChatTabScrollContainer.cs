using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

/// <summary>
/// Horizontal clip container for chat tabs that scrolls with the mouse wheel without rendering any scrollbar.
/// </summary>
public sealed partial class ChatTabScrollContainer : Container
{
    private const float ScrollStep = 48f;
    private const float SmoothSpeed = 16f;

    [Dependency] private IConfigurationManager _config = default!;

    private float _currentOffset;
    private float _targetOffset;
    private float _maxScroll;
    private bool _reducedMotion;
    private Control? _pendingEnsureVisible;

    public ChatTabScrollContainer()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _config.OnValueChanged(CCVars.ReducedMotion, OnReducedMotionChanged, true);
    }

    protected override void ExitedTree()
    {
        _config.UnsubValueChanged(CCVars.ReducedMotion, OnReducedMotionChanged);
        base.ExitedTree();
    }

    private void OnReducedMotionChanged(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        if (_reducedMotion && !MathHelper.CloseTo(_currentOffset, _targetOffset))
        {
            _currentOffset = _targetOffset;
            InvalidateArrange();
        }
    }

    public void EnsureChildVisible(Control child)
    {
        _pendingEnsureVisible = child;
        InvalidateArrange();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var constraint = new Vector2(float.PositiveInfinity, availableSize.Y);
        var maxHeight = 0f;
        foreach (var child in Children)
        {
            child.Measure(constraint);
            maxHeight = MathF.Max(maxHeight, child.DesiredSize.Y);
        }

        return new Vector2(0f, maxHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        MeasureOverride(finalSize);

        var contentWidth = 0f;
        foreach (var child in Children)
        {
            contentWidth = MathF.Max(contentWidth, child.DesiredSize.X);
        }

        _maxScroll = MathF.Max(0f, contentWidth - finalSize.X);
        _targetOffset = Math.Clamp(_targetOffset, 0f, _maxScroll);
        _currentOffset = Math.Clamp(_currentOffset, 0f, _maxScroll);

        ArrangeChildren(finalSize, contentWidth);

        if (_pendingEnsureVisible != null)
        {
            var target = _pendingEnsureVisible;
            _pendingEnsureVisible = null;

            if (TryGetContentBoundsX(target, out var childLeft, out var childRight))
            {
                if (childLeft < _targetOffset)
                    _targetOffset = childLeft;
                else if (childRight > _targetOffset + finalSize.X)
                    _targetOffset = childRight - finalSize.X;

                _targetOffset = Math.Clamp(_targetOffset, 0f, _maxScroll);
                if (_reducedMotion)
                {
                    _currentOffset = _targetOffset;
                    ArrangeChildren(finalSize, contentWidth);
                }
            }
        }

        return finalSize;
    }

    private void ArrangeChildren(Vector2 finalSize, float contentWidth)
    {
        var arrangeSize = new Vector2(MathF.Max(contentWidth, finalSize.X), finalSize.Y);
        var rect = UIBox2.FromDimensions(new Vector2(-_currentOffset, 0f), arrangeSize);
        foreach (var child in Children)
        {
            child.Arrange(rect);
        }
    }

    private bool TryGetContentBoundsX(Control target, out float left, out float right)
    {
        var x = 0f;
        Control? node = target;
        while (node != null && node != this)
        {
            x += node.Position.X;
            node = node.Parent;
        }

        if (node != this)
        {
            left = 0f;
            right = 0f;
            return false;
        }

        left = x + _currentOffset;
        right = left + target.Size.X;
        return true;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        var delta = args.Delta.X != 0f ? args.Delta.X : -args.Delta.Y;
        if (_maxScroll > 0f && delta != 0f)
        {
            _targetOffset = Math.Clamp(_targetOffset + delta * ScrollStep, 0f, _maxScroll);
            if (_reducedMotion)
            {
                _currentOffset = _targetOffset;
                InvalidateArrange();
            }
        }

        // Always consume wheel input over the tab strip so it never zooms the game viewport.
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (MathF.Abs(_targetOffset - _currentOffset) <= 0.05f)
        {
            if (_currentOffset != _targetOffset)
            {
                _currentOffset = _targetOffset;
                InvalidateArrange();
            }

            return;
        }

        if (_reducedMotion)
        {
            _currentOffset = _targetOffset;
        }
        else
        {
            var lerp = MathF.Min(1f, args.DeltaSeconds * SmoothSpeed);
            _currentOffset = MathHelper.Lerp(_currentOffset, _targetOffset, lerp);
        }

        InvalidateArrange();
    }
}
