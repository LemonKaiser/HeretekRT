using System.Linq;
using Content.Server.Administration.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._WH40K.Factions;
using Content.Shared.Administration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.TypeParsers;
using Content.Server.Administration;

namespace Content.Server._Mono.Company;

[ToolshedCommand(Name = "company"), AnyCommand]
public sealed partial class CompanyCommand : ToolshedCommand
{
    [Dependency] private CompanyManager _company = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerLocator _playerLocator = default!;

    [CommandImplementation("addmember")]
    public async void Add(
        [CommandInvocationContext] IInvocationContext ctx,
        [CommandArgument] string username,
        [CommandArgument] ProtoId<CompanyPrototype> company)
    {
        if (!_prototypes.TryIndex(company, out var companyPrototype))
        {
            ctx.ReportError(new NotAValidPrototype(company, nameof(CompanyPrototype)));
            return;
        }

        if (ctx.Session == null
            || !_admin.HasAdminFlag(ctx.Session, AdminFlags.Admin)
                && !_company.IsOwner(ctx.Session, company)) // owner can add members
        {
            ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
            return;
        }

        var lookup = await _playerLocator.LookupIdByNameAsync(username);
        if (lookup == null)
        {
            ctx.WriteLine(Loc.GetString("cmd-company-player-not-found", ("player", username)));
            return;
        }

        if (_company.IsMember(lookup.UserId, company))
        {
            ctx.WriteLine(Loc.GetString("cmd-company-memberadd-already-whitelisted",
                ("player", username),
                ("companyId", company.Id),
                ("companyName", companyPrototype.Name)));
            return;
        }

        await _company.Ready;
        await _company.FactionOperations.WaitAsync();
        var added = false;
        try
        {
            var administrator = _admin.HasAdminFlag(ctx.Session, AdminFlags.Admin);
            if (!administrator && !_company.IsOwner(ctx.Session, company))
            {
                ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
                return;
            }
            if (companyPrototype.JoinRequired && _company.GetPlayerCompanies(lookup.UserId)
                    .Any(id => id != company && _prototypes.Index(id).JoinRequired))
            {
                ctx.WriteLine(Loc.GetString("wh40k-faction-admin-other-membership"));
                return;
            }
            if (companyPrototype.JoinRequired && !administrator)
            {
                added = await _company.AcceptJoinRequest(ctx.Session, company, lookup.UserId);
                if (!added)
                {
                    ctx.WriteLine(Loc.GetString("wh40k-faction-join-request-required"));
                    return;
                }
            }
            else
                added = await _company.AddMemberAsync(lookup.UserId, company);
        }
        finally
        {
            _company.FactionOperations.Release();
        }
        if (!added)
            return;
        ctx.WriteLine(Loc.GetString("cmd-company-memberadd-added",
            ("player", username),
            ("companyId", company.Id),
            ("companyName", companyPrototype.Name)));
    }

    [CommandImplementation("playercompanies")]
    public async void GetPlayerWhitelist(
        [CommandInvocationContext] IInvocationContext ctx,
        [CommandArgument] string username)
    {
        if (ctx.Session == null
            || !_admin.HasAdminFlag(ctx.Session, AdminFlags.Whitelist)
                && ctx.Session.Name != username) // allow looking at own companies
        {
            ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
            return;
        }

        var lookup = await _playerLocator.LookupIdByNameAsync(username);
        if (lookup == null)
        {
            ctx.WriteLine(Loc.GetString("cmd-company-player-not-found", ("player", username)));
            return;
        }

        var whitelists = _company.GetPlayerCompanies(lookup.UserId);
        ctx.WriteLine(Loc.GetString("cmd-company-playercompanies-whitelisted-for",
            ("player", username),
            ("companies", string.Join(", ", whitelists))));
    }

    [CommandImplementation("members")]
    public async void GetCompanyWhitelist(
        [CommandInvocationContext] IInvocationContext ctx,
        [CommandArgument] ProtoId<CompanyPrototype> company)
    {
        if (!_prototypes.TryIndex(company, out var companyPrototype))
        {
            ctx.ReportError(new NotAValidPrototype(company, nameof(CompanyPrototype)));
            return;
        }

        if (ctx.Session == null
            || !_admin.HasAdminFlag(ctx.Session, AdminFlags.Whitelist)
                && !_company.IsMember(ctx.Session.UserId, company)) // members can see other members
        {

            ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
            return;
        }

        var whitelisted = _company.GetCompanyMembers(company);

        var members = whitelisted.Where(w => !w.Owner).Select(m => m.LastSeenUserName);
        var owners = whitelisted.Where(w => w.Owner).Select(m => m.LastSeenUserName);

        ctx.WriteLine(Loc.GetString("cmd-company-members-whitelisted-for",
            ("company", company),
            ("players", string.Join(", ", members)),
            ("owners", string.Join(", ", owners))));
    }

    [CommandImplementation("setowner")]
    public async void SetOwner(
        [CommandInvocationContext] IInvocationContext ctx,
        [CommandArgument] ProtoId<CompanyPrototype> company,
        [CommandArgument] string username,
        [CommandArgument] bool owner
    )
    {
        if (!_prototypes.TryIndex(company, out var companyPrototype))
        {
            ctx.ReportError(new NotAValidPrototype(company, nameof(CompanyPrototype)));
            return;
        }

        // only admins can set owner
        if (ctx.Session == null
            || !_admin.HasAdminFlag(ctx.Session, AdminFlags.Admin))
        {

            ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
            return;
        }

        // check if we're trying to owner a non-member
        var lookup = await _playerLocator.LookupIdByNameAsync(username);
        if (lookup == null)
        {
            ctx.WriteLine(Loc.GetString("cmd-company-player-not-found", ("player", username)));
            return;
        }

        if (!_company.IsMember(lookup.UserId, company))
        {
            ctx.WriteLine(Loc.GetString("cmd-company-was-not-whitelisted",
                ("player", username),
                ("companyId", company.Id),
                ("companyName", companyPrototype.Name)));
            return;
        }

        await _company.Ready;
        await _company.FactionOperations.WaitAsync();
        var changed = false;
        try
        {
            if (!_admin.HasAdminFlag(ctx.Session, AdminFlags.Admin))
                return;
            changed = await _company.SetMemberRoleAsync(company, lookup.UserId, owner ? FactionRole.Leader : FactionRole.Member);
        }
        finally
        {
            _company.FactionOperations.Release();
        }
        if (!changed)
        {
            ctx.WriteLine(Loc.GetString("cmd-company-was-not-whitelisted", ("player", username),
                ("companyId", company.Id), ("companyName", companyPrototype.Name)));
            return;
        }
        ctx.WriteLine(Loc.GetString("cmd-company-setowner-success", ("player", username), ("status", owner)));
    }

    [CommandImplementation("removemember")]
    public async void Remove(
        [CommandInvocationContext] IInvocationContext ctx,
        [CommandArgument] string username,
        [CommandArgument] ProtoId<CompanyPrototype> company)
    {
        if (!_prototypes.TryIndex(company, out var companyPrototype))
        {
            ctx.ReportError(new NotAValidPrototype(company, nameof(CompanyPrototype)));
            return;
        }

        if (ctx.Session == null
            || !_admin.HasAdminFlag(ctx.Session, AdminFlags.Admin)
                && !_company.IsOwner(ctx.Session, company)) // owner can remove members
        {

            ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
            return;
        }

        var lookup = await _playerLocator.LookupIdByNameAsync(username);
        if (lookup == null)
        {
            ctx.WriteLine(Loc.GetString("cmd-company-player-not-found", ("player", username)));
            return;
        }

        if (!_company.IsMember(lookup.UserId, company))
        {
            ctx.WriteLine(Loc.GetString("cmd-company-was-not-whitelisted",
                ("player", username),
                ("companyId", company.Id),
                ("companyName", companyPrototype.Name)));
            return;
        }

        await _company.Ready;
        await _company.FactionOperations.WaitAsync();
        try
        {
            if (!_admin.HasAdminFlag(ctx.Session, AdminFlags.Admin) &&
                !FactionPermissions.CanRemove(_company.GetRole(ctx.Session.UserId, company),
                    _company.GetRole(lookup.UserId, company), ctx.Session.UserId == lookup.UserId))
            {
                ctx.WriteLine(Loc.GetString("cmd-company-not-enough-permissions"));
                return;
            }
            await _company.RemoveMember(lookup.UserId, company);
        }
        finally
        {
            _company.FactionOperations.Release();
        }
        ctx.WriteLine(Loc.GetString("cmd-company-memberremove-removed",
            ("player", username),
            ("companyId", company.Id),
            ("companyName", companyPrototype.Name)));
    }
}
