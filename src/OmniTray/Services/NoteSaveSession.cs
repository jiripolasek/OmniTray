// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

namespace OmniTray.Services;

// Tracks pending catalog writes across note selection changes. The storage format is unchanged.
internal sealed class NoteSaveSession(Func<Task> save)
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private long _revision;
    private long _savedRevision;

    public bool HasUnsavedChanges => Interlocked.Read(ref this._revision) != Interlocked.Read(ref this._savedRevision);

    public Exception? LastError { get; private set; }

    public void MarkChanged() => Interlocked.Increment(ref this._revision);

    public async Task<bool> FlushAsync()
    {
        await this._saveGate.WaitAsync();
        try
        {
            while (this.HasUnsavedChanges)
            {
                var revision = Interlocked.Read(ref this._revision);
                try { await save(); }
                catch (Exception exception)
                {
                    this.LastError = exception;
                    return false;
                }

                Interlocked.Exchange(ref this._savedRevision, revision);
                this.LastError = null;
                // An edit during the awaited write needs another snapshot before close can succeed.
            }

            return true;
        }
        finally { this._saveGate.Release(); }
    }
}
