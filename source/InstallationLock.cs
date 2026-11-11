namespace LittleQuieter;

// Setup and removal share this lock, including staging, rollback and restart.
internal sealed class InstallationLock : IDisposable
{
    internal const string Name = @"Local\LittleQuieter.Install.v1";
    private readonly Mutex mutex = new Mutex(false, Name);

    public InstallationLock()
    {
        try
        {
            if (mutex.WaitOne(0))
                return;
        }
        catch (AbandonedMutexException) { return; } // The previous owner exited; we now own it.
        mutex.Dispose();
        throw new IOException("Another Little Quieter installation or removal is in progress. Try again when it finishes.");
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
