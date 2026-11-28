using Content.Server.Temperature.Components;
using Content.Shared._WH40K.Synthetics;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.Timing;

namespace Content.Server._WH40K.Synthetics;

/// <summary>
/// Freezes cybernetic/IPC servos below <see cref="StiffTemperature"/>: progressively slows movement
/// and seizes the chassis (knockdown + jitter while remaining conscious) below <see cref="SeizeTemperature"/>.
/// </summary>
public sealed partial class Wh40kChassisColdSystem : EntitySystem
{
    public const float StiffTemperature = 180f;
    public const float SeizeTemperature = 90f;
    public const float MinTemperature = 60f;
    public const float MaxColdSlowdown = 0.65f;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SeizeKnockdown = TimeSpan.FromSeconds(1.5);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private TimeSpan _nextPoll;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextPoll)
            return;

        _nextPoll = now + PollInterval;
        var query = EntityQueryEnumerator<Wh40kChassisComponent, TemperatureComponent>();
        while (query.MoveNext(out var uid, out var chassis, out var temperature))
        {
            var slowdown = ComputeSlowdown(temperature.CurrentTemperature);
            if (MathF.Abs(chassis.ColdSlowdown - slowdown) > 0.01f || slowdown == 0f && chassis.ColdSlowdown != 0f)
            {
                chassis.ColdSlowdown = slowdown;
                Dirty(uid, chassis);
                _speed.RefreshMovementSpeedModifiers(uid);
            }

            if (temperature.CurrentTemperature <= SeizeTemperature && !_mobState.IsDead(uid))
            {
                _jitter.DoJitter(uid, SeizeKnockdown, refresh: true, amplitude: 8f, frequency: 6f);
                _stun.TryKnockdown(uid, SeizeKnockdown, refresh: true);
            }
        }
    }

    public static float ComputeSlowdown(float temperature)
    {
        if (temperature >= StiffTemperature)
            return 0f;

        var cold = (StiffTemperature - temperature) / (StiffTemperature - MinTemperature);
        return MaxColdSlowdown * Math.Clamp(cold, 0f, 1f);
    }
}
