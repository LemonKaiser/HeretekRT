using Content.Shared._WH40K.Bosses;

namespace Content.Client._WH40K.Bosses;

internal static class Wh40kChaosMarineTransitionVisuals
{
    public const float Radius = Wh40kChaosMarineCombat.TransitionBurstRadius + 1.25f;
    public const float AfterglowSeconds = .55f;

    // Negative means inactive. Use entity time so pauses and joining midway preserve the choreography.
    public static float Elapsed(Wh40kChaosMarineComponent state, TimeSpan now)
    {
        if (state.Attack == MarineAttack.Transition && state.TransitionProtected &&
            now >= state.StartedAt && now < state.EndsAt + TimeSpan.FromSeconds(AfterglowSeconds))
            return (float) (now - state.StartedAt).TotalSeconds;
        if (state.Attack == MarineAttack.None && state.LastAttack == MarineAttack.Transition && state.PhaseTwo &&
            now >= state.EndsAt && now < state.EndsAt + TimeSpan.FromSeconds(AfterglowSeconds))
            return 5 + (float) (now - state.EndsAt).TotalSeconds;
        return -1;
    }
}
