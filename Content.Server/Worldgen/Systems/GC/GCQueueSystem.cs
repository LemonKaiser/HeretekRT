using System.Linq;
using Content.Server.Worldgen.Components.GC;
using Content.Server.Worldgen.Prototypes;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Worldgen.Systems.GC;

/// <summary>
///     This handles delayed garbage collection of entities, to avoid overloading the tick in particularly expensive cases.
/// </summary>
public sealed partial class GCQueueSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;

    [ViewVariables] private TimeSpan _maximumProcessTime = TimeSpan.Zero;

    [ViewVariables] private readonly Dictionary<string, GCQueueState> _queues = new();

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _cfg.OnValueChanged(CCVars.GCMaximumTimeMs, s => _maximumProcessTime = TimeSpan.FromMilliseconds(s),
            true);
    }

    /// <inheritdoc />CCVars
    public override void Update(float frameTime)
    {
        var overallWatch = new Stopwatch();
        var queueWatch = new Stopwatch();
        var queues = _queues.ToList();
        _random.Shuffle(queues); // Avert resource starvation by always processing in random order.
        overallWatch.Start();
        foreach (var (pId, state) in queues)
        {
            if (overallWatch.Elapsed > _maximumProcessTime)
                return;

            var proto = _proto.Index<GCQueuePrototype>(pId);
            if (!state.Draining)
            {
                if (state.Queue.Count < proto.MinDepthToProcess)
                    continue;

                state.Draining = true;
            }

            if (state.Queue.Count == 0)
            {
                state.Draining = false;
                continue;
            }

            queueWatch.Restart();

            // Mono Begin - Dynamic Queue Times (Like League of Legends)
            var entsOverDepth = Math.Max(1, state.Queue.Count - proto.MinDepthToProcess + 1);
            // We get a constant associated with the prototype, and multiply it by the count of objects in that prototype. Dynamic scaling, instead of static constants.
            var maxQueueTickTime = TimeSpan.FromMilliseconds(Math.Max(0.05, entsOverDepth * proto.TimeDeletePerObject));

            // Mono End

            while (queueWatch.Elapsed < maxQueueTickTime && state.Queue.Count != 0 &&
                   overallWatch.Elapsed < _maximumProcessTime)
            {
                var e = state.Queue.Dequeue();
                state.Queued.Remove(e);
                if (!Deleted(e))
                {
                    var ev = new TryCancelGC();
                    RaiseLocalEvent(e, ref ev);

                    if (!ev.Cancelled)
                        Del(e);
                }
            }

            if (state.Queue.Count == 0)
                state.Draining = false;
        }
    }

    /// <summary>
    ///     Attempts to GC an entity. This functions as QueueDel if it can't.
    /// </summary>
    /// <param name="e">Entity to GC.</param>
    public void TryGCEntity(EntityUid e)
    {
        if (!TryComp<GCAbleObjectComponent>(e, out var comp))
        {
            QueueDel(e); // not our problem :)
            return;
        }

        if (!_queues.TryGetValue(comp.Queue, out var state))
        {
            state = new GCQueueState();
            _queues[comp.Queue] = state;
        }

        if (!state.Queued.Add(e))
            return;

        var proto = _proto.Index<GCQueuePrototype>(comp.Queue);
        if (state.Queue.Count >= proto.Depth)
        {
            state.Queued.Remove(e);
            QueueDel(e); // whelp, too full.
            return;
        }

        if (proto.TrySkipQueue)
        {
            var ev = new TryGCImmediately();
            RaiseLocalEvent(e, ref ev);
            if (!ev.Cancelled)
            {
                state.Queued.Remove(e);
                QueueDel(e);
                return;
            }
        }

        state.Queue.Enqueue(e);
    }

    public string GetCleanupStatus()
    {
        var queued = 0;
        foreach (var state in _queues.Values)
            queued += state.Queue.Count;

        return $"{nameof(GCQueueSystem)}: queues={_queues.Count}, queued={queued}";
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _queues.Clear();
    }

    private sealed class GCQueueState
    {
        public readonly Queue<EntityUid> Queue = new();
        public readonly HashSet<EntityUid> Queued = new();
        public bool Draining;
    }
}

/// <summary>
///     Fired by GCQueueSystem to check if it can simply immediately GC an entity, for example if it was never fully
///     loaded.
/// </summary>
/// <param name="Cancelled">Whether or not the immediate deletion attempt was cancelled.</param>
[ByRefEvent]
[PublicAPI]
public record struct TryGCImmediately(bool Cancelled = false);

/// <summary>
///     Fired by GCQueueSystem to check if the collection of the given entity should be cancelled, for example it's chunk
///     being loaded again.
/// </summary>
/// <param name="Cancelled">Whether or not the deletion attempt was cancelled.</param>
[ByRefEvent]
[PublicAPI]
public record struct TryCancelGC(bool Cancelled = false);

