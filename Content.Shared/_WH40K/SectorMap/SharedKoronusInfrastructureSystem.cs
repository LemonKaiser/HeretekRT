using Content.Shared._WH40K.SectorMap.Components;
using Content.Shared.Tiles;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;

namespace Content.Shared._WH40K.SectorMap;

/// <summary>Shared interaction gate; the server selects the authored infrastructure.</summary>
public sealed partial class SharedKoronusInfrastructureSystem : EntitySystem
{
    public bool IsReadOnly(EntityUid target)
        => HasComp<KoronusCriticalInfrastructureComponent>(target) &&
           Transform(target).GridUid is { } grid && HasComp<ProtectedGridComponent>(grid);

    public bool IsOpenUiVerb(EntityUid target, Verb verb)
        => TryComp<ActivatableUIComponent>(target, out var ui) &&
           (ui.VerbOnly ? verb.GetType() == typeof(Verb) : verb is ActivationVerb) &&
           verb.Category == null && verb.Text == Loc.GetString(ui.VerbText);

    public bool UsesNativeUiActivation(EntityUid target)
        => TryComp<KoronusCriticalInfrastructureComponent>(target, out var protection) && protection.AllowUiActivation;
}
