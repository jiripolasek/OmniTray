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
}
