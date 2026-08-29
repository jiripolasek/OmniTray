// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Services;

internal sealed record DropCommandTemplateDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string Glyph,
    IReadOnlyList<ContentRequirement> Requirements,
    Func<DropCommandInstance, IReadOnlyList<ContentRequirement>>? ResolveRequirements,
    bool ConfiguresApplication,
    bool RequiresDestinationFolder,
    Func<DropCommandInstance, DropCommandConfirmationContext, DropCommandConfirmationRequest>?
        CreateConfirmation,
    Func<DropCommandInstance, DropCommandInput, string?> ValidateInput,
    Func<DropCommandExecutionService, DropCommandInstance, DropCommandInput, nint, Task<DropCommandExecutionResult>>
        ExecuteAsync);

internal sealed record DropCommandConfirmationContext(int ItemCount, bool IsFromStack);

internal sealed record DropCommandConfirmationRequest(
    string Title,
    string Message,
    string PrimaryButtonText);

internal sealed class DropCommandProviderRegistry
{
    public event EventHandler? ProvidersChanged;
    private readonly object _gate = new();

    private readonly Dictionary<string, DropCommandTemplateDescriptor> _providers =
        new(StringComparer.Ordinal);

    public IReadOnlyList<DropCommandTemplateDescriptor> Providers
    {
        get
        {
            lock (this._gate)
            {
                return this._providers.Values.ToArray();
            }
        }
    }

    public void Register(DropCommandTemplateDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Id))
        {
            throw new ArgumentException("A command provider ID is required.", nameof(provider));
        }

        lock (this._gate)
        {
            if (!this._providers.TryAdd(provider.Id.Trim(), provider))
            {
                throw new InvalidOperationException(
                    $"A drop command provider with ID '{provider.Id}' is already registered.");
            }
        }

        this.ProvidersChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool Unregister(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("A command provider ID is required.", nameof(providerId));
        }

        bool removed;
        lock (this._gate)
        {
            removed = this._providers.Remove(providerId.Trim());
        }

        if (removed)
        {
            this.ProvidersChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public bool TryGet(string providerId, out DropCommandTemplateDescriptor provider)
    {
        lock (this._gate)
        {
            return this._providers.TryGetValue(providerId, out provider!);
        }
    }
}

internal static class DropCommandTemplates
{
    private static readonly IReadOnlyList<ContentRequirement> DesktopAppRequirements =
    [
        ContentRequirement.All(ContentProperty.HasLocalPath),
        ContentRequirement.All(ContentProperty.HasStorageItem)
    ];

    private static readonly IReadOnlyList<ContentRequirement> PackagedAppRequirements =
    [
        ContentRequirement.All(ContentProperty.HasLocalPath),
        ContentRequirement.All(ContentProperty.HasFile, ContentProperty.HasImageFile)
    ];

    public static DropCommandProviderRegistry Registry { get; } = CreateDefaultRegistry();

    public static IReadOnlyList<DropCommandTemplateDescriptor> All => Registry.Providers;

    public static bool TryGet(string templateId, out DropCommandTemplateDescriptor descriptor) =>
        Registry.TryGet(templateId, out descriptor);

    public static DropCommandTemplateDescriptor? Get(string templateId) =>
        TryGet(templateId, out var descriptor) ? descriptor : null;

    private static DropCommandProviderRegistry CreateDefaultRegistry()
    {
        var registry = new DropCommandProviderRegistry();
        foreach (var provider in CreateBuiltInProviders())
        {
            registry.Register(provider);
        }

        return registry;
    }

    private static IReadOnlyList<DropCommandTemplateDescriptor> CreateBuiltInProviders() =>
    [
        new(
            DropCommandTemplateIds.OpenInApp,
            "Open in app",
            "Open dropped content in a desktop executable or an installed packaged app.",
            "\uE8A7",
            DesktopAppRequirements,
            ResolveOpenAppRequirements,
            true,
            false,
            null,
            static (command, input) => DropCommandExecutionService.ValidateOpenInAppInput(command, input),
            static (service, command, input, _) => service.OpenInAppAsync(command, input)),
        new(
            DropCommandTemplateIds.CopyToFolder,
            "Copy to folder",
            "Copy dropped files, folders, and images to a chosen folder.",
            "\uE8B0",
            [ContentRequirement.All(ContentProperty.HasStorageItem, ContentProperty.HasBitmap)],
            null,
            false,
            true,
            null,
            static (command, input) => DropCommandExecutionService.ValidateTransferInput(command, input, false),
            static (service, command, input, _) => service.TransferToFolderAsync(command, input, false)),
        new(
            DropCommandTemplateIds.MoveToFolder,
            "Move to folder",
            "Move original files, folders, and images to a chosen folder.",
            "\uE8DE",
            [
                ContentRequirement.All(ContentProperty.HasOriginalPath),
                ContentRequirement.All(ContentProperty.HasStorageItem)
            ],
            null,
            false,
            true,
            static (command, context) => new DropCommandConfirmationRequest(
                command.DisplayName,
                context.IsFromStack
                    ? $"Move {context.ItemCount} {GetItemNoun(context.ItemCount)} using “{command.DisplayName}”? Successful items will be removed from their OmniTray stack."
                    : $"Move {context.ItemCount} original {GetItemNoun(context.ItemCount)} using “{command.DisplayName}”?",
                "Move"),
            static (command, input) => DropCommandExecutionService.ValidateTransferInput(command, input, true),
            static (service, command, input, _) => service.TransferToFolderAsync(command, input, true)),
        new(
            DropCommandTemplateIds.Recycle,
            "Recycle",
            "Send original files and folders to the Windows Recycle Bin.",
            "\uE74D",
            [
                ContentRequirement.All(ContentProperty.HasOriginalPath),
                ContentRequirement.All(ContentProperty.HasStorageItem)
            ],
            null,
            false,
            false,
            static (command, context) => new DropCommandConfirmationRequest(
                command.DisplayName,
                $"Send {context.ItemCount} {GetItemNoun(context.ItemCount)} to the Windows Recycle Bin?",
                "Recycle"),
            static (_, input) => DropCommandExecutionService.ValidateRecycleInput(input),
            static (service, _, input, _) => service.RecycleAsync(input)),
        new(
            DropCommandTemplateIds.CopyToClipboard,
            "Copy to clipboard",
            "Put the dropped content on the Windows clipboard.",
            "\uE8C8",
            [ContentRequirement.All(ContentProperty.CanCopy)],
            null,
            false,
            false,
            null,
            static (_, _) => null,
            static (service, command, input, _) => service.CopyToClipboard(command, input)),
        new(
            DropCommandTemplateIds.Share,
            "Share",
            "Share dropped content using the Windows share sheet.",
            "\uE72D",
            [ContentRequirement.All(ContentProperty.CanShare)],
            null,
            false,
            false,
            null,
            static (_, input) => DropCommandExecutionService.ValidateShareInput(input),
            static (service, command, input, ownerHwnd) => service.ShareAsync(
                command,
                input,
                ownerHwnd))
    ];

    public static DropCommandInstance CreateInstance(string templateId)
    {
        var template = Get(templateId) ??
                       throw new ArgumentException("The command template is not available.", nameof(templateId));
        IReadOnlyDictionary<string, string>? parameters = template.ConfiguresApplication
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [DropCommandParameterNames.ApplicationTarget] =
                    DropCommandApplicationTargetIds.DesktopExecutable
            }
            : null;
        return DropCommandInstance.Create(
            template.Id,
            template.DisplayName,
            parameters);
    }

    public static DropCommandConfirmationRequest? CreateConfirmation(
        DropCommandInstance instance,
        DropCommandConfirmationContext context)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(context.ItemCount);
        return Get(instance.TemplateId)?.CreateConfirmation?.Invoke(instance, context);
    }

    public static string GetSummary(DropCommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!TryGet(instance.TemplateId, out var template))
        {
            return "Command template unavailable";
        }

        if (template.ConfiguresApplication)
        {
            return GetApplicationTargetId(instance) switch
            {
                DropCommandApplicationTargetIds.DesktopExecutable =>
                    TryGetParameter(instance, DropCommandParameterNames.ExecutablePath, out var executable)
                        ? Path.GetFileNameWithoutExtension(executable)
                        : "Choose a desktop application",
                DropCommandApplicationTargetIds.PackagedApp =>
                    TryGetParameter(instance, DropCommandParameterNames.PackagedAppDisplayName, out var displayName)
                        ? displayName
                        : TryGetParameter(instance, DropCommandParameterNames.AppUserModelId, out var appUserModelId)
                            ? appUserModelId
                            : "Choose a packaged application",
                _ => "Application target unavailable"
            };
        }

        if (template.RequiresDestinationFolder)
        {
            return TryGetParameter(instance, DropCommandParameterNames.DestinationFolder, out var destination)
                ? destination
                : "Choose a destination folder";
        }

        return template.Description;
    }

    public static IReadOnlyList<ContentRequirement> GetRequirements(DropCommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!TryGet(instance.TemplateId, out var template))
        {
            return [];
        }

        // Target parameters can narrow provider policy, but command instances do not own
        // representation choices.
        return template.ResolveRequirements?.Invoke(instance) ?? template.Requirements;
    }

    public static string GetAcceptanceText(DropCommandInstance instance)
    {
        var requirements = GetRequirements(instance);
        return requirements.Count == 0
            ? "No supported content"
            : string.Join(' ', requirements.Select(static requirement => requirement.Describe()));
    }

    public static bool IsConfigured(DropCommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!TryGet(instance.TemplateId, out var template))
        {
            return false;
        }

        return (!template.ConfiguresApplication || IsApplicationConfigured(instance)) &&
               (!template.RequiresDestinationFolder ||
                TryGetParameter(instance, DropCommandParameterNames.DestinationFolder, out var destination) &&
                Directory.Exists(destination));
    }

    public static string GetApplicationTargetId(DropCommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return TryGetParameter(instance, DropCommandParameterNames.ApplicationTarget, out var targetId)
            ? targetId
            : DropCommandApplicationTargetIds.DesktopExecutable;
    }

    public static bool IsPackagedAppUserModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.Trim().IndexOf('!');
        return separator > 0 && separator < value.Trim().Length - 1;
    }

    public static bool TryGetParameter(
        DropCommandInstance instance,
        string name,
        out string value)
    {
        if (instance.Parameters.TryGetValue(name, out var candidate) && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsApplicationConfigured(DropCommandInstance instance) =>
        GetApplicationTargetId(instance) switch
        {
            DropCommandApplicationTargetIds.DesktopExecutable =>
                TryGetParameter(instance, DropCommandParameterNames.ExecutablePath, out var executable) &&
                File.Exists(executable),
            DropCommandApplicationTargetIds.PackagedApp =>
                TryGetParameter(instance, DropCommandParameterNames.AppUserModelId, out var appUserModelId) &&
                IsPackagedAppUserModelId(appUserModelId),
            _ => false
        };

    private static IReadOnlyList<ContentRequirement> ResolveOpenAppRequirements(
        DropCommandInstance instance) =>
        GetApplicationTargetId(instance) switch
        {
            DropCommandApplicationTargetIds.DesktopExecutable => DesktopAppRequirements,
            DropCommandApplicationTargetIds.PackagedApp => PackagedAppRequirements,
            _ => []
        };

    private static string GetItemNoun(int itemCount) => itemCount == 1 ? "item" : "items";
}
