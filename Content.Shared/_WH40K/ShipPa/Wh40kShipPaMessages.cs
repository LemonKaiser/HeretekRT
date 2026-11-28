using Robust.Shared.Serialization;

namespace Content.Shared._WH40K.ShipPa;

[Serializable, NetSerializable]
public sealed class Wh40kShipAlertCodeRequestMessage : BoundUserInterfaceMessage
{
    public string Code;

    public Wh40kShipAlertCodeRequestMessage(string code)
    {
        Code = code;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kShipGeneralQuartersRequestMessage : BoundUserInterfaceMessage
{
    public bool Active;

    public Wh40kShipGeneralQuartersRequestMessage(bool active)
    {
        Active = active;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kShipDistressRequestMessage : BoundUserInterfaceMessage
{
    public bool Active;

    public Wh40kShipDistressRequestMessage(bool active)
    {
        Active = active;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kShipPaVolumeRequestMessage : BoundUserInterfaceMessage
{
    public float Volume;

    public Wh40kShipPaVolumeRequestMessage(float volume)
    {
        Volume = volume;
    }
}

[Serializable, NetSerializable]
public sealed class Wh40kShipPaAnnounceRequestMessage : BoundUserInterfaceMessage
{
    public string Text;

    public Wh40kShipPaAnnounceRequestMessage(string text)
    {
        Text = text;
    }
}
