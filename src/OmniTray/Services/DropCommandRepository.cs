// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace OmniTray.Services;

internal sealed class DropCommandRepository
{
    private const string CatalogFileName = "drop-command-catalog.json";
    private const string TemporaryCatalogFileName = "drop-command-catalog.tmp";
    private const int CurrentVersion = 3;
    // Version 1 remains readable; its per-command acceptedKinds field is intentionally ignored.
    private const int FirstSupportedVersion = 1;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DropCommandCatalogState> LoadAsync()
    {
        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            if (await folder.TryGetItemAsync(CatalogFileName) is not StorageFile file)
            {
                return DropCommandCatalogState.Empty;
            }

            try
            {
                var document = JsonSerializer.Deserialize(
                    await FileIO.ReadTextAsync(file),
                    DropCommandCatalogJsonContext.Default.DropCommandCatalogDocument);
                if (document is null)
                {
                    throw new JsonException("The command catalog is empty.");
                }

                if (document.Version < FirstSupportedVersion || document.Version > CurrentVersion)
                {
                    await PreserveUnsupportedCatalogAsync(file, document.Version);
                    return DropCommandCatalogState.Empty;
                }

                var commands = document.Commands.Select(RestoreCommand).ToArray();
                if (commands.Select(static command => command.Id).Distinct().Count() != commands.Length)
                {
                    throw new JsonException("Command IDs must be unique.");
                }

                var layouts = document.Layouts.Select(RestoreLayout).ToArray();
                if (layouts.Select(static layout => layout.SurfaceId).Distinct(StringComparer.Ordinal).Count() !=
                    layouts.Length)
                {
                    throw new JsonException("Command surface IDs must be unique.");
                }

                var knownCommands = commands.Select(static command => command.Id).ToHashSet();
                var windows = document.OpenWindows
                    .Where(window => knownCommands.Contains(window.CommandId) && window.Width > 0 && window.Height > 0)
                    .GroupBy(static window => window.CommandId)
                    .Select(static group => group.Last())
                    .Select(static window => new DropCommandWindowState(
                        window.CommandId,
                        window.X,
                        window.Y,
                        window.Width,
                        window.Height,
                        window.IsMinimal,
                        window.NormalWidth > 0 ? window.NormalWidth : window.Width,
                        window.NormalHeight > 0 ? window.NormalHeight : window.Height))
                    .ToArray();
                return new DropCommandCatalogState(commands, layouts, windows);
            }
            catch
            {
                await PreserveCorruptCatalogAsync(file);
                return DropCommandCatalogState.Empty;
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    public async Task SaveAsync(DropCommandCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var document = new DropCommandCatalogDocument
        {
            Version = CurrentVersion,
            Commands = [.. state.Commands.Select(CreateCommandDocument)],
            Layouts = [.. state.Layouts.Select(CreateLayoutDocument)],
            OpenWindows =
            [
                .. state.OpenWindows.Select(static window => new DropCommandWindowDocument
                {
                    CommandId = window.CommandId,
                    X = window.X,
                    Y = window.Y,
                    Width = window.Width,
                    Height = window.Height,
                    IsMinimal = window.IsMinimal,
                    NormalWidth = window.NormalWidth,
                    NormalHeight = window.NormalHeight
                })
            ]
        };
        var json = JsonSerializer.Serialize(
            document,
            DropCommandCatalogJsonContext.Default.DropCommandCatalogDocument);

        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var temporary = await folder.CreateFileAsync(
                TemporaryCatalogFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(temporary, json);
            if (await folder.TryGetItemAsync(CatalogFileName) is StorageFile existing)
            {
                await temporary.MoveAndReplaceAsync(existing);
            }
            else
            {
                await temporary.RenameAsync(CatalogFileName, NameCollisionOption.ReplaceExisting);
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    private static DropCommandInstance RestoreCommand(DropCommandDocument document) =>
        DropCommandInstance.Restore(
            document.Id,
            document.TemplateId,
            document.DisplayName,
            document.Parameters,
            document.IsEnabled,
            document.Tint);

    private static DropCommandSurfaceLayout RestoreLayout(DropCommandLayoutDocument document) =>
        DropCommandSurfaceLayout.Restore(
            document.SurfaceId,
            document.Nodes.Select(static node => node.Kind switch
            {
                "folder" => (DropCommandPlacementNode)DropCommandFolderNode.Restore(
                    node.Id,
                    node.ParentId,
                    node.Order,
                    node.Name),
                "command" => DropCommandLeafNode.Restore(
                    node.Id,
                    node.ParentId,
                    node.Order,
                    node.CommandInstanceId),
                _ => throw new JsonException($"Unknown placement node kind: {node.Kind}")
            }).ToArray());

    private static DropCommandDocument CreateCommandDocument(DropCommandInstance command) => new()
    {
        Id = command.Id,
        TemplateId = command.TemplateId,
        DisplayName = command.DisplayName,
        Parameters = new Dictionary<string, string>(command.Parameters, StringComparer.Ordinal),
        IsEnabled = command.IsEnabled,
        Tint = command.Tint
    };

    private static DropCommandLayoutDocument CreateLayoutDocument(DropCommandSurfaceLayout layout) => new()
    {
        SurfaceId = layout.SurfaceId,
        Nodes =
        [
            .. layout.Nodes.Select(static node => node switch
            {
                DropCommandFolderNode folder => new DropCommandNodeDocument
                {
                    Kind = "folder",
                    Id = folder.Id,
                    ParentId = folder.ParentId,
                    Order = folder.Order,
                    Name = folder.Name
                },
                DropCommandLeafNode leaf => new DropCommandNodeDocument
                {
                    Kind = "command",
                    Id = leaf.Id,
                    ParentId = leaf.ParentId,
                    Order = leaf.Order,
                    CommandInstanceId = leaf.CommandInstanceId
                },
                _ => throw new InvalidOperationException("Unknown command placement node type.")
            })
        ]
    };

    private static async Task PreserveCorruptCatalogAsync(StorageFile file)
    {
        try
        {
            await file.RenameAsync(
                $"drop-command-catalog.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                NameCollisionOption.GenerateUniqueName);
        }
        catch
        {
            // An empty command catalogue is still usable if quarantine fails.
        }
    }

    private static async Task PreserveUnsupportedCatalogAsync(StorageFile file, int version)
    {
        try
        {
            await file.RenameAsync(
                $"drop-command-catalog.unsupported-v{version}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                NameCollisionOption.GenerateUniqueName);
        }
        catch
        {
            // Starting with an empty catalog remains possible if the unsupported file cannot move.
        }
    }
}

internal sealed record DropCommandCatalogState(
    IReadOnlyList<DropCommandInstance> Commands,
    IReadOnlyList<DropCommandSurfaceLayout> Layouts,
    IReadOnlyList<DropCommandWindowState> OpenWindows)
{
    public static DropCommandCatalogState Empty { get; } = new([], [], []);
}

internal sealed record DropCommandWindowState(
    Guid CommandId,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsMinimal,
    int NormalWidth,
    int NormalHeight);

internal sealed class DropCommandCatalogDocument
{
    public int Version { get; set; }

    public List<DropCommandDocument> Commands { get; set; } = [];

    public List<DropCommandLayoutDocument> Layouts { get; set; } = [];

    public List<DropCommandWindowDocument> OpenWindows { get; set; } = [];
}

internal sealed class DropCommandDocument
{
    public Guid Id { get; set; }

    public string TemplateId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);

    public bool IsEnabled { get; set; } = true;

    public string Tint { get; set; } = TrayTintIds.Neutral;
}

internal sealed class DropCommandLayoutDocument
{
    public string SurfaceId { get; set; } = string.Empty;

    public List<DropCommandNodeDocument> Nodes { get; set; } = [];
}

internal sealed class DropCommandNodeDocument
{
    public string Kind { get; set; } = string.Empty;

    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid CommandInstanceId { get; set; }
}

internal sealed class DropCommandWindowDocument
{
    public Guid CommandId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsMinimal { get; set; }

    public int NormalWidth { get; set; }

    public int NormalHeight { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(DropCommandCatalogDocument))]
internal partial class DropCommandCatalogJsonContext : JsonSerializerContext;
