namespace OmniTray.Core.ClassifierProviders;

internal sealed class TableContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.table",
        "Table classifier",
        "omnitray.table",
        "Table",
        ContentFacets.Tabular)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsTabular(context.Text, context.Html, context.Rtf);
}