// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.ViewModels.Organizer;

public sealed class NoteLibraryEntry(StickyNote note, string location, DateTimeOffset time, bool isDeleted)
{
    public StickyNote Note { get; } = note;
    public string Location { get; } = location;
    public DateTimeOffset Time { get; } = time;
    public bool IsDeleted { get; } = isDeleted;
    public string Preview => this.Note.Text.Length > 220 ? this.Note.Text[..220] + "…" : this.Note.Text;
    public string TimeText => $"{(this.IsDeleted ? "Deleted" : "Updated")} {this.Time.ToLocalTime():g}";
}
