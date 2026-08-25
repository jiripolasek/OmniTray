// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;

namespace OmniTray.CommandPalette.Commands;

internal sealed partial class OpenOmniTrayCommand : InvokableCommand
{
    private readonly Uri _activationUri;

    internal OpenOmniTrayCommand(Uri activationUri, string name, IconInfo icon)
    {
        this._activationUri = activationUri ?? throw new ArgumentNullException(nameof(activationUri));
        this.Name = name;
        this.Icon = icon;
    }

    public override CommandResult Invoke()
    {
        try
        {
            Process.Start(new ProcessStartInfo(this._activationUri.AbsoluteUri) { UseShellExecute = true });
            return CommandResult.Dismiss();
        }
        catch (Win32Exception)
        {
            return CommandResult.ShowToast("OmniTray isn't installed or its activation link is unavailable.");
        }
    }
}
