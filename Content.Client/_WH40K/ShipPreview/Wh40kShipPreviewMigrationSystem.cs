using System.IO;
using System.Linq;
using Robust.Shared.ContentPack;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WH40K.ShipPreview;

/// <summary>
/// Applies entity prototype migration maps to client-deserialized ship preview grids so older ship files load cleanly.
/// </summary>
public sealed partial class Wh40kShipPreviewMigrationSystem : EntitySystem
{
    [Dependency] private IResourceManager _resMan = default!;

    private static readonly ResPath[] MigrationFiles =
    {
        new("/migration.yml"),
        new("/nf_migration.yml"),
        new("/mono_migration.yml"),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeEntityRead);
    }

    private void OnBeforeEntityRead(BeforeEntityReadEvent ev)
    {
        foreach (var path in MigrationFiles)
        {
            if (!_resMan.TryContentFileRead(path, out var stream))
                continue;

            using var reader = new StreamReader(stream, EncodingHelpers.UTF8);
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode mapping)
                continue;

            foreach (var (from, node) in mapping)
            {
                if (node is not ValueDataNode value)
                    continue;

                if (string.IsNullOrWhiteSpace(value.Value) || value.Value == "null")
                    ev.DeletedPrototypes.Add(from);
                else
                    ev.RenamedPrototypes[from] = value.Value;
            }
        }
    }
}
