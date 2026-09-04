using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind.Components;
using Content.Shared.Teleportation.Systems;
using Content.Server.Particles;
using Content.Shared.Particles;
using Robust.Shared.Map;

namespace Content.Server.Teleportation;

public sealed partial class PortalSystem : SharedPortalSystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private ParticleSpawnSystem _particles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // TODO Move to shared
    protected override void LogTeleport(EntityUid portal, EntityUid subject, EntityCoordinates source,
        EntityCoordinates target)
    {
        // LogTeleport is reached only after SharedPortalSystem has validated the route and immediately before
        // it moves the subject. The two supplied coordinates are consequently the real departure and arrival.
        _particles.Spawn(
            _transform.ToMapCoordinates(source),
            "HrtWarpRiftBurst",
            parameters: new ParticleSpawnParameters(Intensity: 0.75f),
            rateLimitSource: portal,
            cooldown: TimeSpan.FromMilliseconds(150));
        _particles.Spawn(
            _transform.ToMapCoordinates(target),
            "HrtTeleporterArrival",
            parameters: new ParticleSpawnParameters(Intensity: 0.75f),
            attachedEntity: subject,
            rateLimitSource: portal,
            cooldown: TimeSpan.FromMilliseconds(150));

        if (HasComp<MindContainerComponent>(subject) && !HasComp<GhostComponent>(subject))
            _adminLogger.Add(LogType.Teleport, LogImpact.Low, $"{ToPrettyString(subject):player} teleported via {ToPrettyString(portal)} from {source} to {target}");
    }
}
