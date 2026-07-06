using Content.Shared._WH40K.Wave;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WH40K.Wave;

/// <summary>
/// Applies a lightweight vertex wave shader to cloaks, flags and similar soft sprites.
/// </summary>
public sealed class WH40KWaveShaderSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "WH40KWave";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, ShaderInstance> _shaderInstances = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WH40KWaveShaderComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WH40KWaveShaderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WH40KWaveShaderComponent, BeforePostShaderRenderEvent>(OnBeforePostShaderRender);
    }

    private void OnStartup(Entity<WH40KWaveShaderComponent> ent, ref ComponentStartup args)
    {
        EnsureResolvedWaveProfile(ent);
        ApplyShader(ent.Owner, GetOrCreateShader(ent.Owner));
    }

    private void OnShutdown(Entity<WH40KWaveShaderComponent> ent, ref ComponentShutdown args)
    {
        ApplyShader(ent.Owner, null);
        _shaderInstances.Remove(ent.Owner);
    }

    private void ApplyShader(Entity<SpriteComponent?> ent, ShaderInstance? instance)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        ent.Comp.PostShader = instance;
        ent.Comp.GetScreenTexture = false;
        ent.Comp.RaiseShaderEvent = instance != null;
    }

    private void OnBeforePostShaderRender(Entity<WH40KWaveShaderComponent> ent, ref BeforePostShaderRenderEvent args)
    {
        EnsureResolvedWaveProfile(ent);

        if (args.Sprite.PostShader is not { } shader)
            return;

        shader.SetParameter("Speed", ent.Comp.Speed * ent.Comp.ResolvedSpeedMultiplier);
        shader.SetParameter("Dis", ent.Comp.Dis * ent.Comp.ResolvedDisMultiplier);
        shader.SetParameter("Offset", ent.Comp.ResolvedPhaseOffset);
    }

    private ShaderInstance GetOrCreateShader(EntityUid uid)
    {
        if (_shaderInstances.TryGetValue(uid, out var shader))
            return shader;

        shader = _prototype.Index<ShaderPrototype>(ShaderId).InstanceUnique();
        _shaderInstances[uid] = shader;
        return shader;
    }

    private void EnsureResolvedWaveProfile(Entity<WH40KWaveShaderComponent> ent)
    {
        if (ent.Comp.ResolvedWaveProfile)
            return;

        var seed = GetDeterministicSeed(ent.Owner);
        ent.Comp.ResolvedPhaseOffset = ent.Comp.Offset + HashToRange(seed ^ 0x68bc21, 0f, MathF.Tau);
        ent.Comp.ResolvedSpeedMultiplier = HashToRange(
            seed ^ unchecked((int) 0x9e3779b9),
            1f - ent.Comp.SpeedVariance,
            1f + ent.Comp.SpeedVariance);
        ent.Comp.ResolvedDisMultiplier = HashToRange(
            seed ^ unchecked((int) 0x7f4a7c15),
            1f - ent.Comp.DisVariance,
            1f + ent.Comp.DisVariance);
        ent.Comp.ResolvedWaveProfile = true;
    }

    private int GetDeterministicSeed(EntityUid uid)
    {
        var netEntity = GetNetEntity(uid);
        if (netEntity.Valid)
            return netEntity.Id;

        var (worldPos, _) = _transform.GetWorldPositionRotation(uid);
        var xBits = BitConverter.SingleToInt32Bits(worldPos.X);
        var yBits = BitConverter.SingleToInt32Bits(worldPos.Y);
        return uid.Id ^ (xBits * 397) ^ yBits;
    }

    private static float HashToRange(int seed, float min, float max)
    {
        return min + (max - min) * Hash01(seed);
    }

    private static float Hash01(int seed)
    {
        var value = unchecked((uint) seed);
        value ^= value >> 16;
        value *= 0x7feb352d;
        value ^= value >> 15;
        value *= 0x846ca68b;
        value ^= value >> 16;
        return (value & 0x00FFFFFFu) / 16777215f;
    }
}
