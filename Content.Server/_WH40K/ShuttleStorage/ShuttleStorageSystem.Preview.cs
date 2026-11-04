using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._WH40K.ShuttleStorage;
using Robust.Shared.Network;

namespace Content.Server._WH40K.ShuttleStorage;

public sealed partial class ShuttleStorageSystem
{
    private readonly Dictionary<EntityUid, (TimeSpan Until, ShuttleStoragePreview? Preview)> _gridPreviews = new();

    private ShuttleStoragePreview? GridPreview(EntityUid grid, ShuttleStorageInspection info)
    {
        if (!ShuttleStorageLimits.FitsDimensions(info.Width, info.Height))
            return null;
        if (_gridPreviews.TryGetValue(grid, out var cached) && cached.Until > _timing.CurTime)
            return cached.Preview;
        if (_gridPreviews.Count >= 128)
            _gridPreviews.Clear();
        var preview = _serializer.PreviewGrid(grid, info);
        _gridPreviews[grid] = (_timing.CurTime + TimeSpan.FromSeconds(1), preview);
        return preview;
    }

    private void UpdatePreview(NetUserId owner, View view)
    {
        if (view.Row is not { State: not Wh40kShuttleState.Empty } row)
        {
            view.Preview = null;
            view.PreviewRead = null;
            view.PreviewRevision = -1;
            return;
        }
        if (view.PreviewRevision != row.Revision)
        {
            view.Preview = null;
            view.PreviewRevision = row.Revision;
            view.PreviewRead = ReadPreviewAsync(owner, row.Revision, _serializer.PreviewTypes());
        }
        if (view.PreviewRead is not { IsCompleted: true } read)
            return;
        try { view.Preview = read.GetAwaiter().GetResult(); }
        catch (Exception error) { Log.Warning($"Hangar projection unavailable for {owner}: {error.Message}"); }
        view.PreviewRead = null;
    }

    private async Task<ShuttleStoragePreview?> ReadPreviewAsync(NetUserId owner, long revision,
        IReadOnlyDictionary<string, ShuttleStorageCell> types)
    {
        // Bounded BLOB read once per viewed revision. Parsing never touches ECS or creates a grid/map.
        try
        {
            var row = await _db.GetWh40kShuttleAsync(owner.UserId, payload: true);
            if (row?.Revision != revision || row.Payload is not { Length: > 0 and <= ShuttleStorageLimits.Bytes } payload ||
                row.Sha256 is not { Length: 32 } hash)
                return null;
            return await Task.Run(() => CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), hash)
                ? ShuttleStorageSerializer.PreviewPayload(payload, types) : null);
        }
        catch (Exception error)
        {
            Log.Warning($"Hangar projection could not be read for {owner}: {error.Message}");
            return null;
        }
    }
}
