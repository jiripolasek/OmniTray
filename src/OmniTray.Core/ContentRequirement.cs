// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum ContentRequirementCardinality
{
    All,
    Any,
    ExactlyOne
}

public sealed record ContentRequirement
{
    private ContentRequirement(
        ContentRequirementCardinality cardinality,
        IReadOnlyList<ContentProperty> alternatives,
        IReadOnlyList<string> tagAlternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        ArgumentNullException.ThrowIfNull(tagAlternatives);
        if (alternatives.Count == 0 && tagAlternatives.Count == 0)
        {
            throw new ArgumentException("At least one content property or tag is required.");
        }

        this.Cardinality = cardinality;
        this.Alternatives = alternatives.Distinct().ToArray();
        this.TagAlternatives = tagAlternatives
            .Select(static tagId => string.IsNullOrWhiteSpace(tagId)
                ? throw new ArgumentException("Content tag IDs cannot be empty.", nameof(tagAlternatives))
                : tagId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public ContentRequirementCardinality Cardinality { get; }

    public IReadOnlyList<ContentProperty> Alternatives { get; }

    public IReadOnlyList<string> TagAlternatives { get; }

    public static ContentRequirement All(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.All, alternatives, []);

    public static ContentRequirement Any(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.Any, alternatives, []);

    public static ContentRequirement ExactlyOne(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.ExactlyOne, alternatives, []);

    public static ContentRequirement All(params string[] tagIds) =>
        new(ContentRequirementCardinality.All, [], tagIds);

    public static ContentRequirement Any(params string[] tagIds) =>
        new(ContentRequirementCardinality.Any, [], tagIds);

    public static ContentRequirement ExactlyOne(params string[] tagIds) =>
        new(ContentRequirementCardinality.ExactlyOne, [], tagIds);

    public bool IsSatisfiedBy(IReadOnlyList<DropItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return this.IsSatisfiedBy(items.Select(ContentMetadataPolicy.GetMetadata).ToArray());
    }

    public bool IsSatisfiedBy(IReadOnlyList<ContentMetadata> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return false;
        }

        var matchingItemCount = items.Count(item =>
            this.Alternatives.Any(property => ContentMetadataPolicy.Matches(item, property)) ||
            this.TagAlternatives.Any(tagId => item.Tags.Any(tag =>
                string.Equals(tag.Id, tagId, StringComparison.Ordinal))));
        return this.Cardinality switch
        {
            ContentRequirementCardinality.All => matchingItemCount == items.Count,
            ContentRequirementCardinality.Any => matchingItemCount > 0,
            ContentRequirementCardinality.ExactlyOne => matchingItemCount == 1,
            _ => false
        };
    }

    public string Describe() => this.Cardinality switch
    {
        ContentRequirementCardinality.All => $"Every item must {this.DescribeAlternatives()}.",
        ContentRequirementCardinality.Any => $"At least one item must {this.DescribeAlternatives()}.",
        ContentRequirementCardinality.ExactlyOne => $"Exactly one item must {this.DescribeAlternatives()}.",
        _ => "The content does not meet this command's requirements."
    };

    private string DescribeAlternatives()
    {
        var descriptions = this.Alternatives
            .Select(DescribeProperty)
            .Concat(this.TagAlternatives.Select(static tagId => $"have the ‘{tagId}’ classification"))
            .ToArray();
        return descriptions.Length == 1
            ? descriptions[0]
            : string.Join(" or ", descriptions);
    }

    private static string DescribeProperty(ContentProperty property) =>
        property.RequirementDescription;
}

public static class ContentRequirements
{
    public static bool AreSatisfiedBy(
        IReadOnlyList<ContentRequirement> requirements,
        IReadOnlyList<DropItem> items,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(items);
        return AreSatisfiedBy(
            requirements,
            items.Select(ContentMetadataPolicy.GetMetadata).ToArray(),
            out reason);
    }

    public static bool AreSatisfiedBy(
        IReadOnlyList<ContentRequirement> requirements,
        IReadOnlyList<ContentMetadata> items,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(items);
        foreach (var requirement in requirements)
        {
            if (!requirement.IsSatisfiedBy(items))
            {
                reason = requirement.Describe();
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }
}
