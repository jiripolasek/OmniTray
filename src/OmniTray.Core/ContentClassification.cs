// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using OmniTray.Core.ClassifierProviders;

namespace OmniTray.Core;

/// <summary>
/// An immutable view of captured content supplied to classifiers. Providers must inspect only
/// this in-memory state and must not perform blocking I/O on the calling thread.
/// </summary>
public sealed class ContentInspectionContext
{
    public ContentInspectionContext(DropItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this.Item = item;
        this.AvailableFormatIds = (item.Capture?.Formats ?? [])
            .Select(static format => format.FormatId)
            .Concat(item.CustomFormats.Select(static format => format.FormatId))
            .Where(static formatId => !string.IsNullOrWhiteSpace(formatId))
            .ToHashSet(StringComparer.Ordinal);
    }

    public DropItem Item { get; }

    public string? Text => this.Item.Text;

    public string? Html => this.Item.Html;

    public string? Rtf => this.Item.Rtf;

    public string? SourcePath => this.Item.SourcePath;

    public string? WebLink => this.Item.Url;

    public string? ApplicationLink => this.Item.ApplicationLink;

    public ContentProvenance Provenance => this.Item.Provenance;

    public ContentBacking Backing => this.Item.Backing;

    public DropFileFacts? FileFacts => this.Item.FileFacts;

    public IReadOnlySet<string> AvailableFormatIds { get; }
}

/// <summary>
/// A stable, opaque tag contributed by a classifier. Third-party tag IDs should be namespaced.
/// </summary>
public sealed record ContentTag
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    public double Confidence { get; init; } = 1d;
}

public sealed record ContentClassifierOutput
{
    public static ContentClassifierOutput Empty { get; } = new();

    public ContentFacets Facets { get; init; }

    public IReadOnlyList<ContentTag> Tags { get; init; } = [];
}

public sealed record ContentClassifierFailure
{
    public string ProviderId { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;
}

public sealed record ContentClassification
{
    public ContentFacets Facets { get; init; }

    public IReadOnlyList<ContentTag> Tags { get; init; } = [];

    public IReadOnlyList<ContentClassifierFailure> Failures { get; init; } = [];
}

/// <summary>
/// A fast, deterministic classifier over content already captured by OmniTray.
/// </summary>
public interface IContentClassifierProvider
{
    /// <summary>A stable, opaque, namespaced provider identifier.</summary>
    string Id { get; }

    /// <summary>A user-facing name suitable for a future provider-management surface.</summary>
    string DisplayName { get; }

    /// <summary>Lower values run first. Registration order breaks ties.</summary>
    int Priority { get; }

    ContentClassifierOutput Classify(ContentInspectionContext context);
}

public sealed record ContentClassifierProviderDescriptor(
    string Id,
    string DisplayName,
    int Priority,
    bool IsEnabled);

/// <summary>
/// Thread-safe registry for built-in and future externally supplied classifiers.
/// Assembly discovery, trust policy, activation, and version compatibility remain host concerns;
/// registering a provider does not load external code by itself.
/// </summary>
public sealed class ContentClassifierRegistry
{
    private const int MaxProviderCount = 128;
    private const int MaxProviderIdLength = 128;
    private const int MaxProviderDisplayNameLength = 128;
    private const int MaxTagsPerProvider = 32;
    private const int MaxTotalTagCount = 128;
    private const int MaxTagIdLength = 128;
    private const int MaxTagDisplayNameLength = 128;
    private const int MaxFailureLength = 512;
    private readonly object _gate = new();
    private readonly List<ProviderRegistration> _registrations = [];
    private long _nextSequence;

    public static ContentClassifierRegistry Default { get; } = CreateDefault();

    public event EventHandler? ProvidersChanged;

    public IReadOnlyList<ContentClassifierProviderDescriptor> Providers
    {
        get
        {
            lock (this._gate)
            {
                return this.GetOrderedRegistrations()
                    .Select(static registration => new ContentClassifierProviderDescriptor(
                        registration.Id,
                        registration.DisplayName,
                        registration.Priority,
                        registration.IsEnabled))
                    .ToArray();
            }
        }
    }

    public static ContentClassifierRegistry CreateDefault()
    {
        var registry = new ContentClassifierRegistry();
        foreach (var provider in BuiltInContentClassifierProviders.Create())
        {
            registry.Register(provider);
        }

        return registry;
    }

    public void Register(IContentClassifierProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var providerId = provider.Id?.Trim() ?? string.Empty;
        var displayName = provider.DisplayName?.Trim() ?? string.Empty;
        var priority = provider.Priority;
        ValidateProviderId(providerId);
        if (displayName.Length == 0)
        {
            throw new ArgumentException(
                "A classifier provider must have a display name.",
                nameof(provider));
        }

        if (displayName.Length > MaxProviderDisplayNameLength)
        {
            throw new ArgumentException(
                $"A classifier provider display name cannot exceed {MaxProviderDisplayNameLength} characters.",
                nameof(provider));
        }

        lock (this._gate)
        {
            if (this._registrations.Any(registration =>
                    string.Equals(registration.Id, providerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"A content classifier with ID '{providerId}' is already registered.");
            }

            if (this._registrations.Count >= MaxProviderCount)
            {
                throw new InvalidOperationException(
                    $"No more than {MaxProviderCount} content classifiers can be registered.");
            }

            this._registrations.Add(new ProviderRegistration(
                provider,
                providerId,
                displayName,
                priority,
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

        return true;
    }

    public ContentClassification Classify(DropItem item) =>
        this.Classify(new ContentInspectionContext(item));

    public ContentClassification Classify(ContentInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ProviderRegistration[] registrations;
        lock (this._gate)
        {
            registrations = this.GetOrderedRegistrations()
                .Where(static registration => registration.IsEnabled)
                .ToArray();
        }

        var facets = ContentFacets.None;
        var tags = new List<ContentTag>();
        var tagIds = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<ContentClassifierFailure>();
        foreach (var registration in registrations)
        {
            var provider = registration.Provider;
            try
            {
                var output = provider.Classify(context) ??
                             throw new InvalidOperationException("The provider returned no result.");
                var providerTags = output.Tags ??
                                   throw new InvalidOperationException("The provider returned no tag collection.");
                var providerTagCount = Math.Min(providerTags.Count, MaxTagsPerProvider);
                var normalizedTags = new List<ContentTag>(providerTagCount);
                for (var index = 0; index < providerTagCount; index++)
                {
                    var tag = providerTags[index];
                    if (string.IsNullOrWhiteSpace(tag.Id) ||
                        string.IsNullOrWhiteSpace(tag.DisplayName) ||
                        tag.Id.Trim().Length > MaxTagIdLength ||
                        tag.DisplayName.Trim().Length > MaxTagDisplayNameLength)
                    {
                        continue;
                    }

                    normalizedTags.Add(tag with
                    {
                        Id = tag.Id.Trim(),
                        DisplayName = tag.DisplayName.Trim(),
                        ProviderId = registration.Id,
                        Confidence = double.IsFinite(tag.Confidence)
                            ? Math.Clamp(tag.Confidence, 0d, 1d)
                            : 0d
                    });
                }

                facets |= output.Facets;
                foreach (var tag in normalizedTags)
                {
                    if (tags.Count < MaxTotalTagCount && tagIds.Add(tag.Id))
                    {
                        tags.Add(tag);
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add(new ContentClassifierFailure
                {
                    ProviderId = registration.Id,
                    Error = Truncate(
                        $"{exception.GetType().Name}: {exception.Message}",
                        MaxFailureLength)
                });
            }
        }

        return new ContentClassification
        {
            Facets = facets,
            Tags = tags,
            Failures = failures
        };
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
                $"A classifier provider ID must be non-empty, contain no whitespace, and be at most {MaxProviderIdLength} characters.",
                nameof(providerId));
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)]}…";

    private sealed class ProviderRegistration(
        IContentClassifierProvider provider,
        string id,
        string displayName,
        int priority,
        long sequence)
    {
        public IContentClassifierProvider Provider { get; } = provider;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public int Priority { get; } = priority;

        public long Sequence { get; } = sequence;

        public bool IsEnabled { get; set; } = true;
    }
}
