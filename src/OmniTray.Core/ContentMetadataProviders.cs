// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using OmniTray.Core.MetadataProviders;

namespace OmniTray.Core;

public sealed record ContentMetadataContribution
{
    public ContentRepresentations Representations { get; init; }

    public ContentActions Actions { get; init; }

    public bool HasLocalPath { get; init; }

    public bool HasOriginalPath { get; init; }

    public bool HasImageFile { get; init; }

    public bool HasFile { get; init; }

    public bool HasFolder { get; init; }
}

public sealed record ContentMetadataProviderFailure
{
    public string ProviderId { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;
}

public sealed record ContentMetadataComposition
{
    public ContentMetadataContribution Contribution { get; init; } = new();

    public IReadOnlyList<ContentMetadataProviderFailure> Failures { get; init; } = [];
}

/// <summary>
///     Contributes representations, actions, and backing facts for captured content. Contributions
///     are additive so providers remain independent and registration order cannot remove capabilities.
/// </summary>
public interface IContentMetadataProvider
{
    string Id { get; }

    string DisplayName { get; }

    int Priority { get; }

    ContentMetadataContribution Inspect(ContentInspectionContext context);
}

public sealed record ContentMetadataProviderDescriptor(
    string Id,
    string DisplayName,
    int Priority,
    bool IsEnabled);

public sealed class ContentMetadataProviderRegistry
{
    public event EventHandler? ProvidersChanged;
    private const int MaxProviderCount = 128;
    private const int MaxProviderIdLength = 128;
    private const int MaxProviderDisplayNameLength = 128;
    private const int MaxFailureLength = 512;
    private readonly object _gate = new();
    private readonly List<ProviderRegistration> _registrations = [];
    private long _nextSequence;

    public static ContentMetadataProviderRegistry Default { get; } = CreateDefault();

    public IReadOnlyList<ContentMetadataProviderDescriptor> Providers
    {
        get
        {
            lock (this._gate)
            {
                return this.GetOrderedRegistrations()
                    .Select(static registration => new ContentMetadataProviderDescriptor(
                        registration.Id,
                        registration.DisplayName,
                        registration.Priority,
                        registration.IsEnabled))
                    .ToArray();
            }
        }
    }

    public static ContentMetadataProviderRegistry CreateDefault()
    {
        var registry = new ContentMetadataProviderRegistry();
        foreach (var provider in BuiltInContentMetadataProviders.Create())
        {
            registry.Register(provider);
        }

        return registry;
    }

    public void Register(IContentMetadataProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var providerId = provider.Id?.Trim() ?? string.Empty;
        var displayName = provider.DisplayName?.Trim() ?? string.Empty;
        ValidateProviderId(providerId);
        if (displayName.Length == 0 || displayName.Length > MaxProviderDisplayNameLength)
        {
            throw new ArgumentException(
                $"A metadata provider display name must be between 1 and {MaxProviderDisplayNameLength} characters.",
                nameof(provider));
        }

        lock (this._gate)
        {
            if (this._registrations.Any(registration =>
                    string.Equals(registration.Id, providerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"A content metadata provider with ID '{providerId}' is already registered.");
            }

            if (this._registrations.Count >= MaxProviderCount)
            {
                throw new InvalidOperationException(
                    $"No more than {MaxProviderCount} content metadata providers can be registered.");
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

    public ContentMetadataComposition Compose(DropItem item) =>
        this.Compose(new ContentInspectionContext(item));

    public ContentMetadataComposition Compose(ContentInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ProviderRegistration[] registrations;
        lock (this._gate)
        {
            registrations = this.GetOrderedRegistrations()
                .Where(static registration => registration.IsEnabled)
                .ToArray();
        }

        var representations = ContentRepresentations.None;
        var actions = ContentActions.None;
        var hasLocalPath = false;
        var hasOriginalPath = false;
        var hasImageFile = false;
        var hasFile = false;
        var hasFolder = false;
        var failures = new List<ContentMetadataProviderFailure>();
        foreach (var registration in registrations)
        {
            try
            {
                var contribution = registration.Provider.Inspect(context) ??
                                   throw new InvalidOperationException("The provider returned no contribution.");
                representations |= contribution.Representations;
                actions |= contribution.Actions;
                hasLocalPath |= contribution.HasLocalPath;
                hasOriginalPath |= contribution.HasOriginalPath;
                hasImageFile |= contribution.HasImageFile;
                hasFile |= contribution.HasFile;
                hasFolder |= contribution.HasFolder;
            }
            catch (Exception exception)
            {
                failures.Add(new ContentMetadataProviderFailure
                {
                    ProviderId = registration.Id,
                    Error = Truncate($"{exception.GetType().Name}: {exception.Message}", MaxFailureLength)
                });
            }
        }

        return new ContentMetadataComposition
        {
            Contribution = new ContentMetadataContribution
            {
                Representations = representations,
                Actions = actions,
                HasLocalPath = hasLocalPath,
                HasOriginalPath = hasOriginalPath,
                HasImageFile = hasImageFile,
                HasFile = hasFile,
                HasFolder = hasFolder
            },
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
                $"A metadata provider ID must be non-empty, contain no whitespace, and be at most {MaxProviderIdLength} characters.",
                nameof(providerId));
        }
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : $"{value[..(maximumLength - 1)]}…";

    private sealed class ProviderRegistration(
        IContentMetadataProvider provider,
        string id,
        string displayName,
        int priority,
        long sequence)
    {
        public IContentMetadataProvider Provider { get; } = provider;

        public string Id { get; } = id;

        public string DisplayName { get; } = displayName;

        public int Priority { get; } = priority;

        public long Sequence { get; } = sequence;

        public bool IsEnabled { get; set; } = true;
    }
}
