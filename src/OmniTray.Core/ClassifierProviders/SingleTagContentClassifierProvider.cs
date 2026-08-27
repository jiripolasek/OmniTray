namespace OmniTray.Core.ClassifierProviders;

internal abstract class SingleTagContentClassifierProvider(
    string id,
    string displayName,
    string tagId,
    string tagDisplayName,
    ContentFacets facet) : IContentClassifierProvider
{
    private const int BuiltInPriority = 100;

    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public int Priority => BuiltInPriority;

    public ContentClassifierOutput Classify(ContentInspectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return this.IsMatch(context)
            ? new ContentClassifierOutput
            {
                Facets = facet,
                Tags =
                [
                    new ContentTag
                    {
                        Id = tagId,
                        DisplayName = tagDisplayName
                    }
                ]
            }
            : ContentClassifierOutput.Empty;
    }

    protected abstract bool IsMatch(ContentInspectionContext context);
}