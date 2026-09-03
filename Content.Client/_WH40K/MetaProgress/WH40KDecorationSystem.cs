using System;
using System.Collections.Generic;
using Content.Shared._WH40K.MetaProgress;

namespace Content.Client._WH40K.MetaProgress;

public sealed class WH40KDecorationSystem : EntitySystem
{
    private WH40KDecorationState? _state;
    private bool _requestInFlight;
    private long _lastRevision = -1;
    private long _serverEpoch;
    private long _nextSelectionRequestId;
    private readonly Dictionary<WH40KMetaDecorationCategory, long> _pendingSelections = new();

    public event Action<WH40KDecorationState>? StateUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WH40KDecorationStateEvent>(OnState);
    }

    public bool TryGetState(out WH40KDecorationState state)
    {
        if (_state != null)
        {
            state = _state;
            return true;
        }

        state = default!;
        return false;
    }

    public void EnsureState()
    {
        if (_state != null || _requestInFlight)
            return;

        _requestInFlight = true;
        RaiseNetworkEvent(new WH40KDecorationRequestStateEvent());
    }

    /// <summary>
    ///     Отправляет не более одного изменения на категорию до авторитетного ответа сервера.
    /// </summary>
    public bool Select(WH40KMetaDecorationCategory category, string decorationId)
    {
        if (string.IsNullOrWhiteSpace(decorationId))
            return false;

        if (_pendingSelections.ContainsKey(category))
            return false;

        var requestId = ++_nextSelectionRequestId;
        _pendingSelections.Add(category, requestId);
        RaiseNetworkEvent(new WH40KDecorationSetSelectionEvent(category, decorationId.Trim(), requestId));
        return true;
    }

    public bool IsSelectionPending(WH40KMetaDecorationCategory category)
    {
        return _pendingSelections.ContainsKey(category);
    }

    private void OnState(WH40KDecorationStateEvent ev, EntitySessionEventArgs args)
    {
        if (_serverEpoch != ev.State.ServerEpoch)
        {
            _serverEpoch = ev.State.ServerEpoch;
            _lastRevision = -1;
            _state = null;
            _pendingSelections.Clear();
        }

        if (ev.State.Revision < _lastRevision)
        {
            _requestInFlight = false;
            return;
        }

        _requestInFlight = false;
        _lastRevision = ev.State.Revision;
        if (ev.State.AcknowledgedSelectionRequestId != 0)
        {
            WH40KMetaDecorationCategory? acknowledgedCategory = null;
            foreach (var (category, requestId) in _pendingSelections)
            {
                if (requestId == ev.State.AcknowledgedSelectionRequestId)
                {
                    acknowledgedCategory = category;
                    break;
                }
            }

            if (acknowledgedCategory is { } completedCategory)
                _pendingSelections.Remove(completedCategory);
        }
        _state = ev.State;
        StateUpdated?.Invoke(ev.State);
    }
}
