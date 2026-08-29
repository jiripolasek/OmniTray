// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class ContentMetadataTests
{
    [TestMethod]
    public void OriginalFile_SeparatesStorageRepresentationFromShellActions()
    {
        var item = DropItem.CreateStorageItem("Report.xlsx", @"C:\Reports\Report.xlsx", false);

        var metadata = ContentMetadataPolicy.GetMetadata(item);

        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.StorageItem));
        Assert.IsTrue(metadata.HasOriginalPath);
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Open));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Reveal));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Copy));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Cut));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Delete));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Share));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.ShowProperties));
    }

    [TestMethod]
    public void OwnedCapture_DoesNotExposeDestructiveShellActions()
    {
        var item = DropItem.CreateImage("Captured chart", @"C:\AppData\chart.png", true);

        var metadata = ContentMetadataPolicy.GetMetadata(item);

        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Bitmap));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Open));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Copy));
        Assert.IsFalse(metadata.Actions.HasFlag(ContentActions.Cut));
        Assert.IsFalse(metadata.Actions.HasFlag(ContentActions.Delete));
        Assert.IsFalse(metadata.HasOriginalPath);
    }

    [TestMethod]
    public void ExcelRange_IsTextWithRichRepresentationsAndTabularFacet()
    {
        const string plainText = "Region\tSales\r\nNorth\t42";
        const string html
            = "Version:1.0\r\n<!--StartFragment--><table><tr><td>Region</td><td>Sales</td></tr></table><!--EndFragment-->";
        const string rtf = @"{\rtf1\ansi Region\tab Sales\par North\tab 42}";
        var item = DropItem.CreateRichText(plainText, html, rtf, sourceApplicationName: "Microsoft Excel");

        var metadata = ContentMetadataPolicy.GetMetadata(item);

        Assert.AreEqual(DropItemKind.Text, item.Kind);
        Assert.AreEqual("Microsoft Excel", item.SourceApplicationName);
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Text));
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Html));
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Rtf));
        Assert.IsFalse(metadata.Representations.HasFlag(ContentRepresentations.Bitmap));
        Assert.IsTrue(metadata.Facets.HasFlag(ContentFacets.Tabular));
    }

    [TestMethod]
    public void All_HasOriginalPath_RequiresEveryItemToMatch()
    {
        var original = DropItem.CreateStorageItem("Report.xlsx", @"C:\Reports\Report.xlsx", false);
        var owned = DropItem.CreateImage("Captured.png", @"C:\AppData\Captured.png", true);

        var requirement = ContentRequirement.All(ContentProperty.HasOriginalPath);

        Assert.IsTrue(requirement.IsSatisfiedBy([original]));
        Assert.IsFalse(requirement.IsSatisfiedBy([original, owned]));
    }

    [TestMethod]
    public void Any_HasBitmapOrHasImageFile_AcceptsAnImageRepresentation()
    {
        var text = DropItem.CreateText("No image here");
        var image = DropItem.CreateImage("Chart.png", @"C:\Pictures\Chart.png");

        var requirement = ContentRequirement.Any(
            ContentProperty.HasBitmap,
            ContentProperty.HasImageFile);

        Assert.IsTrue(requirement.IsSatisfiedBy([text, image]));
        Assert.IsFalse(requirement.IsSatisfiedBy([text]));
    }

    [TestMethod]
    public void ExactlyOne_HasHtml_CountsMatchingItemsNotRepresentations()
    {
        var rich = DropItem.CreateText("Rich", html: "<strong>Rich</strong>");
        var plain = DropItem.CreateText("Plain");
        var anotherRich = DropItem.CreateText("Also rich", html: "<em>Also rich</em>");

        var requirement = ContentRequirement.ExactlyOne(ContentProperty.HasHtml);

        Assert.IsTrue(requirement.IsSatisfiedBy([rich, plain]));
        Assert.IsFalse(requirement.IsSatisfiedBy([rich, anotherRich]));
    }

    [TestMethod]
    public void ApplicationLink_IsPreservedAsARepresentationAndAction()
    {
        var item = DropItem.CreateText(
            "Open draft",
            applicationLink: "mailto:person@example.com?subject=Draft");

        var metadata = ContentMetadataPolicy.GetMetadata(item);
        var plan = DropItemExportPlan.Create([item]);

        Assert.AreEqual("mailto:person@example.com?subject=Draft", item.ApplicationLink);
        Assert.AreEqual(item.ApplicationLink, plan.ApplicationLink);
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.ApplicationLink));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Open));
        Assert.IsTrue(metadata.Facets.HasFlag(ContentFacets.Email));
    }

    [TestMethod]
    public void CodeAndColorRecognition_RemainFacetsOfTextItems()
    {
        var code = DropItem.CreateText("```csharp\nConsole.WriteLine();\n```");
        var color = DropItem.CreateText("#1234AB");

        var codeMetadata = ContentMetadataPolicy.GetMetadata(code);
        var colorMetadata = ContentMetadataPolicy.GetMetadata(color);

        Assert.AreEqual(DropItemKind.Text, code.Kind);
        Assert.IsTrue(codeMetadata.Facets.HasFlag(ContentFacets.Code));
        Assert.AreEqual(DropItemKind.Text, color.Kind);
        Assert.IsTrue(colorMetadata.Facets.HasFlag(ContentFacets.Color));
    }

    [TestMethod]
    public void MarkdownJsonDateAndOcrRecognition_RemainDerivedFacets()
    {
        var markdown = DropItem.CreateText("# Heading\n\n- first");
        var json = DropItem.CreateText("{\"enabled\":true}");
        var date = DropItem.CreateText("2026-08-26T13:26:07+02:00");
        var ocr = DropItem.CreateText("Recognized words").WithMetadata(
            capture: new DropCaptureMetadata
            {
                CaptureId = Guid.NewGuid(),
                CapturedAt = DateTimeOffset.UtcNow,
                Formats =
                [
                    new DataFormatInventoryEntry
                    {
                        FormatId = "Contoso.OcrText", Status = DataFormatReadStatus.Succeeded
                    }
                ]
            });

        Assert.IsTrue(ContentMetadataPolicy.GetMetadata(markdown).Facets.HasFlag(ContentFacets.Markdown));
        Assert.IsTrue(ContentMetadataPolicy.GetMetadata(json).Facets.HasFlag(ContentFacets.Json));
        Assert.IsTrue(ContentMetadataPolicy.GetMetadata(date).Facets.HasFlag(ContentFacets.DateTime));
        Assert.IsTrue(ContentMetadataPolicy.GetMetadata(ocr).Facets.HasFlag(ContentFacets.OcrText));
        Assert.AreEqual(DropItemKind.Text, json.Kind);
    }

    [TestMethod]
    public void ExportPlan_SeparatesApplicationLinkFromSourceAttribution()
    {
        var resource = new DropItemHtmlResource
        {
            ResourceKey = "https://example.com/chart.png", ManagedRelativePath = @"Content\chart.png", Size = 42
        };
        var item = DropItem.CreateText(
                "Open draft",
                html: "<img src=\"https://example.com/chart.png\">",
                applicationLink: "contoso-mail://draft/42")
            .WithMetadata(
                new ContentProvenance
                {
                    ApplicationName = "Contoso Mail",
                    PackageFamilyName = "Contoso.Mail_123",
                    SourceWebLink = "https://example.com/drafts/42",
                    SourceApplicationLink = "contoso-mail://folder/drafts"
                },
                htmlResources: [resource]);

        var plan = DropItemExportPlan.Create([item]);

        Assert.AreEqual("contoso-mail://draft/42", plan.ApplicationLink);
        Assert.AreEqual("contoso-mail://folder/drafts", plan.SourceApplicationLink);
        Assert.AreEqual("Contoso.Mail_123", plan.SourcePackageFamilyName);
        Assert.AreEqual(resource, plan.HtmlResources.Single());
    }

    [TestMethod]
    public void ExcelRange_ExportPlanReAdvertisesExactRichRepresentations()
    {
        const string plainText = "Region\tSales\r\nNorth\t42";
        const string html
            = "Version:1.0\r\nStartHTML:0000000105\r\n<!--StartFragment--><table><tr><td>Region</td><td>Sales</td></tr></table><!--EndFragment-->";
        const string rtf = @"{\rtf1\ansi Region\tab Sales\par North\tab 42}";
        var item = DropItem.CreateRichText(plainText, html, rtf);

        var plan = DropItemExportPlan.Create([item]);

        Assert.AreEqual(plainText, plan.Text);
        Assert.AreEqual(html, plan.Html);
        Assert.AreEqual(rtf, plan.Rtf);
    }

    [TestMethod]
    public void ExcelRange_ExportPlanIncludesPreservedNativeFormats()
    {
        var biffBytes = new byte[] { 0x09, 0x08, 0x10, 0x00 };
        var item = DropItem.CreateRichText("Region\tSales", "<table></table>", "{\\rtf1}")
            .WithCustomFormats(
            [
                DropItemDataFormat.CreateBinary("Biff12", biffBytes),
                DropItemDataFormat.CreateText("Csv", "Region,Sales")
            ]);
        biffBytes[0] = 0;

        var metadata = ContentMetadataPolicy.GetMetadata(item);
        var plan = DropItemExportPlan.Create([item]);

        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Custom));
        Assert.HasCount(2, plan.CustomFormats);
        Assert.AreEqual("Biff12", plan.CustomFormats[0].FormatId);
        CollectionAssert.AreEqual(
            new byte[] { 0x09, 0x08, 0x10, 0x00 },
            plan.CustomFormats[0].GetBinaryData());
        Assert.AreEqual("Region,Sales", plan.CustomFormats[1].Text);
    }

    [TestMethod]
    public void MultipleItems_DoNotAdvertiseOneItemsNativeFormatsForTheWholeSelection()
    {
        var excelRange = DropItem.CreateText("Region\tSales")
            .WithCustomFormats([DropItemDataFormat.CreateBinary("Biff12", [1, 2, 3])]);

        var plan = DropItemExportPlan.Create([excelRange, DropItem.CreateText("Another item")]);

        Assert.IsEmpty(plan.CustomFormats);
    }

    [TestMethod]
    public void RepresentationUpdates_PreserveNativeFormatsAndApplicationLink()
    {
        var item = DropItem.CreateText(
                "Region\tSales",
                applicationLink: "mailto:analyst@example.com")
            .WithCustomFormats([DropItemDataFormat.CreateText("Csv", "Region,Sales")]);

        var updated = item.WithRepresentations(html: "<table></table>");

        Assert.HasCount(1, updated.CustomFormats);
        Assert.AreEqual("Csv", updated.CustomFormats[0].FormatId);
        Assert.AreEqual(item.ApplicationLink, updated.ApplicationLink);
    }

    [TestMethod]
    public void PreviouslyCapturedExcelRange_DoesNotExportImageFallback()
    {
        const string plainText = "Region\tSales\r\nNorth\t42";
        const string html
            = "Version:1.0\r\n<!--StartFragment--><table><tr><td>Region</td><td>Sales</td></tr></table><!--EndFragment-->";
        const string rtf = @"{\rtf1\ansi Region\tab Sales\par North\tab 42}";
        var item = DropItem.CreateImage(
            "Dropped image",
            @"C:\AppData\excel-range.png",
            true,
            plainText,
            html,
            rtf,
            sourceApplicationName: "Microsoft Excel");

        var metadata = ContentMetadataPolicy.GetMetadata(item);
        var plan = DropItemExportPlan.Create([item]);

        Assert.AreEqual(DropItemKind.Image, item.Kind);
        Assert.IsTrue(metadata.Facets.HasFlag(ContentFacets.Tabular));
        Assert.IsFalse(metadata.Representations.HasFlag(ContentRepresentations.Bitmap));
        Assert.AreEqual(plainText, plan.Text);
        Assert.AreEqual(html, plan.Html);
        Assert.AreEqual(rtf, plan.Rtf);
        Assert.IsFalse(plan.IncludesBitmap);
        Assert.IsFalse(plan.IncludesStorageItems);
    }

    [TestMethod]
    public void BrowserImage_StillExportsBitmapAndStorageFile()
    {
        var item = DropItem.CreateImage(
            "Chart",
            @"C:\AppData\chart.png",
            true,
            html: "<!--StartFragment--><img src=\"https://example.com/chart.png\"><!--EndFragment-->");

        var metadata = ContentMetadataPolicy.GetMetadata(item);
        var plan = DropItemExportPlan.Create([item]);

        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.Bitmap));
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.StorageItem));
        Assert.IsTrue(plan.IncludesBitmap);
        Assert.IsTrue(plan.IncludesStorageItems);
    }

    [TestMethod]
    public void UrlItem_ExposesWebLinkAndOpenAction()
    {
        var item = DropItem.CreateUri("https://www.example.com/article", "Example article");

        var metadata = ContentMetadataPolicy.GetMetadata(item);

        Assert.AreEqual(DropItemKind.Uri, item.Kind);
        Assert.AreEqual("https://www.example.com/article", item.Url);
        Assert.IsTrue(metadata.Representations.HasFlag(ContentRepresentations.WebLink));
        Assert.IsTrue(metadata.Actions.HasFlag(ContentActions.Open));
    }
}
