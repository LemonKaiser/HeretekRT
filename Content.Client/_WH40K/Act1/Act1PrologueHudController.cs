using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Screens;
using Content.Shared._WH40K.Act1;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WH40K.Act1;

/// <summary>
/// Keeps the entire HUD out of a prologue participant's view. Escape remains handled by the normal UI because this
/// controller neither captures input nor opens a modal control.
/// </summary>
public sealed partial class Act1PrologueHudController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [UISystemDependency] private readonly ContentAudioSystem _contentAudio = default!;

    private bool _wasActive;

    public void OnStateEntered(GameplayState state)
    {
        _wasActive = false;
    }

    public void OnStateExited(GameplayState state)
    {
        RestoreHud();
        _wasActive = false;
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var active = _players.LocalEntity is { } player &&
                     _entities.HasComponent<Act1PrologueComponent>(player);
        if (active)
        {
            HideHud();
            if (!_wasActive)
                _contentAudio.DisableAmbientMusic();
        }
        else if (_wasActive)
        {
            RestoreHud();
        }

        _wasActive = active;
    }

    private void HideHud()
    {
        if (UIManager.ActiveScreen is InGameScreen screen)
            screen.SetHudVisibleFully(false);
    }

    private void RestoreHud()
    {
        if (UIManager.ActiveScreen is InGameScreen screen)
            screen.SetHudVisibleFully(true);
    }
}
