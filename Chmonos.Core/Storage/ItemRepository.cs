using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

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
    /// 使っている人がいなくなった錠は捨てる（KeyedGate。前は触った商品の数だけ溜まり続けた）。
    /// </summary>
    private readonly KeyedGate<string> _itemLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// 読んだ商品の写し。鍵はファイル名の商品ID、値は「読んだときのファイルの大きさと更新日時」と読んだ中身。
    ///
    /// 全件の読み込みは画面・取り込み・検出・統計など約40か所から呼ばれ、起動だけで5〜6回走る。
    /// 2000件で1回あたり約0.4〜0.6秒・割り当て45MB（2026-09-24 に stress-realcat で測った）で、
    /// ほとんどは前の回から何も変わっていないファイルを読み直していた。
    /// **大きさと更新日時が同じなら前に読んだ中身を返し、変わったファイルだけ読み直す。**
    ///
    /// 中身（<see cref="ItemRecord"/>）は書き換えられない形（init だけのレコードと読むだけの一覧）なので、
    /// 呼んだ所どうし（画面どうし・裏の作業）で同じ物を共有してよい。変えるときは <c>with</c> で写しを作る。
    ///
    /// 新しさ：自分の書き込み（<see cref="WriteAsync"/>）は商品の錠の中で写しも差し替えるので、書いた直後の読み込みから新しい。
    /// アプリの外で書き換えられた物（手で直した JSON）は更新日時か大きさが変わるので読み直す。
    /// 見逃すのは「同じ大きさで、ファイルシステムの時刻の刻み（NTFS で最大約16ms）の中に外から2回書かれた」ときだけ。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedItem> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 控えの1件。**参照で比べるためにクラスにしている**（<see cref="ReadThroughAsync"/> の <c>TryUpdate</c>）。
    /// レコードにすると中身が同じ別の控えを「同じ」とみなし得る。
    /// </summary>
    private sealed class CachedItem(long length, DateTime lastWriteUtc, ItemRecord item)
    {
        public ItemRecord Item { get; } = item;

        public bool Matches(long otherLength, DateTime otherLastWriteUtc)
            => length == otherLength && lastWriteUtc == otherLastWriteUtc;
    }

    public ItemRepository(AppPaths paths)
    {
        _paths = paths;
    }

    /// <remarks>
    /// 読むだけの入口（在るか・読む・控え・説明HTML）は、**商品IDの形でない ID を「無い」として答える**（<see cref="StoreIds"/>）。
    /// ID は登録簿・足跡・やりかけの記録など手で直せる JSON からも来るので、そこで投げると読むだけの画面や裏の作業が止まる。
    /// 場所を組まないので、形の外れた ID で保存先の外を読むことも無い。書く・消すは <see cref="AppPaths"/> が投げる
    /// </remarks>
    public bool Exists(string itemId) => StoreIds.IsItemId(itemId) && File.Exists(_paths.ItemFile(itemId));

    /// <summary>
    /// 1件を読む。**古い形の読み替えはしない**（公開前は、今の形に合わないデータの側を問題にする・ユーザ判断 2026-09-12）。
    /// 以前は旧形式の購入記録（orderedVariations）を読むたびに purchases へ移していた。
    ///
    /// ファイルが前に読んだときと同じ（大きさと更新日時）なら、読んだ写しを返す（<see cref="_cache"/>）。
    /// </summary>
    public Task<ItemRecord?> LoadAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (!StoreIds.IsItemId(itemId))
        {
            return Task.FromResult<ItemRecord?>(null);
        }

        var path = _paths.ItemFile(itemId);
        var info = new FileInfo(path);
        return info.Exists
            ? ReadThroughAsync(itemId, path, info.Length, info.LastWriteTimeUtc, cancellationToken)
            : Task.FromResult<ItemRecord?>(null);
    }

    /// <summary>
    /// 控えが今のファイルと合えば控えを、合わなければ読んで控える。
    ///
    /// **大きさと日時は読む前に取った物を渡す。**読む前に取れば、読んだ中身は必ずその日時と同じか新しい。
    /// 逆（読んでから日時を取る）だと、読んだ後に書かれた新しい日時に古い中身を結び付けて、以後ずっと古い物を返す。
    /// 控えを入れるのは、見たときから誰も差し替えていないときだけ（書き手が錠の中で入れた新しい控えを古い中身で潰さない）。
    /// </summary>
    private async Task<ItemRecord?> ReadThroughAsync(
        string itemId,
        string path,
        long length,
        DateTime lastWriteUtc,
        CancellationToken cancellationToken)
    {
        _cache.TryGetValue(itemId, out var seen);
        if (seen is not null && seen.Matches(length, lastWriteUtc))
        {
            return seen.Item;
        }

        var item = await JsonStore.ReadAsync<ItemRecord>(path, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var entry = new CachedItem(length, lastWriteUtc, item);
        if (seen is null)
        {
            _cache.TryAdd(itemId, entry);
        }
        else
        {
            _cache.TryUpdate(itemId, entry, seen);
        }

        return item;
    }

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

    /// <summary>
    /// 書いて、写しも書いた物に差し替える。**商品の錠を持った所から呼ぶ**（写しの差し替えを書いた順に並べるため）。
    ///
    /// 書いた後の大きさと日時で控えるので、直後の読み込みは読み直さずに書いた物を返す。
    /// </summary>
    private async Task WriteAsync(ItemRecord item, CancellationToken cancellationToken)
    {
        var path = _paths.ItemFile(item.Id);
        try
        {
            // 書くたびに、書いた版を items/.prev へ控える（読めなくなったときに通知の画面から戻す元）
            await JsonStore.WriteAsync(path, item, _paths.ItemCopyFile(item.Id), cancellationToken);
        }
        catch
        {
            // 置き換えの途中で失敗すると、本体が新旧どちらか分からない。控えは捨てて次に読み直す
            _cache.TryRemove(item.Id, out _);
            throw;
        }

        var info = new FileInfo(path);
        if (info.Exists)
        {
            _cache[item.Id] = new CachedItem(info.Length, info.LastWriteTimeUtc, item);
        }
        else
        {
            _cache.TryRemove(item.Id, out _);
        }
    }

    /// <summary>
    /// 組み直した結果が今の中身と同じなら書かない。**呼んだ側への答え（書いたか）は変えない**——
    /// 「触る物があった」ことは同じで、ディスクへ同じ中身を書き直すのを省くだけ。
    ///
    /// 取り込み直すと、変わっていない商品もファイルを足す道（<see cref="SaveLocalAsync"/>）を通り、
    /// 1件ごとに一時ファイルへ書いてディスクへ書き出し（Flush）、置き換えていた（300本の取り込み直しで毎回300件）。
    /// 書き直すと更新日時も動くので、商品の写し（<see cref="_cache"/>）も次の読み込みで読み直しになっていた。
    ///
    /// 比べるのは保存する形（JSON）で、書いたら読み戻して同じになる物は同じとみなす。
    /// </summary>
    private async Task WriteIfChangedAsync(ItemRecord current, ItemRecord updated, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(current, updated) || SameContent(current, updated))
        {
            return;
        }

        await WriteAsync(updated, cancellationToken);
    }

    private static bool SameContent(ItemRecord left, ItemRecord right)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(left, JsonStore.Options).AsSpan()
            .SequenceEqual(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(right, JsonStore.Options));

    private KeyedGate<string>.Handle LockFor(string itemId)
        => _itemLocks.For(itemId);

    /// <summary>その商品の錠を持っている・待っている人の数（試験で、書き手が錠の前まで来たかを見る）。</summary>
    internal int LockUsers(string itemId) => _itemLocks.UsersOf(itemId);

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

            await WriteIfChangedAsync(
                existing,
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
    /// <param name="beforeWrite">
    /// 書く直前に、錠の中で、今の値と書く値を渡して呼ぶ。偽を返したら書かない（false を返す）。
    /// やりかけの記録へ「書く前と書いた後の指紋」を残すため（IDの変更。<c>Services/OperationFingerprint</c>）——
    /// 錠の外で読んだ値の指紋だと、読んでから書くまでに取り込みが書いた分で食い違う。
    /// </param>
    /// <returns>書いたか（itemが消えていた・触る物が無かった・<paramref name="beforeWrite"/> が断ったときは false）。</returns>
    public async Task<bool> ChangeLocalAsync(
        string itemId,
        Func<LocalBlock, LocalBlock?> change,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default,
        Func<LocalBlock, LocalBlock, Task<bool>>? beforeWrite = null)
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
            if (beforeWrite is not null && !await beforeWrite(existing.Local, merged))
            {
                return false;
            }

            await WriteIfChangedAsync(existing, existing with { Local = merged }, cancellationToken);
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
            await WriteIfChangedAsync(existing, existing with { Booth = booth, Local = local }, cancellationToken);
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
            .Where(id => KeepWellFormed(id, $"{id}.json"))
            .ToList();
    }

    /// <summary>形の外れたファイル名・中の ID を書き残したもの。全件の読み込みは何度も呼ばれるので、1つにつき1回だけ書く</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _reportedMalformed = new(StringComparer.Ordinal);

    /// <summary>
    /// 商品IDの形なら真。外れていれば書き残して偽（操作から外す）。
    /// 外した物は手で直したファイルの点検（<see cref="Services.HandEditCheck"/>）にも出るので、ここでは書き残すだけ
    /// </summary>
    private bool KeepWellFormed(string? id, string where)
    {
        if (StoreIds.IsItemId(id))
        {
            return true;
        }

        if (_reportedMalformed.TryAdd(where, 0))
        {
            Diagnostics.AppLog.Warn("商品の記録を読む", $"items/{where} は商品IDの形でないので扱わない（「{id}」）");
        }

        return false;
    }

    /// <summary>
    /// ファイル名が商品IDの形でない物（手で付けた名前）。全件の読み込みからは外してある。
    /// 手で直したファイルの点検（<see cref="Services.HandEditCheck"/>）に出す
    /// </summary>
    public IReadOnlyList<string> FindMalformedFileNames()
    {
        if (!Directory.Exists(_paths.ItemsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(_paths.ItemsDir, "*.json")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !StoreIds.IsItemId(Path.GetFileNameWithoutExtension(name)))
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
    ///
    /// **何度呼んでも軽い**（2026-09-24）。変わっていないファイルは読まずに写しを返すので、
    /// 2回目からは列挙と比べるだけ（2000件で1回 0.33〜0.67秒・45MB → 3〜13ms・1.4MB）。返す商品は呼んだ所どうしで共有される
    /// （書き換えられない形なので安全）。新しさは「呼んだ時点のディスク」と同じで、このアプリが書いた物は書き終えた直後から入る。
    /// 画面が全件を抱え続けて自分で差分を追う必要は無く、要るたびにこれを呼べばよい。
    /// </remarks>
    public Task<ItemLoadResult> LoadAllAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => LoadAllCoreAsync(progress, cancellationToken), cancellationToken);

    private async Task<ItemLoadResult> LoadAllCoreAsync(
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var failures = new List<ItemReadFailure>();
        var read = await ReadAllAsync(failures, progress, cancellationToken);
        return new ItemLoadResult
        {
            // 中の ID が形を外れた商品は外す。画面と裏の作業はどれも中の ID で画像や説明の場所を組む。
            // ファイル名とずれているので、手で直したファイルの点検（ファイル名と中の商品IDが違う）には出る
            Items = read
                .Where(pair => KeepWellFormed(pair.Item.Id, $"{pair.FileId}.json の中の id"))
                .Select(pair => pair.Item)
                .ToList(),
            FailedItemIds = failures.Select(failure => failure.ItemId).ToList(),
        };
    }

    /// <summary>
    /// 全件を「ファイル名の商品ID と中身」の組で読む。写しと合うファイルは読まない（<see cref="_cache"/>）。
    ///
    /// 大きさと日時は列挙で取れている物を使う（1件ずつ問い直すと、それだけで2000回ファイルシステムに問い合わせる）。
    /// 列挙に出なかった商品（外した・手で消した）の控えはここで捨てる。
    /// </summary>
    private async Task<List<(string FileId, ItemRecord Item)>> ReadAllAsync(
        List<ItemReadFailure> failures,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var found = new List<(string, ItemRecord)>();
        if (!Directory.Exists(_paths.ItemsDir))
        {
            _cache.Clear();
            return found;
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        var loaded = 0;
        foreach (var file in new DirectoryInfo(_paths.ItemsDir).EnumerateFiles("*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemId = Path.GetFileNameWithoutExtension(file.Name);
            if (string.IsNullOrEmpty(itemId) || !KeepWellFormed(itemId, file.Name))
            {
                continue;
            }

            listed.Add(itemId);
            try
            {
                if (await ReadThroughAsync(itemId, file.FullName, file.Length, file.LastWriteTimeUtc, cancellationToken) is { } item)
                {
                    found.Add((itemId, item));
                }
                else if (File.Exists(file.FullName))
                {
                    // 中身が「null」だけのファイル。在るのに商品にならず、前は黙って検索から消えていた
                    failures.Add(new ItemReadFailure(itemId, Line: null, IsBroken: true));
                }
            }
            catch (System.Text.Json.JsonException exception)
            {
                // 行番号は 0 から数える。人がエディタで開いて探す数に直す
                failures.Add(new ItemReadFailure(itemId, exception.LineNumber + 1, IsBroken: true));
            }
            catch (IOException)
            {
                // ほかのアプリが開いている・読む途中で消えた。壊れてはいないので、通知には出さない
                failures.Add(new ItemReadFailure(itemId, Line: null, IsBroken: false));
            }

            progress?.Report(++loaded);
        }

        foreach (var gone in _cache.Keys.Where(id => !listed.Contains(id)).ToList())
        {
            // 列挙の後に作られた商品の控えまで捨てることがあるが、次に読むときに読み直すだけで害は無い
            _cache.TryRemove(gone, out _);
        }

        return found;
    }

    /// <summary>
    /// ファイル名と中の商品IDが違う物（手で直したときのずれ・L6）。全件の読み込みと同じ写しから引く。
    /// 読めなかったファイルは含めない（「読めなかった商品」として別に数えている）。
    /// </summary>
    public Task<IReadOnlyList<(string FileId, string ItemId)>> FindMisnamedAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<(string FileId, string ItemId)>>(
            async () => (await ReadAllAsync([], null, cancellationToken))
                .Where(pair => !string.Equals(pair.FileId, pair.Item.Id, StringComparison.Ordinal))
                .Select(pair => (pair.FileId, pair.Item.Id))
                .ToList(),
            cancellationToken);

    /// <summary>
    /// 壊れていて読めない商品の記録（JSON として読めない・中身が null）。全件の読み込みと同じ写しから引く。
    /// ほかのアプリが開いていて読めなかっただけの物は入れない（次に読めば読める）。
    /// </summary>
    public Task<IReadOnlyList<ItemReadFailure>> FindUnreadableAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ItemReadFailure>>(
            async () =>
            {
                var failures = new List<ItemReadFailure>();
                await ReadAllAsync(failures, null, cancellationToken);
                return failures.Where(failure => failure.IsBroken).ToList();
            },
            cancellationToken);

    /// <summary>
    /// 戻せる控え（<see cref="AppPaths.ItemCopyFile"/>）があるか。読めて、中の商品IDがファイル名と同じ物だけを数える
    /// （ID の違う控えを据えると、以後の保存が別のファイルへ書かれる）。
    /// </summary>
    public bool HasUsableCopy(string itemId) => StoreIds.IsItemId(itemId) && IsUsableRecord(_paths.ItemCopyFile(itemId), itemId);

    /// <summary>
    /// 控えが読めるなら、画面に出す名前（表示名があればそれ、無ければ BOOTH の名前）。読めない・名前が無いときは null。
    /// 読めない記録の知らせの題にする（ID は使う人が扱う第一の情報にしない。ユーザ判断 2026-10-05）。
    /// </summary>
    public string? ReadCopyName(string itemId)
    {
        if (ReadCopy(itemId) is not { } record)
        {
            return null;
        }

        var name = record.DisplayName;
        return string.IsNullOrWhiteSpace(name) || string.Equals(name, itemId, StringComparison.Ordinal) ? null : name;
    }

    /// <summary>
    /// 控えが読めて、中の商品IDが同じなら、その記録。読めない・無いときは null。
    /// 読めない記録の知らせの行に、その商品の絵を添えるのに使う（本体は読めないが、絵の並びと★の指名は控えにある）。
    /// </summary>
    public ItemRecord? ReadCopy(string itemId)
    {
        if (!StoreIds.IsItemId(itemId))
        {
            return null;
        }

        try
        {
            return JsonStore.Read<ItemRecord>(_paths.ItemCopyFile(itemId)) is { } record
                && string.Equals(record.Id, itemId, StringComparison.Ordinal)
                    ? record
                    : null;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsUsableRecord(string path, string itemId)
    {
        try
        {
            return JsonStore.Read<ItemRecord>(path) is { } record
                && string.Equals(record.Id, itemId, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>本体が今は読めるか（壊れていない）。無ければ偽。</summary>
    private static bool IsReadable(string path)
    {
        try
        {
            return JsonStore.Read<ItemRecord>(path) is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 読めない本体を <c>items/_broken/{id}-{日時}.json</c> へよけ、控えを本体に据える（通知の「1つ前の版に戻す」）。
    ///
    /// **商品の錠の中で、本体がまだ読めないことを見てから動かす。**押すまでの間に手で直していれば何もしない。
    /// 壊れた本体は消さない：壊れた中にしか無い入力を、後から手で救えるように。
    /// </summary>
    public async Task<BrokenItemOutcome> RestoreCopyAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var path = _paths.ItemFile(itemId);
            if (!File.Exists(path))
            {
                return BrokenItemOutcome.Missing;
            }

            if (IsReadable(path))
            {
                return BrokenItemOutcome.NotBroken;
            }

            var copy = _paths.ItemCopyFile(itemId);
            if (!File.Exists(copy))
            {
                return BrokenItemOutcome.NoCopy;
            }

            if (!IsUsableRecord(copy, itemId))
            {
                return BrokenItemOutcome.CopyUnreadable;
            }

            SetAside(itemId, path);

            // 控えは残す（据えた本体と同じ中身。次にアプリが書けば書き直される）
            var temporaryPath = path + ".restore.tmp";
            File.Copy(copy, temporaryPath, overwrite: true);
            File.Move(temporaryPath, path, overwrite: true);
            _cache.TryRemove(itemId, out _);
            return BrokenItemOutcome.Done;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 読めない本体を <c>items/_broken</c> へよける（通知の「BOOTHから作り直す」の前半）。よけた先を返す。
    /// 本体が無い・今は読めるなら動かさず、その答えだけを返す。
    /// </summary>
    public async Task<(BrokenItemOutcome Outcome, string? MovedTo)> SetAsideBrokenAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var path = _paths.ItemFile(itemId);
            if (!File.Exists(path))
            {
                return (BrokenItemOutcome.Missing, null);
            }

            if (IsReadable(path))
            {
                return (BrokenItemOutcome.NotBroken, null);
            }

            return (BrokenItemOutcome.Done, SetAside(itemId, path));
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// よけた本体を元の場所へ戻す（作り直しが BOOTH から取れずに終わったとき）。戻したかを返す。
    /// その間に別の道（取り込み）が同じ商品を作っていれば戻さない——読めない方で読める方を潰さない。
    /// </summary>
    public async Task<bool> PutBackBrokenAsync(string itemId, string movedTo, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
            var path = _paths.ItemFile(itemId);
            if (File.Exists(path) || !File.Exists(movedTo) || !StoreIds.IsInside(movedTo, _paths.BrokenItemsDir))
            {
                return false;
            }

            File.Move(movedTo, path);
            _cache.TryRemove(itemId, out _);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 本体を <c>_broken</c> へ移す。名前は「商品ID-日時」で、同じ秒に2つ目が来たら番号を足す（前によけた物を上書きしない）。
    /// 商品の錠と書き込みの門を持った所から呼ぶ。
    /// </summary>
    private string SetAside(string itemId, string path)
    {
        Directory.CreateDirectory(_paths.BrokenItemsDir);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var target = Path.Combine(_paths.BrokenItemsDir, $"{itemId}-{stamp}.json");
        for (var number = 2; File.Exists(target); number++)
        {
            target = Path.Combine(_paths.BrokenItemsDir, $"{itemId}-{stamp}-{number}.json");
        }

        File.Move(path, target);
        _cache.TryRemove(itemId, out _);
        return target;
    }

    /// <summary>表示用の説明HTML。商品ページを開いた時だけ読む。</summary>
    public async Task<string?> LoadDescriptionHtmlAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (!StoreIds.IsItemId(itemId))
        {
            return null;
        }

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
    /// <param name="beforeWrite">書く直前に、錠の中で、今の値（無ければ作った物）と書く値を渡して呼ぶ。偽なら書かない（<see cref="ChangeLocalAsync"/> と同じ）。</param>
    /// <returns>書いたか（<paramref name="change"/> が null を返した・<paramref name="beforeWrite"/> が断ったら false）。</returns>
    public async Task<bool> CreateOrChangeLocalAsync(
        string itemId,
        Func<ItemRecord> create,
        Func<LocalBlock, LocalBlock?> change,
        IReadOnlyCollection<LocalField> owns,
        CancellationToken cancellationToken = default,
        BoothBlock? booth = null,
        Func<LocalBlock, LocalBlock, Task<bool>>? beforeWrite = null)
    {
        var gate = LockFor(itemId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var found = await LoadAsync(itemId, cancellationToken);
            var existing = found is null ? create() : (booth is null ? found : found with { Booth = booth });
            if (change(existing.Local) is not { } changed)
            {
                return false;
            }

            var merged = LocalFields.Merge(existing.Local, changed, owns);
            merged = merged with { Purchases = Purchase.Reconcile(merged.Purchases, existing.Booth.Variations) };
            if (beforeWrite is not null && !await beforeWrite(existing.Local, merged))
            {
                return false;
            }

            var updated = existing with { Local = merged };
            if (found is null)
            {
                await WriteAsync(updated, cancellationToken);
            }
            else
            {
                await WriteIfChangedAsync(found, updated, cancellationToken);
            }
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

        // 消す直前に、組んだ場所が保存先の所定のフォルダに収まるかも確かめる（StoreIds の2枚目の守り）。
        // 形の検査を後で緩めても、画像のフォルダの丸ごとの削除が保存先の外へ届かないように
        var imagesDir = _paths.ItemImagesDir(itemId);
        StoreIds.EnsureInside(imagesDir, _paths.ImagesDir);
        StoreIds.EnsureInside(_paths.ItemFile(itemId), _paths.ItemsDir);
        if (Directory.Exists(imagesDir))
        {
            Directory.Delete(imagesDir, recursive: true);
        }

        DeleteIfExists(_paths.ItemHtmlFile(itemId));

        // 外した商品の控えは戻す先が無い。残すと、同じIDで登録し直したときに古い版が戻せてしまう
        DeleteIfExists(_paths.ItemCopyFile(itemId));
        try
        {
            DeleteIfExists(_paths.ItemFile(itemId));
        }
        finally
        {
            _cache.TryRemove(itemId, out _);
        }

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

/// <summary>
/// 全件の読み込みで読めなかった商品の記録1件。
/// <paramref name="Line"/> は JSON の何行目で読めなくなったか（1から。分からなければ null）。
/// <paramref name="IsBroken"/> は中身が壊れているか（偽ならほかのアプリが開いていて読めなかっただけ）。
/// </summary>
public sealed record ItemReadFailure(string ItemId, long? Line, bool IsBroken);

/// <summary>読めない商品の記録を戻す・よけるときの答え。</summary>
public enum BrokenItemOutcome
{
    /// <summary>戻した・よけた。</summary>
    Done,

    /// <summary>本体がもう無い（外した・手で消した）。</summary>
    Missing,

    /// <summary>本体は今は読める（押すまでの間に手で直した）。</summary>
    NotBroken,

    /// <summary>控えが無い。</summary>
    NoCopy,

    /// <summary>控えも読めない（か、中の商品IDが違う）。</summary>
    CopyUnreadable,
}

public sealed class ItemLoadResult
{
    public required IReadOnlyList<ItemRecord> Items { get; init; }

    /// <summary>読み込めなかったitem。手編集で壊れた場合などに、黙って消えないよう返す。</summary>
    public required IReadOnlyList<string> FailedItemIds { get; init; }
}
