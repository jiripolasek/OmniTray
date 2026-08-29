// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.ViewModels;

public sealed partial class OrganizerDetailsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DropItemViewModel? Item { get; private set; }

    [ObservableProperty]
    public partial string StackName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Added { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSize { get; private set; }

    [ObservableProperty]
    public partial string Size { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LocationLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Location { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EmptyTitle { get; internal set; } = "Open a stack";

    [ObservableProperty]
    public partial string EmptyDescription { get; internal set; } = "Double-click a stack to organize its items.";

    internal void SetItem(DropStackViewModel? stack, DropItemViewModel? item)
    {
        this.Item = item;
        this.StackName = stack?.Name ?? string.Empty;
        this.Added = item?.Model.CreatedAt.LocalDateTime.ToString("g") ?? string.Empty;
        this.HasSize = item?.Model.FileFacts?.Size is not null;
        this.Size = item?.Model.FileFacts?.Size is { } size
            ? DataFormatInspectionText.FormatByteCount(size)
            : string.Empty;
        (this.LocationLabel, this.Location) = item is not null ? GetLocation(item.Model) : (string.Empty, string.Empty);
    }

    private static (string Label, string Value) GetLocation(DropItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return ("Path", item.SourcePath);
        }

        if (!string.IsNullOrWhiteSpace(item.Url))
        {
            return ("URL", item.Url);
        }

        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return ("Source", item.SourceUrl);
        }

        if (!string.IsNullOrWhiteSpace(item.ApplicationLink))
        {
            return ("Application link", item.ApplicationLink);
        }

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return ("Preview", DataFormatInspectionText.CreatePreview(item.Text, 240));
        }

        return ("Storage", "Stored in OmniTray");
    }
}
