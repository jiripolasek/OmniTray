// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class StackFilter
{
    public static bool Matches(DropStack stack, string? query)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var searchableText = new List<string> { stack.Name, stack.Tint, $"{stack.Items.Count} items" };
        foreach (var item in stack.Items)
        {
            searchableText.Add(item.DisplayName);
            searchableText.Add(item.Kind.ToString());
            if (!string.IsNullOrWhiteSpace(item.SourcePath))
            {
                searchableText.Add(item.SourcePath);
            }

            if (!string.IsNullOrWhiteSpace(item.Text))
            {
                searchableText.Add(item.Text);
            }
        }

        var terms = query.Split(
            default(char[]),
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term => searchableText.Any(value =>
            value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
