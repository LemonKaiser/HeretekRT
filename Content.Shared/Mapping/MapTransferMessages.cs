using System;
using System.IO;
using Content.Shared.Eui;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Mapping;

public enum MapTransferUiStatus : byte
{
    Ready,
    Disabled,
    AccessDenied,
    Uploading,
    Downloading,
    Completed,
    InvalidMap,
    FileTooLarge,
    QuotaExceeded,
    Busy,
    TimedOut,
    InvalidName,
    NameTaken,
    Failed,
}

[Serializable, NetSerializable]
public sealed class MapTransferFolderEntry(Guid id, string name)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
}

[Serializable, NetSerializable]
public sealed class MapTransferMapEntry(Guid id, string name, long size)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public long Size { get; } = size;
}

[Serializable, NetSerializable]
public sealed class MapTransferEuiState(
    bool enabled,
    bool canUpload,
    bool canDownload,
    bool canGoBack,
    string currentFolder,
    long maxFileBytes,
    MapTransferUiStatus status,
    MapTransferFolderEntry[] folders,
    MapTransferMapEntry[] maps) : EuiStateBase
{
    public bool Enabled { get; } = enabled;
    public bool CanUpload { get; } = canUpload;
    public bool CanDownload { get; } = canDownload;
    public bool CanGoBack { get; } = canGoBack;
    public string CurrentFolder { get; } = currentFolder;
    public long MaxFileBytes { get; } = maxFileBytes;
    public MapTransferUiStatus Status { get; } = status;
    public MapTransferFolderEntry[] Folders { get; } = folders;
    public MapTransferMapEntry[] Maps { get; } = maps;
}

public static class MapTransferEuiMsg
{
    [Serializable, NetSerializable]
    public sealed class Refresh : EuiMessageBase;

    [Serializable, NetSerializable]
    public sealed class OpenFolder(Guid folderId) : EuiMessageBase
    {
        public Guid FolderId { get; } = folderId;
    }

    [Serializable, NetSerializable]
    public sealed class GoBack : EuiMessageBase;

    [Serializable, NetSerializable]
    public sealed class BeginUpload(long declaredSize) : EuiMessageBase
    {
        public long DeclaredSize { get; } = declaredSize;
    }

    [Serializable, NetSerializable]
    public sealed class BeginDownload(Guid mapId) : EuiMessageBase
    {
        public Guid MapId { get; } = mapId;
    }

    [Serializable, NetSerializable]
    public sealed class BeginRename(Guid mapId, string name) : EuiMessageBase
    {
        public Guid MapId { get; } = mapId;
        public string Name { get; } = name;
    }
}

/// <summary>
///     One-shot server grant. It deliberately is not an EUI state so a refresh cannot replay the token.
/// </summary>
public sealed class MsgMapTransferGrant : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Guid OperationId;
    public MapTransferDirection Direction;
    public byte[] Token = Array.Empty<byte>();
    public long DeclaredSize;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        OperationId = buffer.ReadGuid();
        Direction = (MapTransferDirection) buffer.ReadByte();
        if (!MapTransferProtocol.IsDirectionValid(Direction))
            throw new InvalidDataException("Invalid mapper map-transfer direction.");

        Token = new byte[MapTransferProtocol.TokenLength];
        buffer.ReadBytes(Token, 0, Token.Length);
        DeclaredSize = buffer.ReadInt64();
        if (DeclaredSize < 0)
            throw new InvalidDataException("Invalid mapper map-transfer size.");
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        if (!MapTransferProtocol.IsDirectionValid(Direction) || Token.Length != MapTransferProtocol.TokenLength || DeclaredSize < 0)
            throw new InvalidDataException("Invalid mapper map-transfer grant.");

        buffer.Write(OperationId);
        buffer.Write((byte) Direction);
        buffer.Write(Token);
        buffer.Write(DeclaredSize);
    }
}

/// <summary>
///     Sent after the client has opened its local save stream and is ready to receive a download.
/// </summary>
public sealed class MsgMapTransferReady : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Guid OperationId;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        OperationId = buffer.ReadGuid();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(OperationId);
    }
}

/// <summary>
///     Sent after the client has either safely written the entire downloaded map or discarded it.
///     The server does not audit a download as complete until this acknowledgement arrives.
/// </summary>
public sealed class MsgMapTransferDownloadResult : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Guid OperationId;
    public bool Completed;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        OperationId = buffer.ReadGuid();
        Completed = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(OperationId);
        buffer.Write(Completed);
    }
}
