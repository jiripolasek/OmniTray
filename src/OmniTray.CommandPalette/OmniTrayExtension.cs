// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Runtime.InteropServices;

namespace OmniTray.CommandPalette;

[Guid("7D6E7E83-067D-4B71-A1DD-AB7082B1F541")]
public sealed partial class OmniTrayExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly OmniTrayCommandsProvider _provider = new();
    private bool _isDisposed;

    public OmniTrayExtension(ManualResetEvent extensionDisposedEvent)
    {
        this._extensionDisposedEvent
            = extensionDisposedEvent ?? throw new ArgumentNullException(nameof(extensionDisposedEvent));
    }

    public object? GetProvider(ProviderType providerType) =>
        providerType == ProviderType.Commands ? this._provider : null;

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._provider.Dispose();
        this._extensionDisposedEvent.Set();
    }
}
