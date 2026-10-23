using System.Linq;
using System.Threading.Tasks;
using Content.Shared._WH40K.FactionStorage;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<List<Wh40kFactionStorageSlot>> GetWh40kFactionStorageAsync(string faction)
    {
        await using var guard = await GetDb();
        return await guard.DbContext.Wh40kFactionStorageSlots.AsNoTracking()
            .Where(row => row.Faction == faction).OrderBy(row => row.Slot).ToListAsync();
    }

    public async Task<Wh40kFactionStorageSlot?> GetWh40kFactionStorageSlotAsync(string faction, int slot)
    {
        await using var guard = await GetDb();
        return await guard.DbContext.Wh40kFactionStorageSlots.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Faction == faction && row.Slot == slot);
    }

    public async Task<bool> WriteWh40kFactionStorageSlotAsync(Wh40kFactionStorageSlot slot, Guid expectedRevision)
    {
        if (slot.Faction.Length is < 1 or > 128 || slot.Slot is < 0 or >= FactionStorageComponent.Capacity ||
            slot.Revision == Guid.Empty || slot.Revision == expectedRevision ||
            slot.Payload is { Length: 0 or > 1048576 } ||
            (slot.Payload != null && (slot.Sha256 is not { Length: 32 } || slot.Prototype is not { Length: > 0 and <= 128 } || slot.Count <= 0)) ||
            (slot.Payload == null && (slot.Sha256 != null || slot.Prototype != null || slot.Name != null || slot.Description != null || slot.Count != 0)) ||
            slot.Name is { Length: > 4096 } || slot.Description is { Length: > 16384 })
            throw new ArgumentException("Invalid faction storage slot.");

        // Retry only a conflicting insert/update. The caller reconciles an uncertain database response by revision.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var guard = await GetDb();
                var db = guard.DbContext;
                var existing = await db.Wh40kFactionStorageSlots.FindAsync(slot.Faction, slot.Slot);
                if (existing?.Revision == slot.Revision)
                    return true;
                if ((existing?.Revision ?? Guid.Empty) != expectedRevision)
                    return false;

                if (existing == null)
                    db.Wh40kFactionStorageSlots.Add(slot);
                else
                    db.Entry(existing).CurrentValues.SetValues(slot);
                await db.SaveChangesAsync();
                return true;
            }
            catch (Exception error) when (attempt < 3 && QuestRetryable(error))
            {
                await Task.Delay(10 * (attempt + 1));
            }
        }
    }
}
