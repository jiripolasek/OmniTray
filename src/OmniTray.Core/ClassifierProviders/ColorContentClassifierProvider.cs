// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class ColorContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.color",
        "Color classifier",
        "omnitray.color",
        "Color",
        ContentFacets.Color)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsColor(context.Text);
}
