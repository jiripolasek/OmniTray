// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class MarkdownContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.markdown",
        "Markdown classifier",
        "omnitray.markdown",
        "Markdown",
        ContentFacets.Markdown)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsMarkdown(context.Text, context.SourcePath);
}
