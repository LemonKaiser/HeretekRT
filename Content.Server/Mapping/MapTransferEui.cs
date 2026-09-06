using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Eui;
using Content.Shared.Mapping;
using Robust.Shared.Asynchronous;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Mapping;

public sealed partial class MapTransferEui : BaseEui
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private MapTransferManager _transfers = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<Guid, ResPath> _folderIds = [];
    private readonly Dictionary<Guid, ResPath> _mapIds = [];
    private ResPath _currentDirectory = ResPath.Root;
    private ResPath _rootDirectory = ResPath.Root;
    private MapTransferUiStatus _status = MapTransferUiStatus.Ready;
    private TimeSpan _nextNavigationAt;
    private ResPath _pendingMapDirectory = ResPath.Root;
    private int _mapListingGeneration;
    private bool _mapListingInProgress;
    private bool _initialized;

    public MapTransferEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        _admins.OnPermsChanged += OnPermsChanged;

        if (!_transfers.TryGetRootDirectory(out _rootDirectory))
        {
            _status = MapTransferUiStatus.Disabled;
            StateDirty();
            return;
        }

        _currentDirectory = _rootDirectory;
        _initialized = true;
        RefreshDirectory();
    }

    public override void Closed()
    {
        base.Closed();
        _admins.OnPermsChanged -= OnPermsChanged;
        _transfers.CancelForUi(this, MapTransferUiStatus.Failed);
    }

    public override EuiStateBase GetNewState()
    {
        var enabled = _initialized && _transfers.CanUse(Player);
        if (!enabled && _status == MapTransferUiStatus.Ready)
            _status = _transfers.IsFeatureEnabled ? MapTransferUiStatus.AccessDenied : MapTransferUiStatus.Disabled;

        var folders = new List<MapTransferFolderEntry>(_folderIds.Count);
        foreach (var (id, path) in _folderIds)
        {
            folders.Add(new MapTransferFolderEntry(id, path.Filename));
        }

        var maps = new List<MapTransferMapEntry>(_mapIds.Count);
        foreach (var (id, path) in _mapIds)
        {
            if (_transfers.TryGetMapSize(path, out var size))
                maps.Add(new MapTransferMapEntry(id, path.Filename, size));
        }

        return new MapTransferEuiState(
            enabled,
            enabled && _transfers.UploadsAllowed,
            enabled && _transfers.DownloadsAllowed,
            enabled && _currentDirectory != _rootDirectory,
            _currentDirectory == _rootDirectory ? Loc.GetString("map-transfer-root") : _currentDirectory.Filename,
            _transfers.MaxFileBytes,
            _status,
            folders.ToArray(),
            maps.ToArray());
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_transfers.CanUse(Player))
        {
            _status = _transfers.IsFeatureEnabled ? MapTransferUiStatus.AccessDenied : MapTransferUiStatus.Disabled;
            StateDirty();
            _transfers.CancelForUi(this, _status);
            return;
        }

        switch (msg)
        {
            case MapTransferEuiMsg.Refresh when TryBeginNavigation():
                RefreshDirectory();
                break;

            case MapTransferEuiMsg.OpenFolder open
                when TryBeginNavigation() && _folderIds.TryGetValue(open.FolderId, out var folder):
                _currentDirectory = folder;
                RefreshDirectory();
                break;

            case MapTransferEuiMsg.GoBack when TryBeginNavigation() && _currentDirectory != _rootDirectory:
                _currentDirectory = _currentDirectory.Directory;
                RefreshDirectory();
                break;

            case MapTransferEuiMsg.BeginUpload upload:
                _status = _transfers.TryCreateUpload(Player, _currentDirectory, upload.DeclaredSize, this);
                StateDirty();
                break;

            case MapTransferEuiMsg.BeginDownload download when _mapIds.TryGetValue(download.MapId, out var map):
                _status = _transfers.TryCreateDownload(Player, map, this);
                StateDirty();
                break;

            case MapTransferEuiMsg.BeginRename rename when _mapIds.TryGetValue(rename.MapId, out var map):
                _status = _transfers.TryRenameMap(Player, map, rename.Name);
                if (_status == MapTransferUiStatus.Completed)
                    PopulateDirectory();
                StateDirty();
                break;
        }
    }

    internal void SetOperationStatus(MapTransferUiStatus status, bool refreshDirectory)
    {
        if (IsShutDown)
            return;

        _status = status;
        if (refreshDirectory && _initialized)
            PopulateDirectory();
        StateDirty();
    }

    private void RefreshDirectory()
    {
        _status = MapTransferUiStatus.Ready;
        PopulateDirectory();
        StateDirty();
    }

    private bool TryBeginNavigation()
    {
        if (_timing.RealTime < _nextNavigationAt)
            return false;

        _nextNavigationAt = _timing.RealTime + TimeSpan.FromMilliseconds(250);
        return true;
    }

    private void PopulateDirectory()
    {
        _folderIds.Clear();
        _mapIds.Clear();

        foreach (var folder in _transfers.ListFolders(_currentDirectory))
        {
            _folderIds.Add(Guid.NewGuid(), folder);
        }

        QueueMapListing();
    }

    private void QueueMapListing()
    {
        _pendingMapDirectory = _currentDirectory;
        _mapListingGeneration++;
        if (_mapListingInProgress)
            return;

        StartMapListing(_pendingMapDirectory, _mapListingGeneration);
    }

    private void StartMapListing(ResPath directory, int generation)
    {
        _mapListingInProgress = true;
        _ = PopulateMapsAsync(directory, generation);
    }

    private async Task PopulateMapsAsync(ResPath directory, int generation)
    {
        ResPath[] maps;
        try
        {
            // Parsing 100 maps on the EUI/main thread produces visible server hitches.
            // File validation already runs safely off-thread during upload, so do the read-only listing there too.
            maps = await Task.Run(() => _transfers.ListValidatedMaps(directory).ToArray()).ConfigureAwait(false);
        }
        catch (Exception)
        {
            maps = [];
        }

        _tasks.RunOnMainThread(() =>
        {
            if (IsShutDown)
                return;

            _mapListingInProgress = false;
            if (generation == _mapListingGeneration && directory == _currentDirectory)
            {
                _mapIds.Clear();
                foreach (var map in maps)
                {
                    _mapIds.Add(Guid.NewGuid(), map);
                }

                StateDirty();
            }

            if (generation != _mapListingGeneration)
                StartMapListing(_pendingMapDirectory, _mapListingGeneration);
        });
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player != Player)
            return;

        if (_transfers.CanUse(Player))
        {
            StateDirty();
            return;
        }

        _status = _transfers.IsFeatureEnabled ? MapTransferUiStatus.AccessDenied : MapTransferUiStatus.Disabled;
        _transfers.CancelForUi(this, _status);
        Close();
    }
}
