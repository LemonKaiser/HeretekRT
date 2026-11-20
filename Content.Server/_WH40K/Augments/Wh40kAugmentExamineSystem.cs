using System.Linq;
using Content.Server._WH40K.Localizations;
using Content.Shared._Shitmed.BodyEffects.Subsystems;
using Content.Shared._WH40K.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Examine;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.Augments;

public sealed partial class Wh40kAugmentExamineSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private ILocalizationManager _localization = default!;
    [Dependency] private WH40KEntityLocalizationCache _entityLocalization = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, BodyComponent body, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var augmentations = ExternalAugmentations(uid, body).ToArray();
        if (augmentations.Length == 0)
            return;

        var message = new FormattedMessage();
        message.AddMarkupOrThrow(Loc.GetString("wh40k-augments-examine-header"));
        foreach (var prototype in augmentations)
        {
            var name = _entityLocalization.Get(_localization.DefaultCulture?.Name, prototype.ID).Name;
            if (string.IsNullOrEmpty(name))
                name = prototype.Name;

            message.PushNewline();
            message.AddText(Loc.GetString("wh40k-augments-examine-entry", ("name", name)));
        }

        args.PushMessage(message, priority: -1);
    }

    private IEnumerable<EntityPrototype> ExternalAugmentations(EntityUid uid, BodyComponent body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(uid, body))
        {
            if (Prototype(part) is not { } prototype || !Wh40kAugmentCatalog.Zones(prototype, _componentFactory).Any())
                continue;

            // An original hand or foot is part of the limb already listed. Individual replacements are separate.
            if (TryComp(_body.GetParentPartOrNull(part), out GenerateChildPartComponent? generated) && generated.ChildPart == part)
                continue;

            yield return prototype;
        }

        foreach (var (organ, component) in _body.GetBodyOrgans(uid, body))
        {
            if (Wh40kAugmentCatalog.IsOpticalSlot(component.SlotId)
                && Prototype(organ) is { } prototype && Wh40kAugmentCatalog.Zones(prototype, _componentFactory).Any())
                yield return prototype;
        }
    }
}
