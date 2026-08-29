// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class XmlContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.xml",
        "XML classifier",
        "omnitray.xml",
        "XML",
        ContentFacets.None)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsXml(context.Text, context.SourcePath);
}
