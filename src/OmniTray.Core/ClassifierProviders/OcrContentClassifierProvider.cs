namespace OmniTray.Core.ClassifierProviders;

internal sealed class OcrContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.ocr",
        "OCR classifier",
        "omnitray.ocr",
        "OCR",
        ContentFacets.OcrText)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        context.AvailableFormatIds.Any(static formatId =>
            formatId.Contains("ocr", StringComparison.OrdinalIgnoreCase));
}