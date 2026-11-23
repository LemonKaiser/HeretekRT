using Content.Shared._White.Standing;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server.Standing;

public sealed partial class LayingDownSystem : SharedLayingDownSystem
{
    [Dependency] private INetConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CheckAutoGetUpEvent>(OnCheckAutoGetUp);
    }

    private void OnCheckAutoGetUp(CheckAutoGetUpEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid || GetNetEntity(uid) != ev.User)
            return;

        if (!TryComp(uid, out LayingDownComponent? layingDown))
            return;

        var autoGetUp = _cfg.GetClientCVar(args.SenderSession.Channel, CCVars.AutoGetUp);
        if (layingDown.AutoGetUp == autoGetUp)
            return;

        layingDown.AutoGetUp = autoGetUp;
        Dirty(uid, layingDown);
    }
}
