// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Core;

public enum NotePlacement
{
    StackItem = 0,

    // Retain the persisted value for recovery entries from earlier catalogs.
    LegacyStack = 1,
    Item = 2
}

public sealed record NoteTarget(Guid StackId, NotePlacement Placement, Guid? ItemId = null)
{
    public NoteTarget NormalizePlacement() => this.Placement == NotePlacement.LegacyStack
        ? this with { Placement = NotePlacement.StackItem }
        : this;
}

public sealed record NoteLocation(StickyNote Note, NoteTarget Target);

public static class NoteOperations
{
    public static (DropStack Stack, StickyNote Note) ConvertTextItem(
        DropStack stack,
        Guid itemId,
        bool duplicate,
        string? plainText = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var items = stack.Items.ToList();
        var index = items.FindIndex(item => item.Id == itemId);
        if (index < 0 || items[index].Kind != DropItemKind.Text)
        {
            throw new ArgumentException("Choose an existing text item to convert to a note.", nameof(itemId));
        }

        var source = items[index];
        // RTF-only captures need their plain text extracted by the editor before conversion.
        var text = source.Text ?? plainText ?? (!string.IsNullOrWhiteSpace(source.Html)
            ? ContentDetection.ExtractPlainTextFromHtml(source.Html)
            : throw new ArgumentException("Plain text is required for an RTF-only capture.", nameof(plainText)));
        var now = DateTimeOffset.UtcNow;
        var note = duplicate
            ? StickyNote.Create(text, source.Rtf)
            : new StickyNote(source.Id, text, source.Rtf, NoteColor.Yellow, source.CreatedAt,
                now > source.CreatedAt ? now : source.CreatedAt);
        var noteItem = DropItem.CreateNote(note);
        if (duplicate)
        {
            items.Insert(index + 1, noteItem);
        }
        else
        {
            items[index] = noteItem;
            // Notes cannot own attachments. Keep existing annotations beside the converted item.
            items.InsertRange(index + 1, source.AttachedNotes.Select(DropItem.CreateNote));
        }

        var updated = stack.WithItems(items);
        Validate([updated]);
        return (updated, note);
    }

    public static IReadOnlyList<StickyNote> GetStackNotes(DropStack stack) =>
        stack.Items.Select(item => item.Note).OfType<StickyNote>().ToArray();

    public static IEnumerable<NoteLocation> Enumerate(IEnumerable<DropStack> stacks)
    {
        foreach (var stack in stacks)
        {
            foreach (var item in stack.Items)
            {
                if (item.Note is { } note)
                {
                    yield return new NoteLocation(note, new NoteTarget(stack.Id, NotePlacement.StackItem));
                }

                foreach (var attachment in item.AttachedNotes)
                {
                    yield return new NoteLocation(attachment, new NoteTarget(stack.Id, NotePlacement.Item, item.Id));
                }
            }
        }
    }

    public static NoteLocation? Find(IEnumerable<DropStack> stacks, Guid noteId) =>
        Enumerate(stacks).SingleOrDefault(location => location.Note.Id == noteId);

    public static void Validate(IEnumerable<DropStack> stacks)
    {
        var ids = new HashSet<Guid>();
        foreach (var location in Enumerate(stacks))
        {
            if (!ids.Add(location.Note.Id))
            {
                throw new ArgumentException("Each note must have exactly one placement in the catalog.",
                    nameof(stacks));
            }
        }
    }

    public static IReadOnlyList<DropStack> Add(
        IReadOnlyList<DropStack> stacks,
        StickyNote note,
        NoteTarget target)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(target);
        target = target.NormalizePlacement();
        ValidateTarget(stacks, target, note.Id);
        if (Find(stacks, note.Id) is not null)
        {
            throw new ArgumentException("The note is already in the catalog.", nameof(note));
        }

        return stacks.Select(stack => stack.Id != target.StackId
            ? stack
            : target.Placement switch
            {
                NotePlacement.StackItem => stack.Append([DropItem.CreateNote(note)]),
                NotePlacement.Item => stack.WithItems(stack.Items.Select(item => item.Id == target.ItemId
                    ? item.WithAttachedNotes(item.AttachedNotes.Append(note))
                    : item)),
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            }).ToArray();
    }

    public static IReadOnlyList<DropStack> Relocate(
        IReadOnlyList<DropStack> stacks,
        Guid noteId,
        NoteTarget target)
    {
        var location = Find(stacks, noteId) ??
                       throw new ArgumentException("The note no longer exists.", nameof(noteId));
        ArgumentNullException.ThrowIfNull(target);
        target = target.NormalizePlacement();
        // Validate before changing either owner, including attempts to attach a note to itself.
        ValidateTarget(stacks, target, noteId);
        return location.Target == target ? stacks : Add(Delete(stacks, noteId), location.Note, target);
    }

    public static IReadOnlyList<DropStack> Update(IReadOnlyList<DropStack> stacks, StickyNote note)
    {
        var location = Find(stacks, note.Id) ?? throw new ArgumentException("The note no longer exists.", nameof(note));
        if (note.CreatedAt != location.Note.CreatedAt || note.UpdatedAt < location.Note.UpdatedAt)
        {
            throw new ArgumentException("Note history cannot be replaced with older content.", nameof(note));
        }

        if (note == location.Note)
        {
            return stacks;
        }

        return Rewrite(stacks, location, note);
    }

    public static IReadOnlyList<DropStack> Delete(IReadOnlyList<DropStack> stacks, Guid noteId)
    {
        var location = Find(stacks, noteId);
        return location is null ? stacks : Rewrite(stacks, location, null);
    }

    private static IReadOnlyList<DropStack> Rewrite(
        IReadOnlyList<DropStack> stacks,
        NoteLocation location,
        StickyNote? replacement)
    {
        IReadOnlyList<StickyNote> RewriteAttachments(IReadOnlyList<StickyNote> notes) => notes
            .Select(note => note.Id == location.Note.Id ? replacement : note)
            .OfType<StickyNote>().ToArray();

        return stacks.Select(stack =>
        {
            if (stack.Id != location.Target.StackId)
            {
                return stack;
            }

            return location.Target.Placement switch
            {
                NotePlacement.Item => stack.WithItems(stack.Items.Select(item => item.Id == location.Target.ItemId
                    ? item.WithAttachedNotes(RewriteAttachments(item.AttachedNotes))
                    : item)),
                _ => stack.WithItems(stack.Items
                    .Select(item => item.Note?.Id == location.Note.Id
                        ? replacement is null ? null : DropItem.CreateNote(replacement)
                        : item)
                    .OfType<DropItem>())
            };
        }).ToArray();
    }

    private static void ValidateTarget(IReadOnlyList<DropStack> stacks, NoteTarget target, Guid noteId)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(target);
        var stack = stacks.SingleOrDefault(stack => stack.Id == target.StackId)
                    ?? throw new ArgumentException("The destination stack no longer exists.", nameof(target));
        if (!Enum.IsDefined(target.Placement) ||
            (target.Placement != NotePlacement.Item && target.ItemId is not null))
        {
            throw new ArgumentException("Invalid note placement.", nameof(target));
        }

        if (target.Placement == NotePlacement.Item &&
            !stack.Items.Any(item => item.Id == target.ItemId && item.Id != noteId && item.Kind != DropItemKind.Note))
        {
            throw new ArgumentException("Choose an existing item other than a note.", nameof(target));
        }

        if (target.Placement == NotePlacement.StackItem &&
            stack.Items.Any(item => item.Id == noteId && item.Note?.Id != noteId))
        {
            throw new ArgumentException("The destination already contains that item ID.", nameof(target));
        }
    }
}
