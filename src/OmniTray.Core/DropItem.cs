// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum DropItemKind
{
    File,
    Folder,
    Text,
    Image
}

public sealed record DropItem
{
    private DropItem(
        Guid id,
        DropItemKind kind,
        string displayName,
        string? sourcePath,
        string? text,
        bool isOwned,
        DateTimeOffset createdAt)
    {
        this.Id = id;
        this.Kind = kind;
        this.DisplayName = displayName;
        this.SourcePath = sourcePath;
        this.Text = text;
        this.IsOwned = isOwned;
        this.CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public DropItemKind Kind { get; }

    public string DisplayName { get; }

    public string? SourcePath { get; }

    public string? Text { get; }

    public bool IsOwned { get; }

    public DateTimeOffset CreatedAt { get; }

    public static DropItem CreateStorageItem(
        string displayName,
        string? sourcePath,
        bool isFolder,
        bool isOwned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new DropItem(
            Guid.NewGuid(),
            isFolder ? DropItemKind.Folder : DropItemKind.File,
            displayName.Trim(),
            sourcePath,
            null,
            isOwned,
            DateTimeOffset.UtcNow);
    }

    public static DropItem CreateText(
        string text,
        string? sourcePath = null,
        bool isOwned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (isOwned)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        }

        var normalized = string.Join(' ', text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
        var displayName = normalized.Length <= 48 ? normalized : $"{normalized[..47]}…";

        return new DropItem(
            Guid.NewGuid(),
            DropItemKind.Text,
            displayName,
            sourcePath,
            text,
            isOwned,
            DateTimeOffset.UtcNow);
    }

    public static DropItem CreateImage(string displayName, string sourcePath, bool isOwned = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return new DropItem(
            Guid.NewGuid(),
            DropItemKind.Image,
            displayName.Trim(),
            sourcePath,
            null,
            isOwned,
            DateTimeOffset.UtcNow);
    }

    public static DropItem Restore(
        Guid id,
        DropItemKind kind,
        string displayName,
        string? sourcePath,
        string? text,
        bool isOwned,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An item ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (kind == DropItemKind.Text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(text);
        }

        if (kind == DropItemKind.Image)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        }

        return new DropItem(
            id,
            kind,
            displayName.Trim(),
            sourcePath,
            text,
            isOwned,
            createdAt);
    }
}
