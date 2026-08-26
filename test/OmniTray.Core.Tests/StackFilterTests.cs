// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackFilterTests
{
    private static readonly DropStack Stack = DropStack.Create(
        [
            DropItem.CreateStorageItem("holiday-photo.png", @"C:\Pictures\holiday-photo.png", false),
            DropItem.CreateText("Remember the blue suitcase")
        ],
        "Travel ideas",
        "Mint");

    [TestMethod]
    [DataRow("")]
    [DataRow("travel")]
    [DataRow("MINT")]
    [DataRow("holiday-photo")]
    [DataRow("pictures")]
    [DataRow("blue suitcase")]
    [DataRow("travel suitcase")]
    [DataRow("2 items")]
    public void Matches_SearchesStackAndItemContent(string query)
    {
        Assert.IsTrue(StackFilter.Matches(Stack, query));
    }

    [TestMethod]
    public void Matches_RequiresEverySearchTerm()
    {
        Assert.IsFalse(StackFilter.Matches(Stack, "travel invoice"));
    }

    [TestMethod]
    public void Matches_SearchesUrlAndSourceMetadata()
    {
        var stack = DropStack.Create(
            [DropItem.CreateUri("https://contoso.example/report", sourceApplicationName: "Microsoft Edge")],
            "Links");

        Assert.IsTrue(StackFilter.Matches(stack, "contoso"));
        Assert.IsTrue(StackFilter.Matches(stack, "edge"));
    }

    [TestMethod]
    public void Matches_SearchesDerivedFacetsAndApplicationLinks()
    {
        var stack = DropStack.Create(
            [
                DropItem.CreateText(
                    "Region\tSales",
                    html: "<table><tr><td>Region</td></tr></table>",
                    applicationLink: "mailto:analyst@example.com")
            ],
            "Data");

        Assert.IsTrue(StackFilter.Matches(stack, "table"));
        Assert.IsTrue(StackFilter.Matches(stack, "email"));
        Assert.IsTrue(StackFilter.Matches(stack, "mailto"));
    }
}
