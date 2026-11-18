using Content.Client.Particles;
using Content.Shared._WH40K.Blood;
using Content.Shared._WH40K.Invisibility;
using Content.Shared.Particles;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Blood;

public sealed partial class WH40KBleedingVisualSystem : EntitySystem
{
    private static readonly ProtoId<ParticleEffectPrototype> BloodDrip = "HrtBloodDrip";

    [Dependency] private ParticleSystem _particles = default!;

    private readonly Dictionary<EntityUid, BleedingEffect> _effects = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WH40KBleedingVisualComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KBleedingVisualComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WH40KBleedingVisualComponent> entity, ref ComponentStartup args)
    {
        _effects[entity.Owner] = new BleedingEffect(entity.Comp);
    }

    private void OnShutdown(Entity<WH40KBleedingVisualComponent> entity, ref ComponentShutdown args)
    {
        if (_effects.Remove(entity.Owner, out var effect))
            _particles.RemoveParticle(effect.Emitter);
    }

    public override void FrameUpdate(float frameTime)
    {
        foreach (var (uid, effect) in _effects)
        {
            if (_particles.Quality == 0 || !CanEmit(uid))
            {
                _particles.RemoveParticle(effect.Emitter);
                effect.Emitter = null;
                continue;
            }

            var intensity = effect.Visual.Severity == WH40KBleedingVisualSeverity.Severe ? 1f : 0.55f;
            if (effect.Emitter is not { Exhausted: false } emitter)
            {
                effect.Emitter = _particles.CreateParticle(BloodDrip, uid,
                    new ParticleSpawnParameters(Color: effect.Visual.Color, Intensity: intensity));
                continue;
            }

            emitter.Intensity = intensity;
            emitter.ColorOverride = effect.Visual.Color;
        }
    }

    private bool CanEmit(EntityUid uid)
    {
        var meta = MetaData(uid);
        return (meta.Flags & MetaDataFlags.Detached) == 0 && !meta.EntityPaused &&
               TryComp(uid, out TransformComponent? xform) && xform.MapID != MapId.Nullspace &&
               TryComp(uid, out SpriteComponent? sprite) && sprite.AddToTree && sprite.Color.A > 0.01f &&
               !HasComp<WH40KPsykerInvisibilityComponent>(uid) &&
               !(TryComp(uid, out StealthComponent? stealth) && stealth.Enabled);
    }

    public override void Shutdown()
    {
        foreach (var effect in _effects.Values)
            _particles.RemoveParticle(effect.Emitter);

        _effects.Clear();
        base.Shutdown();
    }

    private sealed class BleedingEffect(WH40KBleedingVisualComponent visual)
    {
        public readonly WH40KBleedingVisualComponent Visual = visual;
        public ActiveEmitter? Emitter;
    }
}
