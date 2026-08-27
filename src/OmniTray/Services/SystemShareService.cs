// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace OmniTray.Services;

internal sealed class SystemShareService
{
    private static readonly TimeSpan DataRequestTimeout = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly Dictionary<nint, ShareOperation> _pendingOperations = [];
    private readonly HashSet<ShareOperation> _payloadOperations = [];

    public async Task ShowAsync(
        nint ownerHwnd,
        IReadOnlyList<DropItem> items,
        string title,
        IReadOnlyList<DropItem> transientItems)
    {
        if (ownerHwnd == 0)
        {
            throw new ArgumentException("An owner window is required.", nameof(ownerHwnd));
        }

        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(transientItems);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }

        var content = await PreparedShareContent.CreateAsync(items, title);

        // ShowShareUIForWindow cannot reliably start while a WinUI Drop callback is
        // still on the native stack. Yield once so the async Drop handler can return
        // before asking Windows to open the Share sheet.
        await Task.Yield();

        var manager = DataTransferManagerInterop.GetForWindow(ownerHwnd);
        var operation = new ShareOperation(
            this,
            manager,
            ownerHwnd,
            content,
            transientItems.ToArray());
        ShareOperation? replacedOperation;
        lock (this._sync)
        {
            this._pendingOperations.TryGetValue(ownerHwnd, out replacedOperation);
            this._pendingOperations[ownerHwnd] = operation;
        }

        replacedOperation?.Abort(new OperationCanceledException("A newer Share request replaced this one."));

        try
        {
            operation.Start(ownerHwnd);
            var preparationError = await operation.RequestPrepared.WaitAsync(DataRequestTimeout);
            if (preparationError is not null)
            {
                throw preparationError;
            }
        }
        catch (TimeoutException exception)
        {
            var timeout = new TimeoutException(
                "Windows did not open the Share sheet. Try the Share command again.",
                exception);
            operation.Abort(timeout);
            throw timeout;
        }
        catch (Exception exception)
        {
            operation.Abort(exception);
            throw;
        }
    }

    private void BeginPayload(ShareOperation operation)
    {
        lock (this._sync)
        {
            if (this._pendingOperations.TryGetValue(operation.OwnerHwnd, out var pendingOperation) &&
                ReferenceEquals(pendingOperation, operation))
            {
                this._pendingOperations.Remove(operation.OwnerHwnd);
            }

            this._payloadOperations.Add(operation);
        }
    }

    private void Complete(ShareOperation operation, bool releaseTransientItems)
    {
        lock (this._sync)
        {
            if (this._pendingOperations.TryGetValue(operation.OwnerHwnd, out var pendingOperation) &&
                ReferenceEquals(pendingOperation, operation))
            {
                this._pendingOperations.Remove(operation.OwnerHwnd);
            }

            this._payloadOperations.Remove(operation);
        }

        if (releaseTransientItems)
        {
            _ = ContentStore.DeleteOwnedAsync(operation.TransientItems);
        }
    }

    private sealed class PreparedShareContent
    {
        private readonly IReadOnlyList<IStorageItem> _storageItems;
        private readonly string? _applicationLink;
        private readonly string? _html;
        private readonly string? _rtf;
        private readonly string? _sourceUrl;
        private readonly string? _sourceApplicationName;
        private readonly string? _sourcePackageFamilyName;
        private readonly string? _sourceApplicationLink;
        private readonly IReadOnlyList<DropItemHtmlResource> _htmlResources;
        private readonly string? _text;
        private readonly string _title;
        private readonly string? _url;

        private PreparedShareContent(
            string title,
            string? text,
            string? html,
            string? rtf,
            string? url,
            string? applicationLink,
            string? sourceUrl,
            string? sourceApplicationName,
            string? sourcePackageFamilyName,
            string? sourceApplicationLink,
            IReadOnlyList<DropItemHtmlResource> htmlResources,
            IReadOnlyList<IStorageItem> storageItems)
        {
            this._title = title;
            this._text = text;
            this._html = html;
            this._rtf = rtf;
            this._url = url;
            this._applicationLink = applicationLink;
            this._sourceUrl = sourceUrl;
            this._sourceApplicationName = sourceApplicationName;
            this._sourcePackageFamilyName = sourcePackageFamilyName;
            this._sourceApplicationLink = sourceApplicationLink;
            this._htmlResources = htmlResources;
            this._storageItems = storageItems;
        }

        public static async Task<PreparedShareContent> CreateAsync(
            IReadOnlyList<DropItem> items,
            string title)
        {
            var text = string.Join(
                Environment.NewLine,
                items
                    .Where(static item => item.Kind is DropItemKind.Text or DropItemKind.Uri)
                    .Select(static item => item.Text)
                    .Where(static value => !string.IsNullOrEmpty(value)));
            var storageItems = new List<IStorageItem>();
            foreach (var item in items.Where(static item =>
                         item.Kind is not DropItemKind.Text and not DropItemKind.Uri &&
                         !string.IsNullOrWhiteSpace(item.SourcePath)))
            {
                storageItems.Add(item.Kind == DropItemKind.Folder
                    ? await StorageFolder.GetFolderFromPathAsync(item.SourcePath!)
                    : await StorageFile.GetFileFromPathAsync(item.SourcePath!));
            }

            var singleItem = items.Count == 1 ? items[0] : null;
            if (string.IsNullOrEmpty(text) &&
                storageItems.Count == 0 &&
                string.IsNullOrWhiteSpace(singleItem?.Html) &&
                string.IsNullOrWhiteSpace(singleItem?.Rtf) &&
                string.IsNullOrWhiteSpace(singleItem?.Url) &&
                string.IsNullOrWhiteSpace(singleItem?.ApplicationLink))
            {
                throw new InvalidOperationException("The drop did not contain shareable content.");
            }

            return new PreparedShareContent(
                title,
                string.IsNullOrEmpty(text) ? null : text,
                singleItem?.Html,
                singleItem?.Rtf,
                singleItem?.Url,
                singleItem?.ApplicationLink,
                singleItem?.SourceUrl,
                singleItem?.SourceApplicationName,
                singleItem?.SourcePackageFamilyName,
                singleItem?.SourceApplicationLink,
                singleItem?.HtmlResources ?? [],
                storageItems);
        }

        public void WriteTo(DataPackage data)
        {
            data.RequestedOperation = DataPackageOperation.Copy;
            data.Properties.Title = this._title;
            if (this._text is not null)
            {
                data.SetText(this._text);
            }

            if (this._html is not null)
            {
                data.SetHtmlFormat(this._html);
            }

            if (this._rtf is not null)
            {
                data.SetRtf(this._rtf);
            }

            if (ContentDetection.TryNormalizeWebUrl(this._url, out var url))
            {
                data.SetWebLink(new Uri(url));
            }

            if (Uri.TryCreate(this._applicationLink, UriKind.Absolute, out var applicationLink))
            {
                data.SetApplicationLink(applicationLink);
            }

            if (ContentDetection.TryNormalizeWebUrl(this._sourceUrl, out var sourceUrl))
            {
                data.Properties.ContentSourceWebLink = new Uri(sourceUrl);
            }

            if (!string.IsNullOrWhiteSpace(this._sourceApplicationName))
            {
                try
                {
                    data.Properties.ApplicationName = this._sourceApplicationName;
                }
                catch
                {
                    // Invalid attribution metadata must not break the share payload.
                }
            }

            if (!string.IsNullOrWhiteSpace(this._sourcePackageFamilyName))
            {
                try
                {
                    data.Properties.PackageFamilyName = this._sourcePackageFamilyName;
                }
                catch
                {
                    // Invalid attribution metadata must not break the share payload.
                }
            }

            if (Uri.TryCreate(this._sourceApplicationLink, UriKind.Absolute, out var sourceApplicationLink))
            {
                try
                {
                    data.Properties.ContentSourceApplicationLink = sourceApplicationLink;
                }
                catch
                {
                    // Invalid attribution metadata must not break the share payload.
                }
            }

            foreach (var resource in this._htmlResources)
            {
                try
                {
                    data.ResourceMap[resource.ResourceKey] = RandomAccessStreamReference.CreateFromUri(
                        ContentStore.CreateHtmlResourceUri(resource));
                }
                catch
                {
                    // Missing managed resources must not break the remaining share payload.
                }
            }

            if (this._storageItems.Count > 0)
            {
                data.SetStorageItems(this._storageItems);
            }
        }
    }

    private sealed class ShareOperation
    {
        private readonly SystemShareService _owner;
        private readonly DataTransferManager _manager;
        private readonly PreparedShareContent _content;
        private readonly TaskCompletionSource<Exception?> _requestPrepared =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DataPackage? _dataPackage;
        private int _state;

        public ShareOperation(
            SystemShareService owner,
            DataTransferManager manager,
            nint ownerHwnd,
            PreparedShareContent content,
            IReadOnlyList<DropItem> transientItems)
        {
            this._owner = owner;
            this._manager = manager;
            this.OwnerHwnd = ownerHwnd;
            this._content = content;
            this.TransientItems = transientItems;
        }

        public nint OwnerHwnd { get; }

        public IReadOnlyList<DropItem> TransientItems { get; }

        public Task<Exception?> RequestPrepared => this._requestPrepared.Task;

        public void Start(nint ownerHwnd)
        {
            this._manager.DataRequested += this.OnDataRequested;
            DataTransferManagerInterop.ShowShareUIForWindow(ownerHwnd);
        }

        public void Abort(Exception exception) => this.Finish(false, exception);

        private void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (Interlocked.CompareExchange(ref this._state, 1, 0) != 0)
            {
                args.Request.FailWithDisplayText("This Share request is no longer available.");
                return;
            }

            this._manager.DataRequested -= this.OnDataRequested;
            this._owner.BeginPayload(this);
            try
            {
                this._dataPackage = args.Request.Data;
                this._dataPackage.Destroyed += this.OnDataPackageDestroyed;
                this._dataPackage.ShareCompleted += this.OnShareCompleted;
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                {
                    this._dataPackage.ShareCanceled += this.OnShareCanceled;
                }

                this._content.WriteTo(this._dataPackage);
                this._requestPrepared.TrySetResult(null);
            }
            catch (Exception exception)
            {
                args.Request.FailWithDisplayText(
                    string.IsNullOrWhiteSpace(exception.Message)
                        ? "The content could not be prepared for sharing."
                        : exception.Message);
                this._requestPrepared.TrySetResult(exception);
                this.Finish(false);
            }
        }

        private void OnShareCompleted(DataPackage sender, ShareCompletedEventArgs args) => this.Finish(true);

        private void OnShareCanceled(DataPackage sender, object args) => this.Finish(true);

        private void OnDataPackageDestroyed(DataPackage sender, object args) => this.Finish(true);

        private void Finish(bool releaseTransientItems, Exception? pendingError = null)
        {
            var previousState = Interlocked.Exchange(ref this._state, 2);
            if (previousState == 2)
            {
                return;
            }

            try
            {
                this._manager.DataRequested -= this.OnDataRequested;
            }
            catch
            {
                // The owning window can disappear while the system share UI is closing.
            }

            try
            {
                if (this._dataPackage is not null)
                {
                    this._dataPackage.Destroyed -= this.OnDataPackageDestroyed;
                    this._dataPackage.ShareCompleted -= this.OnShareCompleted;
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                    {
                        this._dataPackage.ShareCanceled -= this.OnShareCanceled;
                    }
                }
            }
            catch
            {
                // A destroyed data package has already released the share payload.
            }

            if (previousState == 0)
            {
                this._requestPrepared.TrySetResult(
                    pendingError ?? new InvalidOperationException("The Share request ended before Windows requested its content."));
            }

            this._owner.Complete(this, releaseTransientItems);
        }
    }
}
