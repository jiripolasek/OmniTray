// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum NoteColor
{
    Yellow,
    Peach,
    Pink,
    Lavender,
    Blue,
    Mint
}

public sealed record StickyNote
{
    public StickyNote(
        Guid id,
        string text,
        string? rtf,
        NoteColor color,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A note ID is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        if (updatedAt < createdAt)
        {
            throw new ArgumentException("A note cannot be updated before it was created.", nameof(updatedAt));
        }

        this.Id = id;
        this.Text = text;
        this.Rtf = rtf;
        this.Color = color;
        this.CreatedAt = createdAt;
        this.UpdatedAt = updatedAt;
    }

    public Guid Id { get; }

    public string Text { get; }

    public string? Rtf { get; }

    public NoteColor Color { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public string DisplayName
    {
        get
        {
            var title = string.Join(' ', this.Text.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));
            return title.Length == 0 ? "New note" : title.Length <= 48 ? title : $"{title[..47]}…";
        }
    }

    public static StickyNote Create(string text = "", string? rtf = null, NoteColor color = NoteColor.Yellow)
    {
        var now = DateTimeOffset.UtcNow;
        return new StickyNote(Guid.NewGuid(), text, rtf, color, now, now);
    }

    public StickyNote Update(string text, string? rtf, NoteColor color)
    {
        if (text == this.Text && rtf == this.Rtf && color == this.Color)
        {
            return this;
        }

        var now = DateTimeOffset.UtcNow;
        return new StickyNote(this.Id, text, rtf, color, this.CreatedAt,
            now > this.UpdatedAt ? now : this.UpdatedAt.AddTicks(1));
    }

    public StickyNote Duplicate() => Create(this.Text, this.Rtf, this.Color);
}
