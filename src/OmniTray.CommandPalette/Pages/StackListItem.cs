// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.CommandPalette.Pages;

internal sealed partial class StackListItem : ListItem
{
    internal StackListItem(DropStack stack)
        : base(new StackItemsPage(stack))
    {
        ArgumentNullException.ThrowIfNull(stack);

        this.Title = stack.Name;
        this.Subtitle = $"{FormatItemCount(stack.Items.Count)} · {stack.Tint}";
        this.Icon = Icons.Stack;
        this.TextToSuggest = stack.Name;
        this.Tags = [new Tag(stack.Tint)];
        this.MoreCommands =
        [
            new CommandContextItem(new OpenOmniTrayCommand(
                OmniTrayActivation.StackUri(stack.Id),
                "Open stack in OmniTray",
                Icons.Open))
        ];
        this.Details = new Details
        {
            Title = stack.Name, Body = CreateDetailsBody(stack), Size = ContentSize.Medium, HeroImage = Icons.Stack
        };
    }

    private static string FormatItemCount(int count) => count == 1 ? "1 item" : $"{count} items";

    private static string CreateDetailsBody(DropStack stack)
    {
        var heading
            = $"**Tint:** {MarkdownText.Escape(stack.Tint)}  \n**Contents:** {FormatItemCount(stack.Items.Count)}";
        if (stack.Items.Count == 0)
        {
            return $"{heading}\n\nThis stack is empty.";
        }

        var visibleItems = stack.Items.Take(20)
            .Select(item => $"- {MarkdownText.Escape(item.DisplayName)}")
            .ToArray();
        var remaining = stack.Items.Count - visibleItems.Length;
        var suffix = remaining > 0 ? $"\n- …and {remaining} more" : string.Empty;
        return $"{heading}\n\n{string.Join('\n', visibleItems)}{suffix}";
    }
}
