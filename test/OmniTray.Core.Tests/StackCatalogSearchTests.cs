// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackCatalogSearchTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t\r\n ")]
    public void Find_EmptyQueryHasNoResults(string? query)
    {
        Assert.IsEmpty(StackCatalogSearch.Find([DropStack.CreateEmpty("Travel")], query));
    }

    [TestMethod]
    public void Find_StackAndItemMatchesAreSeparateAndStacksComeFirst()
    {
        var item = DropItem.CreateText("Travel checklist");
        var first = DropStack.Create([item], "Notes");
        var second = DropStack.CreateEmpty("Travel plans");

        CollectionAssert.AreEqual(
            new[]
            {
                new StackSearchMatch(second.Id, null, ""),
                new StackSearchMatch(first.Id, item.Id, "Travel checklist")
            },
            StackCatalogSearch.Find([first, second], "travel").ToArray());
    }

    [TestMethod]
    public void Find_MatchesBothStackAndItsItemWithoutDuplicateResults()
    {
        var item = DropItem.CreateText("Travel");
        var stack = DropStack.Create([item], "Travel");

        var matches = StackCatalogSearch.Find([stack], "travel TRAVEL");

        Assert.HasCount(2, matches);
        Assert.IsNull(matches[0].ItemId);
        Assert.AreEqual(item.Id, matches[1].ItemId);
    }

    [TestMethod]
    public void Find_MatchesEmptyStacks()
    {
        var stack = DropStack.CreateEmpty("Project ideas");
        var matches = StackCatalogSearch.Find([stack], " IDEAS\tproject ");

        Assert.HasCount(1, matches);
        Assert.AreEqual(stack.Id, matches[0].StackId);
        Assert.IsNull(matches[0].ItemId);
    }

    [TestMethod]
    public void Find_RequiresEveryTermButCanMatchDifferentItemFields()
    {
        var item = DropItem.CreateStorageItem("Launch.pdf", @"C:\Work\Projects\brief.pdf", false);
        var stack = DropStack.Create([item], "Documents");

        Assert.AreEqual(item.Id, StackCatalogSearch.Find([stack], " LAUNCH\tprojects ").Single().ItemId);
        Assert.IsEmpty(StackCatalogSearch.Find([stack], "launch invoice"));
    }

    [TestMethod]
    public void Find_DoesNotCombineUnrelatedStackOrItemText()
    {
        var stack = DropStack.Create([DropItem.CreateText("Blue"), DropItem.CreateText("Suitcase")], "Travel");

        Assert.IsEmpty(StackCatalogSearch.Find([stack], "blue suitcase"));
        Assert.IsEmpty(StackCatalogSearch.Find([stack], "travel blue"));
    }

    [TestMethod]
    public void Find_SearchesSavedTextBeyondTheCompactDisplayName()
    {
        var item = DropItem.CreateText(new string('a', 200) + " hiddenneedle " + new string('b', 200));
        var match = StackCatalogSearch.Find([DropStack.Create([item], "Notes")], "hiddenneedle").Single();

        Assert.DoesNotContain("hiddenneedle", item.DisplayName);
        Assert.AreEqual(item.Id, match.ItemId);
        StringAssert.Contains(match.Preview, "hiddenneedle");
        Assert.IsLessThanOrEqualTo(162, match.Preview.Length);
        StringAssert.StartsWith(match.Preview, "…");
        StringAssert.EndsWith(match.Preview, "…");
    }

    [TestMethod]
    public void Find_PreviewIsSingleLine()
    {
        var item = DropItem.CreateText("Shopping\r\n\tblue   suitcase");

        Assert.AreEqual("Shopping blue suitcase",
            StackCatalogSearch.Find([DropStack.Create([item], "Notes")], "blue").Single().Preview);
    }

    [TestMethod]
    [DataRow("contoso.example")]
    [DataRow("research.example")]
    [DataRow("microsoft edge")]
    [DataRow("onenote:")]
    public void Find_SearchesLinksAndSourceMetadata(string query)
    {
        var item = DropItem.CreateUri("https://contoso.example/report", "Report",
            sourceUrl: "https://research.example/", sourceApplicationName: "Microsoft Edge",
            applicationLink: "onenote:https://contoso.example/notebook");

        Assert.AreEqual(item.Id, StackCatalogSearch.Find([DropStack.Create([item], "Links")], query).Single().ItemId);
    }

    [TestMethod]
    public void Find_SearchesHtmlTextWithoutSearchingMarkupOrScripts()
    {
        var item = DropItem.CreateRichText(null,
            "<p class='hiddenclass'>Blue &amp; green suitcase</p><script>secretScript()</script><style>.privateStyle { color: red }</style>",
            null);
        var stack = DropStack.Create([item], "Notes");

        Assert.AreEqual(item.Id, StackCatalogSearch.Find([stack], "green &").Single().ItemId);
        Assert.IsEmpty(StackCatalogSearch.Find([stack], "hiddenclass"));
        Assert.IsEmpty(StackCatalogSearch.Find([stack], "secretScript"));
        Assert.IsEmpty(StackCatalogSearch.Find([stack], "privateStyle"));
    }

    [TestMethod]
    public void Find_SearchesEveryItemKindWithoutReadingFiles()
    {
        DropItem[] items =
        [
            DropItem.CreateStorageItem("Needle.pdf", @"Z:\does-not-exist\Needle.pdf", false),
            DropItem.CreateStorageItem("Needle folder", @"Z:\does-not-exist\Needle", true),
            DropItem.CreateImage("Needle.png", @"Z:\does-not-exist\Needle.png"),
            DropItem.CreateText("Needle note"),
            DropItem.CreateUri("https://example.test/needle")
        ];

        CollectionAssert.AreEqual(items.Select(item => item.Id).ToArray(),
            StackCatalogSearch.Find([DropStack.Create(items, "Collection")], "needle")
                .Select(match => match.ItemId!.Value).ToArray());
    }

    [TestMethod]
    public void Find_SharedStacksAppearOnlyOnce()
    {
        var item = DropItem.CreateText("Travel");
        var stack = DropStack.Create([item], "Travel");

        Assert.HasCount(2, StackCatalogSearch.Find([stack, stack, stack], "travel"));
    }

    [TestMethod]
    public void Find_TheSameItemInDifferentStacksKeepsBothNavigationTargets()
    {
        var item = DropItem.CreateText("Travel");
        var first = DropStack.Create([item], "First");
        var second = DropStack.Create([item], "Second");

        CollectionAssert.AreEqual(new[] { first.Id, second.Id },
            StackCatalogSearch.Find([first, second], "travel").Select(match => match.StackId).ToArray());
    }

    [TestMethod]
    public void Find_CancelledSearchStops()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            StackCatalogSearch.Find([DropStack.CreateEmpty("Travel")], "travel", cancellation.Token));
    }
}
