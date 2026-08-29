// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Windows.Management.Core;
using Windows.Management.Deployment;

namespace OmniTray.CommandPalette.Sources;

internal sealed class OmniTrayCatalogSource
{
    internal event EventHandler? Changed;
    private const string OmniTrayPackageIdentityName = "149aab7e-830a-4928-81d1-74d5a57b1fd9";
    private const string CatalogFileName = "stack-catalog.json";
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private readonly Lock _syncRoot = new();
    private bool _isDisposed;
    private CancellationTokenSource? _reloadDebounce;
    private IReadOnlyList<DropStack> _stacks = [];
    private string? _watchedFolder;
    private FileSystemWatcher? _watcher;

    internal bool IsInitialized { get; private set; }

    internal string? StatusMessage { get; private set; }

    internal OmniTrayCatalogSource()
    {
        _ = Task.Run(this.RefreshAsync);
    }

    internal IReadOnlyList<DropStack> GetSnapshot()
    {
        lock (this._syncRoot)
        {
            return this._stacks;
        }
    }

    internal void Close()
    {
        CancellationTokenSource? debounce;
        FileSystemWatcher? watcher;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._isDisposed = true;
            debounce = this._reloadDebounce;
            this._reloadDebounce = null;
            watcher = this._watcher;
            this._watcher = null;
        }

        debounce?.Cancel();
        debounce?.Dispose();
        watcher?.Dispose();
        this._refreshGate.Dispose();
    }

    private async Task RefreshAsync()
    {
        try
        {
            await this._refreshGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (this.IsDisposed())
            {
                return;
            }

            var catalogPath = ResolveCatalogPath();
            if (catalogPath is null)
            {
                this.Update([], "OmniTray isn't installed for this user.");
                return;
            }

            this.ConfigureWatcher(Path.GetDirectoryName(catalogPath)!);
            if (!File.Exists(catalogPath))
            {
                this.Update([], "OmniTray has no saved stacks yet.");
                return;
            }

            var json = await File.ReadAllTextAsync(catalogPath).ConfigureAwait(false);
            this.Update(StackCatalogReader.ReadStacks(json), null);
        }
        catch (Exception)
        {
            this.Update([], "OmniTray's saved stacks couldn't be read. Open the app to check its catalog.");
        }
        finally
        {
            try
            {
                this._refreshGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static string? ResolveCatalogPath()
    {
        var package = new PackageManager()
            .FindPackagesForUser(string.Empty)
            .Where(candidate => string.Equals(
                candidate.Id.Name,
                OmniTrayPackageIdentityName,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static candidate => candidate.Id.Version.Major)
            .ThenByDescending(static candidate => candidate.Id.Version.Minor)
            .ThenByDescending(static candidate => candidate.Id.Version.Build)
            .ThenByDescending(static candidate => candidate.Id.Version.Revision)
            .FirstOrDefault();
        if (package is null)
        {
            return null;
        }

        var appData = ApplicationDataManager.CreateForPackageFamily(package.Id.FamilyName);
        return Path.Combine(appData.LocalFolder.Path, CatalogFileName);
    }

    private void ConfigureWatcher(string folder)
    {
        lock (this._syncRoot)
        {
            if (this._isDisposed || string.Equals(this._watchedFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this._watcher?.Dispose();
            this._watchedFolder = folder;
            this._watcher = new FileSystemWatcher(folder, CatalogFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            this._watcher.Changed += this.OnCatalogChanged;
            this._watcher.Created += this.OnCatalogChanged;
            this._watcher.Deleted += this.OnCatalogChanged;
            this._watcher.Renamed += this.OnCatalogChanged;
        }
    }

    private void OnCatalogChanged(object sender, FileSystemEventArgs args)
    {
        var nextDebounce = new CancellationTokenSource();
        CancellationTokenSource? previousDebounce;
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                nextDebounce.Dispose();
                return;
            }

            previousDebounce = this._reloadDebounce;
            this._reloadDebounce = nextDebounce;
        }

        previousDebounce?.Cancel();
        previousDebounce?.Dispose();
        _ = this.ReloadAfterDelayAsync(nextDebounce);
    }

    private async Task ReloadAfterDelayAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), debounce.Token).ConfigureAwait(false);
            await this.RefreshAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (debounce.IsCancellationRequested)
        {
        }
        finally
        {
            lock (this._syncRoot)
            {
                if (ReferenceEquals(this._reloadDebounce, debounce))
                {
                    this._reloadDebounce = null;
                }
            }

            debounce.Dispose();
        }
    }

    private void Update(IReadOnlyList<DropStack> stacks, string? statusMessage)
    {
        lock (this._syncRoot)
        {
            if (this._isDisposed)
            {
                return;
            }

            this._stacks = stacks;
            this.StatusMessage = statusMessage;
            this.IsInitialized = true;
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsDisposed()
    {
        lock (this._syncRoot)
        {
            return this._isDisposed;
        }
    }
}
