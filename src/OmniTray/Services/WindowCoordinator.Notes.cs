// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Services;

internal sealed partial class WindowCoordinator
{
    private readonly Dictionary<Guid, NoteWindow> _noteWindows = [];

    internal void ShowNotes(bool deleted)
    {
        this.HidePopup();
        this.GetStackOrganizerWindow().SelectNotes(deleted);
    }

    internal void ShowNoteOwner(Guid stackId, Guid? itemId)
    {
        var stack = this._viewModel.Stacks.FirstOrDefault(stack => stack.Model.Id == stackId);
        if (stack is not null)
        {
            this.ShowStackOrganizer(stack);
            if (itemId is { } id) { this._stackOrganizerWindow?.RevealItem(id); }
        }
    }

    public void SetNoteEditingEnabled(bool enabled)
    {
        this._stackOrganizerWindow?.SetNoteEditingEnabled(enabled);
        foreach (var window in this._noteWindows.Values)
        {
            window.SetEditingEnabled(enabled);
        }
    }

    public void ShowNote(Guid noteId)
    {
        if (this._viewModel.FindNote(noteId) is not { } location)
        {
            return;
        }

        if (!this._noteWindows.TryGetValue(noteId, out var window))
        {
            window = new NoteWindow(this._viewModel, location.Note);
            this._noteWindows.Add(noteId, window);
            window.Closed += (_, _) => this._noteWindows.Remove(noteId);
            CenterWindow(window, 400, 400);
        }

        this.HidePopup();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter &&
            presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        window.Activate();
    }
}
