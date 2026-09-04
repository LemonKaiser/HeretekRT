using System.Numerics;
using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Destructible;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Gibbing.Events;
using Content.Shared.Materials;
using Content.Shared.Mining.Components;
using Content.Shared.Particles;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared._Shitmed.Targeting;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server.Particles;

/// <summary>
/// Turns completed physical damage and destruction into local, material-aware particle bursts.
/// This system observes post-mitigation damage only, so cancelled hits and fully absorbed damage stay silent.
/// </summary>
public sealed class MaterialParticleSystem : EntitySystem
{
    private static readonly HashSet<string> PhysicalDamageTypes = new(StringComparer.Ordinal)
    {
        "Blunt",
        "Slash",
        "Piercing",
        "Structural",
    };

    [Dependency] private readonly ParticleSpawnSystem _particles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<DestructibleComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<MetaDataComponent, MachineDeconstructedEvent>(OnMachineDeconstructed);
        SubscribeLocalEvent<EntityGibbedEvent>(OnEntityGibbed);
    }

    private void OnDamageChanged(Entity<DamageableComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta is not { } damageDelta || !args.DamageIncreased)
            return;

        var physicalDamage = GetPhysicalDamage(damageDelta);
        if (physicalDamage <= 0f)
            return;

        var anchor = GetVisualAnchor(ent.Owner);
        var material = ResolveMaterial(ent.Owner, anchor, ent.Comp);

        // Blood is deliberately a restrained visual response. Actual bleeding and puddles remain entirely owned by
        // BloodstreamSystem; this merely communicates a confirmed physical hit.
        if (material == ParticleSurfaceMaterial.Flesh &&
            !_random.Prob(Math.Clamp(0.05f + physicalDamage * 0.035f, 0.08f, 0.4f)))
        {
            return;
        }

        var coordinates = GetImpactCoordinates(ent.Owner, anchor, args);
        var profile = GetImpactProfile(args, physicalDamage);
        var parameters = new ParticleSpawnParameters(
            EmitAngle: GetEmissionAngle(coordinates, args.Origin),
            Intensity: profile.Intensity);

        _particles.SpawnMaterial(
            coordinates,
            profile.EffectSet,
            material,
            parameters: parameters,
            rateLimitSource: anchor,
            cooldown: TimeSpan.FromMilliseconds(material == ParticleSurfaceMaterial.Flesh ? 275 : 125));
    }

    private void OnDestroyed(Entity<DestructibleComponent> ent, ref DestructionEventArgs args)
    {
        SpawnDebris(ent.Owner, _transform.GetMapCoordinates(ent));
    }

    private void OnMachineDeconstructed(EntityUid uid, MetaDataComponent component, MachineDeconstructedEvent args)
    {
        SpawnDebris(uid, _transform.GetMapCoordinates(uid), 0.9f);
    }

    private void OnEntityGibbed(ref EntityGibbedEvent args)
    {
        var uid = args.Target;
        var anchor = GetVisualAnchor(uid);
        if (!TryComp<BloodstreamComponent>(anchor, out var bloodstream))
            return;

        // Gibbing is already complete when this event fires. Resolve only the cosmetic tint from the established blood
        // reagent; no solution, puddle or other gameplay state is created here.
        Color? tint = null;
        if (_proto.TryIndex<ReagentPrototype>(bloodstream.BloodReagent, out var reagent))
            tint = reagent.SubstanceColor;

        _particles.Spawn(
            _transform.GetMapCoordinates(uid),
            "HrtGibMist",
            parameters: new ParticleSpawnParameters(
                Color: tint,
                EmitAngle: _random.NextAngle(),
                Intensity: 1f),
            rateLimitSource: anchor,
            cooldown: TimeSpan.FromMilliseconds(400));
    }

    /// <summary>
    /// Emits a material-specific destruction burst. Kept public so completed RCD deconstruction can reuse the exact
    /// same material classifier after it has safely removed its target from play.
    /// </summary>
    public void SpawnDebris(EntityUid target, MapCoordinates coordinates, float intensity = 1.15f)
    {
        if (!Exists(target))
            return;

        var anchor = GetVisualAnchor(target);
        var parameters = new ParticleSpawnParameters(
            EmitAngle: _random.NextAngle(),
            Intensity: Math.Clamp(intensity, 0.6f, 1.35f));

        _particles.SpawnMaterial(
            coordinates,
            "HrtParticleDebrisSet",
            ResolveMaterial(target, anchor),
            parameters: parameters,
            rateLimitSource: anchor,
            cooldown: TimeSpan.FromMilliseconds(225));
    }

    private float GetPhysicalDamage(DamageSpecifier damage)
    {
        var total = 0f;
        foreach (var (type, value) in damage.DamageDict)
        {
            if (value > 0 && PhysicalDamageTypes.Contains(type))
                total += value.Float();
        }

        return total;
    }

    private EntityUid GetVisualAnchor(EntityUid target)
    {
        if (TryComp<BodyPartComponent>(target, out var part) &&
            part.Body is { } body &&
            Exists(body))
        {
            return body;
        }

        return target;
    }

    private ParticleSurfaceMaterial ResolveMaterial(
        EntityUid target,
        EntityUid visualAnchor,
        DamageableComponent? damageable = null)
    {
        if (HasComp<BloodstreamComponent>(visualAnchor))
            return ParticleSurfaceMaterial.Flesh;

        // Asteroid and ore prototypes currently inherit a metallic damage modifier in this fork. Their concrete
        // gameplay component is a more reliable source for a visual classification.
        if (HasComp<OreVeinComponent>(target) || HasComp<MiningScannerViewableComponent>(target))
            return ParticleSurfaceMaterial.Stone;

        if (TryComp<PhysicalCompositionComponent>(target, out var composition) &&
            TryResolveComposition(composition, out var compositionMaterial))
        {
            return compositionMaterial;
        }

        damageable ??= CompOrNull<DamageableComponent>(target);
        if (damageable?.DamageModifierSetId is not { } modifierSet)
            return ParticleSurfaceMaterial.Default;

        return modifierSet.Id switch
        {
            "Glass" or "RGlass" => ParticleSurfaceMaterial.Glass,
            "Wood" => ParticleSurfaceMaterial.Wood,
            "Rock" => ParticleSurfaceMaterial.Stone,
            "Metallic" or "StructuralMetallic" or "StructuralMetallicStrong" or "FlimsyMetallic" or
                "StrongMetallic" or "PerforatedMetallic" or "Electronic" => ParticleSurfaceMaterial.Metal,
            _ => ParticleSurfaceMaterial.Default,
        };
    }

    private static bool TryResolveComposition(
        PhysicalCompositionComponent composition,
        out ParticleSurfaceMaterial material)
    {
        var ids = composition.MaterialComposition.Keys;

        if (ContainsMaterial(ids, "Glass"))
        {
            material = ParticleSurfaceMaterial.Glass;
            return true;
        }

        if (ContainsMaterial(ids, "Ice"))
        {
            material = ParticleSurfaceMaterial.Ice;
            return true;
        }

        if (ContainsMaterial(ids, "Wood") || ContainsMaterial(ids, "Cardboard"))
        {
            material = ParticleSurfaceMaterial.Wood;
            return true;
        }

        if (ContainsMaterial(ids, "Plastic"))
        {
            material = ParticleSurfaceMaterial.Plastic;
            return true;
        }

        if (ContainsMaterial(ids, "Stone") || ContainsMaterial(ids, "Ore") ||
            ContainsMaterial(ids, "Diamond") || ContainsMaterial(ids, "Quartz"))
        {
            material = ParticleSurfaceMaterial.Stone;
            return true;
        }

        if (ContainsMaterial(ids, "Steel") || ContainsMaterial(ids, "Iron") ||
            ContainsMaterial(ids, "Metal") || ContainsMaterial(ids, "Gold") ||
            ContainsMaterial(ids, "Silver") || ContainsMaterial(ids, "Uranium") ||
            ContainsMaterial(ids, "Plasma") || ContainsMaterial(ids, "Brass") ||
            ContainsMaterial(ids, "Bananium"))
        {
            material = ParticleSurfaceMaterial.Metal;
            return true;
        }

        material = default;
        return false;
    }

    private static bool ContainsMaterial(IEnumerable<string> ids, string fragment)
    {
        return ids.Any(id => id.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private MapCoordinates GetImpactCoordinates(EntityUid target, EntityUid anchor, DamageChangedEvent args)
    {
        var anchorCoordinates = _transform.GetMapCoordinates(anchor);
        var partOffset = GetBodyPartOffset(args.TargetPart);

        // Physics exposes one collider for a humanoid, while the targeting system has already resolved a specific
        // limb. For a limb hit the semantic body-part point is more visually accurate than that collider's centre.
        if (partOffset != Vector2.Zero)
            return new MapCoordinates(anchorCoordinates.Position + partOffset, anchorCoordinates.MapId);

        if (args.ImpactCoordinates is { } reported && reported.MapId == anchorCoordinates.MapId &&
            Vector2.DistanceSquared(reported.Position, anchorCoordinates.Position) <= 2.25f)
        {
            return reported;
        }

        var position = anchorCoordinates.Position;
        if (args.Origin is { } origin && Exists(origin))
        {
            var originCoordinates = _transform.GetMapCoordinates(origin);
            var direction = position - originCoordinates.Position;
            if (originCoordinates.MapId == anchorCoordinates.MapId && direction.LengthSquared() > 0.0025f)
                position -= Vector2.Normalize(direction) * 0.22f;
        }

        return new MapCoordinates(position, anchorCoordinates.MapId);
    }

    private static Vector2 GetBodyPartOffset(TargetBodyPart? targetPart)
    {
        if (targetPart is not { } part || part == TargetBodyPart.All)
            return Vector2.Zero;

        return part switch
        {
            TargetBodyPart.Head => new Vector2(0f, 0.27f),
            TargetBodyPart.LeftArm => new Vector2(-0.22f, 0.05f),
            TargetBodyPart.LeftHand => new Vector2(-0.3f, -0.02f),
            TargetBodyPart.RightArm => new Vector2(0.22f, 0.05f),
            TargetBodyPart.RightHand => new Vector2(0.3f, -0.02f),
            TargetBodyPart.LeftLeg => new Vector2(-0.12f, -0.2f),
            TargetBodyPart.LeftFoot => new Vector2(-0.14f, -0.32f),
            TargetBodyPart.RightLeg => new Vector2(0.12f, -0.2f),
            TargetBodyPart.RightFoot => new Vector2(0.14f, -0.32f),
            _ => Vector2.Zero,
        };
    }

    private (ProtoId<ParticleEffectSetPrototype> EffectSet, float Intensity) GetImpactProfile(
        DamageChangedEvent args,
        float physicalDamage)
    {
        if (args.Tool is { } tool && Exists(tool))
        {
            // A thrown blade may also be embeddable, so classify it before generic projectiles.
            if (HasComp<ThrownItemComponent>(tool))
                return ("HrtParticleImpactThrown", Math.Clamp(0.30f + physicalDamage * 0.012f, 0.30f, 0.50f));

            if (HasComp<ProjectileComponent>(tool))
                return ("HrtParticleImpactSet", Math.Clamp(0.85f + physicalDamage * 0.035f, 0.85f, 1.35f));

            if (HasComp<MeleeWeaponComponent>(tool))
                return ("HrtParticleImpactMelee", Math.Clamp(0.50f + physicalDamage * 0.020f, 0.50f, 0.80f));

            if (HasComp<GunComponent>(tool))
                return ("HrtParticleImpactSet", Math.Clamp(0.85f + physicalDamage * 0.035f, 0.85f, 1.35f));
        }

        // Damage sources without a concrete tool remain quieter than a confirmed weapon impact.
        return ("HrtParticleImpactSet", Math.Clamp(0.60f + physicalDamage * 0.030f, 0.60f, 1.15f));
    }

    private Angle? GetEmissionAngle(MapCoordinates impact, EntityUid? origin)
    {
        if (origin is not { } source || !Exists(source))
            return null;

        var sourceCoordinates = _transform.GetMapCoordinates(source);
        // Debris reflects away from the impacted face. With no surface normal in this 2D damage event, the
        // inverse incoming vector (impact -> source) is the reliable visual approximation and prevents sparks
        // from appearing to pass through the target.
        var direction = sourceCoordinates.Position - impact.Position;
        if (sourceCoordinates.MapId != impact.MapId || direction.LengthSquared() <= 0.0025f)
            return null;

        return Angle.FromWorldVec(direction);
    }
}
