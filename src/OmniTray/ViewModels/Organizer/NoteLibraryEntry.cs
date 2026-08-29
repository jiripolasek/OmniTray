// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.ViewModels.Organizer;

public sealed class NoteLibraryEntry(
    StickyNote note,
    NoteTarget target,
    string location,
    DateTimeOffset time,
    bool isDeleted)
{
    public StickyNote Note { get; } = note;
    public NoteTarget Target { get; } = target;
    public string Location { get; } = location;
    public DateTimeOffset Time { get; } = time;
    public bool IsDeleted { get; } = isDeleted;
    public bool CanGoToStack => !this.IsDeleted;
    public bool CanChangeColor => !this.IsDeleted;
    public string OpenLabel => this.IsDeleted ? "Restore" : "Open";
    public string DeleteLabel => this.IsDeleted ? "Delete permanently" : "Delete";
    public string Preview => this.Note.Text.Length > 220 ? this.Note.Text[..220] + "…" : this.Note.Text;
    public string TimeText => $"{(this.IsDeleted ? "Deleted" : "Updated")} {this.Time.ToLocalTime():g}";
}
