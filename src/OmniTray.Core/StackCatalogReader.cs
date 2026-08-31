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
        NoteOperations.Validate(stacks);
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
            item.Html,
            item.Rtf,
            item.Url,
            item.SourceUrl,
            item.SourceApplicationName,
            item.IsOwned,
            item.CreatedAt,
            RestoreCustomFormats(item.CustomFormats),
            item.ApplicationLink,
            item.SourcePackageFamilyName,
            item.SourceApplicationLink,
            item.Capture,
            item.Backing,
            item.FileFacts,
            item.HtmlResources,
            item.Note,
            item.AttachedNotes)),
        stack.InspectorViewMode,
        stack.AttachedNotes,
        RestoreVirtualSource(stack.VirtualSource),
        stack.ItemSortMode);

    private static VirtualStackSource? RestoreVirtualSource(VirtualStackSourceReadDocument? source) =>
        source is null
            ? null
            : VirtualStackSource.Create(source.ProviderId, source.Configuration, source.Capabilities);

    private static IReadOnlyList<DropItemDataFormat> RestoreCustomFormats(
        IEnumerable<ItemDataFormatReadDocument> documents)
    {
        var formats = new List<DropItemDataFormat>();
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.FormatId))
            {
                continue;
            }

            var format = document.Kind switch
            {
                DropItemDataFormatKind.Text when document.Text is not null =>
                    DropItemDataFormat.CreateText(document.FormatId, document.Text),
                DropItemDataFormatKind.Binary when document.Data is not null =>
                    DropItemDataFormat.CreateBinary(document.FormatId, document.Data),
                _ => null
            };
            if (format is not null)
            {
                formats.Add(format);
            }
        }

        return formats;
    }
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

    public StackItemSortMode ItemSortMode { get; set; } = StackItemSortMode.Default;

    public VirtualStackSourceReadDocument? VirtualSource { get; set; }

    public List<ItemReadDocument> Items { get; set; } = [];

    public List<StickyNote> AttachedNotes { get; set; } = [];
}

internal sealed class VirtualStackSourceReadDocument
{
    public string ProviderId { get; set; } = string.Empty;

    public string? Configuration { get; set; }

    public VirtualStackCapabilities Capabilities { get; set; }
}

internal sealed class ItemReadDocument
{
    public StickyNote? Note { get; set; }

    public List<StickyNote> AttachedNotes { get; set; } = [];

    public Guid Id { get; set; }

    public DropItemKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? SourcePath { get; set; }

    public string? Text { get; set; }

    public string? Html { get; set; }

    public string? Rtf { get; set; }

    public string? Url { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceApplicationName { get; set; }

    public string? ApplicationLink { get; set; }

    public string? SourcePackageFamilyName { get; set; }

    public string? SourceApplicationLink { get; set; }

    public DropCaptureMetadata? Capture { get; set; }

    public ContentBacking? Backing { get; set; }

    public DropFileFacts? FileFacts { get; set; }

    public List<DropItemHtmlResource> HtmlResources { get; set; } = [];

    public List<ItemDataFormatReadDocument> CustomFormats { get; set; } = [];

    public bool IsOwned { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ItemDataFormatReadDocument
{
    public string FormatId { get; set; } = string.Empty;

    public DropItemDataFormatKind Kind { get; set; }

    public string? Text { get; set; }

    public byte[]? Data { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StackCatalogReadDocument))]
internal partial class StackCatalogReadJsonContext : JsonSerializerContext;
