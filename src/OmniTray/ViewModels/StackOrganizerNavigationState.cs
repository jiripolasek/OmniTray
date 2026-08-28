// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

using OmniTray.Core;

namespace OmniTray.ViewModels;

// UI-independent navigation state; pages and note-save sessions are owned by the window.
public sealed class StackOrganizerNavigationState
{
    public StackOrganizerPage Page { get; private set; }
    public EdgeShelfSide? ScopeSide { get; private set; }
    public Guid? StackId { get; private set; }
    public bool OpenedFromSearch { get; private set; }
    public string SearchQuery { get; private set; } = string.Empty;
    public StackOrganizerSearchOrigin? SearchOrigin { get; private set; }
    public Guid? LastSearchStackId { get; private set; }
    public Guid? LastSearchItemId { get; private set; }

    public void SelectScope(EdgeShelfSide? side)
    {
        this.ScopeSide = side;
        this.ShowOverview();
    }

    public void ShowOverview()
    {
        this.ClearSearch();
        this.StackId = null;
        this.Page = StackOrganizerPage.Overview;
    }

    public void ShowNotes()
    {
        this.ShowOverview();
        this.ScopeSide = null;
        this.Page = StackOrganizerPage.Notes;
    }

    public void OpenStack(Guid stackId, bool fromSearch = false)
    {
        if (!fromSearch) { this.ClearSearch(); }
        this.OpenedFromSearch = fromSearch;
        this.StackId = stackId;
        this.Page = StackOrganizerPage.Stack;
    }

    public bool BeginSearch(string query, Guid? selectedItemId)
    {
        query = query.Trim();
        if (query.Length == 0) { return false; }
        if (this.Page != StackOrganizerPage.Search && !this.OpenedFromSearch)
        {
            this.SearchOrigin = new(this.Page, this.StackId, selectedItemId);
        }
        this.SearchQuery = query;
        this.LastSearchStackId = null;
        this.LastSearchItemId = null;
        return true;
    }

    public void ShowSearch()
    {
        this.Page = StackOrganizerPage.Search;
        this.StackId = null;
        this.OpenedFromSearch = false;
    }

    public void OpenSearchResult(Guid stackId, Guid? itemId)
    {
        this.LastSearchStackId = stackId;
        this.LastSearchItemId = itemId;
        this.OpenStack(stackId, fromSearch: true);
    }

    private void ClearSearch()
    {
        this.OpenedFromSearch = false;
        this.SearchQuery = string.Empty;
        this.SearchOrigin = null;
        this.LastSearchStackId = null;
        this.LastSearchItemId = null;
    }
}

public enum StackOrganizerPage
{
    Overview,
    Stack,
    Notes,
    Search
}

public sealed record StackOrganizerSearchOrigin(StackOrganizerPage Page, Guid? StackId, Guid? ItemId);
