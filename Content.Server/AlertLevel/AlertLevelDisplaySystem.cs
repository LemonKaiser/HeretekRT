using Content.Server.Power.Components;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared.AlertLevel;
using Content.Shared.Power;

namespace Content.Server.AlertLevel;

public sealed partial class AlertLevelDisplaySystem : EntitySystem
{
    [Dependency] private AlertLevelSystem _alertLevelSystem = default!;
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AlertLevelChangedEvent>(OnAlertChanged);
        SubscribeLocalEvent<AlertLevelDisplayComponent, ComponentInit>(OnDisplayInit);
        SubscribeLocalEvent<AlertLevelDisplayComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnAlertChanged(AlertLevelChangedEvent args)
    {
        var query = EntityQueryEnumerator<AlertLevelDisplayComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out _, out var appearance))
        {
            if (_operationalDomains.TryResolveOperationalDomain(uid, out var domain) &&
                domain.Owner == args.Station)
            {
                _appearance.SetData(uid, AlertLevelDisplay.CurrentLevel, args.AlertLevel, appearance);
            }
        }
    }

    private void OnDisplayInit(EntityUid uid, AlertLevelDisplayComponent alertLevelDisplay, ComponentInit args)
    {
        if (TryComp(uid, out AppearanceComponent? appearance))
        {
            if (_alertLevelSystem.TryGetDomainAlertLevel(uid, out _, out var alert))
            {
                _appearance.SetData(uid, AlertLevelDisplay.CurrentLevel, alert.CurrentLevel, appearance);
            }
        }
    }
    private void OnPowerChanged(EntityUid uid, AlertLevelDisplayComponent alertLevelDisplay, ref PowerChangedEvent args)
    {
        if (!TryComp(uid, out AppearanceComponent? appearance))
            return;

        _appearance.SetData(uid, AlertLevelDisplay.Powered, args.Powered, appearance);
    }
}
