namespace BoothAssetManager.Core.Scanning;

/// <summary>
/// 取り込みの対象フォルダ。**走らせている最中にも足せる。**
///
/// 「1ファイルだけ後から見つかった」は普通に起きる。実行中を塞ぐと、
/// 終わるのを待ってから押し直すことになり、待った意味が無い。
///
/// パイプラインは1本のままにして、こちらを増やす形にした。
/// 2本走らせると取得の順序（画像より先にJSON）を保てず、
/// どちらの進捗を出すのかも決められなくなる。
///
/// 呼ぶ側は別スレッドなので、中は錠を持つ。
/// </summary>
public sealed class ImportWorkSet
{
    private readonly object _gate = new();

    /// <summary>まだ走査していないもの。</summary>
    private readonly List<string> _pending = [];

    /// <summary>一度でも受け付けたもの。同じフォルダを二度走査しないために持つ。</summary>
    private readonly HashSet<string> _accepted = new(StringComparer.OrdinalIgnoreCase);

    public ImportWorkSet()
    {
    }

    public ImportWorkSet(IEnumerable<string> folders) => Add(folders);

    /// <summary>まだ走査していないものが残っているか。</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>一度でも受け付けたフォルダ。表示と、まとめの件数に使う。</summary>
    public IReadOnlyList<string> Accepted
    {
        get
        {
            lock (_gate)
            {
                return _accepted.ToList();
            }
        }
    }

    /// <summary>
    /// 足す。**同じものは二度入らない。**
    ///
    /// 既に受け付けたフォルダの配下も入れない。親を走査すれば配下も見るので、
    /// 入れると同じファイルを二度ハッシュすることになる。
    /// 逆に、既にあるものの**親**を足した場合は入れる（外側にまだ見ていない範囲がある）。
    /// </summary>
    /// <returns>実際に足した件数。</returns>
    public int Add(IEnumerable<string> folders)
    {
        lock (_gate)
        {
            var added = 0;

            foreach (var folder in folders)
            {
                var normalized = Normalize(folder);
                if (normalized.Length == 0 || _accepted.Contains(normalized))
                {
                    continue;
                }

                if (_accepted.Any(existing => IsUnder(normalized, existing)))
                {
                    continue;
                }

                _accepted.Add(normalized);
                _pending.Add(normalized);
                added++;
            }

            return added;
        }
    }

    /// <summary>
    /// まだ走査していないものを取り下げる。
    ///
    /// 「積んだ直後に取り消したい」への答えがこれ。取り込み全体を止めなくても、
    /// **まだ順番が来ていなければ**一覧から外すだけで消える。
    /// 既に走査したものは戻せない（取り消しではなく、全体の中断で対応する）。
    /// </summary>
    /// <returns>取り下げられたか。既に走査済みなら false。</returns>
    public bool Remove(string folder)
    {
        lock (_gate)
        {
            var normalized = Normalize(folder);
            var index = _pending.FindIndex(entry =>
                string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                return false;
            }

            _pending.RemoveAt(index);
            _accepted.Remove(normalized);
            return true;
        }
    }

    /// <summary>
    /// まだ走査していないものを全部取り出す。取り出した分は保留から消える。
    /// 走査の途中で足されたものは次の周回で出てくる。
    /// </summary>
    public IReadOnlyList<string> TakePending()
    {
        lock (_gate)
        {
            var taken = _pending.ToList();
            _pending.Clear();
            return taken;
        }
    }

    // ---- ③を待っている商品（U8・U10） ----
    //
    // ①で作った商品は検索や件数には出してよいが、対応アバターの検出（③）が済むまでは編集に出さない
    // （ユーザ判断：「対応アバターの処理までは終わらせてからの方が安全」）。
    // 検出の途中で人が対応アバターを直すと、検出の書き込みと取り合いになる。
    // 画面は取り込みと同じこの集まりを見て判断するので、別に状態を持たずに済む

    private readonly HashSet<string> _awaitingDetection = new(StringComparer.Ordinal);
    private int _addedCount;

    /// <summary>この取り込みで①から作った商品の数。一覧へ出していない件数を数えるのに使う。</summary>
    public int AddedCount
    {
        get
        {
            lock (_gate)
            {
                return _addedCount;
            }
        }
    }

    /// <summary>①は済んだが③がまだの商品の数。</summary>
    public int AwaitingDetectionCount
    {
        get
        {
            lock (_gate)
            {
                return _awaitingDetection.Count;
            }
        }
    }

    public bool IsAwaitingDetection(string itemId)
    {
        lock (_gate)
        {
            return _awaitingDetection.Contains(itemId);
        }
    }

    /// <summary>①で商品を作った。③が済むまで編集に出さない。</summary>
    public void NoteAdded(string itemId)
    {
        lock (_gate)
        {
            if (_awaitingDetection.Add(itemId))
            {
                _addedCount++;
            }
        }
    }

    /// <summary>
    /// ③の段が終わった。検出が失敗しても、検出を使わない設定でも外す——
    /// 外さないと、その商品は取り込みが終わるまで編集できないままになる。
    /// </summary>
    public void NoteDetectionDone(IEnumerable<string> itemIds)
    {
        lock (_gate)
        {
            foreach (var itemId in itemIds)
            {
                _awaitingDetection.Remove(itemId);
            }
        }
    }

    // ---- 残りの問い合わせの見込み（U1） ----
    //
    // 取り込みの時間のほとんどはBOOTHへの問い合わせの間隔（1.5秒以上）を待つ時間なので、
    // 残り時間は「残りの問い合わせの数 × 1件あたりの時間」で出せる。数は取り込みしか知らないので、
    // 取り込みがここへ書き、画面が読む。画像は既に手元にあれば問い合わせないので、多めに出ることがある

    private int _jsonLeft;
    private int _pagesLeft;
    private int _imagesLeft;

    /// <summary>残りの問い合わせ（①商品の情報・②商品ページ・④⑤⑥画像）。</summary>
    public (int Json, int Pages, int Images) RequestsLeft
    {
        get
        {
            lock (_gate)
            {
                return (Math.Max(0, _jsonLeft), Math.Max(0, _pagesLeft), Math.Max(0, _imagesLeft));
            }
        }
    }

    /// <summary>見込みを足す（済んだ分は負の数で引く）。</summary>
    public void PlanRequests(int json = 0, int pages = 0, int images = 0)
    {
        lock (_gate)
        {
            _jsonLeft += json;
            _pagesLeft += pages;
            _imagesLeft += images;
        }
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(path.Trim());

    /// <summary><paramref name="path"/> が <paramref name="root"/> の中にあるか。</summary>
    private static bool IsUnder(string path, string root)
        => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
