using System.Numerics;
using Content.Shared._WH40K.Bosses;
using Robust.Client.GameObjects;
using Robust.Client.Utility;
using Robust.Shared.Graphics.RSI;

namespace Content.Client._WH40K.Bosses;

internal readonly record struct MarineExhaustMount(Vector2 Position, bool AboveBody, float Scale);

internal static class Wh40kChaosMarineJumpVisuals
{
    public static RsiDirection Facing(SpriteComponent sprite, Angle worldRotation, Angle eyeRotation) =>
        sprite.EnableDirectionOverride ? sprite.DirectionOverride.Convert(RsiDirectionType.Dir4) :
            SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, (worldRotation + eyeRotation).Reduced().FlipPositive());

    public static string FacingName(RsiDirection facing) => facing switch
    {
        RsiDirection.North => "north",
        RsiDirection.East => "east",
        RsiDirection.West => "west",
        _ => "south",
    };

    // Sprite-local metres: the layer transform supplies scale, offset and camera rotation.
    public static MarineExhaustMount Mount(RsiDirection facing, int side) => facing switch
    {
        RsiDirection.North => new(new Vector2(side == 0 ? -.32f : .32f, -.20f), true, 1),
        RsiDirection.East => side == 0 ? new(new(-.25f, -.15f), false, .8f) : new(new(-.43f, -.23f), true, 1),
        RsiDirection.West => side == 0 ? new(new(.25f, -.15f), false, .8f) : new(new(.43f, -.23f), true, 1),
        _ => new(new Vector2(side == 0 ? -.37f : .37f, -.23f), false, 1),
    };

    public static float Exhaust(Wh40kChaosMarineComponent state, TimeSpan now)
    {
        if (state.Attack == MarineAttack.None && state.LastAttack == MarineAttack.Jump)
            return .9f * (1 - Wh40kChaosMarineCombat.Smooth((float) (now - state.EndsAt).TotalSeconds /
                Wh40kChaosMarineCombat.Delay(.18f, state, .1f)));
        if (state.Attack != MarineAttack.Jump || now < state.StartedAt) return 0;
        if (now >= state.ExecuteAt) return 1;
        var windup = Math.Max(.001, (state.ExecuteAt - state.StartedAt).TotalSeconds);
        return .12f + .78f * Wh40kChaosMarineCombat.Smooth((float) ((now - state.StartedAt).TotalSeconds / windup));
    }
}
