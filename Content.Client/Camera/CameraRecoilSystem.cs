using System.Numerics;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Camera;

public sealed partial class CameraRecoilSystem : SharedCameraRecoilSystem
{
    [Dependency] private IConfigurationManager _configManager = default!;

    private float _intensity;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<CameraKickEvent>(OnCameraKick);

        Subs.CVar(_configManager, CCVars.ScreenShakeIntensity, OnCvarChanged, true);
    }

    private void OnCvarChanged(float value)
    {
        _intensity = value;
    }

    private void OnCameraKick(CameraKickEvent ev)
    {
        KickCamera(GetEntity(ev.NetEntity), ev.Recoil);
    }

    public override void KickCamera(EntityUid uid, Vector2 recoil, CameraRecoilComponent? component = null)
    {
        if (_intensity == 0)
            return;

        if (!Resolve(uid, ref component, false))
            return;

        recoil *= _intensity;

        Wh40kKickCamera(uid, component, recoil);
    }
}
