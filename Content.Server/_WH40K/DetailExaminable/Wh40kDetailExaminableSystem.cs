using System.Linq;
using Content.Server.EUI;
using Content.Shared.CCVar;
using Content.Shared._WH40K.DetailExaminable;
using Content.Shared.DetailExaminable;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._WH40K.DetailExaminable;

public sealed class Wh40kDetailExaminableSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;

    private readonly HashSet<Wh40kDetailExaminableEui> _openDetails = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<DetailExaminableComponent, GetVerbsEvent<ExamineVerb>>(OnGetExamineVerbs);
        SubscribeLocalEvent<DetailExaminableComponent, EntityTerminatingEvent>(OnDetailEntityTerminating);

        _config.OnValueChanged(CCVars.FlavorText, _ => CloseAllDetails());
        _config.OnValueChanged(CCVars.FlavorTraitsEnabled, _ => CloseAllDetails());
        _config.OnValueChanged(CCVars.FlavorOocEnabled, _ => CloseAllDetails());
        _config.OnValueChanged(CCVars.FlavorLinksEnabled, _ => CloseAllDetails());
        _config.OnValueChanged(CCVars.FlavorGyrEnabled, _ => CloseAllDetails());
    }

    public override void Update(float frameTime)
    {
        foreach (var details in _openDetails.ToArray())
        {
            if (!CanKeepDetailsOpen(details))
                details.Close();
        }
    }

    private void OnGetExamineVerbs(Entity<DetailExaminableComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (!_config.GetCVar(CCVars.FlavorText))
            return;

        if (Identity.Name(args.Target, EntityManager) != MetaData(args.Target).EntityName)
            return;

        var inRange = _examine.IsInDetailsRange(args.User, entity);
        var user = args.User;
        args.Verbs.Add(new ExamineVerb
        {
            Act = () => OpenDetails(user, entity.Owner),
            Text = Loc.GetString("detail-examinable-verb-text"),
            Category = VerbCategory.Examine,
            Disabled = !inRange,
            Message = inRange ? null : Loc.GetString("detail-examinable-verb-disabled"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/examine.svg.192dpi.png")),
        });
    }

    private void OpenDetails(EntityUid user, EntityUid target)
    {
        if (!TryComp<DetailExaminableComponent>(target, out var detail) ||
            !TryComp<HumanoidAppearanceComponent>(target, out var humanoid) ||
            !_players.TryGetSessionByEntity(user, out var session))
        {
            return;
        }

        var showTraits = _config.GetCVar(CCVars.FlavorTraitsEnabled);
        var showOoc = _config.GetCVar(CCVars.FlavorOocEnabled) && detail.ShareOocContent;
        var showLinks = _config.GetCVar(CCVars.FlavorLinksEnabled) && detail.ShareLinksContent;
        var showPreferences = _config.GetCVar(CCVars.FlavorGyrEnabled) && detail.SharePreferencesContent;
        var showTags = (_config.GetCVar(CCVars.FlavorOocEnabled) || _config.GetCVar(CCVars.FlavorLinksEnabled)) &&
                       !string.IsNullOrWhiteSpace(detail.TagsContent);

        var state = new Wh40kDetailExaminableEuiState(
            GetNetEntity(target),
            Identity.Name(target, EntityManager),
            humanoid.Species.Id,
            humanoid.Sex,
            humanoid.Gender,
            showTraits,
            showOoc,
            showLinks,
            showPreferences,
            showTags,
            detail.Content,
            showOoc ? detail.OocContent : string.Empty,
            showTraits ? detail.CharacterContent : string.Empty,
            showPreferences ? detail.GreenContent : string.Empty,
            showPreferences ? detail.YellowContent : string.Empty,
            showPreferences ? detail.RedContent : string.Empty,
            showTags ? detail.TagsContent : string.Empty,
            showLinks ? detail.LinksContent : string.Empty,
            detail.PortraitId);

        var window = new Wh40kDetailExaminableEui(state, target, OnDetailsClosed);
        _openDetails.Add(window);
        _eui.OpenEui(window, session);
        window.StateDirty();
    }

    private bool CanKeepDetailsOpen(Wh40kDetailExaminableEui details)
    {
        if (!_config.GetCVar(CCVars.FlavorText) ||
            details.Player.AttachedEntity is not { Valid: true } user ||
            TryComp<MobStateComponent>(user, out var userMobState) && userMobState.CurrentState == MobState.Dead ||
            !Exists(details.Target) ||
            !TryComp<DetailExaminableComponent>(details.Target, out _) ||
            !TryComp<HumanoidAppearanceComponent>(details.Target, out _) ||
            Identity.Name(details.Target, EntityManager) != MetaData(details.Target).EntityName)
        {
            return false;
        }

        return _examine.IsInDetailsRange(user, details.Target);
    }

    private void OnDetailEntityTerminating(Entity<DetailExaminableComponent> entity, ref EntityTerminatingEvent args)
    {
        foreach (var details in _openDetails.Where(details => details.Target == entity.Owner).ToArray())
        {
            details.Close();
        }
    }

    private void OnDetailsClosed(Wh40kDetailExaminableEui details)
    {
        _openDetails.Remove(details);
    }

    private void CloseAllDetails()
    {
        foreach (var details in _openDetails.ToArray())
        {
            details.Close();
        }
    }
}
