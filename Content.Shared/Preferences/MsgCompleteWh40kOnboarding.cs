using System.IO;
using Content.Shared._WH40K.CharacterCreation;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences;

/// <summary>
/// Sent once by the introductory character creator. The server selects the profile slot itself.
/// </summary>
public sealed class MsgCompleteWh40kOnboarding : NetMessage
{
    public const int MaxSerializedProfileBytes = 128 * 1024;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    public HumanoidCharacterProfile Profile = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var length = buffer.ReadVariableInt32();
        if (length is < 0 or > MaxSerializedProfileBytes)
            throw new InvalidDataException($"WH40K onboarding profile payload is {length} bytes, which exceeds the {MaxSerializedProfileBytes}-byte limit.");

        using var stream = new MemoryStream(length);
        buffer.ReadAlignedMemory(stream, length);
        serializer.DeserializeDirect(stream, out Profile);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        using var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Profile);
        if (stream.Length > MaxSerializedProfileBytes)
            throw new InvalidDataException($"WH40K onboarding profile payload exceeds the {MaxSerializedProfileBytes}-byte limit.");

        buffer.WriteVariableInt32((int) stream.Length);
        stream.TryGetBuffer(out var segment);
        buffer.Write(segment);
    }
}

/// <summary>
/// The only confirmation which lets the client leave the introductory creator.
/// </summary>
public sealed class MsgWh40kOnboardingCompleted : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public Wh40kOnboardingCompletionStatus Status;
    public Wh40kPlayerProgressSnapshot Progress = Wh40kPlayerProgressSnapshot.Unknown;
    public int ProfileSlot = -1;
    public HumanoidCharacterProfile? Profile;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Status = (Wh40kOnboardingCompletionStatus) buffer.ReadByte();
        Progress = new Wh40kPlayerProgressSnapshot(
            (Wh40kActStage) buffer.ReadByte(),
            (Wh40kOnboardingStatus) buffer.ReadByte(),
            buffer.ReadInt32());
        ProfileSlot = buffer.ReadInt32();

        if (!buffer.ReadBoolean())
            return;

        buffer.ReadPadBits();
        var length = buffer.ReadVariableInt32();
        if (length is < 0 or > MsgCompleteWh40kOnboarding.MaxSerializedProfileBytes)
            throw new InvalidDataException($"WH40K onboarding confirmation profile payload is {length} bytes, which exceeds the limit.");

        using var stream = new MemoryStream(length);
        buffer.ReadAlignedMemory(stream, length);
        serializer.DeserializeDirect(stream, out Profile);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Status);
        buffer.Write((byte) Progress.ActStage);
        buffer.Write((byte) Progress.OnboardingStatus);
        buffer.Write(Progress.OnboardingProfileSlot);
        buffer.Write(ProfileSlot);
        buffer.Write(Profile != null);

        if (Profile == null)
            return;

        buffer.WritePadBits();
        using var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Profile);
        if (stream.Length > MsgCompleteWh40kOnboarding.MaxSerializedProfileBytes)
            throw new InvalidDataException("WH40K onboarding confirmation profile payload exceeds the limit.");

        buffer.WriteVariableInt32((int) stream.Length);
        stream.TryGetBuffer(out var segment);
        buffer.Write(segment);
    }
}
