// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.ComponentModel;
using System.Diagnostics;

namespace OmniTray.CommandPalette.Commands;

internal sealed partial class OpenPathCommand : InvokableCommand
{
    private readonly string _path;

    internal OpenPathCommand(string path, DropItemKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this._path = path;
        this.Name = kind == DropItemKind.Folder ? "Open folder" : "Open item";
        this.Icon = kind == DropItemKind.Folder ? Icons.Folder : Icons.Open;
    }

    public override CommandResult Invoke()
    {
        if (!File.Exists(this._path) && !Directory.Exists(this._path))
        {
            return CommandResult.ShowToast("That shelved item is no longer available at its saved path.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(this._path) { UseShellExecute = true });
            return CommandResult.Dismiss();
        }
        catch (Win32Exception)
        {
            return CommandResult.ShowToast("Windows couldn't open that shelved item.");
        }
    }
}
