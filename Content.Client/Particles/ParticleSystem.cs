using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared.CCVar;
using Content.Shared.Particles;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client.Particles;

/// <summary>
/// Client-side particle simulation, quality scheduling and rendering integration.
/// </summary>
public sealed partial class ParticleSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IResourceCache _resource = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly List<ActiveEmitter> _emitters = new();
    private readonly List<ActiveEmitter> _emissionOrder = new();
    private readonly List<ActiveEmitter> _trimOrder = new();
    private readonly Dictionary<ParticleEffectPrototype, (Texture[] Frames, float[] Delays)> _frameCache = new();
    private readonly Dictionary<ParticleEffectPrototype, CompiledParticleEffect> _runtimeCache = new();
    private readonly HashSet<ParticleEffectPrototype> _frameResolveFailures = new();
    private ParticleOverlay _particleOverlay = default!;

    private int _quality;
    private int _manualBudget;
    private int _globalBudget;
    private int _liveParticleCount;
    private float _liveParticleCost;
    private int _remainingSpawnAllowance = ParticleRuntimeMath.MaxParticlesSpawnedPerFrame;
    private bool _rebalanceRequested;
    private ParticleStatistics _statistics;

    private const float TeleportThresholdSquared = 16f * 16f;
    private const float MinParticleLifetimeSeconds = 0.05f;
    private const int HardMaxParticles = 10000;

    private uint _nextHandle = 1;

    /// <summary>
    /// Raised after the client changes the particle quality preset.
    /// Long-running visual systems use it to recreate emitters removed by the <c>Off</c> preset.
    /// </summary>
    public event Action? QualityChanged;

    public int Quality => _quality;
    public ParticleStatistics Statistics => _statistics;

    public override void Initialize()
    {
        base.Initialize();

        _particleOverlay = new ParticleOverlay(this);
        _overlay.AddOverlay(_particleOverlay);

        _cfg.OnValueChanged(CCVars.ParticleQuality, OnQualityChanged, invokeImmediately: true);
        _cfg.OnValueChanged(CCVars.ParticleGlobalBudget, OnManualBudgetChanged, invokeImmediately: true);
        _proto.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cfg.UnsubValueChanged(CCVars.ParticleQuality, OnQualityChanged);
        _cfg.UnsubValueChanged(CCVars.ParticleGlobalBudget, OnManualBudgetChanged);
        _proto.PrototypesReloaded -= OnPrototypesReloaded;
        _overlay.RemoveOverlay(_particleOverlay);
        KillAll();
        _frameCache.Clear();
        _runtimeCache.Clear();
        _frameResolveFailures.Clear();
        _particleOverlay.ClearCaches();
    }

    private void OnQualityChanged(int quality)
    {
        var normalized = Math.Clamp(quality, 0, 3);
        var changed = _quality != normalized;
        _quality = normalized;
        RecomputeBudget();
        _rebalanceRequested = true;

        if (changed)
            QualityChanged?.Invoke();
    }

    private void OnManualBudgetChanged(int budget)
    {
        _manualBudget = Math.Clamp(budget, 0, HardMaxParticles);
        RecomputeBudget();
        _rebalanceRequested = true;
    }

    private void RecomputeBudget()
    {
        var qualityBudget = _quality switch
        {
            1 => 2250,
            2 => 5500,
            3 => HardMaxParticles,
            _ => 0,
        };
        _globalBudget = Math.Min(qualityBudget, _manualBudget);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<ParticleEffectPrototype>())
            return;

        _frameCache.Clear();
        _runtimeCache.Clear();
        _frameResolveFailures.Clear();

        foreach (var emitter in _emitters)
        {
            emitter.Runtime = GetRuntime(emitter.Proto);
            emitter.ParticleCost = ParticleRuntimeMath.GetParticleCost(emitter.Proto);
            ResolveFrames(emitter);
        }

        _rebalanceRequested = true;
    }

    public IReadOnlyList<ActiveEmitter> GetEmitters() => _emitters;

    /// <summary>
    /// Immediately destroys every active emitter and live particle.
    /// </summary>
    public int KillAll()
    {
        var count = _emitters.Count;
        _emitters.Clear();
        _emissionOrder.Clear();
        _trimOrder.Clear();
        _liveParticleCount = 0;
        _liveParticleCost = 0f;
        _statistics = default;
        return count;
    }

    public ActiveEmitter? SpawnEffect(
        [ForbidLiteral] ProtoId<ParticleEffectPrototype> effectId,
        MapCoordinates coords,
        EntityUid? attachedEntity = null,
        Color? colorOverride = null)
    {
        return SpawnEffect(
            effectId,
            coords,
            new ParticleSpawnParameters(Color: colorOverride),
            attachedEntity);
    }

    /// <summary>
    /// Spawns a particle emitter with complete per-instance presentation parameters.
    /// </summary>
    public ActiveEmitter? SpawnEffect(
        [ForbidLiteral] ProtoId<ParticleEffectPrototype> effectId,
        MapCoordinates coords,
        ParticleSpawnParameters parameters,
        EntityUid? attachedEntity = null)
    {
        if (coords.MapId == MapId.Nullspace || !_proto.Resolve(effectId, out var proto) || _quality == 0 ||
            !ParticleSpawnLimits.TryNormalize(parameters, out var normalized))
            return null;

        var emitter = CreateEmitter(proto, coords, attachedEntity, normalized);
        ActivateEmitter(emitter);
        return emitter;
    }

    public override void FrameUpdate(float frameTime)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _remainingSpawnAllowance = ParticleRuntimeMath.MaxParticlesSpawnedPerFrame;
        var ageDelta = Math.Max(frameTime, 0f);
        var simulationDelta = ParticleRuntimeMath.ClampSimulationDelta(frameTime);
        var emissionDelta = ParticleRuntimeMath.ClampEmissionDelta(frameTime);
        var simulatedParticles = 0;
        var emittedParticles = 0;

        if (_quality == 0)
        {
            KillAll();
            return;
        }

        var eye = _eye.CurrentEye;
        var eyeMap = eye.Position.MapId;
        var eyePosition = eye.Position.Position;

        _emissionOrder.Clear();
        for (var i = _emitters.Count - 1; i >= 0; i--)
        {
            var emitter = _emitters[i];
            UpdateEmitterPosition(emitter, ageDelta);
            AdvanceEmitterAge(emitter, ageDelta);
            UpdateLod(emitter, eyeMap, eyePosition);
            simulatedParticles += SimulateEmitter(emitter, ageDelta, simulationDelta);

            if (emitter.Exhausted && emitter.Particles.Count == 0)
            {
                SwapRemoveAt(i);
                continue;
            }

            if (!emitter.Exhausted && emitter.LodMultiplier > 0f)
                _emissionOrder.Add(emitter);
        }

        if (_rebalanceRequested)
        {
            RebalanceToBudget();
            _rebalanceRequested = false;
        }

        _emissionOrder.Sort(EmissionComparison);
        foreach (var emitter in _emissionOrder)
            emittedParticles += EmitForEmitter(emitter, emissionDelta);

        var milliseconds = ElapsedMilliseconds(startedAt);
        _statistics = _statistics with
        {
            ActiveEmitters = _emitters.Count,
            LiveParticles = _liveParticleCount,
            LiveCost = _liveParticleCost,
            SimulatedParticles = simulatedParticles,
            EmittedParticles = emittedParticles,
            SimulationMilliseconds = milliseconds,
        };
    }

    internal void ReportRenderStatistics(int culledParticles, int drawCalls, int drawnParticles, float preparationMilliseconds)
    {
        _statistics = _statistics with
        {
            CulledParticles = culledParticles,
            DrawCalls = drawCalls,
            DrawnParticles = drawnParticles,
            RenderPreparationMilliseconds = preparationMilliseconds,
        };
    }

    private static int EmissionComparison(ActiveEmitter left, ActiveEmitter right)
    {
        var priority = ParticleRuntimeMath.ResolvePriority(right.Proto)
            .CompareTo(ParticleRuntimeMath.ResolvePriority(left.Proto));
        if (priority != 0)
            return priority;

        var distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
        return distance != 0 ? distance : left.Handle.CompareTo(right.Handle);
    }

    private static int TrimComparison(ActiveEmitter left, ActiveEmitter right)
    {
        var priority = ParticleRuntimeMath.ResolvePriority(left.Proto)
            .CompareTo(ParticleRuntimeMath.ResolvePriority(right.Proto));
        if (priority != 0)
            return priority;

        var lod = left.LodMultiplier.CompareTo(right.LodMultiplier);
        if (lod != 0)
            return lod;

        return right.DistanceSquared.CompareTo(left.DistanceSquared);
    }

    private void SwapRemoveAt(int index)
    {
        var last = _emitters.Count - 1;
        if (index != last)
            _emitters[index] = _emitters[last];
        _emitters.RemoveAt(last);
    }

    private void UpdateEmitterPosition(ActiveEmitter emitter, float dt)
    {
        if (emitter.AttachedEntity is not { } attachedEntity)
        {
            if (!emitter.VelocityInitialized)
            {
                emitter.PreviousPosition = emitter.MapCoords.Position;
                emitter.VelocityInitialized = true;
            }
            return;
        }

        if (Deleted(attachedEntity))
        {
            emitter.Exhausted = true;
            emitter.AttachedEntity = null;
            return;
        }

        var attachedCoordinates = _transform.GetMapCoordinates(attachedEntity);
        var newPosition = attachedCoordinates.Position;

        // A Vector2 alone has no map identity. Keeping particles created on the old map while moving this emitter
        // to a new MapId makes world-space particles render at the same numeric coordinates on the destination.
        // Local-space particles would be carried there too, so clear the transient pool and resume cleanly.
        if (emitter.MapCoords.MapId != attachedCoordinates.MapId)
        {
            ClearEmitterParticles(emitter);
            emitter.MapCoords = attachedCoordinates;
            emitter.EmitterVelocity = Vector2.Zero;
            emitter.PreviousPosition = newPosition;
            emitter.VelocityInitialized = true;
            return;
        }

        emitter.MapCoords = attachedCoordinates;

        if (!emitter.VelocityInitialized)
        {
            emitter.PreviousPosition = newPosition;
            emitter.VelocityInitialized = true;
            return;
        }

        var delta = newPosition - emitter.PreviousPosition;
        if (dt > 0f)
        {
            emitter.EmitterVelocity = delta.LengthSquared() > TeleportThresholdSquared
                ? Vector2.Zero
                : delta / dt;
        }
        emitter.PreviousPosition = newPosition;
    }

    private static void AdvanceEmitterAge(ActiveEmitter emitter, float ageDelta)
    {
        emitter.Age += ageDelta;
        if (emitter.Exhausted)
            return;

        var duration = (float) emitter.Proto.Duration.TotalSeconds;
        if (duration > 0f && emitter.Age >= duration)
            emitter.Exhausted = true;
    }

    private void UpdateLod(ActiveEmitter emitter, MapId eyeMap, Vector2 eyePosition)
    {
        if (emitter.MapCoords.MapId != eyeMap)
        {
            emitter.DistanceSquared = float.PositiveInfinity;
            emitter.LodMultiplier = 0f;
            return;
        }

        emitter.DistanceSquared = Vector2.DistanceSquared(emitter.MapCoords.Position, eyePosition);
        var distance = MathF.Sqrt(emitter.DistanceSquared);
        var proto = emitter.Proto;
        emitter.LodMultiplier = ParticleRuntimeMath.GetQualityMultiplier(ParticleRuntimeMath.ResolvePriority(proto), _quality)
            * ParticleRuntimeMath.GetDistanceMultiplier(distance, proto.MaxDistance, proto.BoundsRadius);
    }

    private int SimulateEmitter(ActiveEmitter emitter, float ageDelta, float simulationDelta)
    {
        var proto = emitter.Proto;
        var drag = proto.Drag;
        var constantForce = proto.ConstantForce;
        var terminalSpeed = proto.TerminalSpeed;
        var gravity = proto.Gravity;
        var noiseStrength = proto.NoiseStrength;
        var noiseFrequency = proto.NoiseFrequency;
        var dragMultiplier = drag > 0f ? MathF.Exp(-drag * simulationDelta) : 1f;
        var terminalSpeedSquared = terminalSpeed > 0f ? terminalSpeed * terminalSpeed : 0f;

        if (emitter.AnimationDuration > 0f)
            emitter.AnimationTime = (emitter.AnimationTime + simulationDelta) % emitter.AnimationDuration;

        var simulated = 0;
        var particles = CollectionsMarshal.AsSpan(emitter.Particles);
        for (var i = 0; i < emitter.Particles.Count;)
        {
            ref var particle = ref particles[i];
            particle.Age += ageDelta;
            simulated++;

            if (particle.Age >= particle.Lifetime)
            {
                RemoveParticleAt(emitter, i);
                continue;
            }

            SimulateParticle(ref particle, simulationDelta, dragMultiplier, constantForce, terminalSpeedSquared,
                terminalSpeed, gravity, noiseStrength, noiseFrequency, emitter.Runtime);
            i++;
        }

        return simulated;
    }

    private int EmitForEmitter(ActiveEmitter emitter, float emissionDelta)
    {
        if (emitter.Exhausted || emitter.LodMultiplier <= 0f)
            return 0;

        var proto = emitter.Proto;
        var scaledMaximum = GetScaledMaximum(emitter, proto.MaxCount);
        var emitted = 0;

        for (var index = 0; index < proto.Bursts.Count; index++)
        {
            if (emitter.FiredBursts[index] || emitter.Age < (float) proto.Bursts[index].Time.TotalSeconds)
                continue;

            var burst = proto.Bursts[index];
            emitted += EmitBurst(emitter, burst.Count, scaledMaximum);
            emitter.FiredBursts[index] = true;
        }

        var capacity = scaledMaximum - emitter.Particles.Count;
        if (capacity <= 0)
            return emitted;

        var duration = (float) proto.Duration.TotalSeconds;
        var emissionMultiplier = CompiledParticleEffect.Sample(
            emitter.Runtime.EmissionOverTime,
            duration > 0f ? emitter.Age / duration : emitter.Age,
            1f);
        emitter.EmitAccum += proto.EmissionRate * emissionMultiplier * emissionDelta * emitter.Intensity * emitter.LodMultiplier;
        var requested = (int) emitter.EmitAccum;
        emitter.EmitAccum -= requested;
        requested = Math.Min(requested, capacity);

        for (var i = 0; i < requested && TryEmitParticle(emitter); i++)
            emitted++;

        return emitted;
    }

    private int EmitBurst(ActiveEmitter emitter, int count, int scaledMaximum)
    {
        var requested = (int) Math.Ceiling(count * emitter.Intensity * emitter.LodMultiplier);
        requested = Math.Min(requested, Math.Max(0, scaledMaximum - emitter.Particles.Count));
        var emitted = 0;

        for (var i = 0; i < requested && TryEmitParticle(emitter); i++)
            emitted++;

        return emitted;
    }

    private bool TryEmitParticle(ActiveEmitter emitter)
    {
        if (_remainingSpawnAllowance <= 0 || _liveParticleCount >= _globalBudget ||
            _liveParticleCost + emitter.ParticleCost > _globalBudget)
            return false;

        var proto = emitter.Proto;
        var lifetime = (float) proto.Lifetime.TotalSeconds;
        var lifetimeVariance = (float) proto.LifetimeVariance.TotalSeconds;
        lifetime = Math.Max(MinParticleLifetimeSeconds, lifetime + emitter.Random.NextFloat(-lifetimeVariance, lifetimeVariance));

        var spreadAngle = (float) proto.SpreadAngle.Theta;
        var angle = emitter.EffectiveEmitAngle + emitter.Random.NextFloat(-spreadAngle * 0.5f, spreadAngle * 0.5f);
        var speed = Math.Max(0f, proto.Speed + emitter.Random.NextFloat(-proto.SpeedVariance, proto.SpeedVariance));
        var position = SampleEmissionShape(proto.Shape, emitter.Random);

        var particle = new ParticleData
        {
            Position = proto.WorldSpace ? emitter.MapCoords.Position + position : position,
            Velocity = new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * speed,
            Lifetime = lifetime,
            InverseLifetime = 1f / lifetime,
            SpawnSpeed = speed,
            SpawnIntensity = emitter.Intensity,
            SizeMultiplier = proto.SizeVariance > 0f
                ? 1f + emitter.Random.NextFloat(-proto.SizeVariance, proto.SizeVariance)
                : 1f,
            Rotation = (float) proto.StartRotation.Theta
                + emitter.Random.NextFloat(-(float) proto.StartRotationVariance.Theta, (float) proto.StartRotationVariance.Theta),
            RotationSpeed = (float) proto.RotationSpeed.Theta
                + emitter.Random.NextFloat(-(float) proto.RotationSpeedVariance.Theta, (float) proto.RotationSpeedVariance.Theta),
            AnimationPhase = emitter.AnimationDuration > 0f
                ? emitter.Random.NextFloat(0f, emitter.AnimationDuration)
                : 0f,
            NoiseOffset = new Vector2(emitter.Random.NextFloat(-100f, 100f), emitter.Random.NextFloat(-100f, 100f)),
        };

        particle.Velocity += emitter.InitialVelocity;
        if (proto.InheritVelocity != 0f)
            particle.Velocity += emitter.EmitterVelocity * proto.InheritVelocity;

        emitter.Particles.Add(particle);
        _liveParticleCount++;
        _liveParticleCost += emitter.ParticleCost;
        _remainingSpawnAllowance--;

        return true;
    }

    private void ActivateEmitter(ActiveEmitter emitter)
    {
        _emitters.Add(emitter);
        emitter.LodMultiplier = ParticleRuntimeMath.GetQualityMultiplier(ParticleRuntimeMath.ResolvePriority(emitter.Proto), _quality);

        if (!emitter.Proto.Burst)
            return;

        EmitBurst(emitter, emitter.Proto.MaxCount, GetScaledMaximum(emitter, emitter.Proto.MaxCount));
        emitter.Exhausted = true;
    }

    private ActiveEmitter CreateEmitter(
        ParticleEffectPrototype proto,
        MapCoordinates coords,
        EntityUid? attached,
        ParticleSpawnParameters parameters)
    {
        var emitter = new ActiveEmitter
        {
            Proto = proto,
            MapCoords = coords,
            AttachedEntity = attached,
            Handle = _nextHandle++,
            Runtime = GetRuntime(proto),
            ParticleCost = ParticleRuntimeMath.GetParticleCost(proto),
            ColorOverride = parameters.Color,
            Intensity = parameters.Intensity,
            InitialVelocity = parameters.Velocity ?? Vector2.Zero,
            EffectiveEmitAngle = (float) (parameters.EmitAngle ?? proto.EmitAngle).Theta,
            Random = new ParticleRandom(parameters.Seed ?? _random.Next()),
        };
        ResolveFrames(emitter);

        for (var i = 0; i < proto.Bursts.Count; i++)
            emitter.FiredBursts.Add(false);

        return emitter;
    }

    public void StopEffect(uint handle)
    {
        if (handle == 0)
            return;

        foreach (var emitter in _emitters)
        {
            if (emitter.Handle != handle)
                continue;
            emitter.Exhausted = true;
            break;
        }
    }

    public static void StopEffect(ActiveEmitter emitter) => emitter.Exhausted = true;

    private int GetScaledMaximum(ActiveEmitter emitter, int maxCount)
        => (int) Math.Ceiling(Math.Clamp(maxCount, 0, HardMaxParticles) * emitter.Intensity * emitter.LodMultiplier);

    private void RebalanceToBudget()
    {
        _trimOrder.Clear();
        _trimOrder.AddRange(_emitters);
        _trimOrder.Sort(TrimComparison);

        foreach (var emitter in _trimOrder)
        {
            var desired = GetScaledMaximum(emitter, emitter.Proto.MaxCount);
            while (emitter.Particles.Count > desired)
                RemoveParticleAt(emitter, emitter.Particles.Count - 1);
        }

        foreach (var emitter in _trimOrder)
        {
            while (emitter.Particles.Count > 0 && IsOverBudget())
                RemoveParticleAt(emitter, emitter.Particles.Count - 1);

            if (!IsOverBudget())
                break;
        }
    }

    private bool IsOverBudget()
        => _liveParticleCount > _globalBudget || _liveParticleCost > _globalBudget;

    private void RemoveParticleAt(ActiveEmitter emitter, int index)
    {
        var particles = emitter.Particles;
        var last = particles.Count - 1;
        if (index != last)
            particles[index] = particles[last];
        particles.RemoveAt(last);
        _liveParticleCount--;
        _liveParticleCost -= emitter.ParticleCost;
    }

    private void ClearEmitterParticles(ActiveEmitter emitter)
    {
        var count = emitter.Particles.Count;
        if (count == 0)
            return;

        emitter.Particles.Clear();
        _liveParticleCount = Math.Max(0, _liveParticleCount - count);
        _liveParticleCost = Math.Max(0f, _liveParticleCost - count * emitter.ParticleCost);
    }

    private static void SimulateParticle(
        ref ParticleData particle,
        float dt,
        float dragMultiplier,
        Vector2 constantForce,
        float terminalSpeedSquared,
        float terminalSpeed,
        float gravity,
        float noiseStrength,
        float noiseFrequency,
        CompiledParticleEffect runtime)
    {
        var ageRatio = particle.AgeRatio;
        if (dragMultiplier != 1f)
            particle.Velocity *= dragMultiplier;

        if (constantForce != Vector2.Zero)
            particle.Velocity += constantForce * dt;

        particle.Velocity += CompiledParticleEffect.Sample(runtime.ForceOverLifetime, ageRatio) * dt;

        if (runtime.SpeedOverLifetime != null)
        {
            var curveSpeed = CompiledParticleEffect.Sample(runtime.SpeedOverLifetime, ageRatio, 1f) * particle.SpawnSpeed;
            var currentSpeed = particle.Velocity.Length();
            if (currentSpeed > 0f)
                particle.Velocity = particle.Velocity / currentSpeed * curveSpeed;
        }

        if (terminalSpeedSquared > 0f)
        {
            var speedSquared = particle.Velocity.LengthSquared();
            if (speedSquared > terminalSpeedSquared)
                particle.Velocity *= terminalSpeed / MathF.Sqrt(speedSquared);
        }

        particle.Position += particle.Velocity * dt;
        particle.Position += CompiledParticleEffect.Sample(runtime.VelocityOverLifetime, ageRatio) * dt;
        if (gravity != 0f)
            particle.Position.Y -= gravity * dt * ageRatio;

        if (noiseStrength > 0f)
        {
            var noiseTime = particle.Age * noiseFrequency;
            var x = ValueNoise(particle.NoiseOffset.X + noiseTime, particle.NoiseOffset.Y);
            var y = ValueNoise(particle.NoiseOffset.X, particle.NoiseOffset.Y + noiseTime);
            particle.Position += new Vector2(x, y) * noiseStrength * dt;
        }

        if (particle.RotationSpeed != 0f)
            particle.Rotation += particle.RotationSpeed * dt;
    }

    internal static Vector2 GetParticleWorldPosition(in ParticleData particle, ActiveEmitter emitter)
        => emitter.Proto.WorldSpace ? particle.Position : emitter.MapCoords.Position + particle.Position;

    private CompiledParticleEffect GetRuntime(ParticleEffectPrototype proto)
    {
        if (_runtimeCache.TryGetValue(proto, out var runtime))
            return runtime;

        runtime = new CompiledParticleEffect(proto);
        _runtimeCache.Add(proto, runtime);
        return runtime;
    }

    private void ResolveFrames(ActiveEmitter emitter)
    {
        var proto = emitter.Proto;
        if (_frameCache.TryGetValue(proto, out var cached))
        {
            emitter.Frames = cached.Frames;
            emitter.Delays = cached.Delays;
            emitter.AnimationDuration = GetAnimationDuration(cached.Delays, cached.Frames.Length);
            return;
        }

        if (_frameResolveFailures.Contains(proto))
            return;

        Texture[]? frames = null;
        float[] delays = Array.Empty<float>();
        switch (proto.Sprite)
        {
            case SpriteSpecifier.Rsi rsi:
                try
                {
                    var path = rsi.RsiPath.IsRooted ? rsi.RsiPath : SpriteSpecifierSerializer.TextureRoot / rsi.RsiPath;
                    var resource = _resource.GetResource<RSIResource>(path).RSI;
                    if (!resource.TryGetState(rsi.RsiState, out var state))
                    {
                        _frameResolveFailures.Add(proto);
                        return;
                    }

                    frames = state.GetFrames(RsiDirection.South);
                    delays = state.GetDelays();
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not resolve RSI resource '{rsi.RsiPath}' for particle prototype {proto.ID}: {exception}");
                    _frameResolveFailures.Add(proto);
                    return;
                }
                break;
            case SpriteSpecifier.Texture texture:
                try
                {
                    frames = new[] { _sprite.Frame0(texture) };
                }
                catch (Exception exception)
                {
                    Log.Error($"Could not resolve sprite texture '{texture.TexturePath}' for particle prototype {proto.ID}: {exception}");
                    _frameResolveFailures.Add(proto);
                    return;
                }
                break;
        }

        if (frames == null)
        {
            _frameResolveFailures.Add(proto);
            return;
        }

        _frameCache[proto] = (frames, delays);
        emitter.Frames = frames;
        emitter.Delays = delays;
        emitter.AnimationDuration = GetAnimationDuration(delays, frames.Length);
    }

    private static float GetAnimationDuration(float[] delays, int frameCount)
    {
        if (delays.Length == 0 || frameCount <= 1)
            return 0f;

        var duration = 0f;
        for (var index = 0; index < Math.Min(delays.Length, frameCount); index++)
            duration += Math.Max(0f, delays[index]);
        return duration;
    }

    private static Vector2 SampleEmissionShape(EmissionShapeData shape, ParticleRandom random)
    {
        return shape.Type switch
        {
            EmissionShapeType.Point => Vector2.Zero,
            EmissionShapeType.CircleEdge => SampleCircleEdge(shape.Radius, random),
            EmissionShapeType.CircleFill => SampleCircleFill(shape.Radius, random),
            EmissionShapeType.Box => new Vector2(
                random.NextFloat(-shape.BoxExtents.X, shape.BoxExtents.X),
                random.NextFloat(-shape.BoxExtents.Y, shape.BoxExtents.Y)),
            _ => Vector2.Zero,
        };
    }

    private static Vector2 SampleCircleEdge(float radius, ParticleRandom random)
    {
        var angle = random.NextFloat(0f, MathF.Tau);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }

    private static Vector2 SampleCircleFill(float radius, ParticleRandom random)
    {
        var angle = random.NextFloat(0f, MathF.Tau);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * MathF.Sqrt(random.NextFloat(0f, 1f));
    }

    public static float SampleCurve(List<ParticleCurveKey> curve, float t)
    {
        if (curve.Count == 0)
            return 1f;
        if (curve.Count == 1)
            return curve[0].Value;

        ParticleCurveKey? previous = null;
        ParticleCurveKey? next = null;
        foreach (var key in curve)
        {
            if (key.Time <= t)
                previous = key;
            else
            {
                next = key;
                break;
            }
        }

        if (previous == null)
            return curve[0].Value;
        if (next == null)
            return previous.Value.Value;
        var span = next.Value.Time - previous.Value.Time;
        return span <= 0f ? previous.Value.Value : previous.Value.Value + (next.Value.Value - previous.Value.Value) * ((t - previous.Value.Time) / span);
    }

    public static Color SampleColorCurve(List<ColorCurveKey> curve, float t)
    {
        if (curve.Count == 0)
            return Color.White;
        if (curve.Count == 1)
            return curve[0].Color;

        ColorCurveKey? previous = null;
        ColorCurveKey? next = null;
        foreach (var key in curve)
        {
            if (key.Time <= t)
                previous = key;
            else
            {
                next = key;
                break;
            }
        }

        if (previous == null)
            return curve[0].Color;
        if (next == null)
            return previous.Value.Color;
        var span = next.Value.Time - previous.Value.Time;
        return span <= 0f ? previous.Value.Color : Color.InterpolateBetween(previous.Value.Color, next.Value.Color, (t - previous.Value.Time) / span);
    }

    public static Vector2 SampleVector2Curve(List<Vector2CurveKey> curve, float t)
    {
        if (curve.Count == 0)
            return Vector2.Zero;
        if (curve.Count == 1)
            return curve[0].Value;

        Vector2CurveKey? previous = null;
        Vector2CurveKey? next = null;
        foreach (var key in curve)
        {
            if (key.Time <= t)
                previous = key;
            else
            {
                next = key;
                break;
            }
        }

        if (previous == null)
            return curve[0].Value;
        if (next == null)
            return previous.Value.Value;
        var span = next.Value.Time - previous.Value.Time;
        return span <= 0f ? previous.Value.Value : Vector2.Lerp(previous.Value.Value, next.Value.Value, (t - previous.Value.Time) / span);
    }

    private static float ValueNoise(float x, float y)
    {
        var ix = (int) MathF.Floor(x);
        var iy = (int) MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (d - b - c + a) * fx * fy;
    }

    private static float Hash(int x, int y)
    {
        var n = x + y * 57;
        n = (n << 13) ^ n;
        return 1f - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824f;
    }

    private static float ElapsedMilliseconds(long startedAt)
        => (Stopwatch.GetTimestamp() - startedAt) * 1000f / Stopwatch.Frequency;
}
