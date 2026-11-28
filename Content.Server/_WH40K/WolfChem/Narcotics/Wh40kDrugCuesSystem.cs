using Content.Shared._WH40K.WolfChem.Narcotics;
using Content.Shared.Bed.Sleep;
using Content.Shared.Drunk;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WH40K.WolfChem.Narcotics;

/// <summary>
/// What a high, a crash, a drink or withdrawal looks like from outside, on examine.
/// </summary>
public sealed partial class Wh40kDrugCuesSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private Wh40kAlcoholSystem _alcohol = default!;
    [Dependency] private Wh40kIntoxicationSystem _intoxication = default!;

    public const int WithdrawalStage = 2;

    private static readonly Wh40kDrugFamily[] Families =
    {
        Wh40kDrugFamily.Stimulant, Wh40kDrugFamily.Opioid, Wh40kDrugFamily.Psychedelic, Wh40kDrugFamily.Empathogen,
        Wh40kDrugFamily.Cannabinoid, Wh40kDrugFamily.Combat,
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Wh40kIntoxicationComponent, ExaminedEvent>((uid, _, args) => OnExamined(uid, args));
        SubscribeLocalEvent<Wh40kDependenceComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<Wh40kIntoxicationComponent>(uid))
                OnExamined(uid, args);
        });
        SubscribeLocalEvent<DrunkComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<Wh40kIntoxicationComponent>(uid) && !HasComp<Wh40kDependenceComponent>(uid))
                OnExamined(uid, args);
        });
    }

    private void OnExamined(EntityUid uid, ExaminedEvent args)
    {
        var (face, body) = GetCues(uid, args.IsInDetailsRange);
        var target = Identity.Entity(uid, EntityManager);
        if (face != null)
            args.PushMarkup(Loc.GetString(face, ("target", target)));
        if (body != null)
            args.PushMarkup(Loc.GetString(body, ("target", target)));
    }

    public (string? Face, string? Body) GetCues(EntityUid uid, bool detailed)
    {
        if (_mobState.IsDead(uid))
            return (null, null);

        var face = new Cue();
        var body = new Cue();
        var host = _intoxication.IsNeurowareHost(uid);

        if (TryComp<Wh40kIntoxicationComponent>(uid, out var comp))
        {
            foreach (var family in Families)
            {
                var phase = _intoxication.FamilyPhase(comp, family);
                if (phase == Wh40kDrugPhase.None)
                    continue;

                var name = host ? "synth" : family.ToString().ToLowerInvariant();
                face.Offer($"wh40k-drug-cue-{name}-face", (int) phase * 10);
                if (phase >= Wh40kDrugPhase.Heavy)
                    body.Offer($"wh40k-drug-cue-{name}-body-{PhaseName(phase)}", (int) phase * 10);
            }

            if (detailed)
            {
                foreach (var _ in _intoxication.ActiveCrashes(comp))
                {
                    body.Offer(host ? "wh40k-drug-cue-synth-crash" : "wh40k-drug-cue-crash", 15);
                    break;
                }
            }
        }

        var drunk = _alcohol.DrunkPhase(uid);
        if (drunk != Wh40kDrugPhase.None)
        {
            face.Offer("wh40k-drug-cue-drunk-face", (int) drunk * 10 - 1);
            if (drunk >= Wh40kDrugPhase.Heavy)
                body.Offer($"wh40k-drug-cue-drunk-body-{PhaseName(drunk)}", (int) drunk * 10 - 1);
        }

        if (TryComp<Wh40kDependenceComponent>(uid, out var dependence))
        {
            var stage = 0;
            foreach (var reached in dependence.Stage.Values)
            {
                stage = Math.Max(stage, reached);
            }

            if (stage >= WithdrawalStage)
            {
                body.Offer(host ? "wh40k-drug-cue-synth-withdrawal" : $"wh40k-drug-cue-withdrawal-body-{Math.Min(stage, 3)}", stage * 10 + 1);
                if (stage >= 3 && !host)
                    face.Offer("wh40k-drug-cue-withdrawal-face", stage * 10 + 1);
            }
        }

        var showFace = detailed && EyesVisible(uid);
        var showBody = !_mobState.IsIncapacitated(uid) && !HasComp<SleepingComponent>(uid);
        return (showFace ? face.Key : null, showBody ? body.Key : null);
    }

    private bool EyesVisible(EntityUid uid)
    {
        var ev = new SeeIdentityAttemptEvent();
        RaiseLocalEvent(uid, ev);
        return (ev.TotalCoverage & IdentityBlockerCoverage.EYES) == 0;
    }

    private static string PhaseName(Wh40kDrugPhase phase)
    {
        return phase == Wh40kDrugPhase.Overdose ? "overdose" : "heavy";
    }

    private struct Cue
    {
        public string? Key;
        private int _weight;

        public void Offer(string key, int weight)
        {
            if (Key != null && weight <= _weight)
                return;

            Key = key;
            _weight = weight;
        }
    }
}
