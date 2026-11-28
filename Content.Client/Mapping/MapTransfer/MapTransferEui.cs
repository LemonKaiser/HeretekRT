using System;
using System.IO;
using Content.Client.Eui;
using Content.Shared.Eui;
using Content.Shared.Mapping;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Mapping.MapTransfer;

[UsedImplicitly]
public sealed partial class MapTransferEui : BaseEui
{
    [Dependency] private IFileDialogManager _files = default!;
    [Dependency] private MapTransferClientManager _transfers = default!;

    private readonly MapTransferWindow _window;
    private MapTransferEuiState? _state;
    private bool _isOpen;

    public MapTransferEui()
    {
        IoCManager.InjectDependencies(this);
        _window = new MapTransferWindow();
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
        _window.RefreshRequested += () => SendMessage(new MapTransferEuiMsg.Refresh());
        _window.GoBackRequested += () => SendMessage(new MapTransferEuiMsg.GoBack());
        _window.FolderOpenRequested += id => SendMessage(new MapTransferEuiMsg.OpenFolder(id));
        _window.UploadRequested += StartUpload;
        _window.DownloadRequested += StartDownload;
        _window.RenameRequested += StartRename;
        _window.OpenDownloadFolderRequested += _transfers.OpenDownloadDirectory;
    }

    public override void Opened()
    {
        base.Opened();
        _isOpen = true;
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _isOpen = false;
        _transfers.CancelPending();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not MapTransferEuiState transferState)
            return;

        _state = transferState;
        _window.UpdateState(transferState);
        if (transferState.Status is MapTransferUiStatus.Disabled
            or MapTransferUiStatus.AccessDenied
            or MapTransferUiStatus.InvalidMap
            or MapTransferUiStatus.FileTooLarge
            or MapTransferUiStatus.QuotaExceeded
            or MapTransferUiStatus.Busy
            or MapTransferUiStatus.TimedOut
            or MapTransferUiStatus.InvalidName
            or MapTransferUiStatus.NameTaken
            or MapTransferUiStatus.Failed)
        {
            _transfers.CancelPending();
        }
    }

    private async void StartUpload()
    {
        if (!_isOpen || _state is not { Enabled: true, CanUpload: true })
            return;

        Stream? stream;
        try
        {
            stream = await _files.OpenFile(
                new FileDialogFilters(new FileDialogFilters.Group("yml")),
                FileAccess.Read,
                FileShare.Read);
        }
        catch
        {
            return;
        }

        if (stream == null)
            return;

        // The native picker can outlive this EUI. Do not retain a file handle or send a message for a panel
        // that was closed while the player was choosing a file.
        if (!_isOpen || _state is not { Enabled: true, CanUpload: true } state)
        {
            stream.Dispose();
            return;
        }

        if (!_transfers.TryPrepareUpload(stream, state.MaxFileBytes, out var size))
            return;

        SendMessage(new MapTransferEuiMsg.BeginUpload(size));
    }

    private void StartDownload(Guid mapId)
    {
        if (!_isOpen || _state is not { Enabled: true, CanDownload: true } state)
            return;

        MapTransferMapEntry? map = null;
        foreach (var entry in state.Maps)
        {
            if (entry.Id == mapId)
            {
                map = entry;
                break;
            }
        }

        if (map == null)
            return;

        if (!_transfers.TryPrepareDownload(
                map.Name,
                state.MaxFileBytes,
                _window.OpenDownloadFolderAfterDownload))
            return;

        SendMessage(new MapTransferEuiMsg.BeginDownload(mapId));
    }

    private void StartRename(Guid mapId, string name)
    {
        if (!_isOpen || _state is not { Enabled: true })
            return;

        SendMessage(new MapTransferEuiMsg.BeginRename(mapId, name));
    }
}
