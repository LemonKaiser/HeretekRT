using Content.Client.Decals.UI;
using Content.Client.Gameplay;
using Content.Client.Administration.Managers;
using Content.Client.Sandbox;
using Content.Shared.Decals;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.DecalPlacer;

public sealed partial class DecalPlacerUIController : UIController, IOnStateExited<GameplayState>, IOnSystemChanged<SandboxSystem>
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IClientAdminManager _admin = default!;
    [UISystemDependency] private readonly SandboxSystem _sandbox = default!;

    private DecalPlacerWindow? _window;

    public void ToggleWindow()
    {
        if (_window is { IsOpen: true })
        {
            _window.Close();
        }
        else if (_sandbox.SandboxAllowed || _admin.CanAdminPlace())
        {
            var firstOpen = _window == null || _window.Disposed;
            EnsureWindow();
            if (firstOpen)
                _window!.OpenCenteredAt(new System.Numerics.Vector2(.5f, .62f));
            else
                _window!.Open();
        }
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window == null)
            return;
        _window.Close();
        _window.Dispose();
        _window = null;
    }

    public void OnSystemLoaded(SandboxSystem system)
    {
        _sandbox.SandboxDisabled += CloseWindow;
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public void OnSystemUnloaded(SandboxSystem system)
    {
        _sandbox.SandboxDisabled -= CloseWindow;
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (obj.WasModified<DecalPrototype>())
            ReloadPrototypes();
    }

    private void ReloadPrototypes()
    {
        if (_window == null || _window.Disposed)
            return;

        var prototypes = _prototypes.EnumeratePrototypes<DecalPrototype>();
        _window.Populate(prototypes);
    }

    private void EnsureWindow()
    {
        if (_window is { Disposed: false })
            return;
        _window = UIManager.CreateWindow<DecalPlacerWindow>();
        ReloadPrototypes();
    }

    public void CloseWindow()
    {
        if (_window == null || _window.Disposed)
            return;

        _window.Close();
    }

    internal void PausePlacement() => _window?.PausePlacement();
}
