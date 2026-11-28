using Content.Shared._WH40K.Construction;

namespace Content.Client._WH40K.Construction;

/// <summary>
/// Sends repeat-craft requests from the construction menu to the server.
/// </summary>
public sealed partial class Wh40kCraftRepeatSystem : EntitySystem
{
    public void Request(string prototype, int count)
    {
        RaiseNetworkEvent(new Wh40kCraftRepeatRequestEvent(prototype, count));
    }
}
