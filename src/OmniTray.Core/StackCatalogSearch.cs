// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public sealed record StackSearchMatch(Guid StackId, Guid? ItemId, string Preview);

public static class StackCatalogSearch
{
    public static IReadOnlyList<StackSearchMatch> Find(
        IEnumerable<DropStack> stacks,
        string? query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        var terms = query?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        if (terms.Length == 0)
        {
            return [];
        }

        var stackMatches = new List<StackSearchMatch>();
        var itemMatches = new List<StackSearchMatch>();
        var seenStacks = new HashSet<Guid>();
        foreach (var stack in stacks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenStacks.Add(stack.Id))
            {
                continue;
            }

            if (Matches(terms, [stack.Name]))
            {
                stackMatches.Add(new StackSearchMatch(stack.Id, null, string.Empty));
            }

            foreach (var item in stack.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = !string.IsNullOrWhiteSpace(item.Text)
                    ? item.Text
                    : !string.IsNullOrWhiteSpace(item.Html)
                        ? ContentDetection.ExtractPlainTextFromHtml(item.Html)
                        : null;
                string?[] fields = [item.DisplayName, item.SourcePath, item.Url, item.ApplicationLink,
                    text, item.SourceUrl, item.SourceApplicationName];
                if (!Matches(terms, fields))
                {
                    continue;
                }

                var preview = fields.Skip(1).FirstOrDefault(field =>
                    !string.IsNullOrWhiteSpace(field) && terms.Any(term => field.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    ?? text ?? item.SourcePath ?? item.Url ?? string.Empty;
                itemMatches.Add(new StackSearchMatch(stack.Id, item.Id, CreatePreview(preview, terms)));
            }
        }

        stackMatches.AddRange(itemMatches);
        return stackMatches;
    }

    private static bool Matches(string[] terms, string?[] fields) =>
        terms.All(term => fields.Any(field => field?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));

    private static string CreatePreview(string text, string[] terms)
    {
        const int maximumLength = 160;
        var firstMatch = terms.Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Max(0, firstMatch - 40);
        var length = Math.Min(maximumLength, text.Length - start);
        var excerpt = text.Substring(start, length);
        var singleLine = string.Join(" ", excerpt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return (start > 0 ? "…" : string.Empty) + singleLine + (start + length < text.Length ? "…" : string.Empty);
    }
}
