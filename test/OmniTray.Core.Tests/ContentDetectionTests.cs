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
    public void ExtractSourceUrlFromHtml_ReadsClipboardSourceHeader()
    {
        const string html = "Version:1.0\r\nSourceURL:https://contoso.example/sheet\r\n<!--StartFragment--><table><tr><td>A</td></tr></table><!--EndFragment-->";

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
        const string html = "Version:1.0\r\n<meta name=ProgId content=Excel.Sheet><TABLE x:str><!--StartFragment--><tr><td>North</td><td>42</td></tr><!--EndFragment--></TABLE>";

        Assert.IsTrue(ContentDetection.ContainsHtmlTable(html));
        Assert.IsFalse(ContentDetection.ContainsHtmlTable("<img src=\"https://example.com/chart.png\">"));
    }
}
