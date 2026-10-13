using Content.Client._WH40K.Audio;
using Content.Shared._WH40K.Bosses;
using Content.Shared.GameTicking;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Bosses;

public sealed partial class Wh40kBossSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private Wh40kMusicOverrideSystem _music = default!;
    public Wh40kBossHudData? Boss { get; private set; }
    private TimeSpan _lastState;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesOutsidePrediction = true;
        SubscribeNetworkEvent<Wh40kBossHudStateEvent>(OnState);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetach);
    }

    private void OnState(Wh40kBossHudStateEvent args)
    {
        Boss = args.Boss;
        _lastState = _timing.RealTime;
        if (Boss?.Theme is { } theme && !ResolvedSoundSpecifier.IsNullOrEmpty(theme))
            _music.Request(this, theme, Boss.ThemeAudio, 200, Boss.FadeIn, Boss.FadeOut);
        else
            _music.Release(this);
    }

    private void OnDetach(LocalPlayerDetachedEvent args) => Clear();
    private void OnRestart(RoundRestartCleanupEvent args) => Clear();

    public void Clear()
    {
        Boss = null;
        _music.Release(this, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (Boss != null && _timing.RealTime - _lastState > TimeSpan.FromSeconds(3))
            Clear();
    }

    public override void Shutdown()
    {
        Clear();
        base.Shutdown();
    }
}
