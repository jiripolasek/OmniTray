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
                    this.Formats[index] = new DataFormatInspectionEntry(
                        index + 1,
                        formatId,
                        ClassifyFormat(formatId),
                        payload.Type,
                        payload.Details);
                }
                catch (Exception exception)
                {
                    this.Formats[index] = new DataFormatInspectionEntry(
                        index + 1,
                        formatId,
                        ClassifyFormat(formatId),
                        "Probe failed",
                        $"{exception.GetType().Name} (0x{exception.HResult:X8}): {exception.Message}");
                }
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
            this._isInspecting = false;
            this.InspectClipboardButton.IsEnabled = true;
            this.ClearButton.IsEnabled = this.Formats.Count > 0;
            this.InspectionProgress.IsActive = false;
        }
    }

    private void ClearInspection()
    {
        if (this._isInspecting)
        {
            return;
        }

        this.Formats.Clear();
        this.SourceSummaryText.Text = "Nothing inspected yet";
        this.PackagePropertiesText.Text = "Drop content above or inspect the clipboard.";
        this.StatusBar.IsOpen = false;
        this.ClearButton.IsEnabled = false;
        this.EmptyState.Visibility = Visibility.Visible;
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
