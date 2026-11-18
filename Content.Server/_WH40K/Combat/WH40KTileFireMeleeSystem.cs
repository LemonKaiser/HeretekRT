using Content.Server._ES.TileFires;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Combat;

public sealed partial class WH40KTileFireMeleeSystem : EntitySystem
{
    private const float MaximumReach = 1.5f;
    private const float SuppressionPerHit = 1.4f;
    private const float HotspotVolumePerHit = Atmospherics.CellVolume * 0.12f;
    private const float IgnitionChance = 0.05f;
    private const float IgnitionStacks = 0.75f;
    private static readonly TimeSpan SuppressionInterval = TimeSpan.FromSeconds(0.7);
    private static readonly SoundSpecifier SuppressionSound =
        new SoundPathSpecifier("/Audio/Effects/extinguish.ogg", AudioParams.Default.WithVolume(-10f));

    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ESTileFireSystem _tileFires = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CombatModeComponent, MeleeAttackEvent>(OnMeleeAttack);
    }

    private void OnMeleeAttack(Entity<CombatModeComponent> attacker, ref MeleeAttackEvent args)
    {
        if (args.ClickLocation is not { } click ||
            !TryComp<MeleeWeaponComponent>(args.Weapon, out var weapon) || weapon.Range <= 0f)
            return;

        var now = _timing.CurTime;
        if (TryComp<WH40KTileFireMeleeSuppressionComponent>(attacker, out var suppression) && now < suppression.NextHit)
            return;

        var position = _transform.ToMapCoordinates(click);
        if (!_map.TryFindGridAt(position, out var grid, out var mapGrid))
            return;

        var tile = _map.TileIndicesFor(grid, mapGrid, position);
        var hasTileFire = _tileFires.TryGetTileFire(grid, tile, out var fire) && fire.Comp.CanExtinguish;
        if ((!hasTileFire && !_atmos.IsHotspotActive(grid, tile)) ||
            !_interaction.InRangeUnobstructed(attacker, position, Math.Min(weapon.Range, MaximumReach)))
            return;

        suppression ??= EnsureComp<WH40KTileFireMeleeSuppressionComponent>(attacker);
        suppression.NextHit = now + SuppressionInterval;
        if (TryComp<FlammableComponent>(attacker, out var attackerFire) && !attackerFire.OnFire && _random.Prob(IgnitionChance))
        {
            _flammable.AdjustFireStacks(attacker, IgnitionStacks, attackerFire);
            _flammable.Ignite(attacker, hasTileFire ? fire.Owner : grid, attackerFire);
        }

        var suppressedTileFire = false;
        if (hasTileFire)
        {
            var previousStacks = fire.Comp.FireStacks;
            _flammable.AdjustFireStacks(fire, -SuppressionPerHit, fire.Comp);
            suppressedTileFire = !fire.Comp.OnFire || fire.Comp.FireStacks < previousStacks;
        }

        var suppressedHotspot = _atmos.TryReduceHotspotVolume(grid, tile, HotspotVolumePerHit);
        if (suppressedTileFire || suppressedHotspot)
            _audio.PlayPvs(SuppressionSound, _map.GridTileToLocal(grid, mapGrid, tile));
    }
}

[RegisterComponent]
public sealed partial class WH40KTileFireMeleeSuppressionComponent : Component
{
    public TimeSpan NextHit;
}
