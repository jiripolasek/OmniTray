// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using OmniTray.Services;

namespace OmniTray.Core.Tests;

[TestClass]
public sealed class NoteSaveSessionTests
{
    [TestMethod]
    public async Task OpeningWithoutEditsDoesNotWrite()
    {
        var writes = 0;
        var session = new NoteSaveSession(() =>
        {
            writes++;
            return Task.CompletedTask;
        });

        Assert.IsTrue(await session.FlushAsync());
        Assert.AreEqual(0, writes);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task PendingEditsShareOneCatalogWrite()
    {
        var writes = 0;
        var session = new NoteSaveSession(() =>
        {
            writes++;
            return Task.CompletedTask;
        });
        // Edits can belong to different selected notes: the catalog is the save unit.
        session.MarkChanged();
        session.MarkChanged();

        Assert.IsTrue(await session.FlushAsync());
        Assert.AreEqual(1, writes);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task FailureKeepsEditsPendingUntilRetrySucceeds()
    {
        var writes = 0;
        var failure = new IOException("Catalog write failed.");
        var session = new NoteSaveSession(() => ++writes == 1 ? Task.FromException(failure) : Task.CompletedTask);
        session.MarkChanged();

        Assert.IsFalse(await session.FlushAsync());
        Assert.IsTrue(session.HasUnsavedChanges);
        Assert.AreSame(failure, session.LastError);

        Assert.IsTrue(await session.FlushAsync());
        Assert.AreEqual(2, writes);
        Assert.IsFalse(session.HasUnsavedChanges);
        Assert.IsNull(session.LastError);
    }

    [TestMethod]
    public async Task EditDuringWriteIsSavedBeforeFlushCompletes()
    {
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var session = new NoteSaveSession(() =>
        {
            if (++writes == 1) { return firstWrite.Task; }

            secondStarted.SetResult();
            return secondWrite.Task;
        });
        session.MarkChanged();
        var flush = session.FlushAsync();
        session.MarkChanged();
        firstWrite.SetResult();
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsFalse(flush.IsCompleted);
        Assert.IsTrue(session.HasUnsavedChanges);
        secondWrite.SetResult();
        Assert.IsTrue(await flush.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, writes);
        Assert.IsFalse(session.HasUnsavedChanges);
    }

    [TestMethod]
    public async Task CloseAndAutosaveSerializeWithoutDuplicateWrites()
    {
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var session = new NoteSaveSession(() =>
        {
            writes++;
            return write.Task;
        });
        session.MarkChanged();
        var autosave = session.FlushAsync();
        var closing = session.FlushAsync();

        Assert.AreEqual(1, writes);
        Assert.IsFalse(closing.IsCompleted);
        write.SetResult();
        var results = await Task.WhenAll(autosave, closing).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(results.All(result => result));
        Assert.AreEqual(1, writes);
        Assert.IsFalse(session.HasUnsavedChanges);
    }
}
