// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace OmniTray.Views;

public sealed partial class DataFormatInspectorWindow : Window
{
    private static readonly HashSet<string> StandardFormatIds = new(StringComparer.Ordinal)
    {
        StandardDataFormats.ApplicationLink,
        StandardDataFormats.Bitmap,
        StandardDataFormats.Html,
        StandardDataFormats.Rtf,
        StandardDataFormats.StorageItems,
        StandardDataFormats.Text,
        StandardDataFormats.Uri,
        StandardDataFormats.WebLink
    };

    private int _inspectionGeneration;
    private bool _isInspecting;

    public DataFormatInspectorWindow()
    {
        this.InitializeComponent();
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            this.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            this.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    public ObservableCollection<DataFormatInspectionEntry> Formats { get; } = [];

    public void Inspect(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var generation = ++this._inspectionGeneration;
        this._isInspecting = false;
        this.InspectClipboardButton.IsEnabled = true;
        this.InspectionProgress.IsActive = false;
        this.Formats.Clear();
        this.StatusBar.IsOpen = false;
        this.EmptyState.Visibility = Visibility.Collapsed;

        var capture = item.Capture;
        this.SourceSummaryText.Text = capture is null
            ? $"Stored item · metadata unavailable · {item.CreatedAt.LocalDateTime:g}"
            : $"{capture.Channel} capture · {capture.Formats.Count} format{(capture.Formats.Count == 1 ? string.Empty : "s")} · {capture.CapturedAt.LocalDateTime:g}";
        this.PackagePropertiesText.Text = DescribeStoredItem(item);

        var formats = capture?.Formats ?? CreateStoredFormatInventory(item);
        for (var index = 0; index < formats.Count; index++)
        {
            var format = formats[index];
            this.Formats.Add(new DataFormatInspectionEntry(
                index + 1,
                format.FormatId,
                ClassifyFormat(format.FormatId),
                format.Status.ToString(),
                format.Detail ?? "No capture detail was recorded."));
        }

        foreach (var resource in item.HtmlResources)
        {
            this.Formats.Add(new DataFormatInspectionEntry(
                this.Formats.Count + 1,
                resource.ResourceKey,
                "HTML resource",
                "Managed snapshot",
                $"{DataFormatInspectionText.FormatByteCount(resource.Size)} · {resource.ManagedRelativePath}"));
        }

        var classification = ContentMetadataPolicy.Classifiers.Classify(item);
        foreach (var tag in classification.Tags)
        {
            this.Formats.Add(new DataFormatInspectionEntry(
                this.Formats.Count + 1,
                tag.Id,
                "Classification",
                tag.DisplayName,
                $"Provider: {tag.ProviderId} · Confidence: {tag.Confidence:P0}"));
        }

        foreach (var failure in classification.Failures)
        {
            this.Formats.Add(new DataFormatInspectionEntry(
                this.Formats.Count + 1,
                failure.ProviderId,
                "Classifier",
                "Provider failed",
                failure.Error));
        }

        var metadataComposition = ContentMetadataPolicy.Providers.Compose(item);
        foreach (var failure in metadataComposition.Failures)
        {
            this.Formats.Add(new DataFormatInspectionEntry(
                this.Formats.Count + 1,
                failure.ProviderId,
                "Metadata",
                "Provider failed",
                failure.Error));
        }

        _ = this.AppendThumbnailInspectionAsync(item, generation);

        this.EmptyState.Visibility = this.Formats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        this.ClearButton.IsEnabled = this.Formats.Count > 0;
    }

    private async void OnInspectClipboardClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await this.InspectAsync(Clipboard.GetContent(), "Clipboard");
        }
        catch (Exception exception)
        {
            this.ShowError("The clipboard could not be read.", exception);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs args) => this.ClearInspection();

    private void OnDragEnter(object sender, DragEventArgs args) => this.PrepareDrag(args);

    private void OnDragOver(object sender, DragEventArgs args) => this.PrepareDrag(args);

    private void OnDragLeave(object sender, DragEventArgs args) => this.SetDropTargetActive(false);

    private async void OnDrop(object sender, DragEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            this.SetDropTargetActive(false);
            args.AcceptedOperation = DataPackageOperation.Copy;
            await this.InspectAsync(args.DataView, "Drop");
        }
        catch (Exception exception)
        {
            this.ShowError("The dropped data could not be inspected.", exception);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void PrepareDrag(DragEventArgs args)
    {
        args.AcceptedOperation = DataPackageOperation.Copy;
        args.DragUIOverride.Caption = "Inspect formats";
        args.DragUIOverride.IsCaptionVisible = true;
        args.DragUIOverride.IsContentVisible = true;
        args.DragUIOverride.IsGlyphVisible = true;
        this.SetDropTargetActive(true);
    }

    private async Task InspectAsync(DataPackageView dataView, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        if (this._isInspecting)
        {
            return;
        }

        this._isInspecting = true;
        var generation = ++this._inspectionGeneration;
        this.InspectClipboardButton.IsEnabled = false;
        this.ClearButton.IsEnabled = false;
        this.InspectionProgress.IsActive = true;
        this.StatusBar.IsOpen = false;
        this.Formats.Clear();
        this.EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            var formatIds = dataView.AvailableFormats.ToArray();
            this.SourceSummaryText.Text =
                $"{sourceName} · {formatIds.Length} format{(formatIds.Length == 1 ? string.Empty : "s")} · {DateTime.Now:T}";
            this.PackagePropertiesText.Text = DescribePackage(dataView);

            for (var index = 0; index < formatIds.Length; index++)
            {
                this.Formats.Add(new DataFormatInspectionEntry(
                    index + 1,
                    formatIds[index],
                    ClassifyFormat(formatIds[index]),
                    "Pending",
                    "Waiting for the source provider…"));
            }

            for (var index = 0; index < formatIds.Length; index++)
            {
                var formatId = formatIds[index];
                try
                {
                    var value = await dataView.GetDataAsync(formatId);
                    var payload = await DescribePayloadAsync(value);
                    if (generation != this._inspectionGeneration)
                    {
                        return;
                    }

                    this.Formats[index] = new DataFormatInspectionEntry(
                        index + 1,
                        formatId,
                        ClassifyFormat(formatId),
                        payload.Type,
                        payload.Details);
                }
                catch (Exception exception)
                {
                    if (generation != this._inspectionGeneration)
                    {
                        return;
                    }

                    this.Formats[index] = new DataFormatInspectionEntry(
                        index + 1,
                        formatId,
                        ClassifyFormat(formatId),
                        "Probe failed",
                        $"{exception.GetType().Name} (0x{exception.HResult:X8}): {exception.Message}");
                }
            }

            if (generation != this._inspectionGeneration)
            {
                return;
            }

            if (this.Formats.Count == 0)
            {
                this.EmptyState.Visibility = Visibility.Visible;
                this.StatusBar.Title = "No advertised formats";
                this.StatusBar.Message = "The data package did not advertise any formats.";
                this.StatusBar.Severity = InfoBarSeverity.Warning;
                this.StatusBar.IsOpen = true;
            }
        }
        finally
        {
            if (generation == this._inspectionGeneration)
            {
                this._isInspecting = false;
                this.InspectClipboardButton.IsEnabled = true;
                this.ClearButton.IsEnabled = this.Formats.Count > 0;
                this.InspectionProgress.IsActive = false;
            }
        }
    }

    private void ClearInspection()
    {
        if (this._isInspecting)
        {
            return;
        }

        this._inspectionGeneration++;
        this.Formats.Clear();
        this.SourceSummaryText.Text = "Nothing inspected yet";
        this.PackagePropertiesText.Text = "Drop content above or inspect the clipboard.";
        this.StatusBar.IsOpen = false;
        this.ClearButton.IsEnabled = false;
        this.EmptyState.Visibility = Visibility.Visible;
    }

    private async Task AppendThumbnailInspectionAsync(DropItem item, int generation)
    {
        try
        {
            var resolution = await ContentThumbnailRegistry.Default.ResolveAsync(
                item,
                new ContentThumbnailRequest());
            if (generation != this._inspectionGeneration)
            {
                return;
            }

            if (resolution.Thumbnail is { } thumbnail)
            {
                var fallback = thumbnail.IsFallback ? " · fallback" : string.Empty;
                var cacheKey = string.IsNullOrWhiteSpace(thumbnail.CacheKey)
                    ? string.Empty
                    : $" · Cache key: {thumbnail.CacheKey}";
                this.Formats.Add(new DataFormatInspectionEntry(
                    this.Formats.Count + 1,
                    thumbnail.ProviderId,
                    "Thumbnail",
                    thumbnail.Kind.ToString(),
                    $"{thumbnail.AccessibleLabel}{fallback}{cacheKey}"));
            }

            foreach (var failure in resolution.Failures)
            {
                this.Formats.Add(new DataFormatInspectionEntry(
                    this.Formats.Count + 1,
                    failure.ProviderId,
                    "Thumbnail",
                    "Provider failed",
                    failure.Error));
            }

            this.EmptyState.Visibility = this.Formats.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            this.ClearButton.IsEnabled = this.Formats.Count > 0;
        }
        catch (Exception exception)
        {
            if (generation == this._inspectionGeneration)
            {
                this.Formats.Add(new DataFormatInspectionEntry(
                    this.Formats.Count + 1,
                    "omnitray.thumbnail-resolution",
                    "Thumbnail",
                    "Resolution failed",
                    $"{exception.GetType().Name}: {exception.Message}"));
            }
        }
    }

    private void ShowError(string title, Exception exception)
    {
        this.SetDropTargetActive(false);
        this.StatusBar.Title = title;
        this.StatusBar.Message = $"{exception.GetType().Name} (0x{exception.HResult:X8}): {exception.Message}";
        this.StatusBar.Severity = InfoBarSeverity.Error;
        this.StatusBar.IsOpen = true;
        this.EmptyState.Visibility = this.Formats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetDropTargetActive(bool active)
    {
        this.DropTarget.BorderThickness = active ? new Thickness(2) : new Thickness(1);
        this.DropTargetTitle.Text = active ? "Release to inspect every advertised format" : "Drop anything here";
        this.DropTargetIcon.Opacity = active ? 1 : 0.72;
    }

    private static string ClassifyFormat(string formatId) =>
        formatId.StartsWith("application/x-omnitray-", StringComparison.OrdinalIgnoreCase)
            ? "OmniTray"
            : StandardFormatIds.Contains(formatId)
                ? "Standard"
                : "Custom";

    private static string DescribePackage(DataPackageView dataView)
    {
        var properties = dataView.Properties;
        var details = new List<string>
        {
            $"Requested operation: {dataView.RequestedOperation}"
        };
        AddProperty(details, "Application", properties.ApplicationName);
        AddProperty(details, "Package", properties.PackageFamilyName);
        AddProperty(details, "Title", properties.Title);
        AddProperty(details, "Description", properties.Description);
        AddProperty(details, "Source URL", properties.ContentSourceWebLink?.AbsoluteUri);
        AddProperty(details, "Source app link", properties.ContentSourceApplicationLink?.AbsoluteUri);
        return string.Join(" · ", details);
    }

    private static string DescribeStoredItem(DropItem item)
    {
        var details = new List<string>();
        if (item.Capture is { } capture)
        {
            details.Add($"Capture: {capture.CaptureId:D}");
            details.Add($"Item: {capture.Ordinal + 1}");
            details.Add($"Requested operation: {capture.RequestedOperation}");
        }

        AddProperty(details, "Application", item.SourceApplicationName);
        AddProperty(details, "Package", item.SourcePackageFamilyName);
        AddProperty(details, "Source URL", item.SourceUrl);
        AddProperty(details, "Source app link", item.SourceApplicationLink);
        details.Add($"Backing: {item.Backing.Kind}");
        AddProperty(details, "Path", item.Backing.Path);
        if (item.FileFacts is { } facts)
        {
            AddProperty(details, "Original name", facts.OriginalFileName);
            AddProperty(details, "Content type", facts.ContentType);
            if (facts.Size is { } size)
            {
                details.Add($"Size: {DataFormatInspectionText.FormatByteCount(size)}");
            }

            if (facts.ModifiedAt is { } modifiedAt)
            {
                details.Add($"Modified: {modifiedAt.LocalDateTime:g}");
            }
        }

        return details.Count == 0 ? "No stored metadata is available." : string.Join(" · ", details);
    }

    private static IReadOnlyList<DataFormatInventoryEntry> CreateStoredFormatInventory(DropItem item)
    {
        var formats = new List<DataFormatInventoryEntry>();
        var metadata = ContentMetadataPolicy.GetMetadata(item);
        void Add(string formatId, string detail) => formats.Add(new DataFormatInventoryEntry
        {
            FormatId = formatId,
            Status = DataFormatReadStatus.Succeeded,
            Detail = detail
        });

        if (item.Text is not null)
        {
            Add(StandardDataFormats.Text, $"{item.Text.Length:N0} characters");
        }

        if (item.Html is not null)
        {
            Add(StandardDataFormats.Html, $"{item.Html.Length:N0} characters");
        }

        if (item.Rtf is not null)
        {
            Add(StandardDataFormats.Rtf, $"{item.Rtf.Length:N0} characters");
        }

        if (item.Kind == DropItemKind.Uri && item.Url is not null)
        {
            Add(StandardDataFormats.WebLink, item.Url);
        }

        if (item.ApplicationLink is not null)
        {
            Add(StandardDataFormats.ApplicationLink, item.ApplicationLink);
        }

        if (metadata.Representations.HasFlag(ContentRepresentations.StorageItem))
        {
            Add(StandardDataFormats.StorageItems, item.SourcePath ?? "Stored item");
        }

        if (metadata.Representations.HasFlag(ContentRepresentations.Bitmap))
        {
            Add(StandardDataFormats.Bitmap, item.SourcePath ?? "Stored bitmap");
        }

        foreach (var format in item.CustomFormats)
        {
            Add(
                format.FormatId,
                format.Kind == DropItemDataFormatKind.Text
                    ? $"{format.Text?.Length ?? 0:N0} characters"
                    : DataFormatInspectionText.FormatByteCount((ulong)format.GetBinaryData().Length));
        }

        return formats;
    }

    private static void AddProperty(ICollection<string> details, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            details.Add($"{name}: {value.Trim()}");
        }
    }

    private static async Task<(string Type, string Details)> DescribePayloadAsync(object? value)
    {
        switch (value)
        {
            case null:
                return ("Null", "The provider returned no value.");
            case string text:
                return ("String", $"{text.Length:N0} characters · {DataFormatInspectionText.CreatePreview(text)}");
            case Uri uri:
                return ("URI", uri.AbsoluteUri);
            case IReadOnlyList<IStorageItem> storageItems:
                return ("Storage items", DescribeStorageItems(storageItems));
            case IStorageItem storageItem:
                return (storageItem.GetType().Name, storageItem.Path);
            case IRandomAccessStreamReference streamReference:
                using (var stream = await streamReference.OpenReadAsync())
                {
                    return ("Stream reference", DataFormatInspectionText.FormatByteCount(stream.Size));
                }
            case IRandomAccessStream stream:
                return ("Random-access stream", DataFormatInspectionText.FormatByteCount(stream.Size));
            case IBuffer buffer:
                return ("Buffer", DataFormatInspectionText.FormatByteCount(buffer.Length));
            case byte[] bytes:
                return ("Byte array", DataFormatInspectionText.FormatByteCount((ulong)bytes.LongLength));
            default:
                return (value.GetType().FullName ?? value.GetType().Name, "Opaque value returned by the source.");
        }
    }

    private static string DescribeStorageItems(IReadOnlyList<IStorageItem> items)
    {
        var names = items.Take(4).Select(static item => item.Name).ToArray();
        var suffix = items.Count > names.Length ? $", +{items.Count - names.Length} more" : string.Empty;
        return $"{items.Count:N0} item{(items.Count == 1 ? string.Empty : "s")} · {string.Join(", ", names)}{suffix}";
    }
}

public sealed class DataFormatInspectionEntry
{
    public DataFormatInspectionEntry()
    {
    }

    public DataFormatInspectionEntry(
        int order,
        string formatId,
        string kind,
        string payloadType,
        string details)
    {
        this.Order = order;
        this.FormatId = formatId;
        this.Kind = kind;
        this.PayloadType = payloadType;
        this.Details = details;
    }

    public int Order { get; set; }

    public string FormatId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string PayloadType { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;
}
