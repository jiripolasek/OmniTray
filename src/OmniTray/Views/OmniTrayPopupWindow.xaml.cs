// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Views;

public sealed partial class OmniTrayPopupWindow : Window
{
    public OmniTrayPopupWindow()
    {
        this.InitializeComponent();
        this.Page.SetOwnerWindow(this);
    }
}
