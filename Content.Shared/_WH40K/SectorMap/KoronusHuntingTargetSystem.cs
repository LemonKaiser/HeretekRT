using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Shared._WH40K.SectorMap;

public sealed class KoronusHuntingTargetSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KoronusHuntingProjectileComponent, PreventCollideEvent>(OnPreventCollide);
    }

    public bool IsValidTarget(EntityUid target)
    {
        return !TerminatingOrDeleted(target) &&
               !HasComp<ActorComponent>(target) &&
               !(TryComp<MindContainerComponent>(target, out var mind) && mind.HasMind) &&
               TryComp<MobStateComponent>(target, out var mob) &&
               mob.CurrentState is MobState.Alive or MobState.Critical &&
               _factions.IsFactionHostile("NanoTrasen", (target, CompOrNull<NpcFactionMemberComponent>(target)));
    }

    private void OnPreventCollide(Entity<KoronusHuntingProjectileComponent> ent, ref PreventCollideEvent args)
    {
        // Walls still stop shots. Living allies, players of any faction and corpses do not.
        if ((HasComp<MobStateComponent>(args.OtherEntity) ||
             HasComp<MindContainerComponent>(args.OtherEntity) ||
             HasComp<ActorComponent>(args.OtherEntity)) && !IsValidTarget(args.OtherEntity))
        {
            args.Cancelled = true;
        }
    }
}
