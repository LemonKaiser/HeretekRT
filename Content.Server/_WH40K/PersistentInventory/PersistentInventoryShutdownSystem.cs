using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;

namespace Content.Server._WH40K.PersistentInventory;

public sealed record PersistentInventoryShutdownResult(
    int Eligible,
    int Saved,
    int Failed,
    bool TimedOut,
    int RemainingBound,
    bool EpochMarkedClean)
{
    public bool SafeToContinue => !TimedOut && Failed == 0 && RemainingBound == 0;
}

/// <summary>
/// Saves materialized inventories before controlled world or process cleanup.
/// A server epoch is marked clean only after every save completed and the database
/// confirms that this epoch has no remaining bound lives.
/// </summary>
public sealed partial class PersistentInventoryShutdownSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private PersistentInventoryManager _manager = default!;
    [Dependency] private PersistentInventorySaveSystem _save = default!;

    private readonly object _preparationLock = new();
    private Task<PersistentInventoryShutdownResult>? _preparation;

    public Task<PersistentInventoryShutdownResult> PrepareAsync(
        string reason,
        CancellationToken cancel = default)
    {
        lock (_preparationLock)
        {
            if (_preparation == null || _preparation.IsCompleted)
            {
                _preparation = PrepareOnceAsync(
                    reason,
                    PersistentInventorySaveSource.GracefulShutdown,
                    markEpochClean: true,
                    cancel);
            }

            return _preparation;
        }
    }

    public Task<PersistentInventoryShutdownResult> PrepareRoundRestartAsync(
        string reason,
        CancellationToken cancel = default)
    {
        return PrepareOnceAsync(
            reason,
            PersistentInventorySaveSource.RoundRestart,
            markEpochClean: false,
            cancel);
    }

    private async Task<PersistentInventoryShutdownResult> PrepareOnceAsync(
        string reason,
        PersistentInventorySaveSource source,
        bool markEpochClean,
        CancellationToken cancel)
    {
        if (!_configuration.GetCVar(CCVars.Wh40kPersistentInventoryEnabled))
            return new PersistentInventoryShutdownResult(0, 0, 0, false, 0, false);

        var timeout = TimeSpan.FromSeconds(
            Math.Max(1, _configuration.GetCVar(CCVars.Wh40kPersistentInventoryShutdownTimeoutSeconds)));
        var maximumParallel = Math.Max(
            1,
            _configuration.GetCVar(CCVars.Wh40kPersistentInventoryMaxConcurrentSaves));
        var sessions = _players.Sessions
            .Where(session =>
                session.Status != SessionStatus.Disconnected &&
                session.AttachedEntity is { Valid: true } body &&
                Exists(body) &&
                PersistentInventoryRollout.GetDecision(_configuration, session.UserId) ==
                PersistentInventoryRolloutDecision.Full)
            .ToArray();

        var timedOut = false;
        var gate = new SemaphoreSlim(maximumParallel, maximumParallel);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeoutSource.CancelAfter(timeout);

        var saves = sessions.Select(async session =>
        {
            var entered = false;
            try
            {
                await gate.WaitAsync(timeoutSource.Token);
                entered = true;
                if (session.Status == SessionStatus.Disconnected ||
                    session.AttachedEntity is not { Valid: true } body ||
                    !Exists(body))
                {
                    return false;
                }

                var result = await _save.TrySaveAsync(
                    new PersistentInventorySaveRequest(
                        session.UserId,
                        body,
                        session,
                        source,
                        null,
                        source == PersistentInventorySaveSource.RoundRestart
                            ? "round-restart"
                            : "graceful-shutdown",
                        null,
                        reason,
                        Force: true),
                    timeoutSource.Token);
                if (result.IsSuccess)
                    return true;

                _adminLog.Add(
                    LogType.Mind,
                    LogImpact.Extreme,
                    $"Persistent inventory {source} save failed for {session.UserId}: " +
                    $"{result.Status}, reason {reason}.");
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"Persistent inventory {source} save escaped for {session.UserId}: {exception}.");
                return false;
            }
            finally
            {
                if (entered)
                    gate.Release();
            }
        }).ToArray();

        var allSaves = Task.WhenAll(saves);
        bool[]? completedResults = null;
        try
        {
            completedResults = await allSaves.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancel.IsCancellationRequested;
            _ = ObserveLateSavesAsync(allSaves, source);
        }

        // A timeout counts every unfinished save exactly once as failed. Late task
        // completion is observed only for exceptions and cannot mutate this result.
        var terminalResults = completedResults ??
                              await Task.WhenAll(saves.Where(task => task.IsCompletedSuccessfully));
        var saved = terminalResults.Count(success => success);
        var failed = sessions.Length - saved;
        var remainingBound = int.MaxValue;
        var epochMarkedClean = false;
        if (!timedOut && failed == 0)
        {
            try
            {
                var bound = await _db.GetPersistentInventoryBoundAsync(CancellationToken.None);
                remainingBound = bound.Count(header => header.ServerEpoch == _manager.ServerEpoch);
                if (remainingBound == 0 && markEpochClean)
                {
                    epochMarkedClean = await _db.MarkPersistentInventoryServerEpochCleanAsync(
                        _manager.ServerEpoch,
                        CancellationToken.None);
                    if (!epochMarkedClean)
                        failed++;
                }
            }
            catch (Exception exception)
            {
                failed++;
                Log.Error(
                    $"Persistent inventory {source} final bound-life check failed: " +
                    $"{exception.GetType().Name}.");
            }
        }

        if (remainingBound == int.MaxValue)
            remainingBound = Math.Max(0, sessions.Length - saved);

        var result = new PersistentInventoryShutdownResult(
            sessions.Length,
            saved,
            failed,
            timedOut,
            remainingBound,
            epochMarkedClean);
        PersistentInventoryMetrics.DatabaseOperations
            .WithLabels("shutdown_barrier", result.SafeToContinue ? "success" : timedOut ? "timeout" : "partial")
            .Inc();
        _adminLog.Add(
            LogType.Mind,
            result.SafeToContinue ? LogImpact.High : LogImpact.Extreme,
            $"Persistent inventory {source} barrier completed: eligible {result.Eligible}, " +
            $"saved {result.Saved}, failed {result.Failed}, timedOut {result.TimedOut}, " +
            $"remainingBound {result.RemainingBound}, epochClean {result.EpochMarkedClean}, reason {reason}.");
        return result;
    }

    private async Task ObserveLateSavesAsync(Task allSaves, PersistentInventorySaveSource source)
    {
        try
        {
            await allSaves;
        }
        catch (Exception exception)
        {
            Log.Error($"Late persistent inventory {source} tasks escaped: {exception}.");
        }
    }
}
