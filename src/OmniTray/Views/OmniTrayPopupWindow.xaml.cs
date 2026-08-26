// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Views;

public sealed partial class OmniTrayPopupWindow : Window
{
    internal const int DefaultWidthInDips = 420;

    public OmniTrayPopupWindow()
    {
        this.InitializeComponent();
        this.Page.SetOwnerWindow(this);
        this.Closed += this.OnClosed;
    }

    internal void CloseStackInspector() => this.Page.CloseStackInspector();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        this.Closed -= this.OnClosed;
        this.Page.DisposeStackInspector();
    }
}
