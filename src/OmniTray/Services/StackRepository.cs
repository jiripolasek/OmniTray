// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace OmniTray.Services;

internal sealed class StackRepository
{
    private const string CatalogFileName = "stack-catalog.json";
    private const string TemporaryCatalogFileName = "stack-catalog.tmp";
    private const int EarliestSupportedVersion = StackCatalogReader.EarliestSupportedVersion;
    private const int CurrentVersion = StackCatalogReader.CurrentVersion;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<StackCatalogState> LoadAsync()
    {
        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var item = await folder.TryGetItemAsync(CatalogFileName);
            if (item is not StorageFile file)
            {
                return StackCatalogState.Empty;
            }

            try
            {
                var json = await FileIO.ReadTextAsync(file);
                var catalog = JsonSerializer.Deserialize(
                    json,
                    StackCatalogJsonContext.Default.StackCatalogDocument);
                if (catalog is null)
                {
                    throw new JsonException("The stack catalog was empty.");
                }

                if (catalog.Version is < EarliestSupportedVersion or > CurrentVersion)
                {
                    await PreserveUnsupportedCatalogAsync(file, catalog.Version);
                    return StackCatalogState.Empty;
                }

                var stacks = catalog.Stacks.Select(RestoreStack).ToArray();
                if (stacks.Select(static stack => stack.Id).Distinct().Count() != stacks.Length)
                {
                    throw new JsonException("Stack IDs must be unique in the catalog.");
                }

                var openTrayWindows = catalog.Version >= 2
                    ? RestoreOpenTrayWindows(catalog.OpenTrayWindows)
                    : [];
                var edgeShelves = catalog.Version switch
                {
                    >= 4 => RestoreEdgeShelves(catalog.EdgeShelves, stacks),
                    3 => RestoreLegacyEdgePanel(catalog.EdgePanel, stacks),
                    _ => CreateLegacyDefaultShelves(stacks)
                };
                return new StackCatalogState(
                    stacks,
                    openTrayWindows,
                    edgeShelves,
                    catalog.Version < CurrentVersion);
            }
            catch
            {
                await PreserveCorruptCatalogAsync(file);
                return StackCatalogState.Empty;
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    public async Task SaveAsync(StackCatalogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var catalog = new StackCatalogDocument
        {
            Version = CurrentVersion,
            Stacks = [.. state.Stacks.Select(CreateStackDocument)],
            OpenTrayWindows =
            [
                .. state.OpenTrayWindows.Select(static tray => new TrayWindowDocument
                {
                    StackId = tray.StackId,
                    X = tray.X,
                    Y = tray.Y,
                    Width = tray.Width,
                    Height = tray.Height,
                    IsMinimal = tray.IsMinimal,
                    NormalWidth = tray.NormalWidth,
                    NormalHeight = tray.NormalHeight
                })
            ],
            EdgeShelves =
            [
                .. state.EdgeShelves.Select(static shelf =>
                    new EdgeShelfDocument { Side = shelf.Side, StackIds = [.. shelf.StackIds] })
            ]
        };
        var json = JsonSerializer.Serialize(
            catalog,
            StackCatalogJsonContext.Default.StackCatalogDocument);

        await this._gate.WaitAsync();
        try
        {
            var folder = ApplicationData.Current.LocalFolder;
            var temporaryFile = await folder.CreateFileAsync(
                TemporaryCatalogFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(temporaryFile, json);

            var existing = await folder.TryGetItemAsync(CatalogFileName);
            if (existing is StorageFile existingFile)
            {
                await temporaryFile.MoveAndReplaceAsync(existingFile);
            }
            else
            {
                await temporaryFile.RenameAsync(
                    CatalogFileName,
                    NameCollisionOption.ReplaceExisting);
            }
        }
        finally
        {
            this._gate.Release();
        }
    }

    private static DropStack RestoreStack(StackDocument stack) => DropStack.Restore(
        stack.Id,
        stack.Name,
        stack.Tint,
        stack.Items.Select(static item => DropItem.Restore(
            item.Id,
            item.Kind,
            item.DisplayName,
            item.SourcePath,
            item.Text,
            item.IsOwned,
            item.CreatedAt)));

    private static StackDocument CreateStackDocument(DropStack stack) => new()
    {
        Id = stack.Id,
        Name = stack.Name,
        Tint = stack.Tint,
        Items =
        [
            .. stack.Items.Select(static item => new ItemDocument
            {
                Id = item.Id,
                Kind = item.Kind,
                DisplayName = item.DisplayName,
                SourcePath = item.SourcePath,
                Text = item.Text,
                IsOwned = item.IsOwned,
                CreatedAt = item.CreatedAt
            })
        ]
    };

    private static IReadOnlyList<TrayWindowState> RestoreOpenTrayWindows(
        IEnumerable<TrayWindowDocument> documents) => documents
        .Where(static tray => tray.StackId != Guid.Empty && tray is { Width: > 0, Height: > 0 })
        .GroupBy(static tray => tray.StackId)
        .Select(static group => group.Last())
        .Select(static tray => new TrayWindowState(
            tray.StackId,
            tray.X,
            tray.Y,
            tray.Width,
            tray.Height,
            tray.IsMinimal,
            tray.NormalWidth > 0 ? tray.NormalWidth : tray.Width,
            tray.NormalHeight > 0 ? tray.NormalHeight : tray.Height))
        .ToArray();

    private static IReadOnlyList<EdgeShelfState> RestoreEdgeShelves(
        IEnumerable<EdgeShelfDocument> documents,
        IReadOnlyList<DropStack> stacks)
    {
        var knownStackIds = stacks.Select(static stack => stack.Id).ToHashSet();
        var assignedStackIds = new HashSet<Guid>();
        var documentsBySide = documents
            .Where(static document => Enum.IsDefined(document.Side))
            .GroupBy(static document => document.Side)
            .ToDictionary(static group => group.Key, static group => group.Last());

        return Enum.GetValues<EdgeShelfSide>()
            .Select(side => new EdgeShelfState(
                side,
                documentsBySide.TryGetValue(side, out var document)
                    ? document.StackIds
                        .Where(stackId => knownStackIds.Contains(stackId) && assignedStackIds.Add(stackId))
                        .ToArray()
                    : []))
            .ToArray();
    }

    private static IReadOnlyList<EdgeShelfState> RestoreLegacyEdgePanel(
        EdgePanelDocument? document,
        IReadOnlyList<DropStack> stacks)
    {
        if (document is null)
        {
            return EdgeShelfState.CreateEmptyShelves();
        }

        var knownStackIds = stacks.Select(static stack => stack.Id).ToHashSet();
        var side = document.Side is EdgeShelfSide.Left or EdgeShelfSide.Right
            ? document.Side
            : EdgeShelfSide.Right;
        var orderedStackIds = document.StackIds
            .Where(knownStackIds.Contains)
            .Distinct()
            .ToArray();
        return Enum.GetValues<EdgeShelfSide>()
            .Select(candidate => new EdgeShelfState(
                candidate,
                candidate == side ? orderedStackIds : []))
            .ToArray();
    }

    private static IReadOnlyList<EdgeShelfState> CreateLegacyDefaultShelves(
        IReadOnlyList<DropStack> stacks) => Enum.GetValues<EdgeShelfSide>()
        .Select(side => new EdgeShelfState(
            side,
            side == EdgeShelfSide.Right
                ? stacks.Select(static stack => stack.Id).ToArray()
                : []))
        .ToArray();

    private static async Task PreserveCorruptCatalogAsync(StorageFile file)
    {
        try
        {
            await file.RenameAsync(
                $"stack-catalog.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                NameCollisionOption.GenerateUniqueName);
        }
        catch
        {
            // Recovery still succeeds with an empty catalogue if quarantine fails.
        }
    }

    private static async Task PreserveUnsupportedCatalogAsync(StorageFile file, int version)
    {
        try
        {
            await file.RenameAsync(
                $"stack-catalog.unsupported-v{version}.json",
                NameCollisionOption.GenerateUniqueName);
        }
        catch
        {
            // Leave the unsupported catalog in place if it cannot be preserved under a new name.
        }
    }
}

internal sealed record StackCatalogState(
    IReadOnlyList<DropStack> Stacks,
    IReadOnlyList<TrayWindowState> OpenTrayWindows,
    IReadOnlyList<EdgeShelfState> EdgeShelves,
    bool NeedsMigration = false)
{
    public static StackCatalogState Empty { get; } = new([], [], EdgeShelfState.CreateEmptyShelves());
}

internal sealed record EdgeShelfState(
    EdgeShelfSide Side,
    IReadOnlyList<Guid> StackIds)
{
    public static IReadOnlyList<EdgeShelfState> CreateEmptyShelves() =>
        Enum.GetValues<EdgeShelfSide>()
            .Select(static side => new EdgeShelfState(side, []))
            .ToArray();
}

internal sealed record TrayWindowState(
    Guid StackId,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsMinimal,
    int NormalWidth,
    int NormalHeight);

internal sealed class StackCatalogDocument
{
    public int Version { get; set; }

    public List<StackDocument> Stacks { get; set; } = [];

    public List<TrayWindowDocument> OpenTrayWindows { get; set; } = [];

    public List<EdgeShelfDocument> EdgeShelves { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EdgePanelDocument? EdgePanel { get; set; }
}

internal sealed class StackDocument
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tint { get; set; } = string.Empty;

    public List<ItemDocument> Items { get; set; } = [];
}

internal sealed class ItemDocument
{
    public Guid Id { get; set; }

    public DropItemKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? SourcePath { get; set; }

    public string? Text { get; set; }

    public bool IsOwned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class TrayWindowDocument
{
    public Guid StackId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public bool IsMinimal { get; set; }

    public int NormalWidth { get; set; }

    public int NormalHeight { get; set; }
}

internal sealed class EdgePanelDocument
{
    public EdgeShelfSide Side { get; set; } = EdgeShelfSide.Right;

    public List<Guid> StackIds { get; set; } = [];
}

internal sealed class EdgeShelfDocument
{
    public EdgeShelfSide Side { get; set; }

    public List<Guid> StackIds { get; set; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(StackCatalogDocument))]
internal partial class StackCatalogJsonContext : JsonSerializerContext;
