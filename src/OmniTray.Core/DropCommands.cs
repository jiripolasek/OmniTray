// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

// Provisional in-process MVP model. CLR visibility lets OmniTray projects share it, but this
// does not become a supported extension contract until it is expressed in IDL and a dedicated
// SDK/contract project.
public static class DropCommandTemplateIds
{
    public const string OpenInApp = "omnitray.builtin.open-in-app";
    public const string CopyToFolder = "omnitray.builtin.copy-to-folder";
    public const string MoveToFolder = "omnitray.builtin.move-to-folder";
    public const string Recycle = "omnitray.builtin.recycle";
    public const string CopyToClipboard = "omnitray.builtin.copy-to-clipboard";
    public const string Share = "omnitray.builtin.share";
}

public static class DropCommandParameterNames
{
    public const string ApplicationTarget = "applicationTarget";
    public const string ExecutablePath = "executablePath";
    public const string ExtraArguments = "extraArguments";
    public const string AppUserModelId = "appUserModelId";
    public const string PackagedAppDisplayName = "packagedAppDisplayName";
    public const string DestinationFolder = "destinationFolder";
}

public static class DropCommandApplicationTargetIds
{
    public const string DesktopExecutable = "desktopExecutable";

    public const string PackagedApp = "packagedApp";
}

public static class DropCommandSurfaceIds
{
    public const string Popup = "popup";

    public const string LeftEdge = "edge:left";

    public const string RightEdge = "edge:right";

    public const string TopEdge = "edge:top";

    public const string BottomEdge = "edge:bottom";

    public static string ForEdge(EdgeShelfSide side) => side switch
    {
        EdgeShelfSide.Left => LeftEdge,
        EdgeShelfSide.Right => RightEdge,
        EdgeShelfSide.Top => TopEdge,
        EdgeShelfSide.Bottom => BottomEdge,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };
}

public sealed record DropCommandInstance
{
    private DropCommandInstance(
        Guid id,
        string templateId,
        string displayName,
        IReadOnlyDictionary<string, string> parameters,
        bool isEnabled,
        string tint)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A command ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);

        var normalizedParameters = parameters
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                static pair => pair.Key.Trim(),
                static pair => pair.Value ?? string.Empty,
                StringComparer.Ordinal);

        this.Id = id;
        this.TemplateId = templateId.Trim();
        this.DisplayName = displayName.Trim();
        this.Parameters = normalizedParameters;
        this.IsEnabled = isEnabled;
        this.Tint = tint.Trim();
    }

    public Guid Id { get; }

    public string TemplateId { get; }

    public string DisplayName { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    public bool IsEnabled { get; }

    public string Tint { get; }

    public static DropCommandInstance Create(
        string templateId,
        string displayName,
        IReadOnlyDictionary<string, string>? parameters = null,
        string tint = TrayTintIds.Neutral) =>
        new(
            Guid.NewGuid(),
            templateId,
            displayName,
            parameters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            true,
            tint);

    public static DropCommandInstance Restore(
        Guid id,
        string templateId,
        string displayName,
        IReadOnlyDictionary<string, string> parameters,
        bool isEnabled,
        string tint = TrayTintIds.Neutral) =>
        new(id, templateId, displayName, parameters, isEnabled, tint);

    public DropCommandInstance Reconfigure(
        string displayName,
        IReadOnlyDictionary<string, string> parameters,
        bool isEnabled) =>
        new(this.Id, this.TemplateId, displayName, parameters, isEnabled, this.Tint);

    public DropCommandInstance ChangeTint(string tint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tint);
        return new DropCommandInstance(
            this.Id,
            this.TemplateId,
            this.DisplayName,
            this.Parameters,
            this.IsEnabled,
            tint);
    }
}

public abstract record DropCommandPlacementNode
{
    protected DropCommandPlacementNode(Guid id, Guid? parentId, int order)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A placement node ID is required.", nameof(id));
        }

        if (parentId == Guid.Empty)
        {
            throw new ArgumentException("A parent ID must be null or non-empty.", nameof(parentId));
        }

        this.Id = id;
        this.ParentId = parentId;
        this.Order = order;
    }

    public Guid Id { get; }

    public Guid? ParentId { get; }

    public int Order { get; }
}

public sealed record DropCommandFolderNode : DropCommandPlacementNode
{
    private DropCommandFolderNode(Guid id, Guid? parentId, int order, string name)
        : base(id, parentId, order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.Name = name.Trim();
    }

    public string Name { get; }

    public static DropCommandFolderNode Create(Guid? parentId, int order, string name) =>
        new(Guid.NewGuid(), parentId, order, name);

    public static DropCommandFolderNode Restore(Guid id, Guid? parentId, int order, string name) =>
        new(id, parentId, order, name);
}

public sealed record DropCommandLeafNode : DropCommandPlacementNode
{
    private DropCommandLeafNode(Guid id, Guid? parentId, int order, Guid commandInstanceId)
        : base(id, parentId, order)
    {
        if (commandInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A command instance ID is required.", nameof(commandInstanceId));
        }

        this.CommandInstanceId = commandInstanceId;
    }

    public Guid CommandInstanceId { get; }

    public static DropCommandLeafNode Create(Guid? parentId, int order, Guid commandInstanceId) =>
        new(Guid.NewGuid(), parentId, order, commandInstanceId);

    public static DropCommandLeafNode Restore(
        Guid id,
        Guid? parentId,
        int order,
        Guid commandInstanceId) =>
        new(id, parentId, order, commandInstanceId);
}

public sealed record DropCommandSurfaceLayout
{
    private DropCommandSurfaceLayout(string surfaceId, IReadOnlyList<DropCommandPlacementNode> nodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(nodes);
        ValidateNodes(nodes);

        this.SurfaceId = surfaceId.Trim();
        this.Nodes = nodes.ToArray();
    }

    public string SurfaceId { get; }

    public IReadOnlyList<DropCommandPlacementNode> Nodes { get; }

    public static DropCommandSurfaceLayout CreateEmpty(string surfaceId) => new(surfaceId, []);

    public static DropCommandSurfaceLayout Restore(
        string surfaceId,
        IReadOnlyList<DropCommandPlacementNode> nodes) =>
        new(surfaceId, nodes);

    private static void ValidateNodes(IReadOnlyList<DropCommandPlacementNode> nodes)
    {
        var nodesById = nodes.ToDictionary(static node => node.Id);
        if (nodesById.Count != nodes.Count)
        {
            throw new ArgumentException("Placement node IDs must be unique.", nameof(nodes));
        }

        foreach (var node in nodes)
        {
            if (node.Order < 0)
            {
                throw new ArgumentException("Placement order cannot be negative.", nameof(nodes));
            }

            if (node.ParentId is not { } parentId)
            {
                continue;
            }

            if (!nodesById.TryGetValue(parentId, out var parent) || parent is not DropCommandFolderNode)
            {
                throw new ArgumentException("Every parent must reference a folder in the same layout.", nameof(nodes));
            }

            var visited = new HashSet<Guid> { node.Id };
            var current = parent;
            while (true)
            {
                if (!visited.Add(current.Id))
                {
                    throw new ArgumentException("Placement folders cannot contain a cycle.", nameof(nodes));
                }

                if (current.ParentId is not { } ancestorId)
                {
                    break;
                }

                if (!nodesById.TryGetValue(ancestorId, out current) || current is not DropCommandFolderNode)
                {
                    throw new ArgumentException(
                        "Every ancestor must reference a folder in the same layout.",
                        nameof(nodes));
                }
            }
        }

        if (nodes.OfType<DropCommandLeafNode>()
            .GroupBy(static node => node.CommandInstanceId)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A command can appear at most once in a surface layout.",
                nameof(nodes));
        }
    }
}
