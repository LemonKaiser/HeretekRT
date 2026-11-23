using Content.Shared.CCVar;
using Content.Shared.Flash;
using Content.Shared.Flash.Components;
using Content.Shared.StatusEffect;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Flash
{
    public sealed partial class FlashOverlay : Overlay
    {
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        [Dependency] private IEntityManager _entityManager = default!;
        [Dependency] private IPlayerManager _playerManager = default!;
        [Dependency] private IGameTiming _timing = default!;
        [Dependency] private IConfigurationManager _cfg = default!;

        private readonly SharedFlashSystem _flash;
        private readonly StatusEffectsSystem _statusSys;

        private static readonly ProtoId<ShaderPrototype> StillFlashShaderId = "WH40KFlashedEffectStill";

        public override OverlaySpace Space => OverlaySpace.WorldSpace;
        private readonly ShaderInstance _shader;
        private ShaderInstance? _stillShader;
        public float PercentComplete = 0.0f;
        public Texture? ScreenshotTexture;

        public FlashOverlay()
        {
            IoCManager.InjectDependencies(this);
            _shader = _prototypeManager.Index<ShaderPrototype>("FlashedEffect").InstanceUnique();
            _flash = _entityManager.System<SharedFlashSystem>();
            _statusSys = _entityManager.System<StatusEffectsSystem>();
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            var playerEntity = _playerManager.LocalEntity;

            if (playerEntity == null)
                return;

            if (!_entityManager.HasComponent<FlashedComponent>(playerEntity)
                || !_entityManager.TryGetComponent<StatusEffectsComponent>(playerEntity, out var status))
                return;

            if (!_statusSys.TryGetTime(playerEntity.Value, _flash.FlashedKey, out var time, status))
                return;

            var curTime = _timing.CurTime;
            var lastsFor = (float) (time.Value.Item2 - time.Value.Item1).TotalSeconds;
            var timeDone = (float) (curTime - time.Value.Item1).TotalSeconds;

            PercentComplete = lastsFor > 0f ? Math.Clamp(timeDone / lastsFor, 0f, 1f) : 1f;
        }

        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
                return false;
            if (args.Viewport.Eye != eyeComp.Eye)
                return false;

            return PercentComplete < 1.0f;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (RequestScreenTexture && ScreenTexture != null)
            {
                ScreenshotTexture = ScreenTexture;
                RequestScreenTexture = false; // we only need the first frame, so we can stop the request now for performance reasons
            }
            if (ScreenshotTexture == null)
                return;

            var worldHandle = args.WorldHandle;
            var shader = _cfg.GetCVar(CCVars.AccessibilityDarkFlash)
                ? (_stillShader ??= _prototypeManager.Index(StillFlashShaderId).InstanceUnique())
                : _shader;
            shader.SetParameter("percentComplete", PercentComplete);
            worldHandle.UseShader(shader);
            worldHandle.DrawTextureRectRegion(ScreenshotTexture, args.WorldBounds);
            worldHandle.UseShader(null);
        }

        protected override void DisposeBehavior()
        {
            base.DisposeBehavior();
            _shader.Dispose();
            _stillShader?.Dispose();
            ScreenshotTexture = null;
        }
    }
}
