using System.Linq;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Interaction;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Tools.Components;
using Robust.Shared.Network;

namespace Content.Shared._WH40K.Lathe;

public abstract partial class SharedWh40kFabricationSiloSystem
{
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private SharedInjectorSystem _injector = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popups = default!;

    private void InitializeFilling()
    {
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(SolutionSpikerSystem) });
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, SolutionTransferAttemptEvent>(OnTransferAttempt);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, GetDumpableVerbEvent>(OnGetDumpableVerb);
        SubscribeLocalEvent<Wh40kFabricationSiloComponent, DumpEvent>(OnDump);
    }

    public FixedPoint2 Fill(Entity<Wh40kFabricationSiloComponent> silo,
        EntityUid user,
        EntityUid source,
        Entity<SolutionComponent> sourceSoln,
        FixedPoint2 amount)
    {
        if (!TryGetSolution(silo, out var soln, out var stored))
            return FixedPoint2.Zero;

        var attempt = new SolutionTransferAttemptEvent(source, silo.Owner);
        RaiseLocalEvent(source, ref attempt);
        if (attempt.CancelReason is { } reason)
        {
            _popups.PopupClient(reason, source, user);
            return FixedPoint2.Zero;
        }

        var solution = sourceSoln.Comp.Solution;
        if (solution.Volume == FixedPoint2.Zero)
        {
            _popups.PopupClient(Loc.GetString("comp-solution-transfer-is-empty", ("target", source)), source, user);
            return FixedPoint2.Zero;
        }

        var accepted = new List<string>();
        var refused = new List<string>();
        var acceptedVolume = FixedPoint2.Zero;
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (!IsAcceptedReagent(reagent.Prototype))
            {
                if (!refused.Contains(reagent.Prototype))
                    refused.Add(reagent.Prototype);
                continue;
            }

            if (!accepted.Contains(reagent.Prototype))
                accepted.Add(reagent.Prototype);
            acceptedVolume += quantity;
        }

        if (accepted.Count == 0)
        {
            _popups.PopupClient(Loc.GetString("fabrication-silo-reagents-refused", ("reagents", ReagentNames(refused))),
                silo,
                user);
            return FixedPoint2.Zero;
        }

        var take = FixedPoint2.Min(amount, FixedPoint2.Min(acceptedVolume, stored.AvailableVolume));
        if (take <= FixedPoint2.Zero)
        {
            _popups.PopupClient(Loc.GetString("comp-solution-transfer-is-full", ("target", silo.Owner)), silo, user);
            return FixedPoint2.Zero;
        }

        var moved = solution.SplitSolutionWithOnly(take, accepted.ToArray());
        _solution.UpdateChemicals(sourceSoln);
        _solution.TryAddSolution(soln.Value, moved);

        var message = refused.Count == 0
            ? Loc.GetString("comp-solution-transfer-transfer-solution", ("amount", moved.Volume), ("target", silo.Owner))
            : Loc.GetString("fabrication-silo-fill-partial",
                ("amount", moved.Volume),
                ("reagents", ReagentNames(refused)));
        _popups.PopupClient(message, silo, user);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(user):player} transferred {SharedSolutionContainerSystem.ToPrettyString(moved)} to {ToPrettyString(silo.Owner):target}, which now contains {SharedSolutionContainerSystem.ToPrettyString(stored)}");

        return moved.Volume;
    }

    protected void RefreshClients(Wh40kFabricationSiloComponent comp)
    {
        foreach (var uid in comp.Clients)
        {
            RefreshClient(uid);
        }
    }

    private void OnInteractUsing(Entity<Wh40kFabricationSiloComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = ent.Comp.Kind == Wh40kFabricationSiloKind.Chemicals
            ? TryFillFrom(ent, args.User, args.Used)
            : TryInsertPart(ent, args.User, args.Used);
    }

    private void OnActivate(Entity<Wh40kFabricationSiloComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled ||
            ent.Comp.Kind != Wh40kFabricationSiloKind.Chemicals ||
            !HasComp<AbsorbentComponent>(args.User))
            return;

        _popups.PopupClient(Loc.GetString("fabrication-silo-no-draw"), ent, args.User);
        args.Handled = true;
    }

    private bool TryFillFrom(Entity<Wh40kFabricationSiloComponent> ent, EntityUid user, EntityUid used)
    {
        if (HasComp<AbsorbentComponent>(used))
        {
            _popups.PopupClient(Loc.GetString("fabrication-silo-no-draw"), ent, user);
            return true;
        }

        if (TryComp<SolutionTransferComponent>(used, out var transfer) &&
            transfer.CanSend &&
            _solution.TryGetDrainableSolution(used, out var drainable, out _))
        {
            var amount = transfer.TransferAmount;
            if (TryComp<RefillableSolutionComponent>(ent, out var refill) && refill.MaxRefill is { } maxRefill)
                amount = FixedPoint2.Min(amount, maxRefill);

            Fill(ent, user, used, drainable.Value, amount);
            return true;
        }

        if (TryComp<InjectorComponent>(used, out var injector) &&
            injector.ToggleState == InjectorToggleMode.Inject &&
            _solution.TryGetSolution(used, injector.SolutionName, out var injected, out var injectorSolution))
        {
            Fill(ent, user, used, injected.Value, injector.TransferAmount);
            if (injectorSolution.Volume == FixedPoint2.Zero)
                _injector.SetMode((used, injector), InjectorToggleMode.Draw);
            return true;
        }

        if (TryComp<SolutionSpikerComponent>(used, out var spiker) &&
            _solution.TryGetSolution(used, spiker.SourceSolution, out var spiked, out var spikerSolution))
        {
            Fill(ent, user, used, spiked.Value, spikerSolution.Volume);

            if (spiker.Delete && spikerSolution.Volume == FixedPoint2.Zero && _net.IsServer)
                QueueDel(used);
            return true;
        }

        return false;
    }

    private bool TryInsertPart(Entity<Wh40kFabricationSiloComponent> ent, EntityUid user, EntityUid used)
    {
        if (ent.Comp.Parts is not { } parts || HasComp<ToolComponent>(used))
            return false;

        if (!IsAcceptedPart(used))
        {
            if (!HasComp<DumpableComponent>(used))
                _popups.PopupClient(Loc.GetString("fabrication-silo-part-rejected"), ent, user);
            return false;
        }

        if (parts.ContainedEntities.Count >= ent.Comp.MaxParts)
        {
            _popups.PopupClient(Loc.GetString("fabrication-silo-full"), ent, user);
            return true;
        }

        if (!_container.Insert(used, parts))
        {
            _popups.PopupClient(Loc.GetString("fabrication-silo-insertion-failed"), ent, user);
            return false;
        }

        RefreshClients(ent.Comp);
        return true;
    }

    private void OnTransferAttempt(Entity<Wh40kFabricationSiloComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.To != ent.Owner ||
            !_solution.TryGetDrainableSolution(args.From, out _, out var solution))
            return;

        var refused = solution.Contents
            .Select(reagent => reagent.Reagent.Prototype)
            .Where(reagent => !IsAcceptedReagent(reagent))
            .Distinct()
            .ToList();

        if (refused.Count > 0)
            args.Cancel(Loc.GetString("fabrication-silo-reagents-refused", ("reagents", ReagentNames(refused))));
    }

    private void OnGetDumpableVerb(Entity<Wh40kFabricationSiloComponent> ent, ref GetDumpableVerbEvent args)
    {
        if (ent.Comp.Kind == Wh40kFabricationSiloKind.Parts)
            args.Verb = Loc.GetString("fabrication-silo-dump-verb", ("silo", ent.Owner));
    }

    private void OnDump(Entity<Wh40kFabricationSiloComponent> ent, ref DumpEvent args)
    {
        if (args.Handled || ent.Comp.Parts is not { } parts)
            return;

        args.Handled = true;
        var inserted = false;
        var refused = false;
        var full = false;
        foreach (var item in args.DumpQueue)
        {
            if (!IsAcceptedPart(item))
            {
                refused = true;
                continue;
            }

            if (parts.ContainedEntities.Count >= ent.Comp.MaxParts)
            {
                full = true;
                break;
            }

            inserted |= _container.Insert(item, parts);
        }

        if (inserted)
        {
            args.PlaySound = true;
            RefreshClients(ent.Comp);
        }

        if (full)
            _popups.PopupClient(Loc.GetString("fabrication-silo-full"), ent, args.User);
        else if (refused)
            _popups.PopupClient(Loc.GetString("fabrication-silo-parts-left"), ent, args.User);
    }

    private string ReagentNames(List<string> reagents)
    {
        return ContentLocalizationManager.FormatList(reagents
            .Select(id => _prototype.TryIndex<ReagentPrototype>(id, out var proto) ? proto.LocalizedName : id)
            .ToList());
    }
}
