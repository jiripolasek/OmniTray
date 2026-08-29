// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.VisualBasic.FileIO;

namespace OmniTray.Services;

internal sealed record ResolvedDropCommandItem(DropItem Item, bool IsTransient);

internal sealed record DropCommandSourceReference(Guid StackId, IReadOnlyList<Guid> ItemIds);

internal sealed record DropCommandInput(
    IReadOnlyList<ResolvedDropCommandItem> Items,
    DropCommandSourceReference? SourceReference)
{
    public IReadOnlyList<DropItem> Models => this.Items.Select(static item => item.Item).ToArray();
}

internal sealed record DropCommandExecutionResult(
    IReadOnlyList<Guid> SuccessfulItemIds,
    int FailedCount,
    string? ErrorMessage,
    bool ConsumeSuccessfulSourceItems,
    bool OwnsTransientItemLifetime = false,
    bool ReportsProgressExternally = false)
{
    public int SucceededCount => this.SuccessfulItemIds.Count;

    public bool IsSuccess => this.FailedCount == 0 && this.SucceededCount > 0;

    public bool IsPartial => this.SucceededCount > 0 && this.FailedCount > 0;
}

internal static class DropCommandInputResolver
{
    public static async Task<DropCommandInput> ResolveAsync(
        DataPackageView dataView,
        MainViewModel stacks)
    {
        ArgumentNullException.ThrowIfNull(dataView);
        ArgumentNullException.ThrowIfNull(stacks);

        if (DragDropDataService.HasStackReference(dataView))
        {
            var stackId = await DragDropDataService.ReadStackReferenceAsync(dataView);
            var stack = stackId is { } id
                ? stacks.Stacks.FirstOrDefault(candidate => candidate.Model.Id == id)
                : null;
            return stack is null
                ? new DropCommandInput([], null)
                : FromStackItems(stack, stack.Model.Items);
        }

        if (DragDropDataService.HasItemReference(dataView))
        {
            var reference = await DragDropDataService.ReadItemReferenceAsync(dataView);
            if (reference is null)
            {
                return new DropCommandInput([], null);
            }

            var stack = stacks.Stacks.FirstOrDefault(candidate => candidate.Model.Id == reference.SourceStackId);
            if (stack is null)
            {
                return new DropCommandInput([], null);
            }

            var itemsById = stack.Model.Items.ToDictionary(static item => item.Id);
            var items = reference.ItemIds
                .Where(itemsById.ContainsKey)
                .Select(id => itemsById[id])
                .ToArray();
            return FromStackItems(stack, items);
        }

        var externalItems = await DragDropDataService.ReadAsync(dataView);
        return new DropCommandInput(
            externalItems.Select(static item => new ResolvedDropCommandItem(item, item.IsOwned)).ToArray(),
            null);
    }

    public static DropCommandInput FromStack(DropStackViewModel stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return FromStackItems(stack, stack.Model.Items);
    }

    private static DropCommandInput FromStackItems(
        DropStackViewModel stack,
        IReadOnlyList<DropItem> items) =>
        new(
            items.Select(static item => new ResolvedDropCommandItem(item, false)).ToArray(),
            new DropCommandSourceReference(stack.Model.Id, items.Select(static item => item.Id).ToArray()));
}

internal sealed class DropCommandExecutionService
{
    private readonly SystemShareService _systemShareService = new();

    public bool CanExecute(DropCommandInstance command, DropCommandInput input, out string reason)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);
        if (!command.IsEnabled)
        {
            reason = "This command is disabled.";
            return false;
        }

        if (!DropCommandTemplates.TryGet(command.TemplateId, out var template) ||
            !DropCommandTemplates.IsConfigured(command))
        {
            reason = "This command is not fully configured.";
            return false;
        }

        if (input.Items.Count == 0)
        {
            reason = "The drop contains no usable items.";
            return false;
        }

        if (!ContentRequirements.AreSatisfiedBy(
                DropCommandTemplates.GetRequirements(command),
                input.Models,
                out reason))
        {
            return false;
        }

        if (template.ValidateInput(command, input) is { Length: > 0 } validationError)
        {
            reason = validationError;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool CanPotentiallyExecute(
        DropCommandInstance command,
        DataPackageView dataView,
        MainViewModel stacks)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(dataView);
        ArgumentNullException.ThrowIfNull(stacks);
        if (!command.IsEnabled || !DropCommandTemplates.IsConfigured(command))
        {
            return false;
        }

        if (DragDropDataService.ActiveStackReferenceId is { } stackId &&
            stacks.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId) is { } stack)
        {
            return this.CanExecute(command, DropCommandInputResolver.FromStack(stack), out _);
        }

        if (DragDropDataService.ActiveItemReference is { } reference &&
            stacks.Stacks.FirstOrDefault(stack => stack.Model.Id == reference.SourceStackId) is { } source)
        {
            var itemIds = reference.ItemIds.ToHashSet();
            var input = new DropCommandInput(
                source.Model.Items.Where(item => itemIds.Contains(item.Id))
                    .Select(static item => new ResolvedDropCommandItem(item, false)).ToArray(),
                reference is null ? null : new DropCommandSourceReference(reference.SourceStackId, reference.ItemIds));
            return this.CanExecute(command, input, out _);
        }

        var metadata = ContentMetadataPolicy.CreatePotential(GetRepresentations(dataView));
        return ContentRequirements.AreSatisfiedBy(
            DropCommandTemplates.GetRequirements(command),
            [metadata],
            out _);
    }

    public async Task<DropCommandExecutionResult> ExecuteAsync(
        DropCommandInstance command,
        DropCommandInput input,
        nint ownerHwnd)
    {
        if (!this.CanExecute(command, input, out var reason))
        {
            return new DropCommandExecutionResult([], input.Items.Count, reason, false);
        }

        return DropCommandTemplates.TryGet(command.TemplateId, out var template)
            ? await template.ExecuteAsync(this, command, input, ownerHwnd)
            : new DropCommandExecutionResult(
                [],
                input.Items.Count,
                "The command template is unavailable.",
                false);
    }

    internal static string? ValidateOpenInAppInput(
        DropCommandInstance command,
        DropCommandInput input)
    {
        _ = command;
        foreach (var resolved in input.Items)
        {
            if (!HasAvailablePath(resolved.Item))
            {
                return "Every item must have an available local path.";
            }

            if (resolved.IsTransient)
            {
                return "Open in app currently requires a file-backed drop.";
            }
        }

        return null;
    }

    internal static string? ValidateTransferInput(
        DropCommandInstance command,
        DropCommandInput input,
        bool move)
    {
        _ = DropCommandTemplates.TryGetParameter(
            command,
            DropCommandParameterNames.DestinationFolder,
            out var destinationFolder);
        foreach (var resolved in input.Items)
        {
            if (!HasAvailablePath(resolved.Item))
            {
                return "Every item must have an available local path.";
            }

            if (move && (resolved.Item.IsOwned || resolved.IsTransient))
            {
                return "This command accepts original files and folders only.";
            }

            if (move &&
                resolved.Item.Kind == DropItemKind.Folder &&
                IsFileSystemRoot(resolved.Item.SourcePath))
            {
                return "Filesystem roots cannot be moved or recycled.";
            }

            if (resolved.Item.Kind == DropItemKind.Folder &&
                IsSameOrDescendantPath(destinationFolder, resolved.Item.SourcePath!))
            {
                return "A folder cannot be copied or moved into itself.";
            }
        }

        return move && HasOverlappingSourcePaths(input.Items)
            ? "A folder and one of its children cannot be moved or recycled together."
            : null;
    }

    internal static string? ValidateRecycleInput(DropCommandInput input)
    {
        foreach (var resolved in input.Items)
        {
            if (!HasAvailablePath(resolved.Item))
            {
                return "Every item must have an available local path.";
            }

            if (resolved.Item.IsOwned || resolved.IsTransient)
            {
                return "This command accepts original files and folders only.";
            }

            if (resolved.Item.Kind == DropItemKind.Folder && IsFileSystemRoot(resolved.Item.SourcePath))
            {
                return "Filesystem roots cannot be moved or recycled.";
            }
        }

        return HasOverlappingSourcePaths(input.Items)
            ? "A folder and one of its children cannot be moved or recycled together."
            : null;
    }

    internal static string? ValidateShareInput(DropCommandInput input)
    {
        foreach (var resolved in input.Items)
        {
            if (resolved.Item.Kind is DropItemKind.Text or DropItemKind.Uri)
            {
                continue;
            }

            if (!HasAvailablePath(resolved.Item))
            {
                return "Every item must have an available local path.";
            }
        }

        return null;
    }

    private static ContentRepresentations GetRepresentations(DataPackageView dataView)
    {
        var representations = ContentRepresentations.None;
        if (dataView.Contains(StandardDataFormats.Text))
        {
            representations |= ContentRepresentations.Text;
        }

        if (dataView.Contains(StandardDataFormats.Html))
        {
            representations |= ContentRepresentations.Html;
        }

        if (dataView.Contains(StandardDataFormats.Rtf))
        {
            representations |= ContentRepresentations.Rtf;
        }

        if (dataView.Contains(StandardDataFormats.Bitmap))
        {
            representations |= ContentRepresentations.Bitmap;
        }

        if (dataView.Contains(StandardDataFormats.StorageItems))
        {
            representations |= ContentRepresentations.StorageItem;
        }

        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            representations |= ContentRepresentations.WebLink;
        }

        if (dataView.Contains(StandardDataFormats.ApplicationLink))
        {
            representations |= ContentRepresentations.ApplicationLink;
        }

        if (dataView.Contains(StandardDataFormats.Uri))
        {
            // The legacy URI format does not expose its scheme until the payload is read.
            representations |= ContentRepresentations.WebLink |
                               ContentRepresentations.ApplicationLink;
        }

        return representations;
    }

    internal async Task<DropCommandExecutionResult> OpenInAppAsync(
        DropCommandInstance command,
        DropCommandInput input)
    {
        try
        {
            switch (DropCommandTemplates.GetApplicationTargetId(command))
            {
                case DropCommandApplicationTargetIds.DesktopExecutable:
                    await Task.Run(() => OpenInDesktopApplication(command, input));
                    break;
                case DropCommandApplicationTargetIds.PackagedApp:
                    _ = DropCommandTemplates.TryGetParameter(
                        command,
                        DropCommandParameterNames.AppUserModelId,
                        out var appUserModelId);
                    await PackagedAppService.ActivateFilesAsync(
                        appUserModelId,
                        input.Items.Select(static item => item.Item.SourcePath!).ToArray());
                    break;
                default:
                    throw new InvalidOperationException("The configured application type is not available.");
            }

            return new DropCommandExecutionResult(
                input.Items.Select(static item => item.Item.Id).ToArray(),
                0,
                null,
                false);
        }
        catch (Exception exception)
        {
            return new DropCommandExecutionResult([], input.Items.Count, exception.Message, false);
        }
    }

    private static void OpenInDesktopApplication(
        DropCommandInstance command,
        DropCommandInput input)
    {
        _ = DropCommandTemplates.TryGetParameter(
            command,
            DropCommandParameterNames.ExecutablePath,
            out var executable);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
        };
        if (DropCommandTemplates.TryGetParameter(
                command,
                DropCommandParameterNames.ExtraArguments,
                out var extraArguments))
        {
            foreach (var argument in extraArguments.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        foreach (var item in input.Items)
        {
            startInfo.ArgumentList.Add(item.Item.SourcePath!);
        }

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("The application did not start.");
    }

    internal Task<DropCommandExecutionResult> TransferToFolderAsync(
        DropCommandInstance command,
        DropCommandInput input,
        bool move)
    {
        return Task.Run(() =>
        {
            _ = DropCommandTemplates.TryGetParameter(
                command,
                DropCommandParameterNames.DestinationFolder,
                out var destinationFolder);
            var successfulItemIds = new List<Guid>();
            var errors = new List<string>();
            foreach (var resolved in input.Items)
            {
                try
                {
                    var source = resolved.Item.SourcePath!;
                    var destination = CreateUniqueDestinationPath(destinationFolder, source);
                    if (resolved.Item.Kind == DropItemKind.Folder)
                    {
                        if (move)
                        {
                            FileSystem.MoveDirectory(source, destination);
                        }
                        else
                        {
                            FileSystem.CopyDirectory(source, destination);
                        }
                    }
                    else if (move)
                    {
                        FileSystem.MoveFile(source, destination);
                    }
                    else
                    {
                        FileSystem.CopyFile(source, destination);
                    }

                    successfulItemIds.Add(resolved.Item.Id);
                }
                catch (Exception exception)
                {
                    errors.Add($"{resolved.Item.DisplayName}: {exception.Message}");
                }
            }

            return new DropCommandExecutionResult(
                successfulItemIds,
                input.Items.Count - successfulItemIds.Count,
                errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                move);
        });
    }

    internal Task<DropCommandExecutionResult> RecycleAsync(DropCommandInput input)
    {
        return Task.Run(() =>
        {
            var successfulItemIds = new List<Guid>();
            var errors = new List<string>();
            foreach (var resolved in input.Items)
            {
                try
                {
                    if (resolved.Item.Kind == DropItemKind.Folder)
                    {
                        FileSystem.DeleteDirectory(
                            resolved.Item.SourcePath!,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin,
                            UICancelOption.ThrowException);
                    }
                    else
                    {
                        FileSystem.DeleteFile(
                            resolved.Item.SourcePath!,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin,
                            UICancelOption.ThrowException);
                    }

                    successfulItemIds.Add(resolved.Item.Id);
                }
                catch (Exception exception)
                {
                    errors.Add($"{resolved.Item.DisplayName}: {exception.Message}");
                }
            }

            return new DropCommandExecutionResult(
                successfulItemIds,
                input.Items.Count - successfulItemIds.Count,
                errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
                true);
        });
    }

    internal Task<DropCommandExecutionResult> CopyToClipboard(
        DropCommandInstance command,
        DropCommandInput input)
    {
        try
        {
            var data = new DataPackage();
            DragDropDataService.WriteStandardContent(data, input.Models, command.DisplayName);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            return Task.FromResult(new DropCommandExecutionResult(
                input.Items.Select(static item => item.Item.Id).ToArray(),
                0,
                null,
                false));
        }
        catch (Exception exception)
        {
            return Task.FromResult(
                new DropCommandExecutionResult([], input.Items.Count, exception.Message, false));
        }
    }

    internal async Task<DropCommandExecutionResult> ShareAsync(
        DropCommandInstance command,
        DropCommandInput input,
        nint ownerHwnd)
    {
        try
        {
            await this._systemShareService.ShowAsync(
                ownerHwnd,
                input.Models,
                command.DisplayName,
                input.Items
                    .Where(static item => item.IsTransient)
                    .Select(static item => item.Item)
                    .ToArray());
            return new DropCommandExecutionResult(
                input.Items.Select(static item => item.Item.Id).ToArray(),
                0,
                null,
                false,
                true,
                true);
        }
        catch (Exception exception)
        {
            return new DropCommandExecutionResult([], input.Items.Count, exception.Message, false);
        }
    }

    private static bool HasAvailablePath(DropItem item) =>
        !string.IsNullOrWhiteSpace(item.SourcePath) && PathExists(item);

    private static bool PathExists(DropItem item) => item.Kind == DropItemKind.Folder
        ? Directory.Exists(item.SourcePath)
        : File.Exists(item.SourcePath);

    private static bool HasOverlappingSourcePaths(IReadOnlyList<ResolvedDropCommandItem> items)
    {
        var folders = items
            .Where(static item => item.Item.Kind == DropItemKind.Folder &&
                                  !string.IsNullOrWhiteSpace(item.Item.SourcePath))
            .Select(static item => Path.GetFullPath(item.Item.SourcePath!))
            .ToArray();
        return items
            .Where(static item => !string.IsNullOrWhiteSpace(item.Item.SourcePath))
            .Select(static item => Path.GetFullPath(item.Item.SourcePath!))
            .Any(path => folders.Any(folder =>
                !StringComparer.OrdinalIgnoreCase.Equals(path, folder) &&
                IsSameOrDescendantPath(path, folder)));
    }

    private static bool IsFileSystemRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        return StringComparer.OrdinalIgnoreCase.Equals(fullPath, root);
    }

    private static bool IsSameOrDescendantPath(string candidatePath, string ancestorPath)
    {
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ancestorPath));
        if (StringComparer.OrdinalIgnoreCase.Equals(candidate, ancestor))
        {
            return true;
        }

        var ancestorPrefix = Path.EndsInDirectorySeparator(ancestor)
            ? ancestor
            : ancestor + Path.DirectorySeparatorChar;
        return candidate.StartsWith(ancestorPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateUniqueDestinationPath(string destinationFolder, string sourcePath)
    {
        var name = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var candidate = Path.Combine(destinationFolder, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var extension = Directory.Exists(sourcePath) ? string.Empty : Path.GetExtension(name);
        var stem = extension.Length == 0 ? name : Path.GetFileNameWithoutExtension(name);
        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            candidate = Path.Combine(destinationFolder, $"{stem} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("A unique destination name could not be generated.");
    }
}
