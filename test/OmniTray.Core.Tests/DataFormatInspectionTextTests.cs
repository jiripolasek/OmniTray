// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class DataFormatInspectionTextTests
{
    [TestMethod]
    public void CreatePreview_MakesClipboardWhitespaceVisibleAndTruncates()
    {
        Assert.AreEqual("A\\tB\\r\\nC…", DataFormatInspectionText.CreatePreview("A\tB\r\nCD", 9));
    }

    [TestMethod]
    [DataRow(42UL, "42 bytes")]
    [DataRow(1024UL, "1,024 bytes (1 KiB)")]
    [DataRow(1572864UL, "1,572,864 bytes (1.5 MiB)")]
    public void FormatByteCount_ShowsExactAndReadableSizes(ulong byteCount, string expected)
    {
        Assert.AreEqual(expected, DataFormatInspectionText.FormatByteCount(byteCount));
    }
}
