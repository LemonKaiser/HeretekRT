using Content.Server._WH40K.NpcPry;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCSteeringSystem
{
    private const int PryMaxAttempts = 3;
    private static readonly TimeSpan PryAttemptWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PryGiveUpTime = TimeSpan.FromSeconds(60);

    private bool Wh40kTryNotePryAttempt(EntityUid uid, EntityUid door)
    {
        var memory = EnsureComp<Wh40kNpcPryMemoryComponent>(uid);
        var now = _timing.CurTime;

        if (memory.Door != door)
        {
            memory.Door = door;
            memory.Attempts = 0;
            memory.GiveUpUntil = TimeSpan.Zero;
        }
        else if (now < memory.GiveUpUntil)
            return false;
        else if (now - memory.LastAttempt > PryAttemptWindow)
            memory.Attempts = 0;

        memory.LastAttempt = now;
        if (++memory.Attempts <= PryMaxAttempts)
            return true;

        memory.Attempts = 0;
        memory.GiveUpUntil = now + PryGiveUpTime;
        return false;
    }
}
