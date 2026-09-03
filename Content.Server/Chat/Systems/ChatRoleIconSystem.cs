using Content.Server.Access.Systems;
using Content.Shared.Inventory;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Server.Chat.Systems;

/// <summary>
///     Resolves the public ID-card icon snapshot attached to a chat message.
/// </summary>
public sealed class ChatRoleIconSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IdCardSystem _idCards = default!;

    private static readonly ProtoId<JobIconPrototype> JobIconNoId = "JobIconNoId";

    private static readonly ProtoId<JobIconPrototype> JobIconUnknown = "JobIconUnknown";

    /// <summary>
    ///     Gets the icon from the sender's equipped ID slot. A PDA in that slot is resolved to its contained
    ///     ID card. This deliberately does not inspect hands or backpacks: the displayed icon represents the
    ///     credential currently worn by the sender.
    /// </summary>
    public string? ResolveSenderJobIcon(EntityUid source)
    {
        if (source == EntityUid.Invalid ||
            !Exists(source) ||
            !_inventory.HasSlot(source, "id"))
        {
            // Non-humanoids and objects can produce chat messages too.  They do not have an
            // identity credential, so an "ID missing" icon would be misleading for them.
            return null;
        }

        if (!_inventory.TryGetSlotEntity(source, "id", out var idSlotItem) ||
            !_idCards.TryGetIdCard(idSlotItem.Value, out var idCard))
        {
            return JobIconNoId.Id;
        }

        var candidate = idCard.Comp.JobIcon.Id;
        return candidate != JobIconNoId.Id &&
               candidate != JobIconUnknown.Id &&
               _prototypes.HasIndex<JobIconPrototype>(candidate)
            ? candidate
            : JobIconNoId.Id;
    }
}
