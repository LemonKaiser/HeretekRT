using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WH40K.Merchant;
using Content.Shared._WH40K.Quests;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public Task<bool> ReserveWh40kMedicalCooldownAsync(Guid user, Guid operation, long now, long until, CancellationToken cancel = default)
        => QuestWriteAsync(Wh40kQuestOwnerKey.Account(new Robust.Shared.Network.NetUserId(user)), async db =>
        {
            if (operation == Guid.Empty || now <= 0 || until <= now) return false;
            var row = await db.Wh40kQuestAccountStates.FindAsync([user, Wh40kMedicalTreatment.CooldownKey], cancel);
            if (row != null && row.Amount > now) return false;
            if (row == null)
            {
                if (await db.Wh40kQuestAccountStates.CountAsync(state => state.UserId == user, cancel) >= 512) return false;
                row = new() { UserId = user, Key = Wh40kMedicalTreatment.CooldownKey };
                db.Wh40kQuestAccountStates.Add(row);
            }
            row.Amount = until;
            row.Value = operation.ToString("N");
            return true;
        }, cancel);

    public Task<bool> ReleaseWh40kMedicalCooldownAsync(Guid user, Guid operation, CancellationToken cancel = default)
        => QuestWriteAsync(Wh40kQuestOwnerKey.Account(new Robust.Shared.Network.NetUserId(user)), async db =>
        {
            var row = await db.Wh40kQuestAccountStates.FindAsync([user, Wh40kMedicalTreatment.CooldownKey], cancel);
            if (operation == Guid.Empty || row == null || row.Value != operation.ToString("N")) return false;
            row.Amount = 0;
            row.Value = null;
            return true;
        }, cancel);
}
