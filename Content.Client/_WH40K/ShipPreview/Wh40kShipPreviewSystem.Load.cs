using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.DeviceLinking;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Client._WH40K.ShipPreview;

/// <summary>
/// Deserializes client-side preview grids and strips device links before startup.
/// </summary>
public sealed partial class Wh40kShipPreviewSystem
{
    [Dependency] private IDependencyCollection _dependencies = default!;

    private bool TryLoadGrid(
        Wh40kShipPreviewHandle handle,
        MappingDataNode data,
        [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        grid = null;
        RemoveUnknownComponents(data);
        var migrations = new BeforeEntityReadEvent();
        RaiseLocalEvent(migrations);

        var reader = new EntityDeserializer(
            _dependencies,
            data,
            new DeserializationOptions
            {
                InitializeMaps = false,
                PauseMaps = true,
            },
            migrations.RenamedPrototypes,
            migrations.DeletedPrototypes);

        var loaded = false;
        try
        {
            if (!reader.TryProcessData())
                return false;
            if (reader.Result.Category is not (FileCategory.Grid or FileCategory.Unknown))
                return false;

            reader.CreateEntities();
            if (reader.Result.Category != FileCategory.Grid || reader.Result.Grids.Count != 1)
                return false;

            foreach (var uid in reader.Result.Entities)
            {
                RemComp<DeviceLinkSourceComponent>(uid);

                // Deserialization fills sprite data after ComponentAdd. Re-add it before visualizers start.
                if (TryComp<SpriteComponent>(uid, out var sprite))
                    CopyComp(uid, uid, sprite);
            }

            foreach (var uid in reader.Result.Orphans)
                _xform.SetCoordinates(uid, new EntityCoordinates(handle.MapUid, Transform(uid).LocalPosition));
            reader.Result.Orphans.Clear();

            reader.StartEntities();
            grid = reader.Result.Grids.Single();
            foreach (var uid in reader.Result.Entities)
                _meta.SetEntityPaused(uid, true);

            handle.LoadedEntities = reader.Result;
            loaded = true;
            return true;
        }
        finally
        {
            if (!loaded)
                _loader.Delete(reader.Result);
        }
    }
}
