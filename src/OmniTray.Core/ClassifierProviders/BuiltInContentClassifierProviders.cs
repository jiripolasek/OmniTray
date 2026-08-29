// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.ClassifierProviders;

internal static class BuiltInContentClassifierProviders
{
    public static IReadOnlyList<IContentClassifierProvider> Create() =>
    [
        new TableContentClassifierProvider(),
        new CodeContentClassifierProvider(),
        new EmailContentClassifierProvider(),
        new ColorContentClassifierProvider(),
        new MarkdownContentClassifierProvider(),
        new XmlContentClassifierProvider(),
        new JsonContentClassifierProvider(),
        new DateTimeContentClassifierProvider(),
        new OcrContentClassifierProvider()
    ];
}
