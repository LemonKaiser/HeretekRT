using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<List<Wh40kFactionStorageSlot>> GetWh40kFactionStorageAsync(string faction);
    Task<Wh40kFactionStorageSlot?> GetWh40kFactionStorageSlotAsync(string faction, int slot);
    Task<bool> WriteWh40kFactionStorageSlotAsync(Wh40kFactionStorageSlot slot, Guid expectedRevision);
}

public sealed partial class ServerDbManager
{
    public Task<List<Wh40kFactionStorageSlot>> GetWh40kFactionStorageAsync(string faction)
        => RunDbCommand(() => _db.GetWh40kFactionStorageAsync(faction));

    public Task<Wh40kFactionStorageSlot?> GetWh40kFactionStorageSlotAsync(string faction, int slot)
        => RunDbCommand(() => _db.GetWh40kFactionStorageSlotAsync(faction, slot));

    public Task<bool> WriteWh40kFactionStorageSlotAsync(Wh40kFactionStorageSlot slot, Guid expectedRevision)
        => RunDbCommand(() => _db.WriteWh40kFactionStorageSlotAsync(slot, expectedRevision));
}
