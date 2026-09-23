using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// <c>items/{id}.json</c> の読み書き。1商品につき1ファイルにしているのは、
/// 壊れた時の被害を1件に閉じ込め、差分バックアップと直接編集をしやすくするため。
/// </summary>
public sealed class ItemRepository
{
    private readonly AppPaths _paths;

    /// <summary>
    /// 商品1件ごとの錠。**同じ商品のJSONへ同時に書かせない。**
    ///
    /// 書き手は取り込み・検出・再取得・画面の5系統以上あり、どれも「読む→組み直す→書く」。
    /// 錠が無いと、読んでから書くまでの間に入った相手の変更を消すか、
    /// 一時ファイルの取り合いで保存そのものが落ちる（落ちた保存はほとんど投げっぱなしなので画面には出ない）。
    /// 商品IDで分けているので、別の商品どうしは待たない。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _itemLocks = new(StringComparer.Ordinal);

    public ItemRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public bool Exists(string itemId) => File.Exists(_paths.ItemFile(itemId));

    /// <summary>
    /// 1件を読む。**古い形の読み替えはしない**（公開前は、今の形に合わないデータの側を問題にする・ユーザ判断 2026-09-12）。
    /// 以前は旧形式の購入記録（orderedVariations）を読むたびに purchases へ移していた
    /// </summary>
    public Task<ItemRecord?> LoadAsync(string itemId, CancellationToken cancellationToken = default)
        => JsonStore.ReadAsync<ItemRecord>(_paths.ItemFile(itemId), cancellationToken);

    public async Task SaveAsync(ItemRecord item, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(item.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(item, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private Task WriteAsync(ItemRecord item, CancellationToken cancellationToken)
        => JsonStore.WriteAsync(_paths.ItemFile(item.Id), item, cancellationToken);

    private SemaphoreSlim LockFor(string itemId)
        => _itemLocks.GetOrAdd(itemId, static _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// <c>local</c> のうち、<paramref name="owns"/> で名指しした項目だけを書く。
    ///
    /// 保存の直前に読み直すので、**名指ししなかった項目は今の値がそのまま残る。**
    /// 画面は開いた時点の写しを抱えているため、丸ごと書き戻すと
    /// 開いている間に取り込み・検出・再取得が書いた項目まで古い値で潰してしまう。
    ///
    /// itemが消えていれば **何も書かずに false**。
    /// 取り込み中でも削除を塞がないと決めたので、書く直前に確かめる必要がある。
    ///
    /// <paramref name="booth"/> を渡すと <c>booth</c> ブロックも入れ替える（再取得のため）。
    /// 取得には数秒かかるので、その間に人が入力していることがある。
    /// **読み直しはここで行うので、呼ぶ側は取得の前に読んだ写しをそのまま渡してよい。**
    /// </summary>
    public async Task<bool> SaveLocalAsync(
        string itemId,
        LocalBlock local,
        IReadOnlyCollection<LocalField> owns,
        BoothBlock? booth = null,
        CancellationToken cancellationToken = default)
    {
        // 読み直してから書き終えるまでを錠の中に入れる。読んだ後に別の書き手が入ると、
        // 名指ししなかった項目を守るための読み直しそのものが無駄になる
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                return false;
            }

            var merged = LocalFields.Merge(existing.Local, local, owns);

            // ExistsOnBooth は導ける値なので、どの経路から保存しても同じ式で入れ直す。
            // booth を入れ替えるときは、当然そちらの新しい一覧が正
            var variations = (booth ?? existing.Booth).Variations;
            merged = merged with { Purchases = Purchase.Reconcile(merged.Purchases, variations) };

            await WriteAsync(
                existing with { Booth = booth ?? existing.Booth, Local = merged },
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return true;
    }

    /// <summary>
    /// 読み直した今の値に、渡された変え方を当てて書く（<c>UiCommand.ChangeSettings</c> と同じ形）。
    ///
    /// 「開始時に全件を読み、時間のかかる処理の後にその古い写しで丸ごと書き戻す」経路
    /// （検出・一括書き換え・取り込み）が、その項目自体を古い値で潰していた。
    /// <paramref name="owns"/> は他の項目を守るだけで、名指しした項目そのものは守らないため、
    /// **名指しした項目を古い写しから作る側が、今の値を見て組み直す**必要がある。
    ///
    /// <paramref name="change"/> が null を返したら何も書かない（触る物が無かった）。
    /// </summary>
    /// <returns>書いたか（itemが消えていた・触る物が無かったときは false）。</returns>
    public async Task<bool> ChangeLocalAsync(
        string itemId,
        Func<LocalBlock, LocalBlock?> change,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await LoadAsync(itemId, cancellationToken) is not { } existing
                || change(existing.Local) is not { } changed)
            {
                return false;
            }

            var merged = LocalFields.Merge(existing.Local, changed, owns);
            merged = merged with { Purchases = Purchase.Reconcile(merged.Purchases, existing.Booth.Variations) };

            await WriteAsync(existing with { Local = merged }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return true;
    }

    /// <summary>
    /// 読み直した今の <c>booth</c> に、渡された変え方を当てて書く（<see cref="ChangeLocalAsync"/> の <c>booth</c> 版）。
    ///
    /// 取り込みの②は①で取った <c>booth</c> に説明の節を足して書いていたので、
    /// ①と②の間に人が「商品情報を取り直す」を押すと、取り直した新しい <c>booth</c> が①の古い物に戻っていた。
    /// <c>local</c> には触らない（購入記録の <c>ExistsOnBooth</c> だけは新しい種類の一覧で入れ直す）。
    ///
    /// <paramref name="change"/> が null を返したら何も書かない。
    /// </summary>
    /// <returns>書いたか（itemが消えていた・触る物が無かったときは false）。</returns>
    public async Task<bool> ChangeBoothAsync(
        string itemId,
        Func<BoothBlock, BoothBlock?> change,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await LoadAsync(itemId, cancellationToken) is not { } existing
                || change(existing.Booth) is not { } booth)
            {
                return false;
            }

            var local = existing.Local with { Purchases = Purchase.Reconcile(existing.Local.Purchases, booth.Variations) };
            await WriteAsync(existing with { Booth = booth, Local = local }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return true;
    }

    public IReadOnlyList<string> EnumerateItemIds()
    {
        if (!Directory.Exists(_paths.ItemsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(_paths.ItemsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    /// <summary>
    /// 全件をメモリへ読み込む。説明の生HTMLは含まないので、件数が増えても軽いまま。
    /// 壊れた1件で全体が止まらないよう、読めなかったファイルはスキップして呼び出し側へ返す。
    /// </summary>
    /// <remarks>
    /// **読み込みそのものを画面のスレッドから外す**（ユーザ判断 2026-09-21・C6）。
    /// ファイルを開く所に非同期の指定が無いので <c>await</c> が同期で終わり、
    /// 呼んだスレッドを一度も手放さなかった。2000件ならその全部が1回の固まりになる
    /// （起動・取り込みの後・編集の後の読み直しで、毎回画面が止まっていた）。
    /// </remarks>
    public Task<ItemLoadResult> LoadAllAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => LoadAllCoreAsync(progress, cancellationToken), cancellationToken);

    private async Task<ItemLoadResult> LoadAllCoreAsync(
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var items = new List<ItemRecord>();
        var failures = new List<string>();
        var loaded = 0;

        foreach (var itemId in EnumerateItemIds())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = await LoadAsync(itemId, cancellationToken);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
            {
                failures.Add(itemId);
            }

            progress?.Report(++loaded);
        }

        return new ItemLoadResult { Items = items, FailedItemIds = failures };
    }

    /// <summary>表示用の説明HTML。商品ページを開いた時だけ読む。</summary>
    public async Task<string?> LoadDescriptionHtmlAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var path = _paths.ItemHtmlFile(itemId);
        // JSON と同じく、読んでいる間に⑦の取り直しが置き換えても保存を落とさない開き方で読む
        return File.Exists(path)
            ? await JsonStore.ReadTextAsync(path, cancellationToken)
            : null;
    }

    public Task SaveDescriptionHtmlAsync(string itemId, string html, CancellationToken cancellationToken = default)
        => JsonStore.WriteTextAsync(_paths.ItemHtmlFile(itemId), html, cancellationToken);

    /// <summary>
    /// 管理対象から外す。保存ファイルの実体には触らない。
    ///
    /// **商品ごとの錠の中で消す。**錠の外だと、読み直して書く途中の保存（<see cref="SaveLocalAsync"/> など）が
    /// 消した直後に書き戻し、外したはずの商品が生き返っていた。
    ///
    /// **画像 → 説明HTML → JSON の順に消す。**画像フォルダは開いている絵があると消せないことがあり、
    /// 前は JSON を先に消していたので、そこで投げると「商品は無いのに画像だけ残る」半端な状態になった
    /// （誰も片付けない）。JSON を最後にすれば、途中で投げても商品は残り、もう一度外せば済む。
    /// 欠けた画像は裏の取得が取り直す。
    ///
    /// **非同期で待つ。**ファイルを外す・IDを付け替えるは画面のスレッドの文脈から来るので、同期で錠と門を待つと、
    /// 保存先を運んでいる間に画面が止まり、門を開ける側と待ち合って固まる（<see cref="StoreWriteGate.Enter"/>）。
    /// </summary>
    public async Task DeleteAsync(string itemId, CancellationToken cancellationToken = default)
        => await DeleteIfAsync(itemId, static _ => true, cancellationToken);

    /// <summary>
    /// **錠の中で今の値を読み、<paramref name="condition"/> が真のときだけ消す。**消したかを返す（無ければ false）。
    ///
    /// 「空になったら商品ごと消す」を錠の外で決めると、決めてから消すまでの間に取り込みが足したファイルごと消えていた。
    /// </summary>
    public async Task<bool> DeleteIfAsync(
        string itemId,
        Func<ItemRecord, bool> condition,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await LoadAsync(itemId, cancellationToken) is not { } current || !condition(current))
            {
                return false;
            }

            await DeleteLockedAsync(itemId, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 商品を別の場所へ移してから消す（IDの付け替え）。**移す元の錠を持ったまま、今の値を読み、移し、消す。**
    ///
    /// 錠の外で読み直してから消すと、その間に取り込みや人が元の商品へ書いた分は、移されずに元と一緒に消えていた。
    /// <paramref name="moveTo"/> は移せたかを返す。移せなければ元は消さない。移す先は別の商品なので、その錠は
    /// <paramref name="moveTo"/> の中で取ってよい（元と同じIDを渡さないこと。同じ錠を2度取って止まる）。
    /// </summary>
    /// <returns>元が無ければ null。あれば <paramref name="moveTo"/> の答え（真なら元は消えている）。</returns>
    public async Task<bool?> MoveAwayAsync(
        string itemId,
        Func<ItemRecord, Task<bool>> moveTo,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (await LoadAsync(itemId, cancellationToken) is not { } current)
            {
                return null;
            }

            if (!await moveTo(current))
            {
                return false;
            }

            await DeleteLockedAsync(itemId, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 無ければ <paramref name="create"/> で作り、あれば <see cref="ChangeLocalAsync"/> と同じく今の値に当てて書く。
    /// **在るかを見てから書くまでを錠の中で行う。**外で見ると、その間に取り込みが同じIDの商品を作り、
    /// こちらの新しい空の商品で丸ごと上書きしていた。
    /// </summary>
    /// <param name="booth">在ったときに差し替える booth（取ってきたばかりの物）。null なら今のまま。</param>
    /// <returns>書いたか（<paramref name="change"/> が null を返したら false）。</returns>
    public async Task<bool> CreateOrChangeLocalAsync(
        string itemId,
        Func<ItemRecord> create,
        Func<LocalBlock, LocalBlock?> change,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default,
        BoothBlock? booth = null)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await LoadAsync(itemId, cancellationToken) is { } found
                ? (booth is null ? found : found with { Booth = booth })
                : create();
            if (change(existing.Local) is not { } changed)
            {
                return false;
            }

            var merged = LocalFields.Merge(existing.Local, changed, owns);
            merged = merged with { Purchases = Purchase.Reconcile(merged.Purchases, existing.Booth.Variations) };
            await WriteAsync(existing with { Local = merged }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return true;
    }

    /// <summary>商品の錠を持った所から呼ぶ。</summary>
    private async Task DeleteLockedAsync(string itemId, CancellationToken cancellationToken)
    {
        // 消すのも書き込み。運んでいる間は待ち、消している間は「書いている」に数えられる
        using var writing = await StoreWriteGate.EnterAsync(cancellationToken);

        var imagesDir = _paths.ItemImagesDir(itemId);
        if (Directory.Exists(imagesDir))
        {
            Directory.Delete(imagesDir, recursive: true);
        }

        DeleteIfExists(_paths.ItemHtmlFile(itemId));
        DeleteIfExists(_paths.ItemFile(itemId));

        // 画像を消してから JSON を消すまでの間に、画像の取得がフォルダを作り直していることがある
        // （取得は JSON があるかを見てから作る）。JSON が消えた今なら、もう作り直されない
        TryDeleteDirectory(imagesDir);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 商品はもう無い。残った画像は画面に出ないだけで害は無い
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class ItemLoadResult
{
    public required IReadOnlyList<ItemRecord> Items { get; init; }

    /// <summary>読み込めなかったitem。手編集で壊れた場合などに、黙って消えないよう返す。</summary>
    public required IReadOnlyList<string> FailedItemIds { get; init; }
}
