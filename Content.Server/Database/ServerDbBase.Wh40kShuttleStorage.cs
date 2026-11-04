using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Shared._WH40K.ShuttleStorage;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<bool> ResolveWh40kShuttleAsync(Guid owner, long revision, Guid epoch, bool restore, int result)
    {
        if (owner == Guid.Empty || epoch == Guid.Empty || revision < 1)
            return false;
        await using var guard = await GetDb();
        var rows = guard.DbContext.Wh40kShuttleSlots.Where(row => row.Owner == owner &&
            row.Revision == revision && row.State == Wh40kShuttleState.RecoveryRequired);
        if (restore)
            return await rows.Where(row => row.Payload != null && row.Payload.Length <= 1_000_000)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.State, Wh40kShuttleState.Stored)
                    .SetProperty(row => row.Authorized, false).SetProperty(row => row.Revision, revision + 1)
                    .SetProperty(row => row.Epoch, epoch).SetProperty(row => row.Result, result)) == 1;
        return await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.State, Wh40kShuttleState.Empty)
            .SetProperty(row => row.Payload, (byte[]?) null).SetProperty(row => row.Sha256, (byte[]?) null)
            .SetProperty(row => row.Bytes, 0).SetProperty(row => row.Authorized, false)
            .SetProperty(row => row.Revision, revision + 1).SetProperty(row => row.Epoch, epoch)
            .SetProperty(row => row.Result, result)) == 1;
    }

    public async Task<Wh40kShuttleSlot?> GetWh40kShuttleAsync(Guid owner, bool payload)
    {
        await using var guard = await GetDb();
        return await guard.DbContext.Wh40kShuttleSlots.AsNoTracking().Where(row => row.Owner == owner)
            .Select(row => new Wh40kShuttleSlot
            {
                Owner = row.Owner, Revision = row.Revision, Operation = row.Operation, Ship = row.Ship,
                Epoch = row.Epoch, State = row.State, Authorized = row.Authorized, Schema = row.Schema, Result = row.Result,
                Name = row.Name, Width = row.Width, Height = row.Height, Entities = row.Entities,
                Bytes = row.Bytes, SavedAt = row.SavedAt, Sha256 = row.Sha256,
                Payload = payload && row.Payload != null && row.Payload.Length <= 1_000_000 ? row.Payload : null,
            }).SingleOrDefaultAsync();
    }

    public async Task<List<Wh40kShuttleSlot>> GetPendingWh40kShuttlesAsync()
    {
        await using var guard = await GetDb();
        return await guard.DbContext.Wh40kShuttleSlots.AsNoTracking()
            .Where(row => row.State == Wh40kShuttleState.Storing || row.State == Wh40kShuttleState.Loading)
            .Select(row => new Wh40kShuttleSlot { Owner = row.Owner, State = row.State })
            .ToListAsync();
    }

    public async Task<bool> WriteWh40kShuttleAsync(Wh40kShuttleSlot slot, long expectedRevision)
    {
        if (slot.Owner == Guid.Empty || slot.Operation == Guid.Empty || expectedRevision < 0 ||
            slot.Revision != expectedRevision + 1 || slot.Epoch == Guid.Empty || !Enum.IsDefined(slot.State) ||
            slot.Result is < 0 or > 255)
            throw new ArgumentException("Invalid shuttle storage transition.");
        if (slot.State != Wh40kShuttleState.RecoveryRequired && (slot.Name.Length > 4096 || slot.Payload is { Length: 0 or > 1_000_000 } ||
            slot.Payload != null && (slot.Bytes != slot.Payload.Length || slot.Sha256 is not { Length: 32 } ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(slot.Payload), slot.Sha256)) ||
            slot.State is Wh40kShuttleState.Stored or Wh40kShuttleState.Loading &&
                (slot.Payload == null || slot.Schema != 1 || slot.Ship == Guid.Empty ||
                 !ShuttleStorageLimits.FitsDimensions(slot.Width, slot.Height) || slot.Entities is < 1 or > 10000) ||
            slot.State == Wh40kShuttleState.Empty && (slot.Payload != null || slot.Sha256 != null || slot.Bytes != 0)))
            throw new ArgumentException("Invalid shuttle storage transition payload.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var guard = await GetDb();
                var db = guard.DbContext;
                if (slot.State == Wh40kShuttleState.RecoveryRequired)
                {
                    // Preserve even corrupt/oversized bytes verbatim without materializing or replacing the BLOB.
                    var updated = await db.Wh40kShuttleSlots.Where(row => row.Owner == slot.Owner &&
                            row.Revision == expectedRevision && row.Operation == slot.Operation &&
                            row.State != Wh40kShuttleState.Empty)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.State, Wh40kShuttleState.RecoveryRequired)
                            .SetProperty(row => row.Revision, slot.Revision)
                            .SetProperty(row => row.Epoch, slot.Epoch)
                            .SetProperty(row => row.Result, slot.Result));
                    return updated == 1 || await db.Wh40kShuttleSlots.AnyAsync(row => row.Owner == slot.Owner &&
                        row.Operation == slot.Operation && row.Revision == slot.Revision && row.State == slot.State &&
                        row.Epoch == slot.Epoch && row.Result == slot.Result && row.Ship == slot.Ship && row.Authorized == slot.Authorized);
                }
                var previous = await db.Wh40kShuttleSlots.FindAsync(slot.Owner);
                if (previous?.MatchesTransition(slot) == true)
                    return true;
                if ((previous?.Revision ?? 0) != expectedRevision || !ValidShuttleTransition(previous, slot))
                    return false;
                if (previous == null)
                    db.Wh40kShuttleSlots.Add(slot);
                else
                    db.Entry(previous).CurrentValues.SetValues(slot);
                await db.SaveChangesAsync();
                return true;
            }
            catch (Exception error) when (attempt < 3 && QuestRetryable(error))
            {
                await Task.Delay(10 * (attempt + 1));
            }
        }
    }

    private static bool ValidShuttleTransition(Wh40kShuttleSlot? old, Wh40kShuttleSlot next)
    {
        var state = old?.State ?? Wh40kShuttleState.Empty;
        if (state == Wh40kShuttleState.Empty)
            return next.State == Wh40kShuttleState.Storing && !next.Authorized && next.Payload == null &&
                   next.Operation != old?.Operation;
        if (next.State == Wh40kShuttleState.RecoveryRequired)
            return next.Operation == old!.Operation;
        if (next.Operation != old!.Operation && !(state == Wh40kShuttleState.Stored && next.State == Wh40kShuttleState.Loading))
            return false;
        return state switch
        {
            Wh40kShuttleState.Storing => next.State switch
            {
                Wh40kShuttleState.Storing => !old.Authorized && next.Authorized && next.Payload != null,
                Wh40kShuttleState.Stored => old.Authorized && next.Payload != null,
                Wh40kShuttleState.Empty => !old.Authorized,
                _ => false,
            },
            Wh40kShuttleState.Stored => next.State == Wh40kShuttleState.Loading && !next.Authorized && next.Payload != null,
            Wh40kShuttleState.Loading => next.State switch
            {
                Wh40kShuttleState.Loading => !old.Authorized && next.Authorized && next.Payload != null,
                Wh40kShuttleState.Stored => !old.Authorized && next.Payload != null,
                Wh40kShuttleState.Empty => old.Authorized,
                _ => false,
            },
            _ => false,
        };
    }
}
