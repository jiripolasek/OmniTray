// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum CaptureChannel
{
    Drag,
    Clipboard
}

[Flags]
public enum CaptureRequestedOperation
{
    None = 0,
    Copy = 1 << 0,
    Move = 1 << 1,
    Link = 1 << 2
}

public enum DataFormatReadStatus
{
    Advertised,
    Succeeded,
    Failed,
    Skipped
}

public sealed record DataFormatInventoryEntry
{
    public string FormatId { get; init; } = string.Empty;

    public DataFormatReadStatus Status { get; init; }

    public string? Detail { get; init; }
}

public sealed record DropCaptureMetadata
{
    public Guid CaptureId { get; init; }

    public CaptureChannel Channel { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public int Ordinal { get; init; }

    public CaptureRequestedOperation RequestedOperation { get; init; }

    public IReadOnlyList<DataFormatInventoryEntry> Formats { get; init; } = [];
}

public sealed record ContentProvenance
{
    public string? ApplicationName { get; init; }

    public string? PackageFamilyName { get; init; }

    public string? SourceWebLink { get; init; }

    public string? SourceApplicationLink { get; init; }
}

public enum ContentBackingKind
{
    None,
    OriginalPath,
    ManagedSnapshot,
    VirtualFileMaterialization,
    GeneratedProjection
}

public sealed record ContentBacking
{
    public ContentBackingKind Kind { get; init; }

    public string? Path { get; init; }
}

public sealed record DropFileFacts
{
    public string OriginalFileName { get; init; } = string.Empty;

    public string? ContentType { get; init; }

    public ulong? Size { get; init; }

    public DateTimeOffset? ModifiedAt { get; init; }

    public string? Sha256 { get; init; }
}

public sealed record DropItemHtmlResource
{
    public string ResourceKey { get; init; } = string.Empty;

    public string ManagedRelativePath { get; init; } = string.Empty;

    public ulong Size { get; init; }
}
