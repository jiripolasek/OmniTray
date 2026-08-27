namespace OmniTray.Core.ClassifierProviders;

internal sealed class JsonContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.json",
        "JSON classifier",
        "omnitray.json",
        "JSON",
        ContentFacets.Json)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsJson(context.Text, context.SourcePath);
}