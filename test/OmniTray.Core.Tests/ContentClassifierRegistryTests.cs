// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class ContentClassifierRegistryTests
{
    [TestMethod]
    public void DefaultRegistry_ComposesIndependentBuiltInClassifiers()
    {
        var item = DropItem.CreateText(
            "[{\"region\":\"North\",\"sales\":42}]",
            html: "<table><tr><td>North</td><td>42</td></tr></table>");

        var classification = ContentClassifierRegistry.CreateDefault().Classify(item);

        Assert.IsTrue(classification.Facets.HasFlag(ContentFacets.Tabular));
        Assert.IsTrue(classification.Facets.HasFlag(ContentFacets.Json));
        CollectionAssert.Contains(
            classification.Tags.Select(static tag => tag.Id).ToArray(),
            "omnitray.table");
        CollectionAssert.Contains(
            classification.Tags.Select(static tag => tag.Id).ToArray(),
            "omnitray.json");
        Assert.IsEmpty(classification.Failures);
    }

    [TestMethod]
    public void DefaultRegistry_AddsXmlAsAnExtensibleTag()
    {
        var classification = ContentClassifierRegistry.CreateDefault().Classify(
            DropItem.CreateText("<root><value>42</value></root>"));

        CollectionAssert.Contains(
            classification.Tags.Select(static tag => tag.Id).ToArray(),
            "omnitray.xml");
        Assert.IsEmpty(classification.Failures);
    }

    [TestMethod]
    public void RegisteredProvider_AddsSearchableTagWithoutChangingItemKind()
    {
        var registry = new ContentClassifierRegistry();
        registry.Register(new TestProvider(
            "contoso.invoice-classifier",
            10,
            static context => context.Text?.Contains("invoice", StringComparison.OrdinalIgnoreCase) == true
                ? new ContentClassifierOutput
                {
                    Tags =
                    [
                        new ContentTag
                        {
                            Id = "contoso.invoice",
                            DisplayName = "Invoice",
                            ProviderId = "spoofed.provider",
                            Confidence = 0.92
                        }
                    ]
                }
                : ContentClassifierOutput.Empty));
        var item = DropItem.CreateText("Invoice 42");

        var metadata = ContentMetadataPolicy.GetMetadata(item, registry);

        Assert.AreEqual(DropItemKind.Text, item.Kind);
        Assert.AreEqual("contoso.invoice-classifier", metadata.Tags.Single().ProviderId);
        Assert.AreEqual(0.92, metadata.Tags.Single().Confidence);
        Assert.IsTrue(StackFilter.Matches(DropStack.Create([item]), "invoice", registry));
        Assert.IsTrue(StackFilter.Matches(DropStack.Create([item]), "contoso.invoice", registry));
        Assert.IsTrue(ContentRequirement.All("contoso.invoice").IsSatisfiedBy([metadata]));
        Assert.IsFalse(ContentRequirement.All("contoso.receipt").IsSatisfiedBy([metadata]));
    }

    [TestMethod]
    public void ProviderFailure_IsIsolatedFromRemainingClassifiers()
    {
        var registry = new ContentClassifierRegistry();
        registry.Register(new TestProvider(
            "contoso.broken",
            0,
            static _ => throw new InvalidOperationException("Classifier unavailable")));
        registry.Register(new TestProvider(
            "contoso.working",
            1,
            static _ => new ContentClassifierOutput
            {
                Facets = ContentFacets.Code,
                Tags = [new ContentTag { Id = "contoso.script", DisplayName = "Script" }]
            }));

        var classification = registry.Classify(DropItem.CreateText("echo hello"));

        Assert.IsTrue(classification.Facets.HasFlag(ContentFacets.Code));
        Assert.AreEqual("contoso.script", classification.Tags.Single().Id);
        Assert.AreEqual("contoso.broken", classification.Failures.Single().ProviderId);
        StringAssert.Contains(classification.Failures.Single().Error, "Classifier unavailable");
    }

    [TestMethod]
    public void PriorityAndRegistrationOrder_DeterministicallyResolveDuplicateTags()
    {
        var registry = new ContentClassifierRegistry();
        registry.Register(CreateTagProvider("contoso.later", 20, "Later"));
        registry.Register(CreateTagProvider("contoso.first", 10, "First"));

        var classification = registry.Classify(DropItem.CreateText("value"));

        Assert.AreEqual("First", classification.Tags.Single().DisplayName);
        CollectionAssert.AreEqual(
            new[] { "contoso.first", "contoso.later" },
            registry.Providers.Select(static provider => provider.Id).ToArray());
    }

    [TestMethod]
    public void DuplicateProviderIds_AreRejectedAndProvidersCanBeRemoved()
    {
        var registry = new ContentClassifierRegistry();
        registry.Register(CreateTagProvider("contoso.classifier", 0, "One"));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(CreateTagProvider("contoso.classifier", 1, "Two")));
        Assert.IsTrue(registry.Unregister("contoso.classifier"));
        Assert.IsFalse(registry.Unregister("contoso.classifier"));
        Assert.IsEmpty(registry.Providers);
    }

    [TestMethod]
    public void Providers_CanBeDisabledWithoutLosingTheirRegistration()
    {
        var registry = new ContentClassifierRegistry();
        registry.Register(CreateTagProvider("contoso.optional", 0, "Optional"));

        Assert.IsTrue(registry.SetEnabled("contoso.optional", false));

        Assert.IsFalse(registry.Providers.Single().IsEnabled);
        Assert.IsEmpty(registry.Classify(DropItem.CreateText("value")).Tags);
        Assert.IsTrue(registry.SetEnabled("contoso.optional", true));
        Assert.HasCount(1, registry.Classify(DropItem.CreateText("value")).Tags);
    }

    private static IContentClassifierProvider CreateTagProvider(
        string providerId,
        int priority,
        string displayName) => new TestProvider(
        providerId,
        priority,
        _ => new ContentClassifierOutput
        {
            Tags = [new ContentTag { Id = "contoso.duplicate", DisplayName = displayName }]
        });

    private sealed class TestProvider(
        string id,
        int priority,
        Func<ContentInspectionContext, ContentClassifierOutput> classify) : IContentClassifierProvider
    {
        public string Id { get; } = id;

        public string DisplayName { get; } = id;

        public int Priority { get; } = priority;

        public ContentClassifierOutput Classify(ContentInspectionContext context) => classify(context);
    }
}
