// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace OmniTray.ViewModels;

internal sealed class DropCommandCatalogViewModel : ObservableObject
{
    public event EventHandler? CatalogChanged;

    private readonly Dictionary<string, DropCommandSurfaceLayout> _layouts =
        new(StringComparer.Ordinal);

    public ObservableCollection<DropCommandViewModel> Commands { get; } = [];

    public DropCommandCatalogViewModel()
    {
        this.EnsureDefaultLayouts();
    }

    public void Restore(DropCommandCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        this.Commands.Clear();
        foreach (var command in state.Commands)
        {
            this.Commands.Add(new DropCommandViewModel(command));
        }

        this._layouts.Clear();
        foreach (var layout in state.Layouts)
        {
            this._layouts[layout.SurfaceId] = PruneMissingCommandPlacements(layout, state.Commands);
        }

        this.EnsureDefaultLayouts();
        this.RaiseCatalogChanged();
    }

    public DropCommandViewModel AddCommand(DropCommandInstance command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (this.Commands.Any(candidate => candidate.Model.Id == command.Id))
        {
            throw new ArgumentException("Command IDs must be unique.", nameof(command));
        }

        var viewModel = new DropCommandViewModel(command);
        this.Commands.Add(viewModel);
        this.AddPlacement(command.Id, DropCommandSurfaceIds.Popup, null);
        return viewModel;
    }

    public void UpdateCommand(DropCommandInstance command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var existing = this.Commands.FirstOrDefault(candidate => candidate.Model.Id == command.Id) ??
                       throw new ArgumentException("The command does not exist.", nameof(command));
        existing.Update(command);
        this.RaiseCatalogChanged();
    }

    public bool RemoveCommand(Guid commandId)
    {
        var command = this.Commands.FirstOrDefault(candidate => candidate.Model.Id == commandId);
        if (command is null)
        {
            return false;
        }

        this.Commands.Remove(command);
        foreach (var surfaceId in this._layouts.Keys.ToArray())
        {
            var layout = this._layouts[surfaceId];
            this._layouts[surfaceId] = DropCommandSurfaceLayout.Restore(
                surfaceId,
                layout.Nodes
                    .Where(node => node is not DropCommandLeafNode leaf || leaf.CommandInstanceId != commandId)
                    .ToArray());
        }

        this.RaiseCatalogChanged();
        return true;
    }

    public DropCommandFolderNode AddFolder(string surfaceId, Guid? parentId, string name)
    {
        var layout = this.GetLayout(surfaceId);
        ValidateFolderParent(layout, parentId);
        var folder = DropCommandFolderNode.Create(parentId, GetNextOrder(layout, parentId), name);
        this.ReplaceLayout(surfaceId, [.. layout.Nodes, folder]);
        return folder;
    }

    public bool RenameFolder(string surfaceId, Guid folderId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var layout = this.GetLayout(surfaceId);
        if (layout.Nodes.FirstOrDefault(node => node.Id == folderId) is not DropCommandFolderNode folder)
        {
            return false;
        }

        var replacement = DropCommandFolderNode.Restore(
            folder.Id,
            folder.ParentId,
            folder.Order,
            name);
        this.ReplaceLayout(
            surfaceId,
            layout.Nodes.Select(node => node.Id == folderId ? replacement : node).ToArray());
        return true;
    }

    public bool AddPlacement(Guid commandId, string surfaceId, Guid? parentId)
    {
        if (this.Commands.All(command => command.Model.Id != commandId))
        {
            throw new ArgumentException("The command does not exist.", nameof(commandId));
        }

        var layout = this.GetLayout(surfaceId);
        if (layout.Nodes.OfType<DropCommandLeafNode>()
            .Any(leaf => leaf.CommandInstanceId == commandId))
        {
            return false;
        }

        ValidateFolderParent(layout, parentId);
        var leaf = DropCommandLeafNode.Create(parentId, GetNextOrder(layout, parentId), commandId);
        this.ReplaceLayout(surfaceId, [.. layout.Nodes, leaf]);
        return true;
    }

    public bool RemovePlacement(string surfaceId, Guid nodeId)
    {
        var layout = this.GetLayout(surfaceId);
        var node = layout.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node is null)
        {
            return false;
        }

        IReadOnlyList<DropCommandPlacementNode> nodes;
        if (node is DropCommandFolderNode folder)
        {
            nodes = layout.Nodes
                .Where(candidate => candidate.Id != folder.Id)
                .Select(candidate => Reparent(candidate, folder.Id, folder.ParentId))
                .ToArray();
        }
        else
        {
            nodes = layout.Nodes.Where(candidate => candidate.Id != nodeId).ToArray();
        }

        this.ReplaceLayout(surfaceId, NormalizeOrders(nodes));
        return true;
    }

    public bool MovePlacement(string surfaceId, Guid nodeId, int direction)
    {
        if (direction is not -1 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        var layout = this.GetLayout(surfaceId);
        var node = layout.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node is null)
        {
            return false;
        }

        var siblings = layout.Nodes
            .Where(candidate => candidate.ParentId == node.ParentId)
            .OrderBy(static candidate => candidate.Order)
            .ThenBy(static candidate => candidate.Id)
            .ToArray();
        var index = Array.IndexOf(siblings, node);
        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= siblings.Length)
        {
            return false;
        }

        var target = siblings[targetIndex];
        var nodes = layout.Nodes
            .Select(candidate => candidate.Id == node.Id
                ? WithOrder(candidate, target.Order)
                : candidate.Id == target.Id
                    ? WithOrder(candidate, node.Order)
                    : candidate)
            .ToArray();
        this.ReplaceLayout(surfaceId, nodes);
        return true;
    }

    public bool SetPlacementParent(string surfaceId, Guid nodeId, Guid? parentId)
    {
        var layout = this.GetLayout(surfaceId);
        var node = layout.Nodes.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node is null || node.Id == parentId)
        {
            return false;
        }

        ValidateFolderParent(layout, parentId);
        if (node is DropCommandFolderNode && IsDescendant(layout, parentId, node.Id))
        {
            return false;
        }

        var replacement = WithParent(node, parentId, GetNextOrder(layout, parentId));
        var nodes = layout.Nodes.Select(candidate => candidate.Id == nodeId ? replacement : candidate).ToArray();
        this.ReplaceLayout(surfaceId, NormalizeOrders(nodes));
        return true;
    }

    public bool HasPlacement(Guid commandId, string surfaceId) =>
        this.GetLayout(surfaceId).Nodes.OfType<DropCommandLeafNode>()
            .Any(leaf => leaf.CommandInstanceId == commandId);

    public void SetRootPlacements(Guid commandId, IEnumerable<string> surfaceIds)
    {
        var requested = surfaceIds.ToHashSet(StringComparer.Ordinal);
        foreach (var surfaceId in this.GetSurfaceIds())
        {
            var layout = this.GetLayout(surfaceId);
            var existing = layout.Nodes.OfType<DropCommandLeafNode>()
                .FirstOrDefault(leaf => leaf.CommandInstanceId == commandId);
            if (requested.Contains(surfaceId))
            {
                if (existing is null)
                {
                    this.AddPlacement(commandId, surfaceId, null);
                }
            }
            else if (existing is not null)
            {
                this.RemovePlacement(surfaceId, existing.Id);
            }
        }
    }

    public IReadOnlyList<DropCommandPlacementViewModel> GetChildren(string surfaceId, Guid? parentId) =>
        this.GetLayout(surfaceId).Nodes
            .Where(node => node.ParentId == parentId)
            .OrderBy(static node => node.Order)
            .ThenBy(static node => node.Id)
            .Select(node => this.CreatePlacementViewModel(surfaceId, node, 0))
            .Where(static node => node is not null)
            .Cast<DropCommandPlacementViewModel>()
            .ToArray();

    public IReadOnlyList<DropCommandPlacementViewModel> GetFlattened(string surfaceId)
    {
        var result = new List<DropCommandPlacementViewModel>();
        this.AddFlattenedChildren(surfaceId, null, 0, result);
        return result;
    }

    public IReadOnlyList<Guid> GetDescendantCommandIds(string surfaceId, Guid folderId)
    {
        var layout = this.GetLayout(surfaceId);
        var result = new List<Guid>();
        AddDescendantCommandIds(layout, folderId, result);
        return result;
    }

    public DropCommandViewModel? FindCommand(Guid commandId) =>
        this.Commands.FirstOrDefault(command => command.Model.Id == commandId);

    public DropCommandFolderNode? FindFolder(string surfaceId, Guid folderId) =>
        this.GetLayout(surfaceId).Nodes.OfType<DropCommandFolderNode>()
            .FirstOrDefault(folder => folder.Id == folderId);

    public DropCommandCatalogState CreateSnapshot(IReadOnlyList<DropCommandWindowState> openWindows) =>
        new(
            this.Commands.Select(static command => command.Model).ToArray(),
            this._layouts.Values.OrderBy(static layout => layout.SurfaceId, StringComparer.Ordinal).ToArray(),
            openWindows);

    public IReadOnlyList<string> GetSurfaceIds() =>
    [
        DropCommandSurfaceIds.Popup,
        DropCommandSurfaceIds.ForEdge(EdgeShelfSide.Left),
        DropCommandSurfaceIds.ForEdge(EdgeShelfSide.Right),
        DropCommandSurfaceIds.ForEdge(EdgeShelfSide.Top),
        DropCommandSurfaceIds.ForEdge(EdgeShelfSide.Bottom)
    ];

    internal void RefreshSystemColors()
    {
        foreach (var command in this.Commands)
        {
            command.RefreshSystemColors();
        }
    }

    private static DropCommandSurfaceLayout PruneMissingCommandPlacements(
        DropCommandSurfaceLayout layout,
        IReadOnlyList<DropCommandInstance> commands)
    {
        var knownCommands = commands.Select(static command => command.Id).ToHashSet();
        var nodes = layout.Nodes
            .Where(node => node is not DropCommandLeafNode leaf || knownCommands.Contains(leaf.CommandInstanceId))
            .ToArray();
        return DropCommandSurfaceLayout.Restore(layout.SurfaceId, nodes);
    }

    private static int GetNextOrder(DropCommandSurfaceLayout layout, Guid? parentId) =>
        layout.Nodes.Where(node => node.ParentId == parentId).Select(static node => node.Order).DefaultIfEmpty(-1)
            .Max() + 1;

    private static void ValidateFolderParent(DropCommandSurfaceLayout layout, Guid? parentId)
    {
        if (parentId is not null &&
            layout.Nodes.FirstOrDefault(node => node.Id == parentId) is not DropCommandFolderNode)
        {
            throw new ArgumentException("The parent must be a folder in the same surface.", nameof(parentId));
        }
    }

    private static bool IsDescendant(DropCommandSurfaceLayout layout, Guid? candidateId, Guid ancestorId)
    {
        while (candidateId is { } id)
        {
            if (id == ancestorId)
            {
                return true;
            }

            candidateId = layout.Nodes.FirstOrDefault(node => node.Id == id)?.ParentId;
        }

        return false;
    }

    private static DropCommandPlacementNode Reparent(
        DropCommandPlacementNode node,
        Guid oldParentId,
        Guid? newParentId) =>
        node.ParentId == oldParentId ? WithParent(node, newParentId, node.Order) : node;

    private static DropCommandPlacementNode WithParent(
        DropCommandPlacementNode node,
        Guid? parentId,
        int order) =>
        node switch
        {
            DropCommandFolderNode folder => DropCommandFolderNode.Restore(folder.Id, parentId, order, folder.Name),
            DropCommandLeafNode leaf => DropCommandLeafNode.Restore(
                leaf.Id,
                parentId,
                order,
                leaf.CommandInstanceId),
            _ => throw new InvalidOperationException("Unknown command placement node type.")
        };

    private static DropCommandPlacementNode WithOrder(DropCommandPlacementNode node, int order) =>
        WithParent(node, node.ParentId, order);

    private static IReadOnlyList<DropCommandPlacementNode> NormalizeOrders(
        IReadOnlyList<DropCommandPlacementNode> nodes) =>
        nodes.GroupBy(static node => node.ParentId)
            .SelectMany(group => group
                .OrderBy(static node => node.Order)
                .ThenBy(static node => node.Id)
                .Select((node, index) => WithOrder(node, index)))
            .ToArray();

    private void ReplaceLayout(string surfaceId, IReadOnlyList<DropCommandPlacementNode> nodes)
    {
        this._layouts[surfaceId] = DropCommandSurfaceLayout.Restore(surfaceId, nodes);
        this.RaiseCatalogChanged();
    }

    private DropCommandSurfaceLayout GetLayout(string surfaceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        if (this._layouts.TryGetValue(surfaceId, out var layout))
        {
            return layout;
        }

        layout = DropCommandSurfaceLayout.CreateEmpty(surfaceId);
        this._layouts.Add(surfaceId, layout);
        return layout;
    }

    private void EnsureDefaultLayouts()
    {
        foreach (var surfaceId in this.GetSurfaceIds())
        {
            _ = this.GetLayout(surfaceId);
        }
    }

    private DropCommandPlacementViewModel? CreatePlacementViewModel(
        string surfaceId,
        DropCommandPlacementNode node,
        int depth)
    {
        if (node is DropCommandFolderNode folder)
        {
            return DropCommandPlacementViewModel.ForFolder(surfaceId, folder, depth);
        }

        if (node is not DropCommandLeafNode leaf || this.FindCommand(leaf.CommandInstanceId) is not { } command)
        {
            return null;
        }

        return DropCommandPlacementViewModel.ForCommand(surfaceId, leaf, command, depth);
    }

    private void AddFlattenedChildren(
        string surfaceId,
        Guid? parentId,
        int depth,
        ICollection<DropCommandPlacementViewModel> result)
    {
        foreach (var node in this.GetLayout(surfaceId).Nodes
                     .Where(node => node.ParentId == parentId)
                     .OrderBy(static node => node.Order)
                     .ThenBy(static node => node.Id))
        {
            if (this.CreatePlacementViewModel(surfaceId, node, depth) is { } viewModel)
            {
                result.Add(viewModel);
            }

            if (node is DropCommandFolderNode)
            {
                this.AddFlattenedChildren(surfaceId, node.Id, depth + 1, result);
            }
        }
    }

    private static void AddDescendantCommandIds(
        DropCommandSurfaceLayout layout,
        Guid folderId,
        ICollection<Guid> result)
    {
        foreach (var node in layout.Nodes.Where(node => node.ParentId == folderId))
        {
            if (node is DropCommandLeafNode leaf)
            {
                result.Add(leaf.CommandInstanceId);
            }
            else if (node is DropCommandFolderNode)
            {
                AddDescendantCommandIds(layout, node.Id, result);
            }
        }
    }

    private void RaiseCatalogChanged() => this.CatalogChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class DropCommandViewModel : ObservableObject
{
    public DropCommandInstance Model { get; private set; }

    public Guid Id => this.Model.Id;

    public string Name => this.Model.DisplayName;

    public string CompactName => this.Name.Length <= 12 ? this.Name : $"{this.Name[..11]}…";

    public string AccessibleName => $"{this.Name}, drop command, {this.AcceptanceText}";

    public string Tint => this.Model.Tint;

    public Color TintColor => StackTintPalette.Resolve(this.Tint);

    public SolidColorBrush TintBrush { get; }

    public SolidColorBrush TintForegroundBrush { get; }

    public string TemplateId => this.Model.TemplateId;

    public string Glyph => DropCommandTemplates.Get(this.Model.TemplateId)?.Glyph ?? "\uE783";

    public string Summary => DropCommandTemplates.GetSummary(this.Model);

    public string AcceptanceText => DropCommandTemplates.GetAcceptanceText(this.Model);

    public bool IsAvailable => DropCommandTemplates.TryGet(this.Model.TemplateId, out _);

    public bool IsConfigured => DropCommandTemplates.IsConfigured(this.Model);

    public bool IsEnabled => this.Model.IsEnabled && this.IsAvailable && this.IsConfigured;

    internal DropCommandViewModel(DropCommandInstance model)
    {
        this.Model = model;
        var tintColor = this.TintColor;
        this.TintBrush = new SolidColorBrush(tintColor);
        this.TintForegroundBrush = new SolidColorBrush(GetContrastingForeground(tintColor));
    }

    internal void Update(DropCommandInstance model)
    {
        if (model.Id != this.Model.Id)
        {
            throw new ArgumentException("The command identity cannot change.", nameof(model));
        }

        this.Model = model;
        this.UpdateTintBrushes();
        this.OnPropertyChanged(string.Empty);
    }

    internal void RefreshSystemColors()
    {
        if (!StackTintPalette.UsesSystemColor(this.Tint))
        {
            return;
        }

        this.UpdateTintBrushes();
        this.OnPropertyChanged(nameof(this.TintColor));
    }

    private void UpdateTintBrushes()
    {
        var tintColor = this.TintColor;
        this.TintBrush.Color = tintColor;
        this.TintForegroundBrush.Color = GetContrastingForeground(tintColor);
    }

    private static Color GetContrastingForeground(Color background)
    {
        var perceivedBrightness = ((background.R * 299) + (background.G * 587) + (background.B * 114)) / 1000;
        return perceivedBrightness >= 160 ? Colors.Black : Colors.White;
    }
}

public sealed class DropCommandPlacementViewModel : ObservableObject
{
    public string SurfaceId { get; }

    public Guid NodeId { get; }

    public Guid? ParentId { get; }

    public int Depth { get; }

    public Thickness Indent => new(this.Depth * 24, 0, 0, 0);

    public string DisplayName { get; }

    public string Summary { get; }

    public string Glyph { get; }

    public bool IsFolder { get; }

    public bool IsCommand => !this.IsFolder;

    public bool IsEnabled => this.IsFolder || this.Command?.IsEnabled == true;

    public Visibility FolderVisibility => this.IsFolder ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CommandVisibility => this.IsFolder ? Visibility.Collapsed : Visibility.Visible;

    public DropCommandViewModel? Command { get; }

    private DropCommandPlacementViewModel(
        string surfaceId,
        Guid nodeId,
        Guid? parentId,
        int depth,
        string displayName,
        string summary,
        string glyph,
        bool isFolder,
        DropCommandViewModel? command)
    {
        this.SurfaceId = surfaceId;
        this.NodeId = nodeId;
        this.ParentId = parentId;
        this.Depth = depth;
        this.DisplayName = displayName;
        this.Summary = summary;
        this.Glyph = glyph;
        this.IsFolder = isFolder;
        this.Command = command;
    }

    internal static DropCommandPlacementViewModel ForFolder(
        string surfaceId,
        DropCommandFolderNode folder,
        int depth) =>
        new(surfaceId, folder.Id, folder.ParentId, depth, folder.Name, "Command folder", "\uE8B7", true, null);

    internal static DropCommandPlacementViewModel ForCommand(
        string surfaceId,
        DropCommandLeafNode leaf,
        DropCommandViewModel command,
        int depth) =>
        new(
            surfaceId,
            leaf.Id,
            leaf.ParentId,
            depth,
            command.Name,
            command.Summary,
            command.Glyph,
            false,
            command);
}
