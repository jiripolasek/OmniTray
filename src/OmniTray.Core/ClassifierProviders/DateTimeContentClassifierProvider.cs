// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class DateTimeContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.datetime",
        "Date/time classifier",
        "omnitray.datetime",
        "Date/time",
        ContentFacets.DateTime)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsDateTime(context.Text);
}
