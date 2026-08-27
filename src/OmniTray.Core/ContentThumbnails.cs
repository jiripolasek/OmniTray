// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using OmniTray.Core.ThumbnailProviders;

namespace OmniTray.Core;

public enum ContentThumbnailKind
{
    Glyph,
    ColorSwatch,
    EncodedImage
}

public enum ContentThumbnailChrome
{
    Default,
    None
}

public enum ContentThumbnailTheme
{
    Default,
    Light,
    Dark,
    HighContrast
}

public sealed record ContentThumbnailRequest
{
    public uint PixelSize { get; init; } = 120;

    public double RasterScale { get; init; } = 1d;

    public ContentThumbnailTheme Theme { get; init; }
}

public sealed class ContentThumbnailContext
{
    public ContentThumbnailContext(
        DropItem item,
        ContentMetadata metadata,
        ContentThumbnailRequest request)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);
        if (request.PixelSize is < 16 or > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Thumbnail size must be between 16 and 1024 pixels.");
        }

        if (!double.IsFinite(request.RasterScale) || request.RasterScale is < 0.5d or > 8d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Thumbnail raster scale must be between 0.5 and 8.");
        }

        this.Item = item;
        this.Metadata = metadata;
        this.Request = request;
    }

    public DropItem Item { get; }

    public ContentMetadata Metadata { get; }

    public ContentThumbnailRequest Request { get; }
}

public sealed record ContentThumbnailDescriptor
{
    public string ProviderId { get; init; } = string.Empty;

    public ContentThumbnailKind Kind { get; init; }

    public ContentThumbnailChrome Chrome { get; init; }

    public string? Glyph { get; init; }

    public string? Color { get; init; }

    public string? MediaType { get; init; }

    public byte[]? EncodedData { get; init; }

    public string AccessibleLabel { get; init; } = string.Empty;

    public string CacheKey { get; init; } = string.Empty;

    public bool IsFallback { get; init; }

    public static ContentThumbnailDescriptor CreateGlyph(
        string glyph,
        string accessibleLabel,
        string? cacheKey = null) =>
        new()
        {
            Kind = ContentThumbnailKind.Glyph,
            Glyph = glyph,
            AccessibleLabel = accessibleLabel,
            CacheKey = cacheKey ?? string.Empty
        };

    public static ContentThumbnailDescriptor CreateColorSwatch(
        string color,
        string accessibleLabel,
        string? cacheKey = null,
        string? glyph = null) =>
        new()
        {
            Kind = ContentThumbnailKind.ColorSwatch,
            Chrome = ContentThumbnailChrome.None,
            Color = color,
            Glyph = glyph,
            AccessibleLabel = accessibleLabel,
            CacheKey = cacheKey ?? string.Empty
        };
}

public sealed record ContentThumbnailProviderFailure
{
    public string ProviderId { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;
}

public sealed record ContentThumbnailResolution
{
    public ContentThumbnailDescriptor? Thumbnail { get; init; }

    public IReadOnlyList<ContentThumbnailProviderFailure> Failures { get; init; } = [];
}

/// <summary>
/// Stable fallback presentation for OmniTray's intentionally small primary visual taxonomy.
/// Extensible metadata thumbnails resolve before this fallback.
/// </summary>
public static class ContentThumbnailFallback
{
    public static ContentThumbnailDescriptor For(DropItemKind kind)
    {
        var (glyph, label) = kind switch
        {
            DropItemKind.File => ("\uE8A5", "File"),
            DropItemKind.Folder => ("\uE8B7", "Folder"),
            DropItemKind.Text => ("\uE8D2", "Text"),
            DropItemKind.Image => ("\uEB9F", "Image"),
            DropItemKind.Uri => ("\uE71B", "Link"),
            _ => ("\uE7B8", "Content")
        };
        return ContentThumbnailDescriptor.CreateGlyph(glyph, label, kind.ToString()) with
        {
            IsFallback = true
        };
    }
}

public interface IContentThumbnailProvider
{
    string Id { get; }

    string DisplayName { get; }

    int Priority { get; }

    IReadOnlyList<ContentRequirement> Requirements { get; }

    ValueTask<ContentThumbnailDescriptor?> CreateAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken);
}

public sealed record ContentThumbnailProviderDescriptor(
    string Id,
    string DisplayName,
    int Priority,
    bool IsEnabled);

public sealed class ContentThumbnailRegistry
{
    private const int MaxProviderCount = 128;
    private const int MaxProviderIdLength = 128;
    private const int MaxProviderDisplayNameLength = 128;
    private const int MaxFailureLength = 512;
    private const int MaxEncodedImageBytes = 4 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly List<ProviderRegistration> _registrations = [];
    private long _nextSequence;

    public static ContentThumbnailRegistry Default { get; } = CreateDefault();

    public event EventHandler? ProvidersChanged;

    public IReadOnlyList<ContentThumbnailProviderDescriptor> Providers
    {
        get
        {
            lock (this._gate)
            {
                return this.GetOrderedRegistrations()
                    .Select(static registration => new ContentThumbnailProviderDescriptor(
                        registration.Id,
                        registration.DisplayName,
                        registration.Priority,
                        registration.IsEnabled))
                    .ToArray();
            }
        }
    }

    public static ContentThumbnailRegistry CreateDefault()
    {
        var registry = new ContentThumbnailRegistry();
        foreach (var provider in BuiltInContentThumbnailProviders.Create())
        {
            registry.Register(provider);
        }

        return registry;
    }

    public void Register(IContentThumbnailProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var providerId = provider.Id?.Trim() ?? string.Empty;
        var displayName = provider.DisplayName?.Trim() ?? string.Empty;
        ValidateProviderId(providerId);
        if (displayName.Length == 0 || displayName.Length > MaxProviderDisplayNameLength)
        {
            throw new ArgumentException(
                $"A thumbnail provider display name must be between 1 and {MaxProviderDisplayNameLength} characters.",
                nameof(provider));
        }

        lock (this._gate)
        {
            if (this._registrations.Any(registration =>
                    string.Equals(registration.Id, providerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"A content thumbnail provider with ID '{providerId}' is already registered.");
            }

            if (this._registrations.Count >= MaxProviderCount)
            {
                throw new InvalidOperationException(
                    $"No more than {MaxProviderCount} content thumbnail providers can be registered.");
            }

            this._registrations.Add(new ProviderRegistration(
                provider,
                providerId,
                displayName,
                provider.Priority,
                this._nextSequence++));
        }

        this.ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(string providerId)
    {
        ValidateProviderId(providerId);
        var removed = false;
        lock (this._gate)
        {
            var index = this._registrations.FindIndex(registration =>
                string.Equals(registration.Id, providerId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            this._registrations.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            this.ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public bool SetEnabled(string providerId, bool isEnabled)
    {
        ValidateProviderId(providerId);
        var changed = false;
        lock (this._gate)
        {
            var registration = this._registrations.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, providerId, StringComparison.Ordinal));
            if (registration is null)
            {
                return false;
            }

            if (registration.IsEnabled != isEnabled)
            {
                registration.IsEnabled = isEnabled;
                changed = true;
            }
        }

        if (changed)
        {
            this.ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    public ValueTask<ContentThumbnailResolution> ResolveAsync(
        DropItem item,
        ContentThumbnailRequest request,
        CancellationToken cancellationToken = default) =>
        this.ResolveAsync(
            new ContentThumbnailContext(item, ContentMetadataPolicy.GetMetadata(item), request),
            cancellationToken);

    public async ValueTask<ContentThumbnailResolution> ResolveAsync(
        ContentThumbnailContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ProviderRegistration[] registrations;
        lock (this._gate)
        {
            registrations = this.GetOrderedRegistrations()
                .Where(static registration => registration.IsEnabled)
                .ToArray();
        }

        var failures = new List<ContentThumbnailProviderFailure>();
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = registration.Provider;
            try
            {
                var requirements = provider.Requirements ??
                                   throw new InvalidOperationException("The provider returned no requirements.");
                if (!ContentRequirements.AreSatisfiedBy(requirements, [context.Metadata], out _))
                {
                    continue;
                }

                var thumbnail = await provider.CreateAsync(context, cancellationToken);
                if (thumbnail is null)
                {
                    continue;
                }

                ValidateThumbnail(thumbnail);
                return new ContentThumbnailResolution
                {
                    Thumbnail = thumbnail with
                    {
                        ProviderId = registration.Id,
                        EncodedData = thumbnail.EncodedData?.ToArray()
                    },
                    Failures = failures
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new ContentThumbnailProviderFailure
                {
                    ProviderId = registration.Id,
                    Error = Truncate($"{exception.GetType().Name}: {exception.Message}", MaxFailureLength)
                });
            }
        }

        return new ContentThumbnailResolution { Failures = failures };
    }

    private ProviderRegistration[] GetOrderedRegistrations() => this._registrations
        .OrderBy(static registration => registration.Priority)
        .ThenBy(static registration => registration.Sequence)
        .ToArray();

    private static void ValidateProviderId(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) ||
            providerId.Length > MaxProviderIdLength ||
            providerId.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                $"A thumbnail provider ID must be non-empty, contain no whitespace, and be at most {MaxProviderIdLength} characters.",
                nameof(providerId));
        }
    }

    private static void ValidateThumbnail(ContentThumbnailDescriptor thumbnail)
    {
        if (string.IsNullOrWhiteSpace(thumbnail.AccessibleLabel) ||
            thumbnail.AccessibleLabel.Length > 256)
        {
            throw new InvalidOperationException(
                "A thumbnail must have an accessible label no longer than 256 characters.");
        }

        if (thumbnail.CacheKey.Length > 256)
        {
            throw new InvalidOperationException("A thumbnail cache key cannot exceed 256 characters.");
        }

        if (thumbnail.Chrome is not ContentThumbnailChrome.Default and not ContentThumbnailChrome.None)
        {
            throw new InvalidOperationException("The thumbnail chrome mode is not supported.");
        }

        switch (thumbnail.Kind)
        {
            case ContentThumbnailKind.Glyph
                when string.IsNullOrWhiteSpace(thumbnail.Glyph) || thumbnail.Glyph.Length > 8:
                throw new InvalidOperationException(
                    "A glyph thumbnail must contain a glyph no longer than 8 UTF-16 code units.");
            case ContentThumbnailKind.ColorSwatch when !IsHexColor(thumbnail.Color):
                throw new InvalidOperationException("A color swatch must contain a hexadecimal RGB or RGBA color.");
            case ContentThumbnailKind.EncodedImage
                when thumbnail.EncodedData is not { Length: > 0 } data ||
                     data.Length > MaxEncodedImageBytes ||
                     !IsSupportedMediaType(thumbnail.MediaType):
                throw new InvalidOperationException(
                    $"An encoded thumbnail must be a PNG or JPEG no larger than {MaxEncodedImageBytes} bytes.");
            case not ContentThumbnailKind.Glyph and
                not ContentThumbnailKind.ColorSwatch and
                not ContentThumbnailKind.EncodedImage:
                throw new InvalidOperationException("The thumbnail kind is not supported.");
        }
    }

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
        {
            return false;
        }

        var digits = value.AsSpan(1);
        return digits.Length is 3 or 4 or 6 or 8 && digits.ToString().All(Uri.IsHexDigit);
    }

    private static bool IsSupportedMediaType(string? mediaType) =>
        string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)]}…";

    private sealed class ProviderRegistration(
        IContentThumbnailProvider provider,
        string id,
        string displayName,
        int priority,
        long sequence)
    {
        public IContentThumbnailProvider Provider { get; } = provider;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public int Priority { get; } = priority;

        public long Sequence { get; } = sequence;

        public bool IsEnabled { get; set; } = true;
    }
}
