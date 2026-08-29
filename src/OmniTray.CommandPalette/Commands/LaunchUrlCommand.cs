// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;

namespace OmniTray.CommandPalette.Commands;

internal sealed partial class LaunchUrlCommand : InvokableCommand
{
    private readonly string _url;

    internal LaunchUrlCommand(string url, string name = "Open URL")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        this._url = url;
        this.Name = name;
        this.Icon = Icons.Link;
    }

    public override CommandResult Invoke()
    {
        try
        {
            Process.Start(new ProcessStartInfo(this._url) { UseShellExecute = true });
            return CommandResult.Dismiss();
        }
        catch (Win32Exception)
        {
            return CommandResult.ShowToast("Windows couldn't open that URL.");
        }
    }
}
