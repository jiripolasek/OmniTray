// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class ContentDetectionTests
{
    [TestMethod]
    [DataRow("https://example.com/path?q=1", "https://example.com/path?q=1")]
    [DataRow("  HTTP://EXAMPLE.COM/report  ", "http://example.com/report")]
    public void TryNormalizeWebUrl_AcceptsHttpAndHttps(string value, string expected)
    {
        Assert.IsTrue(ContentDetection.TryNormalizeWebUrl(value, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(@"C:\Reports\table.xlsx")]
    [DataRow("mailto:someone@example.com")]
    [DataRow("two words")]
    public void TryNormalizeWebUrl_RejectsNonWebContent(string value)
    {
        Assert.IsFalse(ContentDetection.TryNormalizeWebUrl(value, out _));
    }

    [TestMethod]
    [DataRow("mailto:someone@example.com", "mailto:someone@example.com")]
    [DataRow("  spotify:track:0123456789  ", "spotify:track:0123456789")]
    [DataRow("contoso-app://message/42", "contoso-app://message/42")]
    public void TryNormalizeApplicationLink_AcceptsNonWebSchemes(string value, string expected)
    {
        Assert.IsTrue(ContentDetection.TryNormalizeApplicationLink(value, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("https://example.com")]
    [DataRow("http://example.com")]
    [DataRow(@"C:\Reports\table.xlsx")]
    [DataRow("two words")]
    public void TryNormalizeApplicationLink_RejectsWebLinksPathsAndPlainText(string value)
    {
        Assert.IsFalse(ContentDetection.TryNormalizeApplicationLink(value, out _));
    }

    [TestMethod]
    public void ExtractApplicationLinkFromHtml_ReadsNonWebHref()
    {
        const string html
            = "<!--StartFragment--><a href=\"mailto:someone&#64;example.com\">Email someone</a><!--EndFragment-->";

        Assert.AreEqual(
            "mailto:someone@example.com",
            ContentDetection.ExtractApplicationLinkFromHtml(html));
    }

    [TestMethod]
    public void ExtractSourceUrlFromHtml_ReadsClipboardSourceHeader()
    {
        const string html
            = "Version:1.0\r\nSourceURL:https://contoso.example/sheet\r\n<!--StartFragment--><table><tr><td>A</td></tr></table><!--EndFragment-->";

        Assert.AreEqual(
            "https://contoso.example/sheet",
            ContentDetection.ExtractSourceUrlFromHtml(html));
    }

    [TestMethod]
    public void ExtractPlainTextFromHtml_PreservesCellContentForPreview()
    {
        const string html = "<!--StartFragment--><table><tr><td>North</td><td>42</td></tr></table><!--EndFragment-->";

        Assert.AreEqual("North 42", ContentDetection.ExtractPlainTextFromHtml(html));
    }

    [TestMethod]
    public void ContainsHtmlTable_DetectsExcelStyleClipboardMarkup()
    {
        const string html
            = "Version:1.0\r\n<meta name=ProgId content=Excel.Sheet><TABLE x:str><!--StartFragment--><tr><td>North</td><td>42</td></tr><!--EndFragment--></TABLE>";

        Assert.IsTrue(ContentDetection.ContainsHtmlTable(html));
        Assert.IsFalse(ContentDetection.ContainsHtmlTable("<img src=\"https://example.com/chart.png\">"));
    }

    [TestMethod]
    [DataRow("<?xml version=\"1.0\"?><root><value>42</value></root>")]
    [DataRow("<root><value /></root>")]
    public void IsXml_AcceptsWellFormedXml(string value)
    {
        Assert.IsTrue(ContentDetection.IsXml(value));
    }

    [TestMethod]
    [DataRow("<root>")]
    [DataRow("{\"value\":42}")]
    [DataRow("Plain text")]
    public void IsXml_RejectsMalformedOrNonXmlText(string value)
    {
        Assert.IsFalse(ContentDetection.IsXml(value));
    }

    [TestMethod]
    public void IsXml_AcceptsXmlFileExtensionWithoutMaterializedText()
    {
        Assert.IsTrue(ContentDetection.IsXml(null, @"C:\Reports\data.xml"));
    }

    [TestMethod]
    [DataRow("#f90", "#FF9900")]
    [DataRow("#ff990080", "#FF990080")]
    [DataRow("rgb(255, 153, 0)", "#FF9900")]
    [DataRow("rgb(100% 60% 0%)", "#FF9900")]
    [DataRow("rgba(255, 153, 0, 0.5)", "#FF990080")]
    [DataRow("hsl(36, 100%, 50%)", "#FF9900")]
    [DataRow("hsla(36deg 100% 50% / 50%)", "#FF990080")]
    public void TryNormalizeCssColor_NormalizesSupportedCssForms(string value, string expected)
    {
        Assert.IsTrue(ContentDetection.TryNormalizeCssColor(value, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow("rgb(999, 0, 0)")]
    [DataRow("rgba(255, 0, 0)")]
    [DataRow("hsl(36, nope, 50%)")]
    [DataRow("not-a-color")]
    public void TryNormalizeCssColor_RejectsInvalidCssForms(string value)
    {
        Assert.IsFalse(ContentDetection.TryNormalizeCssColor(value, out _));
        Assert.IsFalse(ContentDetection.IsColor(value));
    }

    [TestMethod]
    [DataRow("video/mp4", ".bin")]
    [DataRow("VIDEO/QUICKTIME", ".bin")]
    [DataRow("application/octet-stream", ".mp4")]
    [DataRow(null, ".MKV")]
    public void IsVideoFile_AcceptsVideoMimeTypeOrKnownExtension(string? contentType, string fileExtension)
    {
        Assert.IsTrue(ContentDetection.IsVideoFile(contentType, fileExtension));
    }

    [TestMethod]
    [DataRow("image/jpeg", ".jpg")]
    [DataRow("application/zip", ".zip")]
    [DataRow(null, ".vhdx")]
    public void IsVideoFile_RejectsNonVideoContent(string? contentType, string fileExtension)
    {
        Assert.IsFalse(ContentDetection.IsVideoFile(contentType, fileExtension));
    }

    [TestMethod]
    [DataRow(true, true, 190u, 130u, true)]
    [DataRow(false, false, 256u, 256u, true)]
    [DataRow(false, false, 256u, 250u, true)]
    [DataRow(false, false, 256u, 220u, false)]
    [DataRow(false, true, 256u, 256u, false)]
    [DataRow(false, false, 0u, 0u, false)]
    public void IsLikelyShellIconThumbnail_DistinguishesIconCanvasesFromVisualContent(
        bool isReportedIcon,
        bool hasIntrinsicVisualContent,
        uint width,
        uint height,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            ContentDetection.IsLikelyShellIconThumbnail(
                isReportedIcon,
                hasIntrinsicVisualContent,
                width,
                height));
    }
}
