// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using Microsoft.UI.Xaml.Controls.Primitives;

namespace OmniTray.Views;

internal sealed class TrayInspectorPopupHost : IDisposable
{
    private readonly Window _owner;
    private readonly Popup _popup;
    private TrayInspectorPopup? _activeInspector;
    private bool _isClosingInspector;
    private bool _isDisposed;
    private PendingInspector? _pendingInspector;

    public TrayInspectorPopupHost(Window owner, Popup popup)
    {
        this._owner = owner ?? throw new ArgumentNullException(nameof(owner));
        this._popup = popup ?? throw new ArgumentNullException(nameof(popup));
    }

    public void Show(
        FrameworkElement placementTarget,
        DropStackViewModel viewModel,
        TrayInspectorPlacement preferredPlacement)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        ArgumentNullException.ThrowIfNull(viewModel);
        if (this._isDisposed || placementTarget.XamlRoot is null)
        {
            return;
        }

        this._pendingInspector = new PendingInspector(placementTarget, viewModel, preferredPlacement);
        if (this._isClosingInspector)
        {
            return;
        }

        if (this._activeInspector is null)
        {
            this.ShowPendingInspector();
            return;
        }

        var outgoingInspector = this._activeInspector;
        this._activeInspector = null;
        this._isClosingInspector = true;
        outgoingInspector.PrepareForClose(() =>
        {
            outgoingInspector.Dispose();
            this._isClosingInspector = false;
            this.ShowPendingInspector();
        });
    }

    public void Close()
    {
        this._pendingInspector = null;
        if (this._isClosingInspector || this._activeInspector is not { } activeInspector)
        {
            return;
        }

        this._activeInspector = null;
        this._isClosingInspector = true;
        activeInspector.PrepareForClose(() =>
        {
            activeInspector.Dispose();
            this._isClosingInspector = false;
            this.ShowPendingInspector();
        });
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._pendingInspector = null;
        if (this._activeInspector is not { } activeInspector)
        {
            return;
        }

        this._activeInspector = null;
        activeInspector.PrepareForClose(activeInspector.Dispose);
    }

    private void ShowPendingInspector()
    {
        if (this._isDisposed || this._isClosingInspector || this._pendingInspector is not { } pendingInspector)
        {
            return;
        }

        this._pendingInspector = null;
        if (pendingInspector.PlacementTarget.XamlRoot is null)
        {
            return;
        }

        var inspector = new TrayInspectorPopup(
            this._owner,
            this._popup,
            pendingInspector.PlacementTarget,
            pendingInspector.ViewModel,
            pendingInspector.PreferredPlacement);
        this._activeInspector = inspector;
        inspector.Show(TrayInspectorMode.Browse);
    }

    private sealed record PendingInspector(
        FrameworkElement PlacementTarget,
        DropStackViewModel ViewModel,
        TrayInspectorPlacement PreferredPlacement);
}
