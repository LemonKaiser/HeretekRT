using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server._WH40K.OperationalDomain;
using Content.Shared.CCVar;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;

namespace Content.Server.AlertLevel;

public sealed partial class AlertLevelSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private OperationalDomainSystem _operationalDomains = default!;

    // Until stations are a prototype, this is how it's going to have to be.
    public const string DefaultAlertLevelSet = "stationAlerts";

    public override void Initialize()
    {
        SubscribeLocalEvent<AlertLevelComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypeReload);
    }

    public override void Update(float time)
    {
        var query = EntityQueryEnumerator<AlertLevelComponent>();

        while (query.MoveNext(out var station, out var alert))
        {
            if (alert.CurrentDelay <= 0)
            {
                if (alert.ActiveDelay)
                {
                    RaiseLocalEvent(new AlertLevelDelayFinishedEvent());
                    alert.ActiveDelay = false;
                }
                continue;
            }

            alert.CurrentDelay -= time;
        }
    }

    private void OnInit(EntityUid uid, AlertLevelComponent comp, ComponentInit args)
    {
        InitializeAlertLevel(uid, comp);
    }

    private void OnPrototypeReload(PrototypesReloadedEventArgs args)
    {
        if (!args.ByType.TryGetValue(typeof(AlertLevelPrototype), out var alertPrototypes)
            || !alertPrototypes.Modified.TryGetValue(DefaultAlertLevelSet, out var alertObject)
            || alertObject is not AlertLevelPrototype alerts)
        {
            return;
        }

        var query = EntityQueryEnumerator<AlertLevelComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.AlertLevels = alerts;

            if (!comp.AlertLevels.Levels.ContainsKey(comp.CurrentLevel))
            {
                var defaultLevel = comp.AlertLevels.DefaultLevel;
                if (string.IsNullOrEmpty(defaultLevel))
                {
                    defaultLevel = comp.AlertLevels.Levels.Keys.First();
                }

                SetLevelDirect(uid, defaultLevel, true, true, true, component: comp);
            }
        }

        RaiseLocalEvent(new AlertLevelPrototypeReloadedEvent());
    }

    public string GetLevel(EntityUid station, AlertLevelComponent? alert = null)
    {
        if (!TryGetDomainAlertLevel(station, out _, out alert))
            return string.Empty;

        return alert.CurrentLevel;
    }

    public float GetAlertLevelDelay(EntityUid station, AlertLevelComponent? alert = null)
    {
        if (!TryGetDomainAlertLevel(station, out _, out alert))
            return float.NaN;

        return alert.CurrentDelay;
    }

    /// <summary>
    /// Resolves the alert state belonging to an entity's operational domain.
    /// Independent vessel grids receive their own alert component on first use.
    /// </summary>
    public bool TryGetDomainAlertLevel(
        EntityUid entity,
        out EntityUid owner,
        [NotNullWhen(true)] out AlertLevelComponent? alert)
    {
        owner = EntityUid.Invalid;
        alert = null;

        if (_operationalDomains.TryResolveOperationalDomain(entity, out var domain))
        {
            owner = domain.Owner;
            alert = EnsureComp<AlertLevelComponent>(owner);
            return InitializeAlertLevel(owner, alert);
        }

        if (!TryComp(entity, out alert))
            return false;

        owner = entity;
        return InitializeAlertLevel(owner, alert);
    }

    /// <summary>
    /// Set the alert level based on the station's entity ID.
    /// </summary>
    /// <param name="station">Station entity UID.</param>
    /// <param name="level">Level to change the station's alert level to.</param>
    /// <param name="playSound">Play the alert level's sound.</param>
    /// <param name="announce">Say the alert level's announcement.</param>
    /// <param name="force">Force the alert change. This applies if the alert level is not selectable or not.</param>
    /// <param name="locked">Will it be possible to change level by crew.</param>
    public void SetLevel(EntityUid station, string level, bool playSound, bool announce, bool force = false,
        bool locked = false, MetaDataComponent? dataComponent = null, AlertLevelComponent? component = null)
    {
        if (!TryGetDomainAlertLevel(station, out var owner, out component))
            return;

        SetLevelDirect(owner, level, playSound, announce, force, locked, component: component);
    }

    private bool InitializeAlertLevel(EntityUid uid, AlertLevelComponent component)
    {
        if (component.AlertLevels != null)
            return true;

        component.AlertLevelPrototype = string.IsNullOrWhiteSpace(component.AlertLevelPrototype)
            ? DefaultAlertLevelSet
            : component.AlertLevelPrototype;

        if (!_prototypeManager.TryIndex(component.AlertLevelPrototype, out AlertLevelPrototype? alerts))
            return false;

        component.AlertLevels = alerts;
        var defaultLevel = alerts.DefaultLevel;
        if (string.IsNullOrEmpty(defaultLevel))
            defaultLevel = alerts.Levels.Keys.First();

        SetLevelDirect(uid, defaultLevel, false, false, true, component: component);
        return true;
    }

    private void SetLevelDirect(EntityUid owner, string level, bool playSound, bool announce, bool force = false,
        bool locked = false, MetaDataComponent? dataComponent = null, AlertLevelComponent? component = null)
    {
        if (!Resolve(owner, ref component))
            return;

        if (component.AlertLevels == null
            || !component.AlertLevels.Levels.TryGetValue(level, out var detail)
            || component.CurrentLevel == level)
        {
            return;
        }

        if (!force)
        {
            if (!detail.Selectable
                || component.CurrentDelay > 0
                || component.IsLevelLocked)
            {
                return;
            }

            component.CurrentDelay = _cfg.GetCVar(CCVars.GameAlertLevelChangeDelay);
            component.ActiveDelay = true;
        }

        component.CurrentLevel = level;
        component.IsLevelLocked = locked;

        var name = level.ToLower();

        if (Loc.TryGetString($"alert-level-{level}", out var locName))
        {
            name = locName.ToLower();
        }

        // Announcement text. Is passed into announcementFull.
        var announcement = detail.Announcement;

        if (Loc.TryGetString(detail.Announcement, out var locAnnouncement))
        {
            announcement = locAnnouncement;
        }

        // The full announcement to be spat out into chat.
        var announcementFull = Loc.GetString("alert-level-announcement", ("name", name), ("announcement", announcement));

        var playDefault = false;
        var hasDomain = _operationalDomains.TryResolveOperationalDomain(owner, out var domain);
        if (playSound && hasDomain)
        {
            if (detail.Sound != null)
            {
                var filter = Filter.Empty();
                foreach (var session in _operationalDomains.GetPlayersInOperationalDomain(domain))
                {
                    filter.AddPlayer(session);
                }
                _audio.PlayGlobal(detail.Sound, filter, true, detail.Sound.Params);
            }
            else
            {
                playDefault = true;
            }
        }

        if (announce && hasDomain)
        {
            _chatSystem.DispatchDomainAnnouncement(
                owner,
                announcementFull,
                sender: Name(owner),
                playDefaultSound: playDefault,
                colorOverride: detail.Color);
        }

        RaiseLocalEvent(new AlertLevelChangedEvent(owner, level));
    }
}

public sealed class AlertLevelDelayFinishedEvent : EntityEventArgs
{}

public sealed class AlertLevelPrototypeReloadedEvent : EntityEventArgs
{}

public sealed class AlertLevelChangedEvent : EntityEventArgs
{
    public EntityUid Station { get; }
    public string AlertLevel { get; }

    public AlertLevelChangedEvent(EntityUid station, string alertLevel)
    {
        Station = station;
        AlertLevel = alertLevel;
    }
}
