// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public static class StackFilter
{
    public static bool Matches(DropStack stack, string? query) =>
        Matches(stack, query, ContentMetadataPolicy.Classifiers);

    public static bool Matches(
        DropStack stack,
        string? query,
        ContentClassifierRegistry classifiers)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(classifiers);
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

            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                searchableText.Add(item.Url);
            }

            if (!string.IsNullOrWhiteSpace(item.SourceUrl))
            {
                searchableText.Add(item.SourceUrl);
            }

            if (!string.IsNullOrWhiteSpace(item.SourceApplicationName))
            {
                searchableText.Add(item.SourceApplicationName);
                AddSourceTerms(searchableText, item.SourceApplicationName);
            }

            if (!string.IsNullOrWhiteSpace(item.SourcePackageFamilyName))
            {
                searchableText.Add(item.SourcePackageFamilyName);
                AddSourceTerms(searchableText, item.SourcePackageFamilyName);
            }

            if (!string.IsNullOrWhiteSpace(item.SourceApplicationLink))
            {
                searchableText.Add(item.SourceApplicationLink);
            }

            if (!string.IsNullOrWhiteSpace(item.ApplicationLink))
            {
                searchableText.Add(item.ApplicationLink);
            }

            var metadata = ContentMetadataPolicy.GetMetadata(item, classifiers);
            var facets = metadata.Facets;
            foreach (var tag in metadata.Tags)
            {
                searchableText.Add(tag.Id);
                searchableText.Add(tag.DisplayName);
                searchableText.Add(tag.ProviderId);
            }
            if (facets.HasFlag(ContentFacets.Tabular))
            {
                searchableText.Add("table tabular");
            }

            if (facets.HasFlag(ContentFacets.Code))
            {
                searchableText.Add("code");
            }

            if (facets.HasFlag(ContentFacets.Email))
            {
                searchableText.Add("email");
            }

            if (facets.HasFlag(ContentFacets.Color))
            {
                searchableText.Add("color");
            }

            if (facets.HasFlag(ContentFacets.Markdown))
            {
                searchableText.Add("markdown");
            }

            if (facets.HasFlag(ContentFacets.Json))
            {
                searchableText.Add("json");
            }

            if (facets.HasFlag(ContentFacets.DateTime))
            {
                searchableText.Add("date time datetime");
            }

            if (facets.HasFlag(ContentFacets.OcrText))
            {
                searchableText.Add("ocr text");
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

    private static void AddSourceTerms(ICollection<string> searchableText, string source)
    {
        searchableText.Add($"from:{source}");
        foreach (var token in source.Split(
                     [' ', '.', '_', '-'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            searchableText.Add($"from:{token}");
        }
    }
}
