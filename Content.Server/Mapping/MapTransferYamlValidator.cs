using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Robust.Shared.EntitySerialization;
using Robust.Shared.ContentPack;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Content.Server.Mapping;

/// <summary>
///     Validates an untrusted map before it enters the mapper-map directory.
///     It intentionally does not instantiate entities or call MapLoaderSystem.
/// </summary>
internal static class MapTransferYamlValidator
{
    private const int MaxScalarLength = 65_536;

    public static bool TryValidate(
        IWritableDirProvider userData,
        ResPath path,
        long maxBytes,
        int maxDepth,
        int maxNodes)
    {
        try
        {
            using (var preflightStream = userData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var length = preflightStream.Length;
                if (length <= 0 || length > maxBytes)
                    return false;

                if (!TryPreflight(preflightStream, maxDepth, maxNodes))
                    return false;
            }

            using var parseStream = userData.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = CreateUtf8Reader(parseStream);
            var documents = DataNodeParser.ParseYamlStream(reader).ToArray();
            if (documents.Length != 1 || documents[0].Root is not MappingDataNode root)
                return false;

            return IsMapRoot(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or YamlException or DataParseException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryPreflight(Stream stream, int maxDepth, int maxNodes)
    {
        if (maxDepth <= 0 || maxNodes <= 0)
            return false;

        using var reader = CreateUtf8Reader(stream);
        var parser = new Parser(reader);
        var documents = 0;
        var depth = 0;
        var nodes = 0;

        while (parser.MoveNext())
        {
            switch (parser.Current)
            {
                case DocumentStart:
                    if (++documents > 1)
                        return false;
                    break;

                case MappingStart:
                case SequenceStart:
                    if (++nodes > maxNodes || ++depth > maxDepth)
                        return false;
                    break;

                case MappingEnd:
                case SequenceEnd:
                    if (--depth < 0)
                        return false;
                    break;

                case Scalar scalar:
                    if (++nodes > maxNodes || scalar.Value.Length > MaxScalarLength)
                        return false;
                    break;

                // SS14 map files do not require aliases. Refusing them removes an alias-expansion DoS class.
                case AnchorAlias:
                    return false;
            }
        }

        return documents == 1 && depth == 0;
    }

    private static bool IsMapRoot(MappingDataNode root)
    {
        if (!root.TryGet<MappingDataNode>("meta", out var meta)
            || !meta.TryGet<ValueDataNode>("format", out var formatNode)
            || !int.TryParse(formatNode.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var format)
            || format < EntityDeserializer.OldestSupportedVersion
            || format > EntityDeserializer.NewestSupportedVersion
            || !root.TryGet<MappingDataNode>("tilemap", out _)
            || !root.TryGet<SequenceDataNode>("entities", out var entities))
        {
            return false;
        }

        // Map format 7 is the current EntitySerializer layout. The mapping command accepts both
        // complete maps and standalone grids, so both are valid mapper files. Entity/save files
        // remain rejected without instantiating anything.
        if (format >= 7)
        {
            return meta.TryGet<ValueDataNode>("category", out var category)
                && IsSupportedMapperCategory(category.Value)
                && root.TryGet<SequenceDataNode>("maps", out _)
                && root.TryGet<SequenceDataNode>("grids", out _)
                && root.TryGet<SequenceDataNode>("orphans", out _)
                && root.TryGet<SequenceDataNode>("nullspace", out _);
        }

        // Legacy file categories are inferred from components. Mapping supports complete maps and grids.
        return HasLegacyMapOrGridComponent(entities);
    }

    private static bool IsSupportedMapperCategory(string category)
    {
        return string.Equals(category, "Map", StringComparison.Ordinal)
            || string.Equals(category, "Grid", StringComparison.Ordinal);
    }

    private static bool HasLegacyMapOrGridComponent(SequenceDataNode entityGroups)
    {
        foreach (var groupNode in entityGroups)
        {
            if (groupNode is not MappingDataNode group)
                continue;

            if (!group.TryGet<SequenceDataNode>("entities", out var entities))
                continue;

            foreach (var entityNode in entities)
            {
                if (entityNode is not MappingDataNode entity)
                    continue;

                if (!entity.TryGet<SequenceDataNode>("components", out var components))
                    continue;

                foreach (var componentNode in components)
                {
                    if (componentNode is not MappingDataNode component)
                        continue;

                    if (component.TryGet<ValueDataNode>("type", out var type)
                        && (string.Equals(type.Value, "Map", StringComparison.Ordinal)
                            || string.Equals(type.Value, "MapGrid", StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static StreamReader CreateUtf8Reader(Stream stream)
    {
        return new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 16 * 1024,
            leaveOpen: false);
    }
}
