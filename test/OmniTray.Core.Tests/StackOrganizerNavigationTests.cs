// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using OmniTray.ViewModels;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class StackOrganizerNavigationTests
{
    [TestMethod]
    public void SearchResultRoundTrip_PreservesOriginalStackAndItem()
    {
        var navigation = new StackOrganizerNavigationState();
        var originStack = Guid.NewGuid();
        var originItem = Guid.NewGuid();
        var resultStack = Guid.NewGuid();
        var resultItem = Guid.NewGuid();
        navigation.SelectScope(EdgeShelfSide.Left);
        navigation.OpenStack(originStack);

        Assert.IsTrue(navigation.BeginSearch("  document  ", originItem));
        navigation.ShowSearch();
        navigation.OpenSearchResult(resultStack, resultItem);

        Assert.AreEqual(StackOrganizerPage.Stack, navigation.Page);
        Assert.AreEqual(resultStack, navigation.StackId);
        Assert.IsTrue(navigation.OpenedFromSearch);

        navigation.ShowSearch();

        Assert.AreEqual("document", navigation.SearchQuery);
        Assert.AreEqual(new StackOrganizerSearchOrigin(StackOrganizerPage.Stack, originStack, originItem), navigation.SearchOrigin);
        Assert.AreEqual(resultStack, navigation.LastSearchStackId);
        Assert.AreEqual(resultItem, navigation.LastSearchItemId);
        Assert.AreEqual(EdgeShelfSide.Left, navigation.ScopeSide);
        Assert.IsNull(navigation.StackId);
    }

    [TestMethod]
    public void NewQueryFromSearchResult_PreservesNotesOrigin()
    {
        var navigation = new StackOrganizerNavigationState();
        navigation.ShowNotes();
        navigation.BeginSearch("first", null);
        navigation.OpenSearchResult(Guid.NewGuid(), Guid.NewGuid());

        navigation.BeginSearch("second", Guid.NewGuid());
        navigation.ShowSearch();

        Assert.AreEqual(new StackOrganizerSearchOrigin(StackOrganizerPage.Notes, null, null), navigation.SearchOrigin);
        Assert.AreEqual("second", navigation.SearchQuery);
        Assert.IsNull(navigation.LastSearchStackId);
        Assert.IsNull(navigation.LastSearchItemId);
    }

    [TestMethod]
    public void SelectScope_LeavesSearchAndClearsItsHistory()
    {
        var navigation = new StackOrganizerNavigationState();
        navigation.OpenStack(Guid.NewGuid());
        navigation.BeginSearch("query", Guid.NewGuid());
        navigation.OpenSearchResult(Guid.NewGuid(), Guid.NewGuid());

        navigation.SelectScope(EdgeShelfSide.Right);

        Assert.AreEqual(StackOrganizerPage.Overview, navigation.Page);
        Assert.AreEqual(EdgeShelfSide.Right, navigation.ScopeSide);
        Assert.IsNull(navigation.StackId);
        Assert.IsFalse(navigation.OpenedFromSearch);
        Assert.AreEqual(string.Empty, navigation.SearchQuery);
        Assert.IsNull(navigation.SearchOrigin);
        Assert.IsNull(navigation.LastSearchStackId);
        Assert.IsNull(navigation.LastSearchItemId);
    }

    [TestMethod]
    public void EmptySearch_DoesNotChangeNavigation()
    {
        var navigation = new StackOrganizerNavigationState();
        var stackId = Guid.NewGuid();
        navigation.OpenStack(stackId);

        Assert.IsFalse(navigation.BeginSearch(" \t ", Guid.NewGuid()));
        Assert.AreEqual(StackOrganizerPage.Stack, navigation.Page);
        Assert.AreEqual(stackId, navigation.StackId);
        Assert.IsNull(navigation.SearchOrigin);
    }

    [TestMethod]
    public void ShowOverview_PreservesEdgeScopeAfterSearch()
    {
        var navigation = new StackOrganizerNavigationState();
        navigation.SelectScope(EdgeShelfSide.Top);
        navigation.BeginSearch("query", null);
        navigation.ShowSearch();

        navigation.ShowOverview();

        Assert.AreEqual(EdgeShelfSide.Top, navigation.ScopeSide);
        Assert.AreEqual(StackOrganizerPage.Overview, navigation.Page);
        Assert.IsNull(navigation.SearchOrigin);
    }

    [TestMethod]
    public void ShowNotes_ClearsStackAndScope()
    {
        var navigation = new StackOrganizerNavigationState();
        navigation.SelectScope(EdgeShelfSide.Bottom);
        navigation.OpenStack(Guid.NewGuid());

        navigation.ShowNotes();

        Assert.AreEqual(StackOrganizerPage.Notes, navigation.Page);
        Assert.IsNull(navigation.ScopeSide);
        Assert.IsNull(navigation.StackId);
    }

    [TestMethod]
    public void OpenStackNormally_EndsSearchNavigation()
    {
        var navigation = new StackOrganizerNavigationState();
        navigation.BeginSearch("query", null);
        navigation.OpenSearchResult(Guid.NewGuid(), Guid.NewGuid());
        var stackId = Guid.NewGuid();

        navigation.OpenStack(stackId);

        Assert.AreEqual(stackId, navigation.StackId);
        Assert.IsFalse(navigation.OpenedFromSearch);
        Assert.IsNull(navigation.SearchOrigin);
        Assert.AreEqual(string.Empty, navigation.SearchQuery);
    }
}
