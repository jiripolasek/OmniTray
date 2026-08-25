// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette;

public sealed partial class OmniTrayCommandsProvider : CommandProvider
{
    private readonly OmniTrayCatalogSource _catalog = new();
    private readonly ICommandItem[] _commands;
    private readonly OmniTrayStacksPage _stacksPage;
    private bool _isDisposed;

    public OmniTrayCommandsProvider()
    {
        this.Id = "OmniTray.CommandPalette";
        this.DisplayName = "OmniTray";
        this.Icon = Icons.Main;

        this._stacksPage = new OmniTrayStacksPage(this._catalog);
        this._commands =
        [
            new CommandItem(this._stacksPage)
            {
                Title = "OmniTray stacks",
                Subtitle = "Browse, search, and retrieve shelved content",
                Icon = Icons.Main,
                MoreCommands =
                [
                    new CommandContextItem(new OpenOmniTrayCommand(
                        OmniTrayActivation.SettingsUri,
                        "Open OmniTray settings",
                        Icons.Settings))
                ]
            },
            new CommandItem(new OpenOmniTrayCommand(
                OmniTrayActivation.OpenUri,
                "Open OmniTray",
                Icons.Open)) { Title = "Open OmniTray", Subtitle = "Show the OmniTray popup", Icon = Icons.Open },
            new CommandItem(new OpenOmniTrayCommand(
                OmniTrayActivation.NewStackUri,
                "Create a new stack",
                Icons.Add))
            {
                Title = "Create a new OmniTray stack", Subtitle = "Open an empty tray ready for drops", Icon = Icons.Add
            }
        ];
    }

    public override ICommandItem[] TopLevelCommands() => this._commands;

    public override void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._stacksPage.Dispose();
        this._catalog.Close();
        base.Dispose();
    }
}
