using Content.Shared._WH40K.DirectionalEmote;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._WH40K.DirectionalEmote.UserInterface;

[UsedImplicitly]
public sealed class WH40KDirectionalEmoteUIController : UIController
{
    [UISystemDependency] private readonly WH40KDirectionalEmoteSystem _directionalEmoteSystem = default!;
    [Dependency] private readonly EntityManager _entityManager = default!;

    private WH40KDirectionalEmoteWindow _emoteWindow = default!;

    public void OpenWindow(NetEntity source, NetEntity target)
    {
        if (!_entityManager.TryGetEntity(source, out var sourceEntity) ||
            !_entityManager.TryGetComponent<WH40KDirectionalEmoteComponent>(sourceEntity, out var emoteComp))
        {
            return;
        }

        EnsureWindow();

        _emoteWindow.Source = source;
        _emoteWindow.Target = target;
        _emoteWindow.SetText(string.Empty);

        _entityManager.TryGetComponent<MetaDataComponent>(_entityManager.GetEntity(target), out var targetMeta);
        var targetName = targetMeta?.EntityName ?? Loc.GetString("wh40k-directional-emote-unknown-target");
        _emoteWindow.Title = Loc.GetString("wh40k-directional-emote-title", ("target", targetName));
        _emoteWindow.UpdateHideNameVisibility(emoteComp.CanHideName);

        _emoteWindow.AcceptPressed = () =>
        {
            _directionalEmoteSystem.TrySendEmote(
                _emoteWindow.Source,
                _emoteWindow.Target,
                _emoteWindow.Text,
                _emoteWindow.HideName);
            _emoteWindow.Close();
            _emoteWindow.SetText(string.Empty);
        };
        _emoteWindow.LastEmotePressed = () => _emoteWindow.SetText(emoteComp.LastEmote);

        _emoteWindow.OpenCentered();
        _emoteWindow.MoveToFront();
    }

    private void EnsureWindow()
    {
        if (_emoteWindow is { Disposed: false })
            return;

        _emoteWindow = UIManager.CreateWindow<WH40KDirectionalEmoteWindow>();
    }
}
