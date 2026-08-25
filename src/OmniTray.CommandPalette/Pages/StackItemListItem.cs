// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette.Pages;

internal sealed partial class StackItemListItem : ListItem
{
    internal StackItemListItem(Guid stackId, DropItem item)
        : base(CreatePrimaryCommand(stackId, item))
    {
        ArgumentNullException.ThrowIfNull(item);

        this.Title = item.DisplayName;
        this.Subtitle = $"{FormatKind(item.Kind)} · {item.CreatedAt.ToLocalTime():g}";
        this.Icon = GetIcon(item.Kind);
        this.TextToSuggest = item.DisplayName;
        this.Tags = [new Tag(FormatKind(item.Kind))];
        this.MoreCommands = CreateMoreCommands(stackId, item);
        this.Details = new Details
        {
            Title = item.DisplayName,
            Body = CreateDetailsBody(item),
            Size = ContentSize.Medium,
            HeroImage = GetIcon(item.Kind)
        };
    }

    private static ICommand CreatePrimaryCommand(Guid stackId, DropItem item)
    {
        if (item.Kind == DropItemKind.Text && !string.IsNullOrWhiteSpace(item.Text))
        {
            return new CopyTextCommand(item.Text) { Name = "Copy text", Icon = Icons.Copy };
        }

        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            return new OpenPathCommand(item.SourcePath, item.Kind);
        }

        return new OpenOmniTrayCommand(
            OmniTrayActivation.StackUri(stackId),
            "Open stack in OmniTray",
            Icons.Open);
    }

    private static IContextItem[] CreateMoreCommands(Guid stackId, DropItem item)
    {
        var commands = new List<IContextItem>
        {
            new CommandContextItem(new OpenOmniTrayCommand(
                OmniTrayActivation.StackUri(stackId),
                "Open stack in OmniTray",
                Icons.Open))
        };

        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            commands.Add(new CommandContextItem(new ShowFileInFolderCommand(item.SourcePath)));
            commands.Add(new CommandContextItem(new CopyTextCommand(item.SourcePath)
            {
                Name = "Copy path", Icon = Icons.Copy
            }));
        }

        return [.. commands];
    }

    private static string CreateDetailsBody(DropItem item)
    {
        var created = item.CreatedAt.ToLocalTime();
        var body = $"**Type:** {FormatKind(item.Kind)}  \n**Shelved:** {created:F}";
        if (!string.IsNullOrWhiteSpace(item.SourcePath))
        {
            body += $"  \n**Path:** `{MarkdownText.Escape(item.SourcePath)}`";
        }

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            body += $"\n\n{MarkdownText.Escape(item.Text)}";
        }

        return body;
    }

    private static string FormatKind(DropItemKind kind) => kind switch
    {
        DropItemKind.File => "File",
        DropItemKind.Folder => "Folder",
        DropItemKind.Text => "Text",
        DropItemKind.Image => "Image",
        _ => "Item"
    };

    private static IconInfo GetIcon(DropItemKind kind) => kind switch
    {
        DropItemKind.File => Icons.File,
        DropItemKind.Folder => Icons.Folder,
        DropItemKind.Text => Icons.Text,
        DropItemKind.Image => Icons.Image,
        _ => Icons.Stack
    };
}
