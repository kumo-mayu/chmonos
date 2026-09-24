namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 鍵（商品ID・ファイルのパス）ごとの錠。**使っている人がいなくなった錠は表から捨てる。**
///
/// 前は鍵ごとの <see cref="SemaphoreSlim"/> を表に足すだけで捨てなかったので、起動している間に触った商品・書いたファイルの数だけ
/// 錠が溜まり続けた（取り込み・検出・画像の保存で数千〜数万）。
///
/// 捨ててよいのは、持っている人も待っている人もいないとき（数える）。数えるのと表から外すのを同じ錠の中で行うので、
/// 捨てた直後に来た人は新しい錠を作り、古い錠を待つ人は残らない（同じ鍵に2つの錠が同時に生きることは無い）。
/// </summary>
internal sealed class KeyedGate<TKey>
    where TKey : notnull
{
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries;

    public KeyedGate(IEqualityComparer<TKey>? comparer = null)
    {
        _entries = new Dictionary<TKey, Entry>(comparer);
    }

    /// <summary>今表にある錠の数（試験で、捨てられたかを見る）。</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    internal sealed class Entry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>持っている人と待っている人の数。</summary>
        public int Users;
    }

    /// <summary>
    /// 鍵の錠を取る口。取った口は必ず <see cref="Handle.Release"/> で返す（待つのに失敗したときは自分で返す）。
    /// 前の <c>var gate = LockFor(id); await gate.WaitAsync(); try { … } finally { gate.Release(); }</c> の形のまま使える。
    /// </summary>
    public Handle For(TKey key)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries[key] = entry;
            }

            entry.Users++;
        }

        return new Handle(this, key, entry);
    }

    private void Leave(TKey key, Entry entry)
    {
        lock (_sync)
        {
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    public sealed class Handle
    {
        private readonly KeyedGate<TKey> _owner;
        private readonly TKey _key;
        private readonly Entry _entry;
        private int _left;

        internal Handle(KeyedGate<TKey> owner, TKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public async Task WaitAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _entry.Gate.WaitAsync(cancellationToken);
            }
            catch
            {
                // 取れなかった（取り消し）ので、数えた分だけ返す。錠そのものは持っていない
                LeaveOnce();
                throw;
            }
        }

        public void Wait()
        {
            try
            {
                _entry.Gate.Wait();
            }
            catch
            {
                LeaveOnce();
                throw;
            }
        }

        /// <summary>錠を放し、使っている人の数から外す。</summary>
        public void Release()
        {
            _entry.Gate.Release();
            LeaveOnce();
        }

        private void LeaveOnce()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0)
            {
                _owner.Leave(_key, _entry);
            }
        }
    }
}
