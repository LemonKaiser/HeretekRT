using System.Linq;
using Content.Server.Chat.Managers;
using System.Threading.Tasks;
using Content.Server._WH40K.PersistentInventory;
using Content.Shared.CCVar;
using Robust.Server;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.ServerUpdates;

/// <summary>
/// Responsible for restarting the server periodically or for update, when not disruptive.
/// </summary>
/// <remarks>
/// This was originally only designed for restarting on *update*,
/// but now also handles periodic restarting to keep server uptime via <see cref="CCVars.ServerUptimeRestartMinutes"/>.
/// </remarks>
public sealed partial class ServerUpdateManager : IPostInjectInit
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IWatchdogApi _watchdog = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IBaseServer _server = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IEntityManager _entities = default!;

    private ISawmill _sawmill = default!;

    [ViewVariables]
    private bool _updateOnRoundEnd;

    private TimeSpan? _restartTime;

    private TimeSpan _uptimeRestart;
    private Task<PersistentInventoryShutdownResult>? _shutdownPreparation;
    private string? _shutdownReason;
    private bool _shutdownIssued;

    public void Initialize()
    {
        _watchdog.UpdateReceived += WatchdogOnUpdateReceived;
        _playerManager.PlayerStatusChanged += PlayerManagerOnPlayerStatusChanged;

        _cfg.OnValueChanged(
            CCVars.ServerUptimeRestartMinutes,
            minutes => _uptimeRestart = TimeSpan.FromMinutes(minutes),
            true);
    }

    public void Update()
    {
        if (UpdateShutdownPreparation())
            return;

        if (_restartTime != null)
        {
            if (_restartTime < _gameTiming.RealTime)
            {
                DoShutdown();
            }
        }
        else
        {
            if (ShouldShutdownDueToUptime())
            {
                ServerEmptyUpdateRestartCheck("uptime");
            }
        }
    }

    /// <summary>
    /// Notify that the round just ended, which is a great time to restart if necessary!
    /// </summary>
    /// <returns>True if the server is going to restart.</returns>
    public bool RoundEnded()
    {
        if (_updateOnRoundEnd || ShouldShutdownDueToUptime())
        {
            DoShutdown();
            return true;
        }

        return false;
    }

    private void PlayerManagerOnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        switch (e.NewStatus)
        {
            case SessionStatus.Connected:
                if (_restartTime != null)
                    _sawmill.Debug("Aborting server restart timer due to player connection");

                _restartTime = null;
                break;
            case SessionStatus.Disconnected:
                ServerEmptyUpdateRestartCheck("last player disconnect");
                break;
        }
    }

    private void WatchdogOnUpdateReceived()
    {
        _chatManager.DispatchServerAnnouncement(Loc.GetString("server-updates-received"));
        _updateOnRoundEnd = true;
        ServerEmptyUpdateRestartCheck("update notification");
    }

    /// <summary>
    ///     Checks whether there are still players on the server,
    /// and if not starts a timer to automatically reboot the server if an update is available.
    /// </summary>
    private void ServerEmptyUpdateRestartCheck(string reason)
    {
        // Can't simple check the current connected player count since that doesn't update
        // before PlayerStatusChanged gets fired.
        // So in the disconnect handler we'd still see a single player otherwise.
        var playersOnline = _playerManager.Sessions.Any(p => p.Status != SessionStatus.Disconnected);
        if (playersOnline || !(_updateOnRoundEnd || ShouldShutdownDueToUptime()))
        {
            // Still somebody online.
            return;
        }

        if (_restartTime != null)
        {
            // Do nothing because we already have a timer running.
            return;
        }

        var restartDelay = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.UpdateRestartDelay));
        _restartTime = restartDelay + _gameTiming.RealTime;

        _sawmill.Debug("Started server-empty restart timer due to {Reason}", reason);
    }

    private void DoShutdown()
    {
        if (_shutdownIssued || _shutdownPreparation != null)
            return;

        _sawmill.Debug($"Shutting down via {nameof(ServerUpdateManager)}!");
        var reasonKey = _updateOnRoundEnd ? "server-updates-shutdown" : "server-updates-shutdown-uptime";
        _shutdownReason = Loc.GetString(reasonKey);
        _shutdownPreparation = _entities.System<PersistentInventoryShutdownSystem>()
            .PrepareAsync(_shutdownReason);
    }

    private bool UpdateShutdownPreparation()
    {
        if (_shutdownPreparation == null)
            return _shutdownIssued;
        if (!_shutdownPreparation.IsCompleted)
            return true;

        try
        {
            var result = _shutdownPreparation.GetAwaiter().GetResult();
            _sawmill.Info(
                $"Persistent inventory shutdown barrier: eligible {result.Eligible}, saved {result.Saved}, " +
                $"failed {result.Failed}, timedOut {result.TimedOut}, remainingBound {result.RemainingBound}, " +
                $"epochClean {result.EpochMarkedClean}.");
        }
        catch (Exception exception)
        {
            _sawmill.Error(
                $"Persistent inventory shutdown barrier failed unexpectedly: {exception.GetType().Name}.");
        }

        _shutdownIssued = true;
        _server.Shutdown(_shutdownReason);
        return true;
    }

    private bool ShouldShutdownDueToUptime()
    {
        return _uptimeRestart != TimeSpan.Zero && _gameTiming.RealTime > _uptimeRestart;
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("restart");
    }
}
