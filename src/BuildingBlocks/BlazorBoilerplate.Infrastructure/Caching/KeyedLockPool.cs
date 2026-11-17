namespace BlazorBoilerplate.Infrastructure.Caching;

/// <summary>
/// 按 key 的一次性互斥锁池：引用计数归零即摘除并释放 SemaphoreSlim。
/// 用单一短锁保护字典，避免 ConcurrentDictionary.AddOrUpdate 的 update factory 在写竞争下重入。
/// </summary>
public sealed class KeyedLockPool
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry(key);
                _entries[key] = entry;
            }
            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseRef(entry);
            throw;
        }

        return new Lease(this, entry);
    }

    private void Release(Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseRef(entry);
    }

    private void ReleaseRef(Entry entry)
    {
        lock (_gate)
        {
            entry.RefCount--;
            if (entry.RefCount > 0)
                return;

            if (_entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                _entries.Remove(entry.Key);
            entry.Semaphore.Dispose();
        }
    }

    private sealed class Entry(string key)
    {
        public string Key { get; } = key;
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount { get; set; }
    }

    private sealed class Lease(KeyedLockPool owner, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(entry);
        }
    }
}
