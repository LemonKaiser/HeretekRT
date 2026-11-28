using System.Numerics;
using Content.Shared.Camera;
using Robust.Shared.Random;

namespace Content.Client.Camera;

public sealed partial class CameraRecoilSystem
{
    [Dependency] private IRobustRandom _random = default!;

    private const float Wh40kMaxOffset = 0.3f;
    private const float Wh40kShakeThreshold = 0.2f;
    private const float Wh40kMaxShakeStrength = 4f;
    private const float Wh40kMinShakeFrequency = 10f;
    private const float Wh40kMaxShakeFrequency = 30f;
    private const float Wh40kMaxShakeTime = 0.8f;
    private const float Wh40kShakeFade = 0.3f;

    private readonly Dictionary<EntityUid, Wh40kShake> _wh40kShakes = new();
    private readonly List<EntityUid> _wh40kShakesDone = new();

    private sealed class Wh40kShake
    {
        public float Strength;
        public float Remaining;
        public float Timer;
    }

    /// <summary>Moves the view a short way; any excess becomes a shake in place that is faster the harder the kick.</summary>
    private void Wh40kKickCamera(EntityUid uid, CameraRecoilComponent component, Vector2 recoil)
    {
        var length = recoil.Length();
        if (!(length > 0f) || float.IsInfinity(length))
            return;

        if (length > Wh40kMaxOffset)
        {
            var overflow = length - Wh40kMaxOffset;
            recoil *= Wh40kMaxOffset / length;

            if (overflow > Wh40kShakeThreshold)
                Wh40kStartShake(uid, overflow);
        }

        var dampen = Math.Min(1f, component.CurrentKick.Length() / Wh40kMaxOffset);
        component.CurrentKick += recoil * (1 - dampen);

        if (component.CurrentKick.Length() > Wh40kMaxOffset)
            component.CurrentKick = component.CurrentKick.Normalized() * Wh40kMaxOffset;

        component.LastKickTime = 0;
    }

    private void Wh40kStartShake(EntityUid uid, float overflow)
    {
        var strength = Math.Min(overflow, Wh40kMaxShakeStrength);
        var duration = Math.Min(Wh40kMaxShakeTime, 0.15f + 0.15f * strength);

        if (!_wh40kShakes.TryGetValue(uid, out var shake))
        {
            _wh40kShakes[uid] = new Wh40kShake { Strength = strength, Remaining = duration };
            return;
        }

        shake.Strength = Math.Max(shake.Strength, strength);
        shake.Remaining = Math.Max(shake.Remaining, duration);
    }

    public override void FrameUpdate(float frameTime)
    {
        Wh40kUpdateShakes(frameTime);
        base.FrameUpdate(frameTime);
    }

    private void Wh40kUpdateShakes(float frameTime)
    {
        if (_intensity == 0)
        {
            _wh40kShakes.Clear();
            return;
        }

        foreach (var (uid, shake) in _wh40kShakes)
        {
            shake.Remaining -= frameTime;
            if (shake.Remaining <= 0 || !TryComp<CameraRecoilComponent>(uid, out var recoil))
            {
                _wh40kShakesDone.Add(uid);
                continue;
            }

            shake.Timer -= frameTime;
            if (shake.Timer > 0)
                continue;

            var frequency = MathHelper.Lerp(Wh40kMinShakeFrequency, Wh40kMaxShakeFrequency, shake.Strength / Wh40kMaxShakeStrength);
            shake.Timer = Math.Max(0f, shake.Timer + 1f / frequency);

            var amplitude = Wh40kMaxOffset * Math.Min(1f, shake.Remaining / Wh40kShakeFade);
            var direction = recoil.CurrentKick.LengthSquared() > 0f
                ? -recoil.CurrentKick.Normalized()
                : _random.NextAngle().ToVec();
            var angle = new Angle(_random.NextFloat(-MathF.PI / 3f, MathF.PI / 3f));
            recoil.CurrentKick = angle.RotateVec(direction) * amplitude;

            recoil.LastKickTime = 0.5f;
        }

        foreach (var uid in _wh40kShakesDone)
        {
            _wh40kShakes.Remove(uid);
        }

        _wh40kShakesDone.Clear();
    }
}
