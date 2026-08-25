// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace OmniTray.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly DropCommandCatalogViewModel _commands;
    private readonly MainViewModel _main;
    private string _commandSurfaceId = DropCommandSurfaceIds.Popup;
    private bool _isDisposed;

    internal SettingsViewModel(MainViewModel main, DropCommandCatalogViewModel commands)
    {
        this._main = main ?? throw new ArgumentNullException(nameof(main));
        this._commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this._main.PropertyChanged += this.OnMainPropertyChanged;
        this._commands.CatalogChanged += this.OnCommandCatalogChanged;
        this.RefreshCommandPlacements();
    }

    public ObservableCollection<DropCommandViewModel> CommandDefinitions => this._commands.Commands;

    public ObservableCollection<DropCommandPlacementViewModel> CommandPlacementItems { get; } = [];

    public bool HasCommands => this.CommandDefinitions.Count > 0;

    public bool IsCommandLayoutEmpty => this.CommandPlacementItems.Count == 0;

    public string CommandSurfaceId
    {
        get => this._commandSurfaceId;
        private set
        {
            if (!this.SetProperty(ref this._commandSurfaceId, value))
            {
                return;
            }

            this.RefreshCommandPlacements();
        }
    }

    public bool EdgeWindowsPaused
    {
        get => this._main.EdgeWindowsPaused;
        set => this._main.EdgeWindowsPaused = value;
    }

    public bool GameModeEnabled
    {
        get => this._main.GameModeEnabled;
        set => this._main.GameModeEnabled = value;
    }

    public bool IsGameModeSuppressing => this._main.IsGameModeSuppressing;

    public string GameModeStatusText => this._main.GameModeStatusText;

    public bool LeftEdgeWindowEnabled
    {
        get => this._main.LeftEdgeWindowEnabled;
        set => this._main.LeftEdgeWindowEnabled = value;
    }

    public bool RightEdgeWindowEnabled
    {
        get => this._main.RightEdgeWindowEnabled;
        set => this._main.RightEdgeWindowEnabled = value;
    }

    public bool TopEdgeWindowEnabled
    {
        get => this._main.TopEdgeWindowEnabled;
        set => this._main.TopEdgeWindowEnabled = value;
    }

    public bool BottomEdgeWindowEnabled
    {
        get => this._main.BottomEdgeWindowEnabled;
        set => this._main.BottomEdgeWindowEnabled = value;
    }

    public bool SyncLeftAndRightEdgeContent
    {
        get => this._main.SyncLeftAndRightEdgeContent;
        set => this._main.SyncLeftAndRightEdgeContent = value;
    }

    public bool SyncTopAndBottomEdgeContent
    {
        get => this._main.SyncTopAndBottomEdgeContent;
        set => this._main.SyncTopAndBottomEdgeContent = value;
    }

    public bool SyncAllEdgeContent
    {
        get => this._main.SyncAllEdgeContent;
        set => this._main.SyncAllEdgeContent = value;
    }

    public bool CanConfigurePairedEdgeContentSync => this._main.CanConfigurePairedEdgeContentSync;

    internal void SetCommandSurface(string surfaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        if (!this._commands.GetSurfaceIds().Contains(surfaceId, StringComparer.Ordinal))
        {
            throw new ArgumentException("The command surface is not supported.", nameof(surfaceId));
        }

        this.CommandSurfaceId = surfaceId;
    }

    internal DropCommandInstance CreateCommand(string templateId) =>
        DropCommandTemplates.CreateInstance(templateId);

    internal void AddCommand(DropCommandInstance command, IReadOnlyList<string> surfaceIds)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(surfaceIds);
        this._commands.AddCommand(command);
        this._commands.SetRootPlacements(command.Id, surfaceIds);
    }

    internal void UpdateCommand(DropCommandInstance command, IReadOnlyList<string> surfaceIds)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(surfaceIds);
        this._commands.UpdateCommand(command);
        this._commands.SetRootPlacements(command.Id, surfaceIds);
    }

    internal bool RemoveCommand(Guid commandId) => this._commands.RemoveCommand(commandId);

    internal IReadOnlyList<DropCommandViewModel> GetCommandsNotOnCurrentSurface() =>
        this.CommandDefinitions
            .Where(command => !this._commands.HasPlacement(command.Id, this.CommandSurfaceId))
            .ToArray();

    internal bool AddCommandToCurrentSurface(Guid commandId) =>
        this._commands.AddPlacement(commandId, this.CommandSurfaceId, null);

    internal DropCommandFolderNode AddRootFolder(string name) =>
        this._commands.AddFolder(this.CommandSurfaceId, null, name);

    internal bool RenameFolder(Guid folderId, string name) =>
        this._commands.RenameFolder(this.CommandSurfaceId, folderId, name);

    internal bool MovePlacement(Guid nodeId, int direction) =>
        this._commands.MovePlacement(this.CommandSurfaceId, nodeId, direction);

    internal bool SetPlacementParent(Guid nodeId, Guid? parentId) =>
        this._commands.SetPlacementParent(this.CommandSurfaceId, nodeId, parentId);

    internal bool RemovePlacement(Guid nodeId) =>
        this._commands.RemovePlacement(this.CommandSurfaceId, nodeId);

    internal IReadOnlyList<DropCommandFolderOption> GetParentFolderOptions(
        DropCommandPlacementViewModel placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var allPlacements = this._commands.GetFlattened(this.CommandSurfaceId);
        var placementsById = allPlacements.ToDictionary(static item => item.NodeId);
        var options = new List<DropCommandFolderOption>
        {
            new(null, "Top level")
        };
        options.AddRange(allPlacements
            .Where(candidate => candidate.IsFolder &&
                                candidate.NodeId != placement.NodeId &&
                                !IsDescendant(candidate, placement.NodeId, placementsById))
            .Select(candidate => new DropCommandFolderOption(
                candidate.NodeId,
                $"{new string(' ', candidate.Depth * 2)}{candidate.DisplayName}")));
        return options;
    }

    internal IReadOnlyList<string> GetCommandSurfaceIds() => this._commands.GetSurfaceIds();

    internal bool HasPlacement(Guid commandId, string surfaceId) =>
        this._commands.HasPlacement(commandId, surfaceId);

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._main.PropertyChanged -= this.OnMainPropertyChanged;
        this._commands.CatalogChanged -= this.OnCommandCatalogChanged;
    }

    private static bool IsDescendant(
        DropCommandPlacementViewModel candidate,
        Guid ancestorId,
        IReadOnlyDictionary<Guid, DropCommandPlacementViewModel> placementsById)
    {
        var parentId = candidate.ParentId;
        while (parentId is { } id)
        {
            if (id == ancestorId)
            {
                return true;
            }

            parentId = placementsById.GetValueOrDefault(id)?.ParentId;
        }

        return false;
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        this.OnPropertyChanged(args.PropertyName);
        if (args.PropertyName == nameof(MainViewModel.SyncAllEdgeContent))
        {
            this.OnPropertyChanged(nameof(this.CanConfigurePairedEdgeContentSync));
        }
    }

    private void OnCommandCatalogChanged(object? sender, EventArgs args)
    {
        this.OnPropertyChanged(nameof(this.HasCommands));
        this.OnPropertyChanged(nameof(this.CommandDefinitions));
        this.RefreshCommandPlacements();
    }

    private void RefreshCommandPlacements()
    {
        this.CommandPlacementItems.Clear();
        foreach (var placement in this._commands.GetFlattened(this.CommandSurfaceId))
        {
            this.CommandPlacementItems.Add(placement);
        }

        this.OnPropertyChanged(nameof(this.IsCommandLayoutEmpty));
    }
}

internal sealed record DropCommandFolderOption(Guid? Id, string DisplayName);
