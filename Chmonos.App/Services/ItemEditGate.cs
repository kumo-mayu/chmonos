namespace Chmonos.App.Services;

/// <summary>
/// 編集画面で開いている商品と、外から使う API の書き込みの順序を決める札（点検37の[大]。ユーザ判断 2026-10-13：開いている商品には外から書かせない）。
///
/// 前は API が入口で「編集画面で開いているか」を1回見るだけだった。見た後、一覧へ名前を足すのや商品の錠を待つ間に編集画面が古い値を読むと、
/// API が書いた後で人が保存したとき、同じ欄の API の分が消えた。そこで次の2つを同じ錠の中で決める。
/// - API は書き始める前に札を取り、書き終えるまで持つ。編集画面が開いていれば札は取れない（409）
/// - 編集画面は商品を読む前に開いた印を立て、その商品へ書いている API があれば、書き終えるまで待ってから読む
/// どちらが先でも、編集画面が読むのは API が書いた後の値になる
/// </summary>
public sealed class ItemEditGate
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Writing> _writing = new(StringComparer.Ordinal);
    private object? _editor;
    private string? _editingItemId;

    private sealed class Writing
    {
        public int Count;
        public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>編集画面で開いている商品（無ければ null）。</summary>
    public string? EditingItemId
    {
        get
        {
            lock (_lock)
            {
                return _editingItemId;
            }
        }
    }

    /// <summary>API が商品へ書く札を取る。編集画面で開いていれば null（取れない）。返した物を手放すまで、編集画面はその商品を読まずに待つ。</summary>
    public IDisposable? TryBeginApiWrite(string itemId)
    {
        lock (_lock)
        {
            if (_editingItemId == itemId)
            {
                return null;
            }

            if (!_writing.TryGetValue(itemId, out var writing))
            {
                writing = new Writing();
                _writing[itemId] = writing;
            }

            writing.Count++;
            return new Release(this, itemId);
        }
    }

    private void EndApiWrite(string itemId)
    {
        TaskCompletionSource? done = null;
        lock (_lock)
        {
            if (_writing.TryGetValue(itemId, out var writing) && --writing.Count == 0)
            {
                _writing.Remove(itemId);
                done = writing.Done;
            }
        }

        done?.TrySetResult();
    }

    /// <summary>
    /// 編集画面が商品を開く（読む前に呼ぶ）。その商品へ書いている API があれば、書き終えたら終わる。
    /// <paramref name="editor"/> は開いた画面（前の画面の「閉じた」が、同じ商品を開き直した新しい画面の印を外さないように持ち主で見分ける）
    /// </summary>
    public Task BeginEditAsync(object editor, string itemId)
    {
        lock (_lock)
        {
            _editor = editor;
            _editingItemId = itemId;
            return _writing.TryGetValue(itemId, out var writing) ? writing.Done.Task : Task.CompletedTask;
        }
    }

    /// <summary>編集画面を離れた（その画面が立てた印だけを外す）。</summary>
    public void EndEdit(object editor)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_editor, editor))
            {
                _editor = null;
                _editingItemId = null;
            }
        }
    }

    private sealed class Release(ItemEditGate gate, string itemId) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.EndApiWrite(itemId);
            }
        }
    }
}
