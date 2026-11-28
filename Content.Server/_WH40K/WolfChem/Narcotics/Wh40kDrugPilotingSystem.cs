using System.Numerics;
using Content.Server.Physics.Controllers;
using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Drunk;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// Flying a ship while high. Stimulants sharpen the turn and thrust and twitch the stick in an overdose; opioids add
/// input lag and hold the stick through a nod; cannabis softens brakes and turn; alcohol makes the heading wander.
/// </summary>
public sealed partial class Wh40kDrugPilotingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private Wh40kAlcoholSystem _alcohol = default!;
    [Dependency] private Wh40kIntoxicationSystem _intoxication = default!;

    private const float Deadzone = 0.02f;
    private const float SwaySkew = 1f;
    private const float TwitchSeconds = 0.2f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kIntoxicationComponent, GetShuttleInputsEvent>(OnIntoxicated, after: new[] { typeof(MoverController) });
        SubscribeLocalEvent<DrunkComponent, GetShuttleInputsEvent>(OnDrunk, after: new[] { typeof(MoverController) });
    }

    private void OnIntoxicated(Entity<Wh40kIntoxicationComponent> ent, ref GetShuttleInputsEvent args)
    {
        Shape(ent, ref args);
    }

    private void OnDrunk(Entity<DrunkComponent> ent, ref GetShuttleInputsEvent args)
    {
        if (!HasComp<Wh40kIntoxicationComponent>(ent))
            Shape(ent, ref args);
    }

    public Wh40kPilotHandling GetHandling(EntityUid uid)
    {
        var handling = new Wh40kPilotHandling();
        handling.Wander = _alcohol.DrunkPhase(uid) switch
        {
            Wh40kDrugPhase.Light => 0.1f,
            Wh40kDrugPhase.Heavy => 0.25f,
            Wh40kDrugPhase.Overdose => 0.45f,
            _ => 0f,
        };

        if (!TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
            return handling;

        var stimulant = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Stimulant);
        if (stimulant != Wh40kDrugPhase.None)
        {
            handling.Angular *= stimulant == Wh40kDrugPhase.Light ? 1.05f : 1.1f;
            handling.Acceleration *= stimulant == Wh40kDrugPhase.Light ? 1.03f : 1.05f;
            handling.Twitch = stimulant == Wh40kDrugPhase.Overdose;
        }
        else if (Crashing(comp, Wh40kDrugFamily.Stimulant))
        {
            handling.Angular *= 0.88f;
            handling.Acceleration *= 0.92f;
        }

        handling.Lag = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Opioid) switch
        {
            Wh40kDrugPhase.Light => 0.25f,
            Wh40kDrugPhase.Heavy => 0.6f,
            Wh40kDrugPhase.Overdose => 1.2f,
            _ => 0f,
        };
        handling.Held = handling.Lag > 0f && _intoxication.IsNodding(comp);

        var (brakes, turn) = _intoxication.FamilyPhase(comp, Wh40kDrugFamily.Cannabinoid) switch
        {
            Wh40kDrugPhase.Light => (0.85f, 0.95f),
            Wh40kDrugPhase.Heavy => (0.65f, 0.85f),
            Wh40kDrugPhase.Overdose => (0.5f, 0.75f),
            _ => (1f, 1f),
        };
        handling.Brakes *= brakes;
        handling.Angular *= turn;
        return handling;
    }

    private bool Crashing(Wh40kIntoxicationComponent comp, Wh40kDrugFamily family)
    {
        foreach (var profile in _intoxication.ActiveCrashes(comp))
        {
            if (_proto.TryIndex(profile, out var proto) && proto.Family == family)
                return true;
        }

        return false;
    }

    private void Shape(EntityUid uid, ref GetShuttleInputsEvent args)
    {
        if (!args.GotInput)
            return;

        var handling = GetHandling(uid);
        args.AngularMul *= handling.Angular;
        args.AccelMul *= handling.Acceleration;

        if (!handling.ShapesInput)
        {
            RemCompDeferred<Wh40kPilotImpairmentComponent>(uid);
            return;
        }

        if (!TryComp<Wh40kPilotImpairmentComponent>(uid, out var state))
        {
            state = AddComp<Wh40kPilotImpairmentComponent>(uid);
            state.Seed = _random.NextFloat(0f, 100f);
            state.NextTwitch = _random.NextFloat(2f, 5f);
        }

        var seconds = args.FrameTime;
        state.Time += seconds;
        var active = args.Input != null;
        var input = args.Input ?? new ShuttleInput(Vector2.Zero, 0f, 0f);
        var strafe = input.Strafe;
        var rotation = input.Rotation;
        var brakes = input.Brakes * handling.Brakes;

        if (active && handling.Wander > 0f)
        {
            var time = state.Time;
            var seed = state.Seed;
            rotation += handling.Wander * Sway(time, seed);
            strafe = new Angle(handling.Wander * SwaySkew * Sway(time * 0.8f, seed + 31f)).RotateVec(strafe);
        }

        if (handling.Twitch)
        {
            state.NextTwitch -= seconds;
            if (state.NextTwitch <= 0f)
            {
                state.NextTwitch = _random.NextFloat(2f, 5f);
                state.TwitchLeft = TwitchSeconds;
                state.TwitchTurn = _random.Prob(0.5f) ? 1f : -1f;
            }
        }

        if (state.TwitchLeft > 0f)
        {
            state.TwitchLeft -= seconds;
            if (active)
                rotation += state.TwitchTurn;
        }

        if (handling.Lag > 0f)
        {
            if (!handling.Held)
            {
                var follow = 1f - MathF.Exp(-seconds / handling.Lag);
                state.Strafe += (strafe - state.Strafe) * follow;
                state.Rotation += (rotation - state.Rotation) * follow;
                state.Brakes += (brakes - state.Brakes) * follow;
            }

            strafe = state.Strafe;
            rotation = state.Rotation;
            brakes = state.Brakes;
            active = strafe.LengthSquared() > Deadzone * Deadzone || MathF.Abs(rotation) > Deadzone || brakes > Deadzone;
        }
        else
        {
            state.Strafe = strafe;
            state.Rotation = rotation;
            state.Brakes = brakes;
        }

        if (!active)
        {
            args.Input = null;
            return;
        }

        if (args.Input == null && TryComp<PilotComponent>(uid, out var pilot))
            args.SetMaxVelocity = pilot.SetMaxVelocity;

        args.Input = new ShuttleInput(strafe, Math.Clamp(rotation, -1f, 1f), brakes);
    }

    private static float Sway(float time, float seed)
    {
        return 0.6f * MathF.Sin(0.9f * time + seed) + 0.4f * MathF.Sin(0.37f * time + 2.1f * seed);
    }
}

public record struct Wh40kPilotHandling()
{
    public float Angular = 1f;
    public float Acceleration = 1f;
    public float Brakes = 1f;
    public float Lag;
    public bool Held;
    public float Wander;
    public bool Twitch;

    public readonly bool ShapesInput => Lag > 0f || Wander > 0f || Twitch || !MathHelper.CloseTo(Brakes, 1f);
}
