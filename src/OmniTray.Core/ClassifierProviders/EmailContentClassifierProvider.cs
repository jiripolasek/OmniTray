// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal sealed class EmailContentClassifierProvider()
    : SingleTagContentClassifierProvider(
        "omnitray.builtin.email",
        "Email classifier",
        "omnitray.email",
        "Email",
        ContentFacets.Email)
{
    protected override bool IsMatch(ContentInspectionContext context) =>
        ContentDetection.IsEmail(context.Text, context.ApplicationLink);
}
