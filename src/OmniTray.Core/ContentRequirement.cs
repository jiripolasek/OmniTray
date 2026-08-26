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
        IReadOnlyList<ContentProperty> alternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        if (alternatives.Count == 0)
        {
            throw new ArgumentException("At least one content property is required.", nameof(alternatives));
        }

        this.Cardinality = cardinality;
        this.Alternatives = alternatives.Distinct().ToArray();
    }

    public ContentRequirementCardinality Cardinality { get; }

    public IReadOnlyList<ContentProperty> Alternatives { get; }

    public static ContentRequirement All(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.All, alternatives);

    public static ContentRequirement Any(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.Any, alternatives);

    public static ContentRequirement ExactlyOne(params ContentProperty[] alternatives) =>
        new(ContentRequirementCardinality.ExactlyOne, alternatives);

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
            this.Alternatives.Any(property => ContentMetadataPolicy.Matches(item, property)));
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
        ContentRequirementCardinality.All => $"Every item must {DescribeAlternatives(this.Alternatives)}.",
        ContentRequirementCardinality.Any => $"At least one item must {DescribeAlternatives(this.Alternatives)}.",
        ContentRequirementCardinality.ExactlyOne => $"Exactly one item must {DescribeAlternatives(this.Alternatives)}.",
        _ => "The content does not meet this command's requirements."
    };

    private static string DescribeAlternatives(IReadOnlyList<ContentProperty> alternatives)
    {
        var descriptions = alternatives.Select(DescribeProperty).ToArray();
        return descriptions.Length == 1
            ? descriptions[0]
            : string.Join(" or ", descriptions);
    }

    private static string DescribeProperty(ContentProperty property) => property switch
    {
        ContentProperty.HasLocalPath => "have an available local path",
        ContentProperty.HasOriginalPath => "have its original path",
        ContentProperty.HasText => "have text",
        ContentProperty.HasHtml => "have HTML",
        ContentProperty.HasRtf => "have rich text",
        ContentProperty.HasBitmap => "have a bitmap",
        ContentProperty.HasImageFile => "have an image file",
        ContentProperty.HasStorageItem => "have a file or folder representation",
        ContentProperty.HasWebLink => "have a web link",
        ContentProperty.HasApplicationLink => "have an application link",
        ContentProperty.HasCustomFormat => "have a native data format",
        ContentProperty.HasFile => "be a file",
        ContentProperty.HasFolder => "be a folder",
        ContentProperty.CanOpen => "be openable",
        ContentProperty.CanReveal => "be revealable",
        ContentProperty.CanCopy => "be copyable",
        ContentProperty.CanCut => "be cuttable",
        ContentProperty.CanDelete => "be deletable",
        ContentProperty.CanShare => "be shareable",
        ContentProperty.IsTabular => "contain tabular content",
        ContentProperty.IsCode => "contain code",
        ContentProperty.IsEmail => "contain an email address",
        ContentProperty.IsColor => "contain a color",
        _ => "meet the content requirement"
    };
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
