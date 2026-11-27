using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._WH40K.BorgModules;

/// <summary>
/// Handles <see cref="ComponentBorgModuleComponent"/> (granting/removing chassis components without stripping
/// pre-existing chassis components) and <see cref="BorgModuleWhitelistComponent"/> (module category limits,
/// mutual incompatibilities, and prerequisites).
/// </summary>
public sealed partial class BorgModuleExtrasSystem : EntitySystem
{
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ComponentBorgModuleComponent, BorgModuleInstalledEvent>(OnComponentModuleInstalled);
        SubscribeLocalEvent<ComponentBorgModuleComponent, BorgModuleUninstalledEvent>(OnComponentModuleUninstalled);
        SubscribeLocalEvent<BorgModuleComponent, BorgCanInsertModuleEvent>(OnCanInsertModule);
        SubscribeLocalEvent<BorgModuleWhitelistComponent, ExaminedEvent>(OnWhitelistExamine);
    }

    private void OnComponentModuleInstalled(Entity<ComponentBorgModuleComponent> ent, ref BorgModuleInstalledEvent args)
    {
        ent.Comp.AppliedComponents.Clear();

        foreach (var name in ent.Comp.Components.Keys)
        {
            var reg = _compFactory.GetRegistration(name);
            if (!EntityManager.HasComponent(args.ChassisEnt, reg.Type))
                ent.Comp.AppliedComponents.Add(name);
        }

        EntityManager.AddComponents(args.ChassisEnt, ent.Comp.Components, removeExisting: false);
    }

    private void OnComponentModuleUninstalled(Entity<ComponentBorgModuleComponent> ent, ref BorgModuleUninstalledEvent args)
    {
        if (TerminatingOrDeleted(args.ChassisEnt))
        {
            ent.Comp.AppliedComponents.Clear();
            return;
        }

        foreach (var name in ent.Comp.AppliedComponents)
        {
            var reg = _compFactory.GetRegistration(name);
            EntityManager.RemoveComponent(args.ChassisEnt, reg.Type);
        }

        ent.Comp.AppliedComponents.Clear();
    }

    private void OnWhitelistExamine(EntityUid uid, BorgModuleWhitelistComponent comp, ExaminedEvent args)
    {
        if (comp.WhitelistInfo is { } info)
            args.PushMarkup(Loc.GetString(info));
    }

    private void OnCanInsertModule(Entity<BorgModuleComponent> ent, ref BorgCanInsertModuleEvent args)
    {
        if (args.Cancelled)
            return;

        TryComp<BorgModuleWhitelistComponent>(ent, out var newWhitelist);
        TryComp<ComponentBorgModuleComponent>(ent, out var newComponents);
        var prerequisiteFulfilled = newWhitelist?.ModuleWhitelist == null;

        foreach (var contained in args.Chassis.Comp.ModuleContainer.ContainedEntities)
        {
            if (!prerequisiteFulfilled && _whitelist.IsWhitelistPass(newWhitelist!.ModuleWhitelist, contained))
                prerequisiteFulfilled = true;

            if (!Incompatible(ent, newWhitelist, newComponents, contained))
                continue;

            if (args.User is { } user)
                _popup.PopupEntity(Loc.GetString("borg-module-incompatible", ("existing", contained)), args.Chassis, user);
            args.Cancelled = true;
            return;
        }

        if (prerequisiteFulfilled)
            return;

        if (args.User is { } prerequisiteUser)
            _popup.PopupEntity(Loc.GetString("borg-module-prerequisite-unfulfilled"), args.Chassis, prerequisiteUser);
        args.Cancelled = true;
    }

    private bool Incompatible(EntityUid module,
        BorgModuleWhitelistComponent? moduleWhitelist,
        ComponentBorgModuleComponent? moduleComponents,
        EntityUid contained)
    {
        if (moduleWhitelist != null && _whitelist.IsWhitelistPass(moduleWhitelist.ModuleBlacklist, contained))
            return true;

        if (TryComp<BorgModuleWhitelistComponent>(contained, out var containedWhitelist) &&
            _whitelist.IsWhitelistPass(containedWhitelist.ModuleBlacklist, module))
            return true;

        if (moduleComponents == null || !TryComp<ComponentBorgModuleComponent>(contained, out var containedComponents))
            return false;

        foreach (var name in moduleComponents.Components.Keys)
        {
            if (containedComponents.Components.ContainsKey(name))
                return true;
        }

        return false;
    }
}
