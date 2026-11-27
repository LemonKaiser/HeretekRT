using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WH40K.Biotype;

/// <summary>
/// Restricts a loadout entry to characters whose effective <see cref="Wh40kBiotype"/> matches.
/// </summary>
public sealed partial class Wh40kBiotypeLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public HashSet<Wh40kBiotype> Biotypes = new();

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (Biotypes.Contains(Wh40kBiotypeGates.Effective(profile, collection.Resolve<IPrototypeManager>())))
        {
            reason = null;
            return true;
        }

        reason = FormattedMessage.FromUnformatted(Loc.GetString("wh40k-biotype-loadout-restriction"));
        return false;
    }
}
