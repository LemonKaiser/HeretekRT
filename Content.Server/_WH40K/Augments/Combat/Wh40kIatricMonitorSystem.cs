using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Shared._WH40K.Augments.Combat;
using Content.Shared.Actions;
using Content.Shared.MedicalScanner;
using Robust.Server.GameObjects;

namespace Content.Server._WH40K.Augments.Combat;

/// <summary>
/// Grants a body with <see cref="Wh40kIatricMonitorComponent"/> a silent self-scan action
/// opening the standard Health Analyzer UI on itself.
/// </summary>
public sealed partial class Wh40kIatricMonitorSystem : EntitySystem
{
    [Dependency] private HealthAnalyzerSystem _analyzer = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Wh40kIatricMonitorComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Wh40kIatricMonitorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<Wh40kIatricMonitorComponent, Wh40kIatricScanEvent>(OnScan);
        SubscribeLocalEvent<Wh40kIatricScannerComponent, BoundUIClosedEvent>(OnClosed);
    }

    private void OnStartup(Entity<Wh40kIatricMonitorComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<Wh40kIatricMonitorComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnScan(Entity<Wh40kIatricMonitorComponent> ent, ref Wh40kIatricScanEvent args)
    {
        var action = args.Action.Owner;
        if (action != ent.Comp.ActionEntity || !TryComp<HealthAnalyzerComponent>(action, out var analyzer))
            return;

        args.Handled = true;
        if (_ui.IsUiOpen(action, HealthAnalyzerUiKey.Key, ent.Owner))
        {
            _ui.CloseUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
            return;
        }

        _ui.OpenUi(action, HealthAnalyzerUiKey.Key, ent.Owner);
        _analyzer.BeginAnalyzingEntity((action, analyzer), ent);
    }

    private void OnClosed(Entity<Wh40kIatricScannerComponent> ent, ref BoundUIClosedEvent args)
    {
        if (HealthAnalyzerUiKey.Key.Equals(args.UiKey) &&
            TryComp<HealthAnalyzerComponent>(ent, out var analyzer) &&
            analyzer.ScannedEntity is { } patient)
        {
            _analyzer.StopAnalyzingEntity((ent, analyzer), patient);
        }
    }
}
