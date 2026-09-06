using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared._WH40K.CharacterCreation;
using Content.Shared.Preferences;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WH40K.CharacterCreation;

/// <summary>
/// Authoritative gate for changes made from the character personalization interface.
/// </summary>
public sealed partial class Wh40kProfileEditSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IAdminManager _admins = default!;

    public Wh40kProfileEditMode Mode =>
        Wh40kProfileEditPolicy.ParseMode(_configuration.GetCVar(CCVars.Wh40kProfileEditMode));

    public bool IsProfileMutationLocked(ICommonSession session) =>
        Mode != Wh40kProfileEditMode.Disabled && !CanBypass(session);

    public bool TryPrepareProfileUpdate(
        ICommonSession session,
        ICharacterProfile? existingProfile,
        ICharacterProfile submittedProfile,
        out ICharacterProfile preparedProfile)
    {
        preparedProfile = submittedProfile;

        if (Mode == Wh40kProfileEditMode.Disabled || CanBypass(session))
            return true;

        if (Mode == Wh40kProfileEditMode.FullLocked ||
            existingProfile is not HumanoidCharacterProfile existingHumanoid ||
            submittedProfile is not HumanoidCharacterProfile submittedHumanoid)
        {
            return false;
        }

        preparedProfile = Wh40kProfileEditPolicy.PreserveIdentityAndAppearance(existingHumanoid, submittedHumanoid);
        return true;
    }

    private bool CanBypass(ICommonSession session)
    {
        return _configuration.GetCVar(CCVars.Wh40kProfileEditAdminBypass) &&
               (_admins.HasAdminFlag(session, AdminFlags.Admin) ||
                _admins.HasAdminFlag(session, AdminFlags.Moderator));
    }
}
