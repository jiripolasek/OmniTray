// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class ContentExtensibilityTests
{
    [TestMethod]
    public void CustomProperty_ProvidesMatchingAndDescriptionWithoutCentralRegistration()
    {
        var property = new ContentProperty(
            "contoso.has-invoice",
            "contain an invoice",
            static metadata => metadata.Tags.Any(tag => tag.Id == "contoso.invoice"));
        var metadata = CreateMetadata(
            [new ContentTag { Id = "contoso.invoice", DisplayName = "Invoice" }]);
        var requirement = ContentRequirement.All(property);

        Assert.IsTrue(requirement.IsSatisfiedBy([metadata]));
        Assert.AreEqual("Every item must contain an invoice.", requirement.Describe());
    }

    [TestMethod]
    public void MetadataProviders_ComposeAdditiveContributionsAndIsolateFailures()
    {
        var registry = new ContentMetadataProviderRegistry();
        registry.Register(new TestMetadataProvider(
            "contoso.broken",
            0,
            static _ => throw new InvalidOperationException("Unavailable")));
        registry.Register(new TestMetadataProvider(
            "contoso.invoice",
            10,
            static _ => new ContentMetadataContribution
            {
                Representations = ContentRepresentations.Custom, Actions = ContentActions.Copy
            }));

        var composition = registry.Compose(DropItem.CreateText("Invoice"));

        Assert.IsTrue(composition.Contribution.Representations.HasFlag(ContentRepresentations.Custom));
        Assert.IsTrue(composition.Contribution.Actions.HasFlag(ContentActions.Copy));
        Assert.AreEqual("contoso.broken", composition.Failures.Single().ProviderId);
    }

    [TestMethod]
    public async Task DefaultThumbnailRegistry_UsesMetadataBeforePrimaryKindFallback()
    {
        var registry = ContentThumbnailRegistry.CreateDefault();

        var color = await registry.ResolveAsync(
            DropItem.CreateText("#1234AB"),
            new ContentThumbnailRequest());
        var functionalColor = await registry.ResolveAsync(
            DropItem.CreateText("rgb(255, 153, 0)"),
            new ContentThumbnailRequest());
        var email = await registry.ResolveAsync(
            DropItem.CreateText("me@example.com"),
            new ContentThumbnailRequest());
        var mailto = await registry.ResolveAsync(
            DropItem.CreateText("Email me", applicationLink: "mailto:me@example.com"),
            new ContentThumbnailRequest());
        var code = await registry.ResolveAsync(
            DropItem.CreateText("```csharp\nConsole.WriteLine();\n```"),
            new ContentThumbnailRequest());
        var codeFile = await registry.ResolveAsync(
            DropItem.CreateStorageItem("Program.cs", @"C:\Source\Program.cs", false),
            new ContentThumbnailRequest());
        var markdown = await registry.ResolveAsync(
            DropItem.CreateText("# Heading"),
            new ContentThumbnailRequest());
        var xml = await registry.ResolveAsync(
            DropItem.CreateText("<root><value>42</value></root>"),
            new ContentThumbnailRequest());
        var table = await registry.ResolveAsync(
            DropItem.CreateRichText("A\tB", "<table><tr><td>A</td><td>B</td></tr></table>", null),
            new ContentThumbnailRequest());
        var plain = await registry.ResolveAsync(
            DropItem.CreateText("Plain text"),
            new ContentThumbnailRequest());

        Assert.AreEqual(ContentThumbnailKind.ColorSwatch, color.Thumbnail?.Kind);
        Assert.AreEqual(ContentThumbnailChrome.None, color.Thumbnail?.Chrome);
        Assert.AreEqual("omnitray.builtin.thumbnail.color", color.Thumbnail?.ProviderId);
        Assert.AreEqual(ContentThumbnailKind.ColorSwatch, functionalColor.Thumbnail?.Kind);
        Assert.AreEqual("#FF9900", functionalColor.Thumbnail?.Color);
        Assert.AreEqual(ContentThumbnailKind.Glyph, email.Thumbnail?.Kind);
        Assert.AreEqual("\uE715", email.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.email", email.Thumbnail?.ProviderId);
        Assert.AreEqual("\uE715", mailto.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.email", mailto.Thumbnail?.ProviderId);
        Assert.AreEqual("\uE943", code.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.code", code.Thumbnail?.ProviderId);
        Assert.AreEqual("\uE943", codeFile.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.code", codeFile.Thumbnail?.ProviderId);
        Assert.AreEqual("\uE8FD", markdown.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.markdown", markdown.Thumbnail?.ProviderId);
        Assert.AreEqual("\uE950", xml.Thumbnail?.Glyph);
        Assert.AreEqual("omnitray.builtin.thumbnail.xml", xml.Thumbnail?.ProviderId);
        Assert.AreEqual(ContentThumbnailKind.Glyph, table.Thumbnail?.Kind);
        Assert.AreEqual("omnitray.builtin.thumbnail.table", table.Thumbnail?.ProviderId);
        Assert.IsTrue(plain.Thumbnail?.IsFallback);
        Assert.AreEqual("omnitray.builtin.thumbnail.primary-kind", plain.Thumbnail?.ProviderId);
    }

    [TestMethod]
    public async Task ThumbnailProviderFailure_FallsThroughAndCannotSpoofProviderIdentity()
    {
        var registry = new ContentThumbnailRegistry();
        registry.Register(new TestThumbnailProvider(
            "contoso.broken",
            0,
            static (_, _) => throw new InvalidOperationException("Renderer unavailable")));
        registry.Register(new TestThumbnailProvider(
            "contoso.working",
            1,
            static (_, _) => ValueTask.FromResult<ContentThumbnailDescriptor?>(
                ContentThumbnailDescriptor.CreateGlyph("X", "Custom") with { ProviderId = "spoofed.provider" })));

        var result = await registry.ResolveAsync(
            DropItem.CreateText("Value"),
            new ContentThumbnailRequest());

        Assert.AreEqual("contoso.working", result.Thumbnail?.ProviderId);
        Assert.AreEqual("contoso.broken", result.Failures.Single().ProviderId);
    }

    [TestMethod]
    public async Task InvalidEncodedThumbnail_IsRejectedBeforeFallingBack()
    {
        var registry = new ContentThumbnailRegistry();
        registry.Register(new TestThumbnailProvider(
            "contoso.invalid-image",
            0,
            static (_, _) => ValueTask.FromResult<ContentThumbnailDescriptor?>(new ContentThumbnailDescriptor
            {
                Kind = ContentThumbnailKind.EncodedImage,
                MediaType = "image/svg+xml",
                EncodedData = [1, 2, 3],
                AccessibleLabel = "Unsafe"
            })));
        registry.Register(new TestThumbnailProvider(
            "contoso.fallback",
            1,
            static (_, _) => ValueTask.FromResult<ContentThumbnailDescriptor?>(
                ContentThumbnailDescriptor.CreateGlyph("F", "Fallback"))));

        var result = await registry.ResolveAsync(
            DropItem.CreateText("Value"),
            new ContentThumbnailRequest());

        Assert.AreEqual("contoso.fallback", result.Thumbnail?.ProviderId);
        Assert.AreEqual("contoso.invalid-image", result.Failures.Single().ProviderId);
    }

    [TestMethod]
    public async Task InvalidThumbnailChrome_IsRejectedBeforeFallingBack()
    {
        var registry = new ContentThumbnailRegistry();
        registry.Register(new TestThumbnailProvider(
            "contoso.invalid-chrome",
            0,
            static (_, _) => ValueTask.FromResult<ContentThumbnailDescriptor?>(
                ContentThumbnailDescriptor.CreateGlyph("X", "Invalid") with { Chrome = (ContentThumbnailChrome)99 })));
        registry.Register(new TestThumbnailProvider(
            "contoso.fallback",
            1,
            static (_, _) => ValueTask.FromResult<ContentThumbnailDescriptor?>(
                ContentThumbnailDescriptor.CreateGlyph("F", "Fallback"))));

        var result = await registry.ResolveAsync(
            DropItem.CreateText("Value"),
            new ContentThumbnailRequest());

        Assert.AreEqual("contoso.fallback", result.Thumbnail?.ProviderId);
        Assert.AreEqual("contoso.invalid-chrome", result.Failures.Single().ProviderId);
    }

    private static ContentMetadata CreateMetadata(IReadOnlyList<ContentTag>? tags = null) =>
        new(
            ContentRepresentations.Text,
            ContentActions.Copy,
            ContentFacets.None,
            tags ?? [],
            false,
            false,
            false,
            false,
            false);

    private sealed class TestMetadataProvider(
        string id,
        int priority,
        Func<ContentInspectionContext, ContentMetadataContribution> inspect) : IContentMetadataProvider
    {
        public string Id => id;

        public string DisplayName => id;

        public int Priority => priority;

        public ContentMetadataContribution Inspect(ContentInspectionContext context) => inspect(context);
    }

    private sealed class TestThumbnailProvider(
        string id,
        int priority,
        Func<ContentThumbnailContext, CancellationToken, ValueTask<ContentThumbnailDescriptor?>> create) :
        IContentThumbnailProvider
    {
        public string Id => id;

        public string DisplayName => id;

        public int Priority => priority;

        public IReadOnlyList<ContentRequirement> Requirements => [];

        public ValueTask<ContentThumbnailDescriptor?> CreateAsync(
            ContentThumbnailContext context,
            CancellationToken cancellationToken) => create(context, cancellationToken);
    }
}
