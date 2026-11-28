using Content.Server.Construction;
using Content.Shared._WH40K.Construction;

namespace Content.Server._WH40K.Construction;

/// <summary>
/// Crafts an item recipe back-to-back until the requested count finishes or a craft fails.
/// </summary>
public sealed partial class Wh40kCraftRepeatSystem : EntitySystem
{
    [Dependency] private ConstructionSystem _construction = default!;

    private readonly Dictionary<EntityUid, (string Prototype, int Remaining)> _jobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<Wh40kCraftRepeatRequestEvent>(OnRequest);
    }

    private async void OnRequest(Wh40kCraftRepeatRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } user)
            return;

        var running = _jobs.ContainsKey(user);
        _jobs[user] = (ev.PrototypeName, Math.Clamp(ev.Count, 1, Wh40kCraftRepeatRequestEvent.MaxCount));

        if (running)
            return;

        try
        {
            while (_jobs.TryGetValue(user, out var job) && job.Remaining > 0)
            {
                _jobs[user] = (job.Prototype, job.Remaining - 1);

                if (args.SenderSession.AttachedEntity != user
                    || !await _construction.TryStartItemConstruction(job.Prototype, user))
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Repeat construction failed for {ToPrettyString(user)}: {e}");
        }
        finally
        {
            _jobs.Remove(user);
        }
    }
}
