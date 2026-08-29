// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public sealed record DeletedNote(
    StickyNote Note,
    NoteTarget Target,
    string StackName,
    string? ItemName,
    DateTimeOffset DeletedAt);

public sealed record NoteCaptureHistory(
    Guid NoteId,
    Guid SourceStackId,
    string SourceStackName,
    DropItem SourceItem,
    int SourceIndex,
    bool IsConversion);

public static class NoteRecovery
{
    public static (IReadOnlyList<DeletedNote> DeletedNotes, IReadOnlyList<NoteCaptureHistory> History) RecordCapture(
        IReadOnlyList<DeletedNote> deleted,
        IReadOnlyList<NoteCaptureHistory> history,
        NoteCaptureHistory capture)
    {
        var entries = deleted.ToList();
        var sources = history.ToList();
        var previous = entries.FindIndex(entry => entry.Note.Id == capture.NoteId);
        if (previous >= 0)
        {
            // An undone conversion leaves the edited note in recovery and restores the text
            // item's identity. Re-conversion must not overwrite that earlier edited note.
            var entry = entries[previous];
            var note = entry.Note;
            var id = Guid.NewGuid();
            entries[previous] = entry with
            {
                Note = new StickyNote(id, note.Text, note.Rtf, note.Color, note.CreatedAt, note.UpdatedAt)
            };
            sources = sources.Select(source => source.NoteId == note.Id ? source with { NoteId = id } : source)
                .ToList();
        }

        if (sources.Any(source => source.NoteId == capture.NoteId))
        {
            throw new ArgumentException("Capture history already exists for this note.", nameof(capture));
        }

        sources.Add(capture);
        return (entries, sources);
    }

    public static void ValidateHistory(
        IReadOnlyList<DropStack> stacks,
        IReadOnlyList<DeletedNote> deleted,
        IReadOnlyList<NoteCaptureHistory> history)
    {
        var noteIds = NoteOperations.Enumerate(stacks).Select(location => location.Note.Id).ToHashSet();
        foreach (var entry in deleted)
        {
            if (!noteIds.Add(entry.Note.Id) || entry.Target.StackId == Guid.Empty ||
                !Enum.IsDefined(entry.Target.Placement) || string.IsNullOrWhiteSpace(entry.StackName))
            {
                throw new ArgumentException("Invalid or duplicate deleted note.", nameof(deleted));
            }
        }

        var historyIds = new HashSet<Guid>();
        foreach (var entry in history)
        {
            if (!historyIds.Add(entry.NoteId) || !noteIds.Contains(entry.NoteId) ||
                entry.SourceItem.Kind != DropItemKind.Text || entry.SourceIndex < 0)
            {
                throw new ArgumentException("Invalid note capture history.", nameof(history));
            }
        }
    }

    public static IReadOnlyList<DeletedNote> FindRemoved(
        IReadOnlyList<DropStack> before,
        IReadOnlyList<DropStack> after,
        DateTimeOffset deletedAt)
    {
        var remaining = NoteOperations.Enumerate(after).Select(location => location.Note.Id).ToHashSet();
        return NoteOperations.Enumerate(before).Where(location => !remaining.Contains(location.Note.Id))
            .Select(location =>
            {
                var stack = before.Single(stack => stack.Id == location.Target.StackId);
                return new DeletedNote(location.Note, location.Target, stack.Name,
                    stack.Items.FirstOrDefault(item => item.Id == location.Target.ItemId)?.DisplayName, deletedAt);
            }).ToArray();
    }

    public static (IReadOnlyList<DropStack> Stacks, StickyNote Note) Restore(
        IReadOnlyList<DropStack> stacks,
        DeletedNote deleted)
    {
        var result = stacks.ToList();
        var stack = result.SingleOrDefault(stack => stack.Id == deleted.Target.StackId);
        if (stack is null)
        {
            stack = DropStack.Restore(deleted.Target.StackId, deleted.StackName, DropStack.DefaultTint, []);
            result.Add(stack);
        }

        // Undoing a conversion restores a text item with the original identity. A recovered
        // edited note gets a fresh identity if that item (or another live note) now occupies it.
        var note = deleted.Note;
        if (NoteOperations.Find(result, note.Id) is not null || result.Any(s => s.Items.Any(i => i.Id == note.Id)))
        {
            note = new StickyNote(Guid.NewGuid(), note.Text, note.Rtf, note.Color, note.CreatedAt, note.UpdatedAt);
        }

        var target = deleted.Target.NormalizePlacement();
        if (target.Placement == NotePlacement.Item &&
            !stack.Items.Any(item => item.Id == target.ItemId && item.Kind != DropItemKind.Note))
        {
            target = new NoteTarget(stack.Id, NotePlacement.StackItem);
        }

        return (NoteOperations.Add(result, note, target), note);
    }

    public static IReadOnlyList<DropStack> UndoConversion(
        IReadOnlyList<DropStack> stacks,
        NoteCaptureHistory history)
    {
        var location = NoteOperations.Find(stacks, history.NoteId)
                       ?? throw new ArgumentException("The converted note is no longer available.", nameof(history));
        if (!history.IsConversion || history.SourceItem.Kind != DropItemKind.Text ||
            stacks.Any(stack =>
                stack.Items.Any(item => item.Id == history.SourceItem.Id && item.Note?.Id != history.NoteId)))
        {
            throw new ArgumentException("The original capture is already present or cannot be restored.",
                nameof(history));
        }

        var targetStack = stacks.Single(stack => stack.Id == location.Target.StackId);
        var index = targetStack.Items.ToList().FindIndex(item => item.Note?.Id == history.NoteId);
        if (index < 0)
        {
            index = Math.Min(history.SourceIndex, targetStack.Items.Count);
        }

        var updated = NoteOperations.Delete(stacks, history.NoteId);
        var attachments = new List<StickyNote>();
        foreach (var original in history.SourceItem.AttachedNotes)
        {
            var current = NoteOperations.Find(updated, original.Id);
            // Restore only annotations still beside this capture. Never move an annotation
            // that the user has since attached elsewhere, or resurrect a deleted annotation.
            if (current?.Target == new NoteTarget(targetStack.Id, NotePlacement.StackItem))
            {
                attachments.Add(current.Note);
                updated = NoteOperations.Delete(updated, original.Id);
            }
        }

        var restoredAnnotationIds = attachments.Select(note => note.Id).ToHashSet();
        index -= targetStack.Items.Take(index).Count(item => restoredAnnotationIds.Contains(item.Id));
        return updated.Select(stack => stack.Id == targetStack.Id
            ? StackOperations.InsertItems(stack, [history.SourceItem.WithAttachedNotes(attachments)],
                Math.Clamp(index, 0, stack.Items.Count))
            : stack).ToArray();
    }
}
