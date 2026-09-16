using Content.Server._NF.Radio;
using Content.Shared.Radio.Components;
using Robust.Server.GameObjects;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Content.Shared.Radio;
using Content.Server._WH40K.OperationalDomain;

namespace Content.Server.Radio.EntitySystems;

/// <summary>
///     Add the intercom UI as a new verb as to not conflict with shuttle UI
/// </summary>
public sealed partial class ShuttleIntercomSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleIntercomComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerb);
        SubscribeLocalEvent<ShuttleIntercomComponent, RadioTransformMessageEvent>(OnRadioTransformMessage);
    }

    private void OnAlternativeVerb(EntityUid uid, ShuttleIntercomComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var openUiVerb = new AlternativeVerb
        {
            Act = () => ToggleUi(uid, component, args.User),
            Text = Loc.GetString("intercom-verb")
        };
        args.Verbs.Add(openUiVerb);
    }

    private void ToggleUi(EntityUid uid, ShuttleIntercomComponent? component = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!TryComp<ActorComponent>(user, out var actor))
            return;

        _ui.TryToggleUi(uid, IntercomUiKey.Key, actor.PlayerSession);
    }

    private void OnRadioTransformMessage(EntityUid uid, ShuttleIntercomComponent component, ref RadioTransformMessageEvent args)
    {
        // Not appending name, nothing to do.
        if (!component.AppendName)
        {
            return;
        }

        // Use the current operational owner first: a station keeps its station name and an
        // independent vessel uses its grid name. The legacy override only names sources
        // that have no operational domain at all.
        if (_operationalDomains.TryResolveOperationalDomain(uid, out var domain))
        {
            args.Name += $" ({Name(domain.Owner)})";
            args.MessageSource = domain.Owner;
            return;
        }

        if (component.OverrideName != null)
            args.Name += $" ({component.OverrideName})";
    }
}
