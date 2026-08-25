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
    IReadOnlyList<DropItemKind> SupportedKinds,
    Func<DropCommandInstance, IReadOnlyList<DropItemKind>>? ResolveAcceptedKinds,
    bool ConfiguresApplication,
    bool RequiresDestinationFolder,
    Func<DropCommandInstance, DropCommandConfirmationContext, DropCommandConfirmationRequest>?
        CreateConfirmation);

internal sealed record DropCommandConfirmationContext(int ItemCount, bool IsFromStack);

internal sealed record DropCommandConfirmationRequest(
    string Title,
    string Message,
    string PrimaryButtonText);

internal static class DropCommandTemplates
{
    private static readonly IReadOnlyList<DropItemKind> DesktopAppKinds =
        [DropItemKind.File, DropItemKind.Folder, DropItemKind.Image];
    private static readonly IReadOnlyList<DropItemKind> PackagedAppKinds =
        [DropItemKind.File, DropItemKind.Image];

    private static readonly IReadOnlyDictionary<string, DropCommandTemplateDescriptor> TemplatesById =
        new[]
        {
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.OpenInApp,
                "Open in app",
                "Open dropped content in a desktop executable or an installed packaged app.",
                "\uE8A7",
                DesktopAppKinds,
                ResolveOpenAppAcceptedKinds,
                true,
                false,
                null),
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.CopyToFolder,
                "Copy to folder",
                "Copy dropped files, folders, and images to a chosen folder.",
                "\uE8B0",
                [DropItemKind.File, DropItemKind.Folder, DropItemKind.Image],
                null,
                false,
                true,
                null),
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.MoveToFolder,
                "Move to folder",
                "Move original files, folders, and images to a chosen folder.",
                "\uE8DE",
                [DropItemKind.File, DropItemKind.Folder, DropItemKind.Image],
                null,
                false,
                true,
                static (command, context) => new DropCommandConfirmationRequest(
                    command.DisplayName,
                    context.IsFromStack
                        ? $"Move {context.ItemCount} {GetItemNoun(context.ItemCount)} using “{command.DisplayName}”? Successful items will be removed from their OmniTray stack."
                        : $"Move {context.ItemCount} original {GetItemNoun(context.ItemCount)} using “{command.DisplayName}”?",
                    "Move")),
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.Recycle,
                "Recycle",
                "Send original files and folders to the Windows Recycle Bin.",
                "\uE74D",
                [DropItemKind.File, DropItemKind.Folder, DropItemKind.Image],
                null,
                false,
                false,
                static (command, context) => new DropCommandConfirmationRequest(
                    command.DisplayName,
                    $"Send {context.ItemCount} {GetItemNoun(context.ItemCount)} to the Windows Recycle Bin?",
                    "Recycle")),
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.CopyToClipboard,
                "Copy to clipboard",
                "Put the dropped content on the Windows clipboard.",
                "\uE8C8",
                [DropItemKind.File, DropItemKind.Folder, DropItemKind.Text, DropItemKind.Image],
                null,
                false,
                false,
                null),
            new DropCommandTemplateDescriptor(
                DropCommandTemplateIds.Share,
                "Share",
                "Share dropped content using the Windows share sheet.",
                "\uE72D",
                [DropItemKind.File, DropItemKind.Folder, DropItemKind.Text, DropItemKind.Image],
                null,
                false,
                false,
                null)
        }.ToDictionary(static template => template.Id, StringComparer.Ordinal);

    public static IReadOnlyList<DropCommandTemplateDescriptor> All { get; } =
        TemplatesById.Values.ToArray();

    public static bool TryGet(string templateId, out DropCommandTemplateDescriptor descriptor) =>
        TemplatesById.TryGetValue(templateId, out descriptor!);

    public static DropCommandTemplateDescriptor? Get(string templateId) =>
        TryGet(templateId, out var descriptor) ? descriptor : null;

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

        return FormatKinds(GetAcceptedKinds(instance));
    }

    public static IReadOnlyList<DropItemKind> GetAcceptedKinds(DropCommandInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!TryGet(instance.TemplateId, out var template))
        {
            return [];
        }

        // Target parameters can narrow a template's policy, but command instances do not own format choices.
        return template.ResolveAcceptedKinds?.Invoke(instance) ?? template.SupportedKinds;
    }

    public static string GetAcceptanceText(DropCommandInstance instance)
    {
        var kinds = GetAcceptedKinds(instance);
        return kinds.Count == 0 ? "No supported content" : $"Accepts {FormatKinds(kinds)}";
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

    private static IReadOnlyList<DropItemKind> ResolveOpenAppAcceptedKinds(
        DropCommandInstance instance) =>
        GetApplicationTargetId(instance) switch
        {
            DropCommandApplicationTargetIds.DesktopExecutable => DesktopAppKinds,
            DropCommandApplicationTargetIds.PackagedApp => PackagedAppKinds,
            _ => []
        };

    private static string FormatKinds(IEnumerable<DropItemKind> kinds) => string.Join(
        ", ",
        kinds.Select(static kind => kind switch
        {
            DropItemKind.File => "files",
            DropItemKind.Folder => "folders",
            DropItemKind.Text => "text",
            DropItemKind.Image => "images",
            _ => kind.ToString().ToLowerInvariant()
        }));

    private static string GetItemNoun(int itemCount) => itemCount == 1 ? "item" : "items";
}
