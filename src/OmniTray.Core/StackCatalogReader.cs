// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniTray.Core;

public static class StackCatalogReader
{
    public static IReadOnlyList<DropStack> ReadStacks(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var catalog = JsonSerializer.Deserialize(
                          json,
                          StackCatalogReadJsonContext.Default.StackCatalogReadDocument) ??
                      throw new JsonException("The stack catalog was empty.");
        var stacks = catalog.Stacks.Select(RestoreStack).ToArray();
        if (stacks.Select(static stack => stack.Id).Distinct().Count() != stacks.Length)
        {
            throw new JsonException("Stack IDs must be unique in the catalog.");
        }

        return stacks;
    }

    private static DropStack RestoreStack(StackReadDocument stack) => DropStack.Restore(
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
            item.CreatedAt)),
        stack.InspectorViewMode);
}

internal sealed class StackCatalogReadDocument
{
    public List<StackReadDocument> Stacks { get; set; } = [];
}

internal sealed class StackReadDocument
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tint { get; set; } = string.Empty;

    public StackInspectorViewMode InspectorViewMode { get; set; } = StackInspectorViewMode.List;

    public List<ItemReadDocument> Items { get; set; } = [];
}

internal sealed class ItemReadDocument
{
    public Guid Id { get; set; }

    public DropItemKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? SourcePath { get; set; }

    public string? Text { get; set; }

    public bool IsOwned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StackCatalogReadDocument))]
internal partial class StackCatalogReadJsonContext : JsonSerializerContext;
