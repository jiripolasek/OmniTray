// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using VisualBasicFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

namespace OmniTray.Services;

internal sealed class VirtualStackService : IDisposable
{
    private readonly Dictionary<string, IVirtualStackProvider> _providers;

    public VirtualStackService()
    {
        IVirtualStackProvider[] providers =
        [
            new RecentFilesVirtualStackProvider(),
            new ClipboardVirtualStackProvider(history: false),
            new ClipboardVirtualStackProvider(history: true),
            new FolderVirtualStackProvider()
        ];
        this._providers = providers.ToDictionary(
            static provider => provider.Definition.Id,
            StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            provider.Changed += this.OnProviderChanged;
        }

        this.Definitions = providers.Select(static provider => provider.Definition).ToArray();
    }

    public event Action<string>? Changed;

    public IReadOnlyList<VirtualStackProviderDefinition> Definitions { get; }

    public VirtualStackSource CreateSource(string providerId, string? configuration)
    {
        var provider = this.GetProvider(providerId);
        return VirtualStackSource.Create(
            provider.Definition.Id,
            provider.NormalizeConfiguration(configuration),
            provider.Definition.Capabilities);
    }

    public Task<IReadOnlyList<DropItem>> ReadAsync(VirtualStackSource source)
    {
        var provider = this.RequireCapability(source, VirtualStackCapabilities.Read);
        return provider.ReadAsync(source.Configuration);
    }

    public Task WriteAsync(VirtualStackSource source, IReadOnlyList<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var provider = this.RequireCapability(source, VirtualStackCapabilities.Write);
        return provider.WriteAsync(source.Configuration, items);
    }

    public Task RemoveAsync(VirtualStackSource source, IReadOnlyList<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        var provider = this.RequireCapability(source, VirtualStackCapabilities.Remove);
        return provider.RemoveAsync(source.Configuration, itemIds);
    }

    public void Dispose()
    {
        foreach (var provider in this._providers.Values)
        {
            provider.Changed -= this.OnProviderChanged;
            provider.Dispose();
        }
    }

    private IVirtualStackProvider RequireCapability(
        VirtualStackSource source,
        VirtualStackCapabilities capability)
    {
        ArgumentNullException.ThrowIfNull(source);
        var provider = this.GetProvider(source.ProviderId);
        if (!source.Can(capability) ||
            (provider.Definition.Capabilities & capability) != capability)
        {
            throw new InvalidOperationException(
                $"The {provider.Definition.DisplayName} source does not support {capability.ToString().ToLowerInvariant()} operations.");
        }

        return provider;
    }

    private IVirtualStackProvider GetProvider(string providerId) =>
        this._providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new InvalidOperationException($"Virtual stack provider '{providerId}' is not available.");

    private void OnProviderChanged(object? sender, EventArgs args)
    {
        if (sender is IVirtualStackProvider provider)
        {
            this.Changed?.Invoke(provider.Definition.Id);
        }
    }
}

internal sealed record VirtualStackProviderDefinition(
    string Id,
    string DisplayName,
    string DefaultStackName,
    VirtualStackCapabilities Capabilities,
    bool RequiresFolder = false);

internal interface IVirtualStackProvider : IDisposable
{
    VirtualStackProviderDefinition Definition { get; }

    event EventHandler? Changed;

    string? NormalizeConfiguration(string? configuration);

    Task<IReadOnlyList<DropItem>> ReadAsync(string? configuration);

    Task WriteAsync(string? configuration, IReadOnlyList<DropItem> items);

    Task RemoveAsync(string? configuration, IReadOnlyList<Guid> itemIds);
}

internal sealed class RecentFilesVirtualStackProvider : IVirtualStackProvider
{
    private const int MaxItems = 100;
    private readonly FileSystemWatcher? _watcher;
    private readonly string _recentFolder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

    public RecentFilesVirtualStackProvider()
    {
        if (!Directory.Exists(this._recentFolder))
        {
            return;
        }

        this._watcher = new FileSystemWatcher(this._recentFolder, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        this._watcher.Changed += this.OnChanged;
        this._watcher.Created += this.OnChanged;
        this._watcher.Deleted += this.OnChanged;
        this._watcher.Renamed += this.OnChanged;
    }

    public VirtualStackProviderDefinition Definition { get; } = new(
        "builtin.recent-files",
        "Recent files",
        "Recent files",
        VirtualStackCapabilities.Read);

    public event EventHandler? Changed;

    public string? NormalizeConfiguration(string? configuration) => null;

    public Task<IReadOnlyList<DropItem>> ReadAsync(string? configuration) => Task.Run<IReadOnlyList<DropItem>>(() =>
    {
        if (!Directory.Exists(this._recentFolder))
        {
            return [];
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<DropItem>();
        foreach (var shortcut in new DirectoryInfo(this._recentFolder)
                     .EnumerateFiles("*.lnk", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static file => file.LastWriteTimeUtc))
        {
            if (items.Count >= MaxItems)
            {
                break;
            }

            try
            {
                var path = ShellLinkResolver.Resolve(shortcut.FullName);
                if (string.IsNullOrWhiteSpace(path) || !paths.Add(path) ||
                    (!File.Exists(path) && !Directory.Exists(path)))
                {
                    continue;
                }

                items.Add(DropItem.CreateStorageItem(
                        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
                        path,
                        Directory.Exists(path),
                        createdAt: shortcut.LastWriteTimeUtc)
                    .WithId(VirtualStackItemId.Create(this.Definition.Id, path)));
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not read recent shortcut '{shortcut.FullName}': {exception.Message}");
            }
        }

        return items;
    });

    public Task WriteAsync(string? configuration, IReadOnlyList<DropItem> items) =>
        throw new InvalidOperationException("Recent files is read-only.");

    public Task RemoveAsync(string? configuration, IReadOnlyList<Guid> itemIds) =>
        throw new InvalidOperationException("Recent files is read-only.");

    public void Dispose()
    {
        if (this._watcher is null)
        {
            return;
        }

        this._watcher.Changed -= this.OnChanged;
        this._watcher.Created -= this.OnChanged;
        this._watcher.Deleted -= this.OnChanged;
        this._watcher.Renamed -= this.OnChanged;
        this._watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        this.Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class ClipboardVirtualStackProvider : IVirtualStackProvider
{
    private readonly bool _history;
    private readonly Dictionary<string, IReadOnlyList<DropItem>> _historyItemCache = new(StringComparer.Ordinal);
    private IReadOnlyList<DropItem>? _cachedItems;
    private Dictionary<Guid, ClipboardHistoryItem> _historyItems = [];

    public ClipboardVirtualStackProvider(bool history)
    {
        this._history = history;
        this.Definition = history
            ? new(
                "builtin.clipboard-history",
                "Clipboard history",
                "Clipboard history",
                VirtualStackCapabilities.Read |
                VirtualStackCapabilities.Write |
                VirtualStackCapabilities.Remove)
            : new(
                "builtin.clipboard",
                "Clipboard",
                "Clipboard",
                VirtualStackCapabilities.Read |
                VirtualStackCapabilities.Write |
                VirtualStackCapabilities.Remove);

        if (history)
        {
            Clipboard.HistoryChanged += this.OnHistoryChanged;
        }
        else
        {
            Clipboard.ContentChanged += this.OnContentChanged;
        }
    }

    public VirtualStackProviderDefinition Definition { get; }

    public event EventHandler? Changed;

    public string? NormalizeConfiguration(string? configuration) => null;

    public async Task<IReadOnlyList<DropItem>> ReadAsync(string? configuration)
    {
        if (this._cachedItems is not null)
        {
            return this._cachedItems;
        }

        this._cachedItems = this._history
            ? await this.ReadHistoryAsync()
            : await this.ReadCurrentAsync();
        return this._cachedItems;
    }

    public Task WriteAsync(string? configuration, IReadOnlyList<DropItem> items)
    {
        if (items.Count == 0)
        {
            return Task.CompletedTask;
        }

        ItemManipulationService.PutOnClipboard(items, DataPackageOperation.Copy);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string? configuration, IReadOnlyList<Guid> itemIds)
    {
        if (this._history)
        {
            foreach (var historyItem in itemIds
                         .Distinct()
                         .Select(id => this._historyItems.GetValueOrDefault(id))
                         .Where(static item => item is not null)
                         .DistinctBy(static item => item!.Id))
            {
                Clipboard.DeleteItemFromHistory(historyItem!);
            }
        }
        else if (itemIds.Count > 0)
        {
            Clipboard.Clear();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (this._history)
        {
            Clipboard.HistoryChanged -= this.OnHistoryChanged;
        }
        else
        {
            Clipboard.ContentChanged -= this.OnContentChanged;
        }

        this.Invalidate();
        this.ClearHistoryItemCache();
    }

    private async Task<IReadOnlyList<DropItem>> ReadCurrentAsync()
    {
        var items = await DragDropDataService.ReadAsync(
            Clipboard.GetContent(),
            CaptureChannel.Clipboard);
        return items.Select((item, index) => item.WithId(
            VirtualStackItemId.Create(
                this.Definition.Id,
                $"{item.CreatedAt.UtcDateTime.Ticks}\0{index}"))).ToArray();
    }

    private async Task<IReadOnlyList<DropItem>> ReadHistoryAsync()
    {
        if (!Clipboard.IsHistoryEnabled())
        {
            this.ClearHistoryItemCache();
            return [];
        }

        var result = await Clipboard.GetHistoryItemsAsync();
        if (result.Status != ClipboardHistoryItemsResultStatus.Success)
        {
            return [];
        }

        var activeHistoryIds = result.Items.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);
        var removedItems = this._historyItemCache
            .Where(pair => !activeHistoryIds.Contains(pair.Key))
            .ToArray();
        foreach (var (historyId, _) in removedItems)
        {
            this._historyItemCache.Remove(historyId);
        }

        if (removedItems.Length > 0)
        {
            _ = ContentStore.DeleteOwnedAsync(removedItems.SelectMany(static pair => pair.Value));
        }

        var items = new List<DropItem>();
        var historyItems = new Dictionary<Guid, ClipboardHistoryItem>();
        foreach (var historyItem in result.Items)
        {
            try
            {
                if (!this._historyItemCache.TryGetValue(historyItem.Id, out var captured))
                {
                    var loaded = await DragDropDataService.ReadAsync(
                        historyItem.Content,
                        CaptureChannel.Clipboard);
                    var capturedItems = new DropItem[loaded.Count];
                    for (var index = 0; index < loaded.Count; index++)
                    {
                        var capturedItem = loaded[index];
                        if (capturedItem.Capture is { } capture)
                        {
                            capturedItem = capturedItem.WithMetadata(
                                capture: capture with { CapturedAt = historyItem.Timestamp });
                        }

                        capturedItems[index] = capturedItem.WithId(
                            VirtualStackItemId.Create(this.Definition.Id, $"{historyItem.Id}\0{index}"));
                    }

                    captured = capturedItems;
                    this._historyItemCache[historyItem.Id] = captured;
                }

                foreach (var item in captured)
                {
                    items.Add(item);
                    historyItems[item.Id] = historyItem;
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Could not read clipboard history item '{historyItem.Id}': {exception.Message}");
            }

            await Task.Yield();
        }

        this._historyItems = historyItems;
        return items;
    }

    private void OnContentChanged(object? sender, object args) => this.OnChanged();

    private void OnHistoryChanged(object? sender, ClipboardHistoryChangedEventArgs args) => this.OnChanged();

    private void OnChanged()
    {
        this.Invalidate();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Invalidate()
    {
        var items = this._cachedItems;
        this._cachedItems = null;
        this._historyItems = [];
        if (!this._history && items is not null)
        {
            _ = ContentStore.DeleteOwnedAsync(items);
        }
    }

    private void ClearHistoryItemCache()
    {
        if (this._historyItemCache.Count == 0)
        {
            return;
        }

        var items = this._historyItemCache.Values.SelectMany(static items => items).ToArray();
        this._historyItemCache.Clear();
        _ = ContentStore.DeleteOwnedAsync(items);
    }
}

internal sealed class FolderVirtualStackProvider : IVirtualStackProvider
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    public VirtualStackProviderDefinition Definition { get; } = new(
        "builtin.folder",
        "Existing folder",
        "Folder",
        VirtualStackCapabilities.Read | VirtualStackCapabilities.Write,
        RequiresFolder: true);

    public event EventHandler? Changed;

    public string? NormalizeConfiguration(string? configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration);
        var path = Path.GetFullPath(configuration);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Folder '{path}' does not exist.");
        }

        this.EnsureWatcher(path);
        return path;
    }

    public Task<IReadOnlyList<DropItem>> ReadAsync(string? configuration)
    {
        var folder = this.NormalizeConfiguration(configuration)!;
        return Task.Run<IReadOnlyList<DropItem>>(() => new DirectoryInfo(folder)
            .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static item => item.LastWriteTimeUtc)
            .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => DropItem.CreateStorageItem(
                    item.Name,
                    item.FullName,
                    item is DirectoryInfo,
                    createdAt: item.LastWriteTimeUtc)
                .WithId(VirtualStackItemId.Create(this.Definition.Id, item.FullName)))
            .ToArray());
    }

    public Task WriteAsync(string? configuration, IReadOnlyList<DropItem> items)
    {
        var folder = this.NormalizeConfiguration(configuration)!;
        var sources = items.Select(static item => item.SourcePath).ToArray();
        if (sources.Any(static path => string.IsNullOrWhiteSpace(path) ||
                                       (!File.Exists(path) && !Directory.Exists(path))))
        {
            throw new InvalidOperationException("Every item must have an available file or folder backing.");
        }

        foreach (var source in sources.Cast<string>().Where(Directory.Exists))
        {
            var sourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            if (folder.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) ||
                folder.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A folder cannot be copied into itself.");
            }
        }

        return Task.Run(() =>
        {
            foreach (var source in sources.Cast<string>())
            {
                var name = Path.GetFileName(source.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
                var destination = CreateUniquePath(folder, name);
                if (Directory.Exists(source))
                {
                    VisualBasicFileSystem.CopyDirectory(source, destination, overwrite: false);
                }
                else
                {
                    File.Copy(source, destination);
                }
            }
        });
    }

    public Task RemoveAsync(string? configuration, IReadOnlyList<Guid> itemIds) =>
        throw new InvalidOperationException("Folder items cannot be removed through OmniTray.");

    public void Dispose()
    {
        foreach (var watcher in this._watchers.Values)
        {
            watcher.Created -= this.OnChanged;
            watcher.Deleted -= this.OnChanged;
            watcher.Renamed -= this.OnChanged;
            watcher.Dispose();
        }

        this._watchers.Clear();
    }

    private static string CreateUniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        for (var index = 2; ; index++)
        {
            path = Path.Combine(folder, $"{baseName} ({index}){extension}");
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }
    }

    private void EnsureWatcher(string path)
    {
        if (this._watchers.ContainsKey(path))
        {
            return;
        }

        var watcher = new FileSystemWatcher(path)
        {
            // ponytail: watch membership only; add debounced metadata changes if live timestamps become necessary.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };
        watcher.Created += this.OnChanged;
        watcher.Deleted += this.OnChanged;
        watcher.Renamed += this.OnChanged;
        this._watchers.Add(path, watcher);
    }

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        this.Changed?.Invoke(this, EventArgs.Empty);
}

internal static class VirtualStackItemId
{
    public static Guid Create(string providerId, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{providerId}\0{value.ToUpperInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

internal static partial class ShellLinkResolver
{
    public static unsafe string? Resolve(string shortcutPath)
    {
        using var shellLink = ShellLinkComObject.Create();
        fixed (char* path = shortcutPath)
        {
            Marshal.ThrowExceptionForHR(shellLink.PersistFile.Load(path, 0));
        }

        Span<char> buffer = stackalloc char[32768];
        fixed (char* target = buffer)
        {
            Marshal.ThrowExceptionForHR(shellLink.ShellLink.GetPath(target, buffer.Length, 0, 4));
        }

        var terminator = buffer.IndexOf('\0');
        return new string(terminator < 0 ? buffer : buffer[..terminator]);
    }
}

internal sealed partial class ShellLinkComObject : IDisposable
{
    private object? _instance;
    private readonly bool _uninitialize;

    private ShellLinkComObject(object instance, IShellLinkW shellLink, IPersistFile persistFile, bool uninitialize)
    {
        this._instance = instance;
        this.ShellLink = shellLink;
        this.PersistFile = persistFile;
        this._uninitialize = uninitialize;
    }

    public IShellLinkW ShellLink { get; }

    public IPersistFile PersistFile { get; }

    public static ShellLinkComObject Create()
    {
        var initializeResult = ShellLinkNativeMethods.CoInitializeEx(0, 0);
        var uninitialize = initializeResult >= 0;
        if (initializeResult < 0 && initializeResult != unchecked((int)0x80010106))
        {
            Marshal.ThrowExceptionForHR(initializeResult);
        }

        try
        {
            var classId = new Guid("00021401-0000-0000-C000-000000000046");
            var interfaceId = new Guid("000214F9-0000-0000-C000-000000000046");
            Marshal.ThrowExceptionForHR(ShellLinkNativeMethods.CoCreateInstance(
                ref classId,
                0,
                1,
                ref interfaceId,
                out var shellLink));
            return new(shellLink, shellLink, (IPersistFile)shellLink, uninitialize);
        }
        catch
        {
            if (uninitialize)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._instance, null) is not { } instance)
        {
            return;
        }

        try
        {
            (instance as IDisposable)?.Dispose();
        }
        finally
        {
            if (this._uninitialize)
            {
                ShellLinkNativeMethods.CoUninitialize();
            }
        }
    }
}

internal static partial class ShellLinkNativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        ref Guid classId,
        nint outer,
        uint context,
        ref Guid interfaceId,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<IShellLinkW>))]
        out IShellLinkW instance);
}

[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig] int GetPath(char* filePath, int characterCount, nint findData, uint flags);
    [PreserveSig] int GetIDList(nint* itemIdList);
    [PreserveSig] int SetIDList(nint itemIdList);
    [PreserveSig] int GetDescription(char* name, int characterCount);
    [PreserveSig] int SetDescription(char* name);
    [PreserveSig] int GetWorkingDirectory(char* directory, int characterCount);
    [PreserveSig] int SetWorkingDirectory(char* directory);
    [PreserveSig] int GetArguments(char* arguments, int characterCount);
    [PreserveSig] int SetArguments(char* arguments);
    [PreserveSig] int GetHotkey(ushort* hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCommand(int* showCommand);
    [PreserveSig] int SetShowCommand(int showCommand);
    [PreserveSig] int GetIconLocation(char* iconPath, int characterCount, int* iconIndex);
    [PreserveSig] int SetIconLocation(char* iconPath, int iconIndex);
    [PreserveSig] int SetRelativePath(char* relativePath, uint reserved);
    [PreserveSig] int Resolve(nint window, uint flags);
    [PreserveSig] int SetPath(char* filePath);
}

[GeneratedComInterface]
[Guid("0000010C-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IPersist
{
    [PreserveSig] int GetClassID(nint classId);
}

[GeneratedComInterface]
[Guid("0000010B-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IPersistFile : IPersist
{
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load(char* fileName, uint mode);
    [PreserveSig] int Save(char* fileName, int remember);
    [PreserveSig] int SaveCompleted(char* fileName);
    [PreserveSig] int GetCurrentFile(char** fileName);
}
