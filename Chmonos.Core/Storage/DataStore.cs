using Chmonos.Core.Models;

namespace Chmonos.Core.Storage;

/// <summary>1つのJSONファイル全体を1つの値として読み書きする小さな入れ物。</summary>
public sealed class JsonFileStore<T> where T : class, new()
{
    private readonly string _path;

    /// <summary>読んだ物を写しとして持つか（<see cref="_shared"/>）。</summary>
    private readonly bool _shareLoaded;

    /// <summary>
    /// 写しを呼び手へ渡すときの複製の作り方。null なら写しそのものを渡す（書き換えられない型）。
    /// </summary>
    private readonly Func<T, T>? _copy;

    /// <summary>
    /// 読んだ値の写し（ファイルの大きさと更新日時・値）。<see cref="_shareLoaded"/> のときだけ持つ。
    ///
    /// 登録簿は商品ページを開くたびに画面のスレッドで2回読まれていた。中身が変わっていなければ読み直さない。
    /// **書き換えられない型（init だけで、一覧も読むだけの型）はそのまま共有する。**<c>List&lt;T&gt;</c> の入れ物は
    /// 呼んだ所が足し引きして書き戻すので、共有すると別の呼び手の写しまで変わってしまう。
    /// 中の1件が書き換えられない型なら、入れ物だけ複製して渡す（<see cref="_copy"/>。知らせ2000件でも参照の並び16KB）。
    /// </summary>
    private volatile Snapshot? _shared;

    /// <summary>
    /// 自分の書き込みの数。書き始めと書き終わりで1つずつ進める（奇数なら書いている最中）。
    /// 読んでいる間に自分が書いたら、読んだ物を写しにしない——同じ大きさで同じ時刻の刻みの中に書き直すと、
    /// 大きさと日時だけでは古い中身と見分けられないため。
    /// </summary>
    private int _writes;

    private sealed record Snapshot(long Length, DateTime LastWriteUtc, int Writes, T Value);

    public JsonFileStore(string path)
        : this(path, shareLoaded: false)
    {
    }

    /// <param name="shareLoaded">
    /// 真なら、ファイルが変わっていない間は読んだ物をそのまま返す。<typeparamref name="T"/> が書き換えられない型のときだけ真にする。
    /// </param>
    public JsonFileStore(string path, bool shareLoaded)
    {
        _path = path;
        _shareLoaded = shareLoaded;
    }

    /// <param name="path">ファイル。</param>
    /// <param name="copyOnLoad">
    /// ファイルが変わっていない間は読んだ物を写しとして持ち、呼び手にはこれで作った複製を渡す。
    /// 入れ物（<c>List&lt;T&gt;</c>）だけが書き換えられ、中の1件は書き換えられない型のときに使う。
    /// </param>
    public JsonFileStore(string path, Func<T, T> copyOnLoad)
    {
        _path = path;
        _shareLoaded = true;
        _copy = copyOnLoad;
    }

    public string Path => _path;

    /// <summary>
    /// 真なら、読み直して書き換える所（<see cref="UpdateAsync(Func{T, T}, CancellationToken)"/>・<see cref="TryUpdateAsync"/>）を、
    /// **錠を取る所から書き終わりまで丸ごと**呼んだスレッドの外で走らせる。大きくなり得るファイル（未確定の記録）に付ける。
    ///
    /// 錠が空いていると、待ちは同期で抜け、続く読み直し（<see cref="LoadForUpdate"/>）は呼んだスレッドでそのまま走る。
    /// 画面のスレッドから来た命令だと、そこで JSON を丸ごと読む間、画面が止まる
    /// （未確定 8万件・37.7MB で、1件登録するたびに 330〜540ms 止まっていた。2026-09-30 に測った）。
    /// 順番（錠 → 今の値を読む → 変える → 書く）は変わらない。変え方の関数が裏のスレッドで呼ばれるようになるだけなので、
    /// 関数の中で画面の物に触る窓口（設定など、書いた値を画面が持つ物）には付けない。
    /// </summary>
    public bool UpdatesOffCallerThread { get; init; }

    /// <summary>
    /// 自分の書き込みの数（書き始めと書き終わりで1つずつ進む）。読んだ物から計算した値を控える所が、
    /// 大きさと日時と合わせて「前と同じか」を見るのに使う（同じ大きさで同じ時刻の刻みの中に書き直しても見分けるため）。
    /// </summary>
    public int WriteCount => Volatile.Read(ref _writes);

    /// <summary>
    /// ファイルが無ければ既定値を返す（初回起動をそのまま通す）。
    /// 共有してよい型なら、ファイルの大きさと更新日時が前に読んだときと同じ間は読み直さない。
    /// </summary>
    public T Load()
    {
        if (!_shareLoaded)
        {
            return JsonStore.Read<T>(_path) ?? new T();
        }

        // 大きさと日時は読む前に取る（読んだ中身がその日時より古くならないように。ItemRepository と同じ理由）
        var writes = Volatile.Read(ref _writes);
        var info = new FileInfo(_path);
        if (!info.Exists)
        {
            return JsonStore.Read<T>(_path) ?? new T();
        }

        if (_shared is { } seen && seen.Writes == writes
            && seen.Length == info.Length && seen.LastWriteUtc == info.LastWriteTimeUtc)
        {
            return Handed(seen.Value);
        }

        var value = JsonStore.Read<T>(_path) ?? new T();
        if (writes % 2 == 0 && Volatile.Read(ref _writes) == writes)
        {
            _shared = new Snapshot(info.Length, info.LastWriteTimeUtc, writes, value);
        }

        return Handed(value);
    }

    /// <summary>
    /// <see cref="Load"/> を呼んだスレッドの外で行う。**画面のスレッドから来得る所が、大きくなり得るファイルを読むときに使う**
    /// （最初の <c>await</c> より前の同期の読みは、呼んだスレッドで走る。命令の頭で未確定の記録を読む所が、そのまま画面を止めていた）。
    /// 読むだけなので錠は取らない。書き換えるなら、ここで読んだ物を書き戻さずに <see cref="UpdateAsync(Func{T, T}, CancellationToken)"/> を通す。
    /// </summary>
    public Task<T> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(Load, cancellationToken);

    /// <summary>写しを呼び手へ渡す形にする。書き換えられる入れ物なら複製（読んだ直後の値も写しと同じ物なので複製する）。</summary>
    private T Handed(T value) => _copy is null ? value : _copy(value);

    /// <summary>
    /// 錠の中で書き換える元を読む。**写しを使わずディスクから読む**（古い写しに変更を当てて書くと、外で直した分を消すため）。
    /// 写しは読むだけの道に使う。書いた後は写しを持たないので、続けて書き換えるときはどのみち読み直しになり、
    /// ここで写しを使っても省けるのは読んだ直後の1回目だけだった。
    /// </summary>
    private T LoadForUpdate() => JsonStore.Read<T>(_path) ?? new T();

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _writes);
        try
        {
            await JsonStore.WriteAsync(_path, value, cancellationToken);
        }
        finally
        {
            // 書いた物は写しにしない。次に読むときに読み直す（書くのは読むよりずっと少ない）
            Interlocked.Increment(ref _writes);
        }
    }

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 最新を読み、変えて書く。**書き手が複数いるファイル**（登録簿など）はこちらを通す。
    ///
    /// 読んでから書くまでの間に別の書き手が入ると、後から書いた方が先の変更を消す。
    /// 対応アバターの検出は始めに読んで数十分後に書くので、その間にアバター画面で保存した名前が
    /// 検出の終わりに消えていた（U15、友人データの初回で約37分）。
    /// 錠はこの窓口1つにつき1本。アプリは <see cref="DataStore"/> を1つだけ持つので、書き手どうしは重ならない。
    /// </summary>
    /// <returns>書いた値。</returns>
    public Task<T> UpdateAsync(Func<T, T> change, CancellationToken cancellationToken = default)
        => UpdateAsync(change, written: null, cancellationToken);

    /// <summary>
    /// <see cref="UpdateAsync(Func{T, T}, CancellationToken)"/> と同じだが、書けたら**錠を持ったまま** <paramref name="written"/> を呼ぶ。
    ///
    /// 書いた値を手元にも持つ（<c>SettingsService.Current</c> など）とき、錠を出てから代入すると、
    /// 2本の更新が重なったときに**先に書いた方の値が後から代入されて**、ディスクより古い値を持ち続ける。
    /// </summary>
    public Task<T> UpdateAsync(Func<T, T> change, Action<T>? written, CancellationToken cancellationToken = default)
        => UpdatesOffCallerThread
            ? Task.Run(() => UpdateCoreAsync(change, written, cancellationToken), cancellationToken)
            : UpdateCoreAsync(change, written, cancellationToken);

    private async Task<T> UpdateCoreAsync(Func<T, T> change, Action<T>? written, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var updated = change(LoadForUpdate());
            await SaveAsync(updated, cancellationToken);
            written?.Invoke(updated);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// <see cref="UpdateAsync"/> と同じだが、**変える物が無ければ書かない**（<paramref name="change"/> が null を返す）。
    /// 「押したが何も変わらなかった」で毎回ファイルを書き直さないため。
    /// </summary>
    /// <returns>書いたか。</returns>
    public Task<bool> TryUpdateAsync(Func<T, T?> change, CancellationToken cancellationToken = default)
        => UpdatesOffCallerThread
            ? Task.Run(() => TryUpdateCoreAsync(change, cancellationToken), cancellationToken)
            : TryUpdateCoreAsync(change, cancellationToken);

    /// <summary>
    /// <see cref="TryUpdateAsync"/> と同じだが、**変え方の中で待てる**。錠を持ったまま、今の値を見て別の物を先に保存し、
    /// それから同じ値を書き換えて返す形に使う（未確定の1件を商品にする：一覧から探す → 商品を保存 → 一覧から外す）。
    ///
    /// 前は「探すために読む → 商品を保存 → 錠の中でもう一度読んで外す」で、1回の登録に記録を2回読んでいた
    /// （未確定 8万件で1回 170〜280ms・68MB）。錠の中で探せば読むのは1回で、探してから外すまでの間に取り込みが一覧を書くことも無い。
    ///
    /// **関数の中で、この窓口の書き換え（UpdateAsync・TryUpdateAsync）を呼ばない**（錠は入れ子にできないので固まる）。
    /// 関数の中で取ってよい錠は、持ったままこの窓口を待つことの無い物だけ（商品ごとの錠は、変え方が同期の関数なので当てはまる）。
    /// 関数が投げたら何も書かない。
    /// </summary>
    /// <returns>書いたか。</returns>
    public Task<bool> TryUpdateAwaitingAsync(Func<T, Task<T?>> change, CancellationToken cancellationToken = default)
        => UpdatesOffCallerThread
            ? Task.Run(() => TryUpdateAwaitingCoreAsync(change, cancellationToken), cancellationToken)
            : TryUpdateAwaitingCoreAsync(change, cancellationToken);

    private async Task<bool> TryUpdateAwaitingCoreAsync(Func<T, Task<T?>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await change(LoadForUpdate()) is not { } updated)
            {
                return false;
            }

            await SaveAsync(updated, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> TryUpdateCoreAsync(Func<T, T?> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (change(LoadForUpdate()) is not { } updated)
            {
                return false;
            }

            await SaveAsync(updated, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// 保存されるもの一式への入り口。どのファイルが何を持つかをここだけ見れば分かるようにする。
/// </summary>
public sealed class DataStore
{
    public DataStore(AppPaths paths)
    {
        Paths = paths;
        Items = new ItemRepository(paths);
        // 書き換えられない型（init だけ・読むだけの一覧）は、読んだ物を共有して読み直しを省く（JsonFileStore の説明）
        UserTags = new JsonFileStore<UserTagMaster>(paths.UserTagsFile, shareLoaded: true);
        Attributes = new JsonFileStore<AttributeMaster>(paths.AttributesFile, shareLoaded: true);
        Avatars = new JsonFileStore<AvatarRegistry>(paths.AvatarRegistryFile, shareLoaded: true);
        Settings = new JsonFileStore<AppSettings>(paths.SettingsFile);
        // 未確定の記録は件数に比例して大きくなる（8万件で 37.7MB）。読み直して書き換える所を、呼んだスレッド（画面）の外で走らせる。
        // 読んだ物の写しは持たない：8万件で約 50〜60MB を持ち続けることになる（2026-09-30 に試作で測った）
        Unresolved = new JsonFileStore<List<UnresolvedFile>>(paths.UnresolvedFile) { UpdatesOffCallerThread = true };
        // 除外の記録も溜まる一方（上限が無い）。印が無いと、錠が空いているときは読み直しから書き切りまでが画面のスレッドで走り、
        // 1個外すたびに 2万件で 54〜74ms・5万件で 144〜184ms 止まっていた（2026-09-30 に測った）。
        // 変え方の関数は3か所（外す・戻す・設定の解除）とも一覧を足し引きするだけで、画面の物に触らない
        Excluded = new JsonFileStore<List<ExcludedEntry>>(paths.ExcludedFile) { UpdatesOffCallerThread = true };
        // 知らせは要確認を開くたび・既読にするたびのナビの数え直しで読まれる（上限2000件で約1MB）。
        // 入れ物は呼び手が足し引きするので複製して渡す。写しが持つ量は1000件で約570KB（stress-manage で測った）、
        // 知らせは2000件で古い既読から捨てるので、多くても約1.1MB
        Notifications = new JsonFileStore<List<NotificationRecord>>(paths.NotificationsFile, copyOnLoad: list => [.. list]);
        SearchHistory = new JsonFileStore<Services.SearchHistoryList>(paths.SearchHistoryFile);
        SavedSearches = new JsonFileStore<Services.SavedSearchList>(paths.SavedSearchesFile);
        // 足跡は「最近」で絞る・並べるたびに画面のスレッドで読まれる。中身は init だけの型なので共有する
        Recent = new JsonFileStore<Services.RecentLog>(paths.RecentFile, shareLoaded: true);
        Modifications = new ModificationRepository(paths);
        ShopBanners = new JsonFileStore<List<ShopBannerRecord>>(paths.ShopBannersFile);
        ShopNotes = new JsonFileStore<List<ShopNoteRecord>>(paths.ShopNotesFile);
        VideoTitles = new JsonFileStore<List<VideoTitleRecord>>(paths.VideoTitlesFile);
        ScanCache = new JsonFileStore<List<ScanCacheEntry>>(paths.ScanCacheFile);
        ImportState = new JsonFileStore<Scanning.ImportState>(paths.ImportStateFile);
        EditSession = new JsonFileStore<EditSession>(paths.EditSessionFile);
        Volumes = new JsonFileStore<List<VolumeRecord>>(paths.VolumesFile);
        UiState = new JsonFileStore<UiState>(paths.UiStateFile);
    }

    public AppPaths Paths { get; }

    public ItemRepository Items { get; }

    public JsonFileStore<UserTagMaster> UserTags { get; }

    public JsonFileStore<AttributeMaster> Attributes { get; }

    public JsonFileStore<AvatarRegistry> Avatars { get; }

    public JsonFileStore<AppSettings> Settings { get; }

    public JsonFileStore<List<UnresolvedFile>> Unresolved { get; }

    public JsonFileStore<List<ExcludedEntry>> Excluded { get; }

    public JsonFileStore<List<NotificationRecord>> Notifications { get; }

    /// <summary>検索の履歴。商品を開いたときに1件積む</summary>
    public JsonFileStore<Services.SearchHistoryList> SearchHistory { get; }

    /// <summary>保存した検索。検索の画面の絞り込み欄の上から呼び出す</summary>
    public JsonFileStore<Services.SavedSearchList> SavedSearches { get; }

    /// <summary>「最近」の足跡（追加・使った・閲覧）</summary>
    public JsonFileStore<Services.RecentLog> Recent { get; }

    /// <summary>改変の記録。1改変1ファイル</summary>
    public ModificationRepository Modifications { get; }

    /// <summary>ショップのバナーを調べた記録。無いショップを何度も探しに行かないため。</summary>
    public JsonFileStore<List<ShopBannerRecord>> ShopBanners { get; }

    /// <summary>ショップに人が付けた星とメモ（<see cref="ShopNoteRecord"/>）。</summary>
    public JsonFileStore<List<ShopNoteRecord>> ShopNotes { get; }

    /// <summary>YouTube の動画のタイトルの控え。30日を過ぎたら取り直すか消す（<see cref="Services.VideoTitleBook"/>）。</summary>
    public JsonFileStore<List<VideoTitleRecord>> VideoTitles { get; }

    public JsonFileStore<List<ScanCacheEntry>> ScanCache { get; }

    /// <summary>中断した取り込みの記録。最後まで終われば消える。</summary>
    public JsonFileStore<Scanning.ImportState> ImportState { get; }

    /// <summary>編集キューの位置。中断して次回続きから再開するために持つ。</summary>
    public JsonFileStore<EditSession> EditSession { get; }

    /// <summary>ドライブ文字と通し番号の組（<see cref="Services.VolumeTable"/>）。</summary>
    public JsonFileStore<List<VolumeRecord>> Volumes { get; }

    /// <summary>画面が覚えている状態。設定とは別のファイル（技術的負債 3-2）。</summary>
    public JsonFileStore<UiState> UiState { get; }
}
