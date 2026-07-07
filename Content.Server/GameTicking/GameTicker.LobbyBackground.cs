using Content.Shared.GameTicking.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [ViewVariables]
    public ProtoId<LobbyBackgroundPrototype>? LobbyBackground { get; private set; }

    [ViewVariables]
    private List<ProtoId<LobbyBackgroundPrototype>>? _lobbyBackgrounds;

    private static readonly string[] WhitelistedBackgroundExtensions = new[] {"png", "jpg", "jpeg", "webp"};

    private void InitializeLobbyBackground()
    {
        _lobbyBackgrounds = new List<ProtoId<LobbyBackgroundPrototype>>();

        foreach (var proto in _prototypeManager.EnumeratePrototypes<LobbyBackgroundPrototype>())
        {
            var hasValidStaticBackground = proto.Background is { } staticPath &&
                                           WhitelistedBackgroundExtensions.Contains(staticPath.Extension, StringComparer.OrdinalIgnoreCase);

            var hasValidGifBackground = proto.BackgroundGif is { } gifPath &&
                                        string.Equals(gifPath.Extension, "gif", StringComparison.OrdinalIgnoreCase);

            if (!hasValidStaticBackground && !hasValidGifBackground)
                continue;

            _lobbyBackgrounds.Add(new ProtoId<LobbyBackgroundPrototype>(proto.ID));
        }

        RandomizeLobbyBackground();
    }

    private void RandomizeLobbyBackground()
    {
        LobbyBackground = _lobbyBackgrounds!.Any()
            ? _robustRandom.Pick(_lobbyBackgrounds!)
            : (ProtoId<LobbyBackgroundPrototype>?) null;
    }
}
