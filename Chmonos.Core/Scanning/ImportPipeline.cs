using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using BoothIdResolver;
using BoothZipInspector;
using BoothZipInspector.Models;

namespace Chmonos.Core.Scanning;

/// <summary>
/// 取り込みの段。**通信する段は、急ぐ順に並んでいる。**
///
/// 商品JSONだけで検索・絞り込み・統計に要るものは全部揃う
/// （名前・ショップ・タグ・カテゴリ・価格・variation・スキ数・R-18・販売終了・公開日）。
/// 画像は見た目のためだけで、探すのにも数えるのにも要らない。
/// 実データでは**画像が全リクエストの76%**を占めるので、後ろに回すと
/// 「使えるようになるまで」が劇的に縮む。
/// </summary>
public enum ImportPhase
{
    Scanning,
    Resolving,

    /// <summary>① 商品JSON（全商品）。ここが終われば検索も統計も成立する。</summary>
    FetchingJson,

    /// <summary>② 商品ページHTML（全商品）。対応アバターの節と説明文。</summary>
    FetchingHtml,

    /// <summary>
    /// ③ 対応アバターの検出。
    ///
    /// ①②の後に置けるのは、判定の材料（category）が商品JSONに入っているから。
    /// 取り込んだ直後は説明文もタグも手元にあるので、ほとんどが通信なしで済む。
    /// </summary>
    Detecting,

    /// <summary>④ 1枚目の画像（全商品）。1枚あれば一覧のカードは完成する。</summary>
    FetchingThumbnails,

    /// <summary>⑤ 残りの画像（商品ごと）。ホバーのギャラリーと商品ページで要る。</summary>
    FetchingGallery,

    /// <summary>⑥ ショップのアイコン。無くても名前で用は足りるので最後。</summary>
    FetchingShopIcons,
}

public sealed class ImportProgress
{
    public required ImportPhase Phase { get; init; }

    public int Current { get; init; }

    public int Total { get; init; }

    /// <summary>
    /// 段の中の小さな段（検出の「見つかった商品を確かめています」など）。件数の前に出す。
    /// 件数だけでは何を数えているかが読めず、検出の「346 / 599」がアバターの数に見えた。
    /// </summary>
    public string? Step { get; init; }

    public string? Detail { get; init; }
}

public sealed class ImportSummary
{
    public int FilesScanned { get; init; }

    /// <summary>実際にSHA-256を計算した件数。キャッシュの効き具合がここに出る。</summary>
    public int FilesHashed { get; init; }

    public int FilesReusedFromCache { get; init; }

    public int FilesExcluded { get; init; }

    /// <summary>既にitemが持っていたので未確定へ流さなかった件数。手作業で紐付けたファイルがここに入る。</summary>
    public int FilesAlreadyOwned { get; init; }

    /// <summary>アーカイブの展開先とみなして取り込まなかったファイル数。</summary>
    public int FilesSkippedAsUnpacked { get; init; }

    /// <summary>
    /// 権限などで読めず、取り込めなかったファイル数（E4）。**0 でないときだけ画面に出す。**
    /// 黙って飛ばしていたので、取り込んだつもりの物が入っていないことに気付けなかった。
    /// </summary>
    public int FilesUnreadable { get; init; }

    /// <summary>
    /// 権限などで中を読めず、取り込めなかったフォルダの数（大容量の確かめ #5・2026-09-30）。**0 でないときだけ画面に出す。**
    /// 前は黙って空として飛ばしていた。中に何件あったかは読めないので、ファイルの数には足さず別に数える。
    /// </summary>
    public int FoldersUnreadable { get; init; }

    /// <summary>
    /// 中身が手元に無いクラウドのファイル（OneDrive の「オンラインのみ」）で、読まなかった数。
    /// 読むとダウンロードが始まるので取り込まないが、黙って飛ばすと取り込んだつもりの物が入っていないことに
    /// 気付けない。数と次の手（「常にこのデバイスに保持する」）を結果に出す（ユーザ判断 2026-09-23）。
    /// 読めなかった物（<see cref="FilesUnreadable"/>）とは直し方が違うので別に数える。
    /// </summary>
    public int FilesOnlineOnly { get; init; }

    /// <summary>
    /// 取り込み元の中で、たどらなかったジャンクション・シンボリックリンクの数（見つからない・移動の点検 15）。
    /// 中の物は取り込んでいない。リンク先を取り込み元に足せば取り込める。
    /// </summary>
    public int LinksSkipped { get; init; }

    /// <summary>
    /// zip として開けず、未確定に「壊れたzip」の印を付けて置いた数（大容量の確かめ 問題4・ユーザ判断 2026-09-30）。**0 でないときだけ画面に出す。**
    /// 読めなかった物（<see cref="FilesUnreadable"/>）とは別に数える——あちらは取り込めていない物で、直し方は「閉じて取り込み直す」。
    /// こちらは取り込めていて（未確定にある）、直し方は「ダウンロードし直す」。
    /// 数えるのは未確定に置いた物だけ。商品に結び付いた物は次の手の案内が違う（未確定の画面には無い）ので、
    /// 別に数える（<see cref="FilesBrokenArchiveOnItems"/>）。
    /// </summary>
    public int FilesBrokenArchive { get; init; }

    /// <summary>
    /// zip として開けず、商品のファイルの記録に印を付けた数（ユーザ判断 2026-09-30）。**0 でないときだけ画面に出す。**
    /// この取り込みで開いてみた物だけを数える（走査していない取り込み元の物は見ていない）。
    /// 新しく結び付いた物も、前から商品が持っていた物も入る——壊れた zip は中身の一覧が空なので、取り込むたびに開き直している。
    /// </summary>
    public int FilesBrokenArchiveOnItems { get; init; }

    /// <summary>
    /// 壊れた zip を持つ商品の名前（<see cref="FilesBrokenArchiveOnItems"/> の持ち主。見つけた順）。
    /// 未確定と違って、商品の側には壊れた物だけを並べる画面が無いので、結果の文でどの商品かを言うために持つ。
    /// </summary>
    public IReadOnlyList<string> BrokenArchiveItemNames { get; init; } = [];

    /// <summary>見つかった展開先フォルダ。削除機能に渡す候補になる。</summary>
    public IReadOnlyList<UnpackedFolder> UnpackedFolders { get; init; } = [];

    public int ItemsAdded { get; init; }

    public int ItemsAlreadyKnown { get; init; }

    public int UnresolvedFiles { get; init; }

    public int NotFound { get; init; }

    public int TemporaryFailures { get; init; }

    /// <summary>
    /// 応答の無い失敗か 5xx が続いたので、問い合わせを打ち切ったか。打ち切ったならその理由（ユーザ判断 2026-09-29）。
    /// 打ち切っていれば、<see cref="TemporaryFailures"/> には打ち切って問い合わせなかった商品も入る。
    /// </summary>
    public BoothOutageKind Stopped { get; init; }

    public int ImagesDownloaded { get; init; }

    /// <summary>③ 検出が対応アバターを書き込んだ商品数。</summary>
    public int AvatarItemsUpdated { get; init; }

    /// <summary>③ 検出で分かったアバターの数。</summary>
    public int AvatarsFound { get; init; }

    /// <summary>③ 検出が途中で止まった理由。止まっても取り込み自体は成立している。</summary>
    public string? AvatarDetectError { get; init; }

    /// <summary>
    /// ③ 検出を走らせたか。**BOOTHから1件も取らなかった取り込みでは走らない**
    /// （既にある商品にファイルを足しただけなど）。検出が読むのはBOOTHから取った説明文・タグ・種類名なので、
    /// 新しく読むものが無い。走らなかったのに「見つかりませんでした」と言わないために持つ。
    /// </summary>
    public bool AvatarDetectRan { get; init; }
}

public interface IImportPipeline
{
    Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>走らせている最中にも対象を足せる版。呼ぶ側が作業集合を握る。</summary>
    Task<ImportSummary> RunAsync(
        ImportWorkSet work,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 取り込みの3フェーズ（走査 → BoothID解決 → BOOTH取得）。
///
/// 中断への備えは2段構え。ハッシュ計算の結果は途中でも定期的に保存し、
/// 取得済みのitemは1件ごとに保存するので、閉じて再実行すれば続きから進む。
/// </summary>
public sealed class ImportPipeline : IImportPipeline
{
    /// <summary>
    /// ハッシュの途中で走査の控えを書く間隔。落ちたときに計算し直すのは長くてもこの間の分だけ
    /// （SSD で数GB。取り込み全体の数十分に比べれば小さい）で、控え全体を書き直すのは10秒に1回で済む。
    /// </summary>
    private const long ScanCacheSaveIntervalMs = 10_000;

    /// <summary>
    /// 走査の控えを、錠の中で今の控えに重ねて書く（<see cref="ScanCacheIndex.MergeInto"/>）。変えた物が無ければ書かない。
    /// 見つからないファイルを探す所も同じ控えに足すので、取り込みの写しで丸ごと書くと相手の分を消す。
    /// </summary>
    private async Task SaveScanCacheAsync(ScanCacheIndex scanCache, CancellationToken cancellationToken)
    {
        if (scanCache.HasChanges)
        {
            await _store.ScanCache.UpdateAsync(scanCache.MergeInto, cancellationToken);
        }
    }

    private readonly DataStore _store;
    private readonly IBoothClient _client;
    private readonly ImagePipeline _images;
    private readonly Func<AppSettings> _currentSettings;
    private readonly FolderScanner _scanner = new();

    /// <summary>③ 対応アバターの検出。渡されなければその段を飛ばす。</summary>
    private readonly Services.IAvatarService? _avatars;

    /// <summary>unitypackage の中身を裏で読む。渡されなければ読まない（使うときに zip を解く）。</summary>
    private readonly Services.UnityPackageCatalog? _unityPackages;

    /// <summary>ドライブ文字と通し番号の組を控える。渡されなければ控えない。</summary>
    private readonly Services.VolumeTable? _volumes;

    /// <summary>
    /// 見つからなくなった日時の見回り。アプリでは起動時の見回りと同じ1つを渡し、重ならないようにする（ユーザ判断 2026-10-05）。
    /// </summary>
    private readonly Services.MissingMarksSweep _missingMarks;

    public ImportPipeline(
        DataStore store,
        IBoothClient client,
        ImagePipeline images,
        AppSettings? settings = null,
        Services.IAvatarService? avatars = null,
        Services.UnityPackageCatalog? unityPackages = null)
        : this(store, client, images, SettingsSource.Fixed(settings), avatars, unityPackages)
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public ImportPipeline(
        DataStore store,
        IBoothClient client,
        ImagePipeline images,
        Func<AppSettings> currentSettings,
        Services.IAvatarService? avatars = null,
        Services.UnityPackageCatalog? unityPackages = null,
        Services.VolumeTable? volumes = null,
        Services.MissingMarksSweep? missingMarks = null)
    {
        _store = store;
        _client = client;
        _images = images;
        _currentSettings = currentSettings;
        _avatars = avatars;
        _unityPackages = unityPackages;
        _volumes = volumes;
        _missingMarks = missingMarks ?? new Services.MissingMarksSweep(store, volumes: volumes);
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    public Task<ImportSummary> RunAsync(
        IReadOnlyList<string> folders,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => RunAsync(new ImportWorkSet(folders), progress, cancellationToken);

    /// <summary>
    /// 対象がまだ残っている限り回り続ける。
    ///
    /// 走っている最中に <see cref="ImportWorkSet.Add"/> されたフォルダは、
    /// 今の周回が終わったところで次の周回として拾う。**押し直す必要がない。**
    /// パイプラインが1本のままなので、取得の順序も進捗の出どころも1つに保てる。
    ///
    /// まとめの件数は周回をまたいで足し合わせる。ユーザにとっては
    /// 「1回の取り込み」なので、途中で足したぶんも同じ数字に入っていてほしい。
    /// </summary>
    /// <remarks>
    /// **全体を画面のスレッドの外で回す**（2026-09-24）。Core は続きを元の文脈へ戻すので、画面から始めた取り込みは
    /// 走査以外（全件の読み込み・登録したフォルダの測り直し・zip の後の組み立て・商品の書き込み・③の検出）を画面のスレッドで回していた
    /// （作り物の 300 本の取り込み直しで、1回 2.5秒のうち 2.0秒が画面のスレッド）。
    /// 画面への知らせは、進み具合（画面の <c>Progress</c>）・通信の様子・画像の保存とも画面のスレッドへ運んで受けている。
    /// 積む（<see cref="ImportWorkSet"/>）は錠で守られている。優先度（Prioritize）は AsyncLocal なので中へ引き継がれる。
    /// </remarks>
    public Task<ImportSummary> RunAsync(
        ImportWorkSet work,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
        // 取り消しの印は中で見る（Task.Run に渡すと、始まる前の取り消しで例外の種類と記録の残り方が変わる）
        => Task.Run(() => RunCoreAsync(work, progress, cancellationToken));

    private async Task<ImportSummary> RunCoreAsync(
        ImportWorkSet work,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        _store.Paths.EnsureCreated();

        // **始めた時点で前回の記録を消す。**書くのは①の中だけ、消すのは最後まで走り切ったときだけだったので、
        // ①より手前（走査・解決）で止めた回と、新しい商品が1件も無かった回は記録に触れない。
        // その結果、起動時に出る「前回は N / M 件まで進んで中断しました」が、
        // 前回ではなくもっと前の回の数字のことがあった（走査は最も長い段なので、そこで止めるのは珍しくない）
        //
        // ただし**前の回に BOOTH の不調で取れなかった商品は引き継ぐ**（ユーザ判断 2026-09-23・#10）。
        // 別のフォルダを取り込んだだけで消すと、取り直す手が無くなる。取り直せたところで外す
        var totals = new ImportTotals();
        totals.CarryUnfetched(_store.ImportState.Load().UnfetchedItems);
        await _store.ImportState.SaveAsync(new ImportState { Unfetched = totals.Unfetched }, cancellationToken);

        // 走査の控えは、控えたのと別のディスクの上なら使い回さない（点検の16）。今のディスクは周回の頭の見方の写しで見る（周回ごとに読み直す）
        var cacheVolumes = Services.VolumeSnapshot.Empty;
        var scanCache = new ScanCacheIndex(_store.ScanCache.Load(), path => cacheVolumes.SerialAt(path));
        var exclusions = new ExclusionFilter(_store.Excluded.Load());

        // 未確定の一覧は取り込みの最中に人も書く。書くたびに、前に書いた物と今の物を比べて人の変更を残す（UnresolvedMerge）。
        // 外付けを外している取り込み元の下の物は、見られなかっただけなので引き継ぐ
        var unresolvedBase = _store.Unresolved.Load();
        var unseenPlaces = new List<string>();

        // この取り込みで走査した取り込み元。終わりに、この下で無くなったパスを走査の控えから落とす
        var scannedTargets = new List<string>();

        // この取り込みで中身を見たファイルの場所。終わりに、同じ中身の移した元（もう無い場所）を走査の控えから落とす
        var seenPaths = new List<string>();

        // unitypackage の中身を裏で読む（2026-09-13 ユーザ判断）。問い合わせは1本ずつ1.5秒空けるので、その間 CPU とディスクは空いている。
        // 前の取り込みで読み残した物（中断など）も、最初の周回で一緒に拾う
        var unityPending = _unityPackages is null ? null : await _unityPackages.FindPendingAsync(cancellationToken);
        var unityWork = new List<Task>();

        // 周回の外で取る画像の列（④1枚目 ⑤残り ⑥ショップのアイコン）。周回をまたいで持ち越す
        var images = new ImageQueue();
        var measuredFolders = false;
        var sweptFiles = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (work.TakePending() is not { Count: > 0 } folders)
            {
                // ①②で打ち切ったなら、前の周回で積んだ画像も取りに行かない（FetchAsync の画像の見込みと同じ理由）。
                // 画像の段そのものが打ち切った後も、この回の残りは取りに行かない（DrainImagesAsync）
                if (images.IsEmpty || totals.Outage.IsStopped || images.Outage.IsStopped)
                {
                    images.Outage.LogIfStopped("取り込みの画像の取得");
                    break;
                }

                // 画像は1件ずつ取り、そのたびに積まれたフォルダが無いかを見る。
                // 積まれていれば、その場で次の周回（走査〜①②③）へ移る（U5）
                totals.AddImages(await DrainImagesAsync(images, work, progress, cancellationToken));
                continue;
            }

            // 既に商品へ紐付けたフォルダの中は見に行かない。
            // 「管理済み」なので未確定へ流す必要が無く、容量も別途数えている。
            // 周回ごとに読み直すのは、前の周回で増えた商品を次の周回が知っている必要があるため。
            // 外した印も商品のJSONの中にあるので、同じ読み込みから引く
            // 登録したフォルダを測り直すのは取り込み1回につき最初の周回だけ（周回ごとに全部を並べ直していた）
            var (registered, owned, owners, recordedAt, detached, probe) = await LoadOwnedAsync(remeasure: !measuredFolders, cancellationToken);
            measuredFolders = true;
            cacheVolumes = probe.Volumes;

            // この周回で記録するパスは今のドライブ文字で書かれるので、文字と通し番号の組はここで確か（ユーザ判断 2026-09-14）
            await RecordVolumesAsync(folders, cancellationToken);
            unseenPlaces.AddRange(folders.Where(UnresolvedMerge.IsOnMissingVolume));
            scannedTargets.AddRange(folders);

            // **走査に入る前に、対象と「走査の途中」を書く**（2026-09-30・大容量の確かめ #2）。
            // 前は記録を書くのが①で1件取れたときだけだったので、走査・ID の特定の途中（大きなライブラリでは最も長い段）で
            // 閉じるか中止すると、始めに書いた空の記録のまま残り、次の起動で帯も起動時の続きも出なかった。
            // 走査の控えは10秒ごとに書くので、続きから走査し直してもハッシュを取り直すのは閉じる直前の分だけ
            await SaveProgressAsync(totals, work, cancellationToken, scanning: true);

            // **走査は画面のスレッドの外で回す**（ユーザ判断 2026-09-21・C4）。
            // `ScanFolders` には `await` が1つも無いので、押した側のスレッドで
            // 全再帰列挙と展開先の実測が丸ごと走り、その間ずっと画面が固まっていた
            //
            // 走査と ID の特定は1ファイルごとに知らせるので、1秒に10回ほどに間引いて最新だけを渡す（LatestProgress）
            var perFile = new LatestProgress<ImportProgress>(progress, report => report.Phase);
            var scan = await Task.Run(
                () => ScanFolders(folders, exclusions, scanCache, registered, perFile, cancellationToken),
                cancellationToken);
            perFile.Flush();
            var resolution = await ResolveAsync(
                scan.Files, scanCache, exclusions, detached, owned, owners, recordedAt, unresolvedBase, perFile, cancellationToken);
            perFile.Flush();
            await SaveScanCacheAsync(scanCache, cancellationToken);

            // 在るのに今回見ていない場所の未確定は、片付いたと見ない（見つからない・移動の点検 4・2026-10-05）。
            // 前は走査した取り込み元の中というだけで落とし、控えに載っているので監視も拾い直さず、どこにも出なくなっていた
            unseenPlaces.AddRange(scan.Unseen);
            unseenPlaces.AddRange(resolution.Unhashed);
            seenPaths.AddRange(scan.Files.Select(file => file.Path));
            await DropReplacedPathsAsync(resolution.Replaced, cancellationToken);
            await RelinkMovedFilesAsync(resolution.Relinked, probe, cancellationToken);

            // 記録しているファイルの場所を全部見て、見つからなくなった日時を付け外しする（ユーザ判断 2026-10-04）。
            // 取り込み1回につき最初の周回だけ（登録したフォルダの測り直しと同じ）。走査で見つけた移し先を結び直した後に見るのは、
            // 移しただけの物に一度「無い」と書いてすぐ消す書き込みを省くため
            if (!sweptFiles)
            {
                sweptFiles = true;
                await NoteMissingFilesAsync(probe, cancellationToken);
            }

            foreach (var (itemId, hash) in resolution.BrokenOwned)
            {
                totals.NoteBrokenOnItem(itemId, hash);
            }

            // 読むのは item に触らないので、①と同時に進めてよい。①に着くのを遅らせないよう、ここでは待たない
            Task? unityReading = null;
            var unityItemIds = new List<string>();
            if (_unityPackages is { } catalog)
            {
                var unityFiles = resolution.FilesByItemId.Values.SelectMany(list => list).ToList();
                unityItemIds.AddRange(resolution.FilesByItemId.Keys);
                if (unityPending is { } pending)
                {
                    unityFiles.AddRange(pending.Files);
                    unityItemIds.AddRange(pending.ItemIds);
                    unityPending = null;
                }

                unityReading = catalog.ReadAsync(unityFiles, cancellationToken);
            }

            // 未確定は積み上げる。前の周回で残ったものを消してはいけない
            totals.Unresolved.AddRange(resolution.Unresolved);
            unresolvedBase = await SaveUnresolvedAsync(totals.Unresolved, unresolvedBase, scannedTargets, unseenPlaces, cancellationToken);

            var fetchResult = await FetchAsync(resolution.FilesByItemId, work, totals, probe, progress, cancellationToken);

            // 入り先を item に写すのは①の後。item の手元のファイルは①も書き、錠が無いので、重なると片方の書き込みが消える。
            // 読み終わっていなければ、読み終わったところで写す（画像の段は待たせない）
            if (unityReading is not null)
            {
                unityWork.Add(ApplyUnityPackagesAsync(unityReading, unityItemIds, cancellationToken));
            }

            // BOOTHに無かったものも未確定へ。ここで落とすと手元から消える
            if (fetchResult.NotFoundFiles.Count > 0)
            {
                // 開けなかった印は、商品のファイルの記録から引き継いである（ToUnresolved）
                totals.Unresolved.AddRange(fetchResult.NotFoundFiles);
                unresolvedBase = await SaveUnresolvedAsync(totals.Unresolved, unresolvedBase, scannedTargets, unseenPlaces, cancellationToken);
            }

            totals.Add(scan, resolution, fetchResult);

            // この周回の1枚目は、今の列に残っている画像より先に取る。
            // 積んだ物が早く一覧に出ることの方が、前の周回の2枚目より先に要る
            // 見込み（U1）は②が終わった時点で数えてある（FetchAsync の中）。ここでは列に積むだけ
            images.Enqueue(fetchResult.WithImages, fetchResult.ShopIcons);
        }

        // 取り込みが終わったと言うのは、裏で読んでいた unitypackage も書き終えてから
        await Task.WhenAll(unityWork);

        // 走査の控えから、今回の取り込み元の下で無くなったパスを落とす（移した・消したファイルの控えが際限なく残っていた）。
        // つながっていないボリュームの上は落とさない（ScanCacheIndex.RemoveMissingUnder）
        scanCache.RemoveMissingUnder(scannedTargets);

        // 監視の新着だけを取り込んだ回は、移した元の場所が取り込み元の下に無い。同じ中身を今回見た、もう無い場所も落とす。
        // 残すと、元へ戻したときに監視が新着と数えない（見つからない・移動の点検 5・ScanCacheIndex.RemoveMovedAway）
        scanCache.RemoveMovedAway(seenPaths);
        await SaveScanCacheAsync(scanCache, cancellationToken);

        // 最後まで来たので途中の記録は要らない。残すと次の起動で「中断した」と嘘をつく。
        // BOOTH の不調で取れなかった商品だけは残す——消すと、そのファイルは商品にも未確定にも入らず、
        // 走査の控えに載っているので監視も新しいと数えず、どこにも出てこなくなる（#10）
        //
        // 応答の無い失敗が続いて打ち切った回は、対象も残す（ユーザ判断 2026-09-29）。②を打ち切った商品は①が済んでいて
        // 取れなかった商品に載らないので、「続きから進む」で対象を走査し直して②へ戻す
        await _store.ImportState.SaveAsync(
            totals.Outage.IsStopped
                ? new ImportState
                {
                    Unfetched = totals.Unfetched,
                    StoppedAt = DateTimeOffset.Now,
                    Targets = work.Accepted,
                    Stopped = totals.Outage.Stopped,
                }
                : totals.Unfetched.Count == 0
                    ? new ImportState()
                    : new ImportState { Unfetched = totals.Unfetched, StoppedAt = DateTimeOffset.Now },
            cancellationToken);

        // 壊れた zip を持つ商品の名前は、取り込みの終わりの今の値で引く（途中で取った商品の名前はここで揃っている）。
        // 途中で人が消した商品は数えない（結果の文が、もう無い商品を指さないように）
        var brokenItemNames = new List<string>();
        var brokenOnItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemId, hashes) in totals.BrokenOnItems)
        {
            if (await _store.Items.LoadAsync(itemId, cancellationToken) is { } holder)
            {
                brokenItemNames.Add(holder.DisplayName);
                brokenOnItems.UnionWith(hashes);
            }
        }

        return totals.ToSummary(brokenOnItems.Count, brokenItemNames);
    }

    /// <summary>未確定の一覧を、錠の中で人の変更と合わせて書く（技術的負債 1-2・1-3）。書いた物を次の比べる元にする。</summary>
    private Task<List<UnresolvedFile>> SaveUnresolvedAsync(
        IReadOnlyList<UnresolvedFile> found,
        IReadOnlyList<UnresolvedFile> lastWritten,
        IReadOnlyList<string> scannedTargets,
        IReadOnlyList<string> unseenPlaces,
        CancellationToken cancellationToken)
        => _store.Unresolved.UpdateAsync(
            current => UnresolvedMerge.ForImport(
                current, lastWritten, found, new RegisteredFolderSet(scannedTargets), new RegisteredFolderSet(unseenPlaces)),
            cancellationToken);

    /// <summary>控えられなくても取り込みは止めない（次に開いたフォルダビューで控え直す）。</summary>
    private async Task RecordVolumesAsync(IReadOnlyList<string> folders, CancellationToken cancellationToken)
    {
        if (_volumes is null)
        {
            return;
        }

        try
        {
            await _volumes.RecordAsync(folders, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.Text.Json.JsonException)
        {
            Diagnostics.AppLog.Error("取り込みの裏の作業", exception);
        }
    }

    /// <summary>読み終わるのを待って、入り先を item に写す。読めなくても取り込みは止めない（次の取り込みで読み直す）。</summary>
    private async Task ApplyUnityPackagesAsync(Task reading, IReadOnlyList<string> itemIds, CancellationToken cancellationToken)
    {
        try
        {
            await reading;
            await _unityPackages!.ApplyAsync(itemIds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 中断。次の取り込みで読み直すだけ
        }
        catch (Exception exception)
        {
            // **何を投げられてもここで受ける。**この作業を待つのは最後の1行だけなので、
            // 中断でそこへ到達しないと「誰にも観測されない Task」になり、
            // 落ちたことが後から `UnobservedTaskException` として遅れて出ていた
            Diagnostics.AppLog.Error("取り込みの裏の作業", exception);
        }
    }

    /// <summary>
    /// 検出の進み具合を、取り込みの進み具合として流し直す。
    ///
    /// 検出は取り込みの1段なので、画面には1本の進捗として見えていてほしい。
    /// 画面が2種類の進捗を混ぜて出すより、ここで形を揃える方が食い違いようがない。
    /// </summary>
    private sealed class DetectProgressAdapter(IProgress<ImportProgress>? inner)
        : IProgress<Services.AvatarDetectProgress>
    {
        public void Report(Services.AvatarDetectProgress value)
            => inner?.Report(new ImportProgress
            {
                Phase = ImportPhase.Detecting,
                Current = value.Done,
                Total = value.Total,
                // 小さな段と、今見ている商品の名前を分けて渡す。名前が無いときに段の名前を
                // 代わりに出すと、件数の前の欄と同じ文が2回並ぶ
                Step = value.Phase,
                Detail = value.Current,
            });
    }

    private static void Report(
        IProgress<ImportProgress>? progress,
        ImportPhase phase,
        int current,
        int total,
        string? detail)
        => progress?.Report(new ImportProgress
        {
            Phase = phase,
            Current = current,
            Total = total,
            Detail = detail,
        });

    /// <summary>周回をまたいだ合計。1回の取り込みとして1つのまとめに畳む。</summary>
    private sealed class ImportTotals
    {
        public List<UnresolvedFile> Unresolved { get; } = [];

        /// <summary>
        /// 中断の記録に出す件数（ユーザ判断 2026-09-21・C12）。**周回をまたいで足し合わせる。**
        ///
        /// 周回ごとの数を書いていたので、走らせている最中にフォルダを積むと
        /// 次の周回が前の周回の数字を上書きし、「30件中3件」で止めたのに「2 / 2」になっていた。
        /// 人から見れば1回の取り込みなので、まとめの件数と同じ数え方にする。
        /// </summary>
        public int PendingTotal { get; private set; }

        public int FetchedTotal { get; private set; }

        public void PlanFetch(int count) => PendingTotal += count;

        public void NoteFetched() => FetchedTotal++;

        /// <summary>BOOTH の不調で①が取れなかった商品（前の回から引き継いだ分を含む・#10）。</summary>
        private readonly Dictionary<string, UnfetchedItem> _unfetched = new(StringComparer.Ordinal);

        public IReadOnlyList<UnfetchedItem> Unfetched
            => _unfetched.Values.OrderBy(item => item.ItemId, StringComparer.Ordinal).ToList();

        /// <summary>
        /// 前の回の分を引き継ぐ。ファイルが消えた物は落とす（取り直しても足す物が無い）。
        /// つながっていないボリュームの上の物は、見えないだけなので残す
        /// </summary>
        public void CarryUnfetched(IEnumerable<UnfetchedItem> previous)
        {
            foreach (var item in previous)
            {
                var paths = item.PathList
                    .Where(path => File.Exists(path) || UnresolvedMerge.IsOnMissingVolume(path))
                    .ToList();
                if (paths.Count > 0 && !string.IsNullOrWhiteSpace(item.ItemId))
                {
                    _unfetched[item.ItemId] = item with { Paths = paths };
                }
            }
        }

        public void NoteUnfetched(string itemId, IEnumerable<LocalFileRecord> files)
        {
            var paths = files.SelectMany(file => file.Paths);
            if (_unfetched.TryGetValue(itemId, out var known))
            {
                paths = known.PathList.Concat(paths);
            }

            _unfetched[itemId] = new UnfetchedItem
            {
                ItemId = itemId,
                Paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }

        /// <summary>取れた・既に商品がある・BOOTH に無かった（未確定へ回した）。どれも取り直す物ではない。</summary>
        public void NoteSettled(string itemId) => _unfetched.Remove(itemId);

        private readonly List<(string ItemId, HashSet<string> Hashes)> _brokenOnItems = [];

        /// <summary>
        /// 壊れた zip を持つ商品と、その zip のハッシュ（見つけた順）。周回をまたいで足し合わせる。
        /// 同じ zip を周回ごと・場所ごとに数え直さないよう、ハッシュで持つ
        /// </summary>
        public IReadOnlyList<(string ItemId, HashSet<string> Hashes)> BrokenOnItems => _brokenOnItems;

        public void NoteBrokenOnItem(string itemId, string hash)
        {
            var index = _brokenOnItems.FindIndex(entry => string.Equals(entry.ItemId, itemId, StringComparison.Ordinal));
            if (index < 0)
            {
                _brokenOnItems.Add((itemId, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hash }));
            }
            else
            {
                _brokenOnItems[index].Hashes.Add(hash);
            }
        }

        /// <summary>
        /// ①②の1件ごとの結果（再試行の後）を続けて数える。応答の無い失敗か 5xx が3件続いたら、この回の問い合わせを打ち切る
        /// （ユーザ判断 2026-09-29）。ネットにつながっていないと1件ごとに再試行で長く待ち
        /// （つながらないとき約13秒・応答が無いとき最長約100秒）、全件を回るので止まって見えた。
        /// BOOTH が落ちている間に全件を回すと、復旧に時間の掛かる相手へ問い合わせを重ね続ける。
        /// 数え方は画像の段と同じ部品に揃える（どこで止まっても同じ決まりで止まる）
        /// </summary>
        public BoothOutageWatch Outage { get; } = new();

        private readonly List<UnpackedFolder> _unpacked = [];
        private int _scanned;
        private int _skippedUnpacked;
        private int _unreadable;
        private int _unreadableFolders;
        private int _onlineOnly;
    private int _links;
        private int _hashed;
        private int _reused;
        private int _excluded;
        private int _alreadyOwned;
        private int _added;
        private int _alreadyKnown;
        private int _notFound;
        private int _temporaryFailures;
        private int _imagesDownloaded;
        private int _avatarItemsUpdated;
        private int _avatarsFound;
        private string? _avatarDetectError;
        private bool _avatarDetectRan;

        public void Add(ScanOutcome scan, ResolutionResult resolution, FetchResult fetch)
        {
            _scanned += scan.Files.Count;
            _unpacked.AddRange(scan.UnpackedFolders);
            _skippedUnpacked += scan.SkippedInsideUnpackedFolders;
            _unreadable += scan.Unreadable;
            _unreadableFolders += scan.UnreadableFolders;
            _onlineOnly += scan.OnlineOnly;
        _links += scan.Links;

            _hashed += resolution.Hashed;
            _reused += resolution.ReusedFromCache;
            _excluded += resolution.Excluded;
            _alreadyOwned += resolution.AlreadyOwned;
            _unreadable += resolution.Unreadable;

            _added += fetch.Added;
            _alreadyKnown += fetch.AlreadyKnown;
            _notFound += fetch.NotFound;
            _temporaryFailures += fetch.TemporaryFailures;

            // 検出は周回ごとに走るので足し合わせる。理由は最後のものを残す
            _avatarItemsUpdated += fetch.AvatarItemsUpdated;
            _avatarsFound += fetch.AvatarsFound;
            _avatarDetectError = fetch.AvatarDetectError ?? _avatarDetectError;
            _avatarDetectRan |= fetch.AvatarDetectRan;
        }

        /// <summary>周回の外で取った画像の数（U5）。</summary>
        public void AddImages(int downloaded) => _imagesDownloaded += downloaded;

        public ImportSummary ToSummary(int brokenOnItems, IReadOnlyList<string> brokenItemNames) => new()
        {
            FilesBrokenArchiveOnItems = brokenOnItems,
            BrokenArchiveItemNames = brokenItemNames,
            FilesScanned = _scanned,
            UnpackedFolders = _unpacked,
            FilesSkippedAsUnpacked = _skippedUnpacked,
            FilesUnreadable = _unreadable,
            FoldersUnreadable = _unreadableFolders,
            FilesOnlineOnly = _onlineOnly,
            LinksSkipped = _links,
            FilesHashed = _hashed,
            FilesReusedFromCache = _reused,
            FilesExcluded = _excluded,
            FilesAlreadyOwned = _alreadyOwned,
            UnresolvedFiles = Unresolved.Count,
            FilesBrokenArchive = Unresolved.Where(file => file.ArchiveBroken)
                .Select(file => file.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            ItemsAdded = _added,
            ItemsAlreadyKnown = _alreadyKnown,
            NotFound = _notFound,
            TemporaryFailures = _temporaryFailures,
            Stopped = Outage.Stopped,
            ImagesDownloaded = _imagesDownloaded,
            AvatarItemsUpdated = _avatarItemsUpdated,
            AvatarsFound = _avatarsFound,
            AvatarDetectError = _avatarDetectError,
            AvatarDetectRan = _avatarDetectRan,
        };
    }

    /// <summary>
    /// 既に管理下にあるものを集める。登録済みフォルダと、itemが持っているファイルのハッシュ。
    /// ついでに登録済みフォルダの中身を数え直して保存する
    /// （数えるのは列挙だけでハッシュは計算しないので速い）。
    /// </summary>
    /// <param name="remeasure">
    /// 登録したフォルダを測り直し、zip が手に入っていないかを見るか。**取り込み1回につき最初の周回だけ**（2026-09-24）。
    /// 周回は積むたびに増え、そのたびに登録したフォルダの中を全部並べ直していた。測った値は容量の表示に使うだけで、
    /// 同じ取り込みの中で何度測っても変わらない。
    /// </param>
    private async Task<(RegisteredFolderSet Registered, IReadOnlyDictionary<string, IReadOnlyList<string>> Owned, IReadOnlyDictionary<string, List<FileOwner>> Owners, IReadOnlyDictionary<string, List<RecordedFile>> RecordedAt, DetachedIndex Detached, FilePresenceProbe Probe)> LoadOwnedAsync(
        bool remeasure,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var paths = new List<string>();

        // zipが手に入っていたフォルダ。知らせは測り終えてから、錠の中で今の一覧に足す
        var archivesFound = new List<(ItemRecord Item, string FolderPath, string ArchivePath)>();

        // 持っているハッシュと、その中身の一覧（持っている zip を開かずに済ませるため。ResolveAsync）
        var owned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        // 持っているハッシュと、それを持つ商品・記録している場所（移したファイルを結び直すため。ResolveAsync）。
        // 外した印の行は入れない——外した商品へ戻すと、人が外した判断を取り込みが覆す
        var owners = new Dictionary<string, List<FileOwner>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded.Items)
        {
            foreach (var file in item.Local.OwnedFiles)
            {
                if (!owned.TryGetValue(file.Hash, out var known) || (known.Count == 0 && file.Contents.Count > 0))
                {
                    owned[file.Hash] = file.Contents;
                }

                if (!owners.TryGetValue(file.Hash, out var list))
                {
                    list = [];
                    owners[file.Hash] = list;
                }

                list.Add(new FileOwner(item.Id, file.Paths, file.ArchiveBroken));
            }
        }

        // 起動時の見回り（MissingMarksSweep）と番を合わせる。同じフォルダを同時に見て、同じ商品を二度書かないように（ユーザ判断 2026-10-05）
        using var sweeping = await _missingMarks.EnterAsync(cancellationToken);

        // 在るかは見回りと同じ部品で見る（ドライブごとに根を1回・3秒で打ち切る。spec background-and-network.md）。
        // 前は Directory.Exists と IsOnMissingVolume を打ち切り無しで呼んでいて、落ちた共有の上の登録フォルダで周回の頭が
        // 1つにつき数十秒止まり得た（根の確かめが1回21秒かかったことがある）。同じ周回のファイルの見回りにも渡し、根を二度待たない
        var probe = _missingMarks.NewProbe();

        foreach (var item in loaded.Items.Where(item => item.Local.LocalFolders.Count > 0))
        {
            var measured = new Dictionary<string, FolderSurvey>(StringComparer.OrdinalIgnoreCase);

            // 「無い」と見たフォルダと、また見つかったフォルダ（LocalFolderRecord.MissingSince・ユーザ判断 2026-10-04）
            var missingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAgain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 在ると見た、ディスクの通し番号がまだ無い登録 → 今そこに来ているディスク（点検の3。在ると見たときに書き足す）
            var volumes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;

            foreach (var folder in item.Local.LocalFolders)
            {
                var presence = probe.OfFolder(folder.Path, folder.Volume);
                if (presence == FilePresence.Present && folder.Volume is null && probe.Volumes.SerialAt(folder.Path) is { } serial)
                {
                    volumes[folder.Path] = serial;
                    changed = true;
                }

                if (presence != FilePresence.Present)
                {
                    // 見つからないものは登録として残すが、スキャンの除外には使わない。
                    // 無いことは記録に残す。ただしドライブごと見えない（外付けを外している・根が答えない）ときは「無い」と書かない
                    // （ファイルの取り込みが外付けの上の場所を残すのと同じ。LocalFileMerger）
                    if (folder.MissingSince is null && presence == FilePresence.Missing)
                    {
                        missingNow.Add(folder.Path);
                        changed = true;
                    }

                    continue;
                }

                paths.Add(folder.Path);
                if (folder.MissingSince is not null)
                {
                    seenAgain.Add(folder.Path);
                    changed = true;
                }

                if (!remeasure)
                {
                    continue;
                }

                // zipが手に入っていれば、フォルダ登録は役目を終えている。
                // 黙っていると容量が二重に乗ったままなので知らせる。
                if (RegisteredFolderSet.FindArchiveFor(folder.Path) is { } archive)
                {
                    archivesFound.Add((item, folder.Path, archive));
                }

                // 中の unitypackage も同じ1回の列挙で拾う（右クリックの「Unityへ送る」を、開くたびにフォルダを並べずに決めるため。メモ65-③）
                var survey = RegisteredFolderSet.Survey(folder.Path);
                measured[folder.Path] = survey;
                if (survey.FileCount != folder.FileCount || survey.TotalBytes != folder.TotalBytes
                    || !survey.SamePackages(folder.UnityPackages) || folder.LastSeenAt is null)
                {
                    changed = true;
                }
            }

            if (changed)
            {
                // 全件を先に読んでから、フォルダを1つずつ測って回る。測るのに時間がかかるので、
                // 書く頃には写しが古い。**測った値を今の一覧に当てる**（古い写しで丸ごと書き戻すと、
                // 測っている間に人がフォルダを外した・ファイルに種類を付けた操作が消える）。
                // 見つからなくなった日時も同じく今の値に当てる（無い間は最初に見た日時を残す。決まりは起動時の見回りと同じ FileMissingMarks.MarkedFolder）。
                // 今の値で変わる物が無ければ書かない（起動時の見回りが先に同じ答えを書いていれば、二度書かない）
                var now = DateTimeOffset.Now;
                LocalFolderRecord WithVolume(LocalFolderRecord folder)
                    => folder.Volume is null && volumes.TryGetValue(folder.Path, out var serial) ? folder with { Volume = serial } : folder;

                await _store.Items.ChangeLocalAsync(
                    item.Id,
                    current =>
                    {
                        var folders = current.LocalFolders.Select(folder => WithVolume(
                            measured.TryGetValue(folder.Path, out var size)
                                ? folder with
                                {
                                    FileCount = size.FileCount,
                                    TotalBytes = size.TotalBytes,
                                    UnityPackages = size.UnityPackages,
                                    LastSeenAt = now,
                                    MissingSince = null,
                                }
                                : seenAgain.Contains(folder.Path)
                                    ? FileMissingMarks.MarkedFolder(folder, FilePresence.Present, now)
                                    : missingNow.Contains(folder.Path)
                                        ? FileMissingMarks.MarkedFolder(folder, FilePresence.Missing, now)
                                        : folder)).ToList();

                        return folders.Where((folder, index) => !ReferenceEquals(folder, current.LocalFolders[index])).Any()
                            ? current with { LocalFolders = folders }
                            : null;
                    },
                    LocalOwners.Import,
                    cancellationToken);
            }
        }

        if (archivesFound.Count > 0)
        {
            await _store.Notifications.TryUpdateAsync(
                notifications =>
                {
                    var before = notifications.Count;
                    foreach (var (item, folderPath, archivePath) in archivesFound)
                    {
                        NoteArchiveFound(notifications, item, folderPath, archivePath);
                    }

                    return notifications.Count == before ? null : notifications;
                },
                cancellationToken);
        }

        // 商品の記録が指している場所（場所 → どの商品の、どの中身か）。走査でそこに別の中身が見つかったら、上書きされた物（ResolveAsync）。
        // 外した印の行も入れる（場所を外すのは同じ。候補にはしない）
        var recordedAt = new Dictionary<string, List<RecordedFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in loaded.Items)
        {
            foreach (var file in item.Local.LocalFiles)
            {
                foreach (var path in file.Paths)
                {
                    // 記録が別のディスクの上の場所なら、今その場所に在る物は上書きではない（2台の外付けが同じ文字を使う。点検の3）。
                    // 入れると、Bの上の同じ名前の別の中身を見て、Aの上の記録から場所を外していた
                    if (PlaceVolumes.Of(file, path) is { } recorded
                        && probe.Volumes.SerialAt(path) is { } now
                        && !string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!recordedAt.TryGetValue(path, out var list))
                    {
                        list = [];
                        recordedAt[path] = list;
                    }

                    list.Add(new RecordedFile(item.Id, file.Hash, !file.Detached));
                }
            }
        }

        return (new RegisteredFolderSet(paths), owned, owners, recordedAt, DetachedIndex.From(loaded.Items), probe);
    }

    /// <summary>
    /// 記録しているファイルの場所を全部見て、見つからなくなった日時（<see cref="LocalFileRecord.MissingSince"/>）を付け外しする
    /// （ユーザ判断 2026-10-04。フォルダの <see cref="LocalFolderRecord.MissingSince"/> と同じく取り込みのたびに見る）。
    /// </summary>
    /// <remarks>
    /// 前は取り込みがその商品のファイルを扱ったときにしか「無い」が記録に残らず、手で zip を消してもカードの印・検索の条件・統計に出なかった。
    /// **場所は外さない**（覚えている場所を黙って消さない。外すのは今までどおり、その商品を扱った取り込み＝<see cref="LocalFileMerger"/> だけ）。
    /// 在るかはドライブごとにまとめて見る（<see cref="FilePresenceProbe"/>。つながっていないドライブの上は見に行かず、書かない）。
    /// 外したファイルも見る（記録は事実なので。印と条件は外したファイルを数えない）。
    /// 書くのは商品ごとの錠の中で今の値に当て、見ている間に場所が変わったファイルには当てない（<see cref="FileMissingMarks.Apply"/>）。
    /// 見回りそのものは起動時の見回りと同じ <see cref="MissingMarksSweep"/>（1本ずつ回るので、起動時の見回りと重ならない）。
    /// </remarks>
    private Task NoteMissingFilesAsync(FilePresenceProbe probe, CancellationToken cancellationToken)
        => _missingMarks.NoteFilesAsync(probe, cancellationToken);

    /// <summary>そのハッシュを持つ商品と、その商品が記録している場所・開けなかった印。</summary>
    private sealed record FileOwner(string ItemId, IReadOnlyList<string> Paths, bool ArchiveBroken);

    /// <summary>商品の記録が指している場所1つ分：どの商品の、どの中身の記録か。外した印の行は <paramref name="Owned"/> が false。</summary>
    private sealed record RecordedFile(string ItemId, string Hash, bool Owned);

    /// <summary>
    /// 記録が指す場所に、別の中身が来ていたら、その場所を記録から外す（ユーザ判断 2026-09-30「3A」）。
    ///
    /// 外さないと、古い中身の記録が「その場所に在る」ままになる（<see cref="LocalFileMerger"/> は場所に何かが在るかしか見ない）。
    /// 更新版を同じ名前で上書きすると商品ページに同じ名前の行が2つ並び、壊れた zip は落とし直しても「壊れたzip」が消えなかった。
    /// 未確定は取り込みのたびに一覧を作り直すので、同じことは起きない。
    ///
    /// **場所が1つも残らなくなった記録は残し、古い版の印（<see cref="LocalFileRecord.Replaced"/>）を付ける**（商品ページで「古い版」の行になる。
    /// 「見つかりません」には数えない・2026-10-05・点検の8）。種類の結び付きや、手で結んだ事実を失わないため。
    /// **壊れた zip の記録だけは記録ごと落とす**——同じ場所に落とし直したのだから、壊れた方はもうどこにも無く、残しても取り戻す物が無い。
    /// 外した印の行からも場所は外す（そこに在るのは別の中身で、「この商品に戻す」相手ではない）。
    ///
    /// 錠の中で今の値に当てる。壊れているかも今の値で見る（読んでからここまでの間に印が下りていれば、記録は残す）。
    /// </summary>
    private async Task DropReplacedPathsAsync(
        IReadOnlyList<(string ItemId, string Hash, string Path)> replaced,
        CancellationToken cancellationToken)
    {
        foreach (var group in replaced.GroupBy(entry => entry.ItemId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.Items.ChangeLocalAsync(
                group.Key,
                current =>
                {
                    var changed = false;
                    var files = new List<LocalFileRecord>();
                    foreach (var file in current.LocalFiles)
                    {
                        var gone = group.Where(entry => string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                            .Select(entry => entry.Path)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var paths = file.Paths.Where(path => !gone.Contains(path)).ToList();
                        if (paths.Count == file.Paths.Count)
                        {
                            files.Add(file);
                            continue;
                        }

                        changed = true;
                        if (paths.Count > 0)
                        {
                            files.Add(file with { Paths = paths });
                        }
                        else if (!file.ArchiveBroken)
                        {
                            // 場所が残らなかった記録は「古い版」と印を付ける（2026-10-05・点検の8）。印が無いと「見つかりません」と
                            // 数えられ続け、探しても見つからない。どこで置き換わったかは行の名前に使う（場所はもう空なので）
                            files.Add(file with
                            {
                                Paths = paths,
                                Replaced = new ReplacedVersion(file.Paths.First(gone.Contains), DateTimeOffset.Now),
                                MissingSince = null,
                            });
                        }
                    }

                    return changed ? current with { LocalFiles = files } : null;
                },
                LocalOwners.Import,
                cancellationToken);
        }
    }

    /// <summary>
    /// 同じ中身を商品が持っているファイルの場所を、その商品に足す（大容量の確かめ A・2026-09-30）。
    /// 対象は、手掛かりから決まらないファイルの持ち主全部と、手掛かりで別の商品に決まったファイルのほかの持ち主。
    /// 実在しなくなった場所は <see cref="LocalFileMerger"/> が落とすので、移した物は新しい場所に置き換わる。
    ///
    /// 錠の中で今の値に当て、読んでからここまでの間に人がこの商品から外した（印を付けた）なら足さない。
    /// 足すと <see cref="LocalFileMerger"/> が印を下ろしてしまい、外した判断を取り込みが覆す。
    /// </summary>
    /// <param name="probe">周回の頭で作った見方（無い場所を外すかを、見回りと同じ部品で決める。<see cref="LocalFileMerger.Merge(IReadOnlyList{LocalFileRecord}, IEnumerable{LocalFileRecord}, FilePresenceProbe)"/>）。</param>
    private async Task RelinkMovedFilesAsync(
        IReadOnlyDictionary<string, List<LocalFileRecord>> relinked,
        FilePresenceProbe probe,
        CancellationToken cancellationToken)
    {
        // 根の覚えは結び直しの間だけ（走査とハッシュの間に外付けを外していたら、周回の頭の「つながっている」は古い）
        var presence = probe.Renewed();
        foreach (var (itemId, discovered) in relinked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.Items.ChangeLocalAsync(
                itemId,
                current =>
                {
                    var stillOwned = current.OwnedFiles.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var files = discovered.Where(file => stillOwned.Contains(file.Hash)).ToList();
                    return files.Count == 0
                        ? null
                        : current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, presence) };
                },
                LocalOwners.Import,
                cancellationToken);
        }
    }

    /// <summary>
    /// 見つけた物のうち、錠の中の今の値でこの商品から外してあるハッシュを除く。
    ///
    /// 行き先は読んだ時点の外した印で決めるが、書くのは後。その間に人が「この商品から外す」を押すと、
    /// 外した行に見つけた物を重ねることになり、<see cref="LocalFileMerger.Merge"/> の「両方が外していた時だけ残す」で
    /// 印が下りて、人の判断を取り込みが覆していた（2026-10-05・見つからない・移動の点検 1。<see cref="RelinkMovedFilesAsync"/> と同じ考え）。
    /// 外した物の場所は「この商品から外す」が未確定へ移している。
    /// </summary>
    private static List<LocalFileRecord> NotDetachedIn(LocalBlock current, IEnumerable<LocalFileRecord> discovered)
    {
        var detached = current.LocalFiles
            .Where(file => file.Detached)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return discovered.Where(file => !detached.Contains(file.Hash)).ToList();
    }

    /// <summary>
    /// 「登録したフォルダのzipが手に入った」を要確認へ書く。
    /// 同じフォルダで何度も出さないよう、未読の同種があれば足さない。
    /// </summary>
    private static void NoteArchiveFound(
        List<NotificationRecord> notifications,
        ItemRecord item,
        string folderPath,
        string archivePath)
    {
        var id = $"archive-found:{folderPath}";
        if (notifications.Any(entry => entry.Id == id && !entry.IsRead))
        {
            return;
        }

        notifications.Add(new NotificationRecord
        {
            Id = id,
            Kind = NotificationKind.ArchiveFoundForFolder,
            ItemId = item.Id,
            Title = item.DisplayName,
            Detail = $"展開先「{Path.GetFileName(folderPath)}」と {Path.GetFileName(archivePath)}",
            CreatedAt = DateTimeOffset.Now,
            IsStrong = true,
        });
    }

    private ScanOutcome ScanFolders(
        IReadOnlyList<string> folders,
        ExclusionFilter exclusions,
        ScanCacheIndex scanCache,
        RegisteredFolderSet registered,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scanned = new List<ScannedFile>();
        var unpacked = new List<UnpackedFolder>();
        var skippedUnpacked = 0;
        var unreadable = 0;
        var unreadableFolders = 0;
        var onlineOnly = 0;
        var unseen = new List<string>();
        var links = 0;

        foreach (var folder in folders)
        {
            var result = _scanner.Scan(folder, cancellationToken);
            unseen.AddRange(result.UnreadableFolders);
            unseen.AddRange(result.NotRead);

            // たどらなかったリンクは数を結果に出し、どれかはログに残す（見つからない・移動の点検 15）。
            // リンクの先を勝手にたどらないのは、ループと二重読みを避けるため。入れたい人はリンク先を取り込み元に足す
            links += result.Links.Count;
            foreach (var link in result.Links)
            {
                Diagnostics.AppLog.Warn("取り込みの走査", $"{link}：リンクなので中を読みませんでした。リンク先を取り込み元に足すと取り込めます");
            }
            unpacked.AddRange(result.UnpackedFolders);
            skippedUnpacked += result.SkippedInsideUnpackedFolders;
            unreadable += result.Unreadable;

            // 中を並べられなかったフォルダは数を結果に出し、どれかはログに残す（ハッシュを取れなかったファイルと同じ・大容量の確かめ #5）
            unreadableFolders += result.UnreadableFolders.Count;
            foreach (var denied in result.UnreadableFolders)
            {
                Diagnostics.AppLog.Warn("取り込みの走査", $"{denied}：フォルダの中を読めませんでした");
            }

            // 中身が手元に無いクラウドのファイルは、読むとダウンロードが始まるので飛ばした。
            // 数は結果に出す（ユーザ判断 2026-09-23）。結果は全体の数だけなので、どのフォルダで何件かはログに残す
            onlineOnly += result.OnlineOnly;
            if (result.OnlineOnly > 0)
            {
                Diagnostics.AppLog.Warn(
                    "取り込みの走査",
                    $"{folder}：中身が手元に無いクラウドのファイル {result.OnlineOnly} 件は読みませんでした（開くとダウンロードが始まるため）");
            }

            foreach (var file in result.Files)
            {
                // 外したパスで、控えから中身も同じと分かる物はここで弾く。ハッシュ計算にすら進ませない。
                // 同じパスでも中身が変わっていれば（落とし直した更新版）ここを通し、解決でハッシュを取って決める
                if (exclusions.IsExcludedWithoutHashing(file, scanCache))
                {
                    continue;
                }

                // 既に商品へ紐付けたフォルダの中身も同じく飛ばす
                if (registered.Contains(file.Path))
                {
                    continue;
                }

                scanned.Add(file);
                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.Scanning,
                    Current = scanned.Count,
                    Detail = file.Path,
                });
            }
        }

        return new ScanOutcome
        {
            Files = scanned,
            UnpackedFolders = unpacked,
            SkippedInsideUnpackedFolders = skippedUnpacked,
            Unreadable = unreadable,
            UnreadableFolders = unreadableFolders,
            OnlineOnly = onlineOnly,
            Unseen = unseen,
            Links = links,
        };
    }

    private sealed class ScanOutcome
    {
        public required List<ScannedFile> Files { get; init; }

        public required List<UnpackedFolder> UnpackedFolders { get; init; }

        public int SkippedInsideUnpackedFolders { get; init; }

        public int Unreadable { get; init; }

        public int UnreadableFolders { get; init; }

        public int OnlineOnly { get; init; }

        /// <summary>在るのに今回見ていない場所（読めなかったフォルダ・オンラインのみ・読めなかった1ファイル）。</summary>
        public required List<string> Unseen { get; init; }

        /// <summary>たどらなかったジャンクション・シンボリックリンクの数。</summary>
        public int Links { get; init; }
    }

    private async Task<ResolutionResult> ResolveAsync(
        List<ScannedFile> scanned,
        ScanCacheIndex scanCache,
        ExclusionFilter exclusions,
        DetachedIndex detached,
        IReadOnlyDictionary<string, IReadOnlyList<string>> owned,
        IReadOnlyDictionary<string, List<FileOwner>> owners,
        IReadOnlyDictionary<string, List<RecordedFile>> recordedAt,
        IReadOnlyList<UnresolvedFile> previousUnresolved,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var filesByItemId = new Dictionary<string, List<LocalFileRecord>>(StringComparer.Ordinal);
        var relinked = new Dictionary<string, List<LocalFileRecord>>(StringComparer.Ordinal);
        var unresolved = new List<UnresolvedFile>();
        var hashed = 0;
        var reused = 0;
        var excluded = 0;
        var alreadyOwned = 0;
        var unreadable = 0;
        var unhashed = new List<string>();
        var brokenArchives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brokenOwned = new List<(string ItemId, string Hash)>();
        var replaced = new List<(string ItemId, string Hash, string Path)>();

        // 前に書いた未確定の「同じ場所にあった商品」（中身のハッシュ → 商品ID）。一覧は取り込みのたびに作り直すので、
        // 引き継がないと次の取り込みで消える（古い記録はもうその場所を指していないので、見つけ直せない）
        var carriedSamePath = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in previousUnresolved.Where(previous => previous.SamePathItemIds.Count > 0))
        {
            carriedSamePath.TryAdd(previous.Hash, previous.SamePathItemIds);
        }

        var processed = 0;
        var lastCacheSave = Environment.TickCount64;

        foreach (var file in scanned)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.Resolving,
                Current = ++processed,
                Total = scanned.Count,
                Detail = Path.GetFileName(file.Path),
            });

            string hash;
            if (scanCache.TryGetHash(file.Path, file.SizeBytes, file.ModifiedAtUtc, out var cachedHash))
            {
                hash = cachedHash;
                reused++;
            }
            else
            {
                try
                {
                    hash = await FileHasher.ComputeSha256Async(file.Path, cancellationToken);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 書き込み中のダウンロードや、ほかのアプリが開いているファイルがここに来る。
                    // 黙って飛ばしていたので、取り込んだつもりの物が入っていないことに気付けなかった。
                    // 走査で読めなかった物（E4）と同じ数に入れて結果に出し、どれかはログに残す
                    Diagnostics.AppLog.Warn("取り込みでファイルを読む", $"{file.Path}：{exception.Message}");
                    unreadable++;
                    unhashed.Add(file.Path);
                    continue;
                }

                scanCache.Set(file.Path, file.SizeBytes, file.ModifiedAtUtc, hash);
                hashed++;

                // 途中でも控えを書く（閉じても計算した分は残す）。**間隔は時間で決める**：本数で決めていた（50本ごと）ので、
                // 小さいファイルが続くと1秒に何度も控え全体（1万件なら約2MB）を書き直していた
                if (Environment.TickCount64 - lastCacheSave >= ScanCacheSaveIntervalMs)
                {
                    await SaveScanCacheAsync(scanCache, cancellationToken);
                    lastCacheSave = Environment.TickCount64;
                }
            }

            // 記録の場所に、別の中身が在る（同じ名前で上書きした・壊れた zip を落とし直した）。古い中身の記録からこの場所を外す
            // （書くのは DropReplacedPathsAsync）。管理から外した中身が来ていても、古い方がそこに無いことは同じ。
            // **ここまで来た物だけを判じる**：ハッシュを取れた（か控えから分かった）場所だけ。走査していない場所・
            // 読めなかったファイル・つながっていないドライブの上の場所は、中身が変わったとは言えないので触らない
            IReadOnlyList<string> formerHolders = [];
            if (recordedAt.TryGetValue(file.Path, out var recordedHere))
            {
                var stale = recordedHere
                    .Where(record => !string.Equals(record.Hash, hash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                replaced.AddRange(stale.Select(record => (record.ItemId, record.Hash, file.Path)));

                // 外した印の行の商品は候補にしない（前の中身を「この商品のものではない」と人が外している）
                formerHolders = [.. stale.Where(record => record.Owned)
                    .Select(record => record.ItemId)
                    .Distinct(StringComparer.Ordinal)];
            }

            // 移動・改名された除外ファイルと、外したパスで控えと合わなかった物は、ハッシュで最終判定する。
            // 外したパスでも中身が違えばここを抜け、新しい物として取り込まれる
            if (exclusions.IsExcludedByHash(hash))
            {
                excluded++;
                continue;
            }

            // **持っている zip は開かない**（2026-09-24）。取り込み直すたびに持っているファイル全部の zip を開き直していた。
            // 中身の一覧は商品が持ち、ID の手掛かりは走査の控えがハッシュと一緒に持つ（どちらも中身だけで決まる）。
            // 商品の一覧が空の物（手で付けた等）は、前と同じく開いて一覧を取る
            IReadOnlyList<BoothClue> clues;
            IReadOnlyList<string> contents;
            var broken = false;
            if (file.IsArchive
                && owned.TryGetValue(hash, out var knownContents) && knownContents.Count > 0
                && scanCache.TryGetClueItemIds(file, hash, out var knownClues))
            {
                clues = [.. knownClues.Select(ClueOf)];
                contents = knownContents;
            }
            else
            {
                // zip の中身読みは同期なので、画面のスレッドへ戻ってから走っていた（C19）。
                // ハッシュ計算だけが本当に非同期で、その直後にここで引っかかる
                var inspected = await Task.Run(() => InspectFile(file), cancellationToken);
                clues = inspected.Clues;
                contents = inspected.Contents;
                broken = inspected.Broken;
                if (broken && brokenArchives.Add(hash))
                {
                    // 前は握りつぶしていて、途中で切れたダウンロードが普通の未確定と見分けられなかった（大容量の確かめ 問題4）。
                    // 印は未確定の行にも商品のファイルの行にも出るが、結果の文が名前を言うのは商品1つだけなので、どれかはここにも残す
                    Diagnostics.AppLog.Warn("取り込みで zip を開く", $"{file.Path}：zip として開けなかった（壊れているか、zip ではない）");
                }

                // 前から商品が持っている壊れた zip（手で結んだ物・前の取り込みで結び付いた物）。結果の数に入れる
                if (broken && owners.TryGetValue(hash, out var brokenHolders))
                {
                    brokenOwned.AddRange(brokenHolders.Select(holder => (holder.ItemId, hash)));
                }

                if (file.IsArchive && inspected.Read)
                {
                    // 開けなかった zip（ほかのアプリが開いている等）は控えない。次の取り込みでまた開く
                    scanCache.SetClueItemIds(file, hash, ClueItemIdsOf(clues));
                }
            }

            var zone = ZoneIdentifierReader.Read(file.Path);

            // このファイルの場所を、記録にまだ持っていない持ち主へ足す予定に積む（書くのは RelinkMovedFilesAsync）。
            // **開けなかった印が今の答えと違う持ち主にも積む**（ユーザ判断 2026-09-30）：手で結んだ壊れた zip は場所が変わらないので、
            // 積まないと印を書く機会が無い。逆（印があるのに今回は開けた）は、開けて中身の一覧が取れたときだけ——
            // ほかのアプリが開いていて読めなかった回に、壊れていないことにしない
            void Relink(IEnumerable<FileOwner> holders)
            {
                // 場所は綴りまで同じかで見る。大文字小文字だけ違う（名前の大文字小文字だけを変えた）物も積み、
                // 足すときに記録の綴りを今の名前に合わせる（LocalFileMerger。2026-10-05・点検の14）
                foreach (var holder in holders.Where(holder =>
                             !holder.Paths.Contains(file.Path, StringComparer.Ordinal)
                             || (holder.ArchiveBroken != broken && (broken || contents.Count > 0))))
                {
                    if (!relinked.TryGetValue(holder.ItemId, out var list))
                    {
                        list = [];
                        relinked[holder.ItemId] = list;
                    }

                    list.Add(new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [file.Path],
                        SizeBytes = file.SizeBytes,
                        Contents = contents,
                        ArchiveBroken = broken,
                    });
                }
            }

            // 商品ページで外したものは候補から落とす。
            // 外す操作が要るのは手掛かりが間違っている場合なので、
            // ここで落とさないと次の取り込みで同じ商品へ戻ってしまう。
            var candidates = IdResolver.Resolve(zone, clues)
                .Where(candidate => !detached.IsDetached(hash, candidate.ItemId))
                .ToList();

            if (candidates.Count == 1)
            {
                var record = new LocalFileRecord
                {
                    Hash = hash,
                    Paths = [file.Path],
                    SizeBytes = file.SizeBytes,
                    Contents = contents,

                    // ダウンロード元の記録から商品が決まると未確定を通らない。開けなかった事実は商品の記録に持っていく
                    ArchiveBroken = broken,
                };

                if (!filesByItemId.TryGetValue(candidates[0].ItemId, out var list))
                {
                    list = [];
                    filesByItemId[candidates[0].ItemId] = list;
                }

                list.Add(record);

                // **手掛かりで決まっても、同じ中身を持つほかの商品へ新しい場所を足す**（ユーザ判断 2026-09-30）。
                // 同じ zip を2つの商品が持つ（片方は手掛かり、もう片方は人が手で結んだ）とき、前は決まった商品にだけ足し、
                // 手で結んだ方は古い場所のまま「見つかりません」になっていた。手掛かりで決まらないとき（下）は両方へ足すので、
                // 手掛かりの有無で結果が分かれていた。決まった商品そのものは「手元にある商品」の道（FetchAsync）が足す
                if (owners.TryGetValue(hash, out var others))
                {
                    Relink(others.Where(holder => !string.Equals(holder.ItemId, candidates[0].ItemId, StringComparison.Ordinal)));
                }
            }
            else if (owners.TryGetValue(hash, out var holders))
            {
                // 未確定画面で手作業で紐付けたファイル。手掛かりからは決まらないので、
                // 毎回ここへ落ちてくる。既にitemが持っていると分かっているものを
                // 作業として出し直すのは嘘なので、未確定には出さない。
                //
                // ただし**記録に無い場所で見つかったら、その商品に足す**（大容量の確かめ A・2026-09-30）。
                // 同一性はハッシュなので、別の取り込み元へ移した・写しを置いただけなら同じ物と言える。
                // 前は黙って飛ばしていたので、移した後は商品の記録が古い場所のまま「見つからない」になっていた。
                // 同じ中身を2つの商品が持つなら両方へ足す（どちらの物かは人が決めたことで、場所は中身の場所）
                alreadyOwned++;
                Relink(holders);
            }
            else
            {
                unresolved.Add(new UnresolvedFile
                {
                    Hash = hash,
                    Paths = [file.Path],
                    SizeBytes = file.SizeBytes,
                    ModifiedAtUtc = file.ModifiedAtUtc,
                    FirstSeenAt = DateTimeOffset.Now,
                    Contents = contents,
                    ZoneHostUrl = zone.HostUrl,
                    ZoneReferrerUrl = zone.ReferrerUrl,
                    CandidateItemIds = candidates.Select(candidate => candidate.ItemId).ToList(),

                    // 同じ場所に前にあった別の中身の持ち主を、候補として残す（結ぶのは人が選んだときだけ）。
                    // 上書きを見つけた回にしか分からないので、前の回に書いた分も同じ中身の記録から引き継ぐ。
                    // この中身をその商品から外してあれば（外した印）載せない——違うと人が決めている
                    SamePathItemIds = [.. formerHolders
                        .Concat(carriedSamePath.GetValueOrDefault(hash) ?? [])
                        .Distinct(StringComparer.Ordinal)
                        .Where(itemId => !detached.IsDetached(hash, itemId))],
                    ArchiveBroken = broken,
                });
            }
        }

        return new ResolutionResult
        {
            FilesByItemId = filesByItemId,
            Relinked = relinked,
            Unresolved = unresolved,
            Hashed = hashed,
            ReusedFromCache = reused,
            Excluded = excluded,
            AlreadyOwned = alreadyOwned,
            Unreadable = unreadable,
            Unhashed = unhashed,
            BrokenOwned = brokenOwned,
            Replaced = replaced,
        };
    }

    /// <summary>ZIPだけ中身を読む。それ以外の形式は Zone.Identifier だけが手掛かりになる。</summary>
    /// <returns>
    /// 手掛かりと中身の一覧、読めたか（読めなかった zip と zip 以外は false）、zip として開けなかったか。
    /// </returns>
    private static (IReadOnlyList<BoothClue> Clues, IReadOnlyList<string> Contents, bool Read, bool Broken) InspectFile(ScannedFile file)
    {
        if (!file.IsArchive)
        {
            return ([], [], false, false);
        }

        try
        {
            var inspection = ZipInspector.Inspect(file.Path);
            return (inspection.Clues, inspection.Summary.Files.Select(entry => entry.RelativePath).ToList(), true, false);
        }
        catch (InvalidDataException)
        {
            // 形式が合わない：目録（末尾の一覧）が無い・崩れている。途中で切れたダウンロードはここに来る。
            // 何度開いても同じなので「壊れている」と言える。目録が無事で中のデータだけが化けた zip は、
            // 全部を解かないと分からないのでここでは見つからない（一時展開で分かる）
            return ([], [], false, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ほかのアプリが開いている・権限が無い。次の取り込みでは開けるかもしれないので、壊れているとは言わない
            return ([], [], false, false);
        }
    }

    /// <summary>
    /// 控えに書く手掛かり。ID を決めるのに使うのは「商品のURL」の手掛かりの商品IDだけ（<see cref="IdResolver.Resolve"/>）なので、
    /// それだけを出てきた順に残す。
    /// </summary>
    private static IReadOnlyList<string> ClueItemIdsOf(IReadOnlyList<BoothClue> clues)
        => [.. clues
            .Where(clue => clue.Kind == BoothClueKind.ItemUrl && clue.ItemId is not null)
            .Select(clue => clue.ItemId!)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>控えた商品IDを、読んだときと同じ働きの手掛かりに戻す。</summary>
    private static BoothClue ClueOf(string itemId) => new()
    {
        Kind = BoothClueKind.ItemUrl,
        Url = IdResolver.ToItemUrl(itemId),
        ItemId = itemId,
        SourcePath = "（走査の控え）",
    };

    /// <summary>
    /// 梯子を段ごとに降りる。**商品ごとに全部取るのではなく、段ごとに全商品を回る。**
    ///
    /// 理由は2つ。
    ///
    /// ひとつは**待ち時間を作業時間に変える**こと。実データでは1商品あたり
    /// JSON 1本・HTML 1本・画像 9.5本で、画像が全体の76%を占める。
    /// 商品ごとに取ると100商品で31分かかり、その間ずっと何も見えない。
    /// ①②だけなら5分で、そこには検索・絞り込み・統計に要るものが全部揃っている。
    ///
    /// もうひとつは**部分的な知識で作業を始めさせない**こと。対応アバターを選ぶとき、
    /// 候補の材料にはvariationの名前が入る。100商品のうち1商品しか知らない状態で
    /// 選ばせると候補が出揃わず、後から選び直すことになる。
    /// </summary>
    private async Task<FetchResult> FetchAsync(
        Dictionary<string, List<LocalFileRecord>> filesByItemId,
        ImportWorkSet work,
        ImportTotals totals,
        FilePresenceProbe probe,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var added = 0;
        var alreadyKnown = 0;
        var notFound = 0;
        var notFoundFiles = new List<UnresolvedFile>();
        var temporaryFailures = 0;
        var avatarItemsUpdated = 0;
        var avatarsFound = 0;
        string? avatarDetectError = null;
        var avatarDetectRan = false;

        // 商品に書いた壊れた zip を結果の数に入れる。BOOTH に無くて未確定へ戻した物・取れなかった物は、商品に入っていないので数えない
        void NoteBroken(string itemId, IEnumerable<LocalFileRecord> files)
        {
            foreach (var file in files.Where(file => file.ArchiveBroken))
            {
                totals.NoteBrokenOnItem(itemId, file.Hash);
            }
        }

        // 手元にある商品は通信が要らない。ファイルを足すだけなので、段に入る前に片付ける
        var pending = new List<(string ItemId, List<LocalFileRecord> Files)>();

        // 取得済みなのに説明が無い商品。①の後・②の前で閉じた取り込みの続き（U9）
        var withoutPage = new List<ItemRecord>();

        // 無い場所を外すかは見回りと同じ部品で見る（根はドライブごとに1回・3秒で打ち切り。点検の12）。
        // 根の覚えはこの段の間だけ（周回の頭からここまでに外付けを外していたら、頭の「つながっている」は古い）
        var presence = probe.Renewed();

        foreach (var (itemId, discovered) in filesByItemId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
            if (existing is null)
            {
                pending.Add((itemId, discovered));
                continue;
            }

            // 取得済みのitemは触らない。中断して再実行した時に、ここが「続きから」を成立させる。
            //
            // 足すのは商品の錠の中で今の一覧に（2026-10-05・file-lifecycle.md「気になった所」3）。上で読んだ写しに足して
            // 書くと、読んでから書くまでの間に人が付けた種類・外す／戻す・見つからなくなった日時が古い値に戻る
            await _store.Items.ChangeLocalAsync(
                itemId,
                current => NotDetachedIn(current, discovered) is { Count: > 0 } files
                    ? current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, presence) }
                    : null,
                [LocalField.LocalFiles],
                cancellationToken);

            alreadyKnown++;
            NoteBroken(itemId, discovered);

            // 前に取れなかった商品でも、今は商品があるならファイルはここで足された
            totals.NoteSettled(itemId);

            // 販売終了の商品はページも無いので戻さない（取りに行っても毎回失敗するだけ）
            if (!existing.Local.IsDelisted && !File.Exists(_store.Paths.ItemHtmlFile(itemId)))
            {
                withoutPage.Add(existing);
            }
        }

        // 残り時間の見込み（U1）。①は新しい商品の数、②はそれに説明の無い取得済みを足した数
        work.PlanRequests(json: pending.Count, pages: pending.Count + withoutPage.Count);

        // 中断の記録は周回をまたいで足し合わせる（C12）
        totals.PlanFetch(pending.Count);

        // 走査が済んだことをここで書く。①で取る物が無い周回は①の中で書かないので、
        // 書かないと②③や画像の間に閉じた回に「途中で中断した」と嘘をつく
        await SaveProgressAsync(totals, work, cancellationToken);

        // ── ① 商品JSON（全商品）。ここが終われば検索も統計も成立する ──
        //
        // 段ごとに優先度を切り替える。人が押した操作はこれより上なので、
        // 取り込みの最中でも「このIDで確認」は待たされない
        using var metadataPriority = BoothClient.Prioritize(BoothPriority.Metadata);

        var fetched = new List<ItemRecord>();
        var shopIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        // 「追加した」の足跡は溜めて、一定件数ごとと①の終わりに書く（1件ずつ書くと recent.json の読み書きが件数の2乗になる。
        // 釣り合いの根拠は RecentStampBuffer.FlushEvery）。足跡を打つのは①だけなので、①を抜けるところが取り込みの区切り
        var addedStamps = new Services.RecentStampBuffer(_store.Recent);
        try
        {
            foreach (var (itemId, discovered) in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, ImportPhase.FetchingJson, ++done, pending.Count, itemId);

                // 応答の無い失敗が続いたら、残りは問い合わせずに「続きから」へ残す（ユーザ判断 2026-09-29）。
                // 取れなかった商品と同じ扱いなので、下の帯の「続きから進む」で取り直せる
                if (totals.Outage.IsStopped)
                {
                    temporaryFailures++;
                    work.PlanRequests(json: -1, pages: -1);
                    totals.NoteUnfetched(itemId, discovered);
                    continue;
                }

                var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
                work.PlanRequests(json: -1);
                totals.Outage.Note(jsonResult);

                if (jsonResult.Status == BoothFetchStatus.NotFound)
                {
                    // BOOTHに無い＝ファイルが無かったことにはならない。
                    // 買っていて手元にあるものなので、未確定へ戻して人に決めてもらう。
                    // 別のIDで再公開されていることもあり、そのときは候補検索が拾える。
                    //
                    // 1回の404で流すのは、**こちらが「非公開だ」と確定する必要がないから。**
                    // 一時的な障害だったなら次の取り込みで普通に確定するだけで、何も失われない
                    notFound++;
                    work.PlanRequests(pages: -1); // ②へは進まない
                    notFoundFiles.AddRange(discovered.Select(file => ToUnresolved(file, itemId)));
                    totals.NoteSettled(itemId);
                    continue;
                }

                // 一時失敗（タイムアウト・5xx・接続失敗）と、200 でも読めない応答（JSON でない・型が変わった）。
                // 後者を投げると、1件のために取り込み全体が止まっていた。
                // どちらも「続きから」に残して取り直せるようにする（#10）。その場で書くのは、この後に閉じても残すため
                if (!jsonResult.IsSuccess || jsonResult.Value is null
                    || BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, itemId: itemId) is not { } booth)
                {
                    temporaryFailures++;
                    work.PlanRequests(pages: -1); // ②へは進まない
                    totals.NoteUnfetched(itemId, discovered);
                    await SaveProgressAsync(totals, work, cancellationToken);
                    continue;
                }

                var item = new ItemRecord
                {
                    Id = itemId,
                    Booth = booth,
                    Local = new LocalBlock
                    {
                        // 見方を渡すのは、見つけた場所にディスクの通し番号を書くため（点検の3）
                        LocalFiles = LocalFileMerger.Merge([], discovered, presence),
                        NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                        LastFetchedAt = DateTimeOffset.Now,
                        NextFetchDueAt = NextFetchDue(itemId),
                    },
                };

                // 1件ずつ保存する。ここで中断しても、取れたぶんはそのまま残る。
                //
                // **「新しい」と判断した時点と書く時点がずれている**（ユーザ判断 2026-09-21・L13）。
                // BOOTH から取る数秒〜数分の間に、未確定の「このIDで登録」が同じ商品を作ることがあり、
                // 丸ごと書くと人が入れた名前・購入記録が消えていた。
                // **在るかを商品の錠の中で見て**、あれば取ってきた `booth` と見つけたファイルだけを今の値に重ねる。
                // 前は錠の外で読み直し、無ければ丸ごと保存していたので、見てから書くまでの間に人の保存が同じ商品を作ると、
                // 人が入れた名前・メモ・購入記録を消していた（2026-10-02。CreateWhileSomeoneSavesTests）
                var created = false;
                await _store.Items.CreateOrChangeLocalAsync(
                    itemId,
                    () =>
                    {
                        created = true;
                        return item;
                    },
                    current => created || NotDetachedIn(current, discovered) is not { Count: > 0 } files
                        ? current
                        : current with { LocalFiles = LocalFileMerger.Merge(current.LocalFiles, files, probe.Renewed()) },
                    LocalOwners.Import,
                    cancellationToken,
                    item.Booth);

                // 「追加」の足跡。**itemのJSONには書かない**（足跡で埋めないため）。
                // 既にある商品には打てないので、そちらは「不明」のまま残る——
                // 後から作った時刻を騙るより、無いと言う方がよい。
                // 時刻は保存した今のもの（書くのは後でも、1件ずつ書いていたときと同じ時刻が残る）
                await addedStamps.AddAsync(itemId, Services.RecentKind.Added, DateTimeOffset.Now);

                fetched.Add(item);
                added++;
                NoteBroken(itemId, discovered);

                // 検索と件数にはもう出してよい。編集は③が済むまで待たせる（U8・U10）
                work.NoteAdded(itemId);

                // どこまで進んだかを残す（理由は SaveProgressAsync）
                totals.NoteFetched();
                totals.NoteSettled(itemId);
                await SaveProgressAsync(totals, work, cancellationToken);

                // アイコンのURLは商品JSONにしか入っていないので、ここで控えて⑥で取りに行く
                if (item.Booth.Shop is { ThumbnailUrl.Length: > 0 } shop)
                {
                    shopIcons[shop.Subdomain] = shop.ThumbnailUrl;
                }
            }
        }
        finally
        {
            // 中止・例外で抜けるときも溜めた分を書く（取れて保存した商品の「追加した」を捨てない）
            await addedStamps.FlushAsync();
        }

        // ── ② 商品ページHTML（全商品）。対応アバターの節と説明文 ──
        //
        // 取得済みでも説明が無い商品はここへ戻す。②③の途中で閉じてから押し直すと、
        // ①が済んだ商品は「取得済み」で飛ばされ、説明が⑦の取り直しの日まで埋まらなかった（U9）
        var pages = fetched.Concat(withoutPage).ToList();
        done = 0;

        for (var index = 0; index < pages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ①と同じく、応答の無い失敗が続いたら残りは問い合わせない。説明の無い商品は、
            // 「続きから進む」で対象を走査し直したときに②へ戻る（取得済みで説明が無い商品は②の対象・U9）
            if (totals.Outage.IsStopped)
            {
                work.PlanRequests(pages: -(pages.Count - index));
                break;
            }

            var item = pages[index];
            Report(progress, ImportPhase.FetchingHtml, ++done, pages.Count, item.Id);

            var htmlResult = await _client.GetItemHtmlAsync(item.Id, cancellationToken);

            // 見込みは問い合わせが済んでから減らす（①と揃える）。取っている最中の1件は残りに数える
            work.PlanRequests(pages: -1);
            totals.Outage.Note(htmlResult);
            if (!htmlResult.IsSuccess || htmlResult.Value is null)
            {
                // 節が取れなくても商品自体は使える。次の段へ進む
                continue;
            }

            var extraction = H2SectionExtractor.Extract(htmlResult.Value);

            // 節は、書く直前に読み直した今の booth に足す。①で取った写しに足して丸ごと書くと、
            // ①と②の間（数分になることもある）に人が「商品情報を取り直す」を押したとき、
            // 取り直した新しい booth が①の古い物に戻っていた。
            // 今の方が新しく取れていれば、節もそちらが同じ時に取った物なので触らない
            var fetchedAt = item.Booth.FetchedAt;
            BoothBlock? written = null;
            await _store.Items.ChangeBoothAsync(
                item.Id,
                current => current.FetchedAt > fetchedAt
                    ? null
                    : written = current with { H2Sections = extraction.Sections },
                cancellationToken);

            if (written is not null)
            {
                pages[index] = item = item with { Booth = written };
            }
            else if (await _store.Items.LoadAsync(item.Id, cancellationToken) is { } newer)
            {
                // 後の③と画像の列は、取り直した新しい画像の一覧で進める
                pages[index] = item = newer;
                continue;
            }
            else
            {
                continue;
            }

            // 説明が無い商品でも空のファイルを置く。置かないと「まだ取っていない」と見分けが付かず、
            // 取り込むたびに取り直しに来る
            await _store.Items.SaveDescriptionHtmlAsync(item.Id, extraction.DescriptionHtml ?? string.Empty, cancellationToken);
        }

        // 画像の問い合わせの見込みは、**②が終わった時点で**数えておく（手元にある絵は取りに行かないので多めに出る）。
        // 以前は周回の最後（③の後）に足していたので、①②③の間ずっと画像の残りが 0 のままで、
        // 「画像を取り終わるまで」が「編集できるまで」と同じ数字になっていた（ユーザ指摘 2026-09-21）。
        // 画像の枚数は②まで済めば分かるので、そこで数える
        //
        // 応答の無い失敗が続いて打ち切ったなら、この回は画像も取りに行かない（取れない物を1件ずつ再試行で待つだけになる）。
        // 取り残した画像は起動時の⑤か「足りない情報を取得」で取れる（ImageBacklog は手元の JSON とディスクの差で対象を決める）。
        // 打ち切った記録は③の間に閉じても残るよう、ここで書いておく
        var takesImages = _images.SavesImages && !totals.Outage.IsStopped;
        if (totals.Outage.IsStopped)
        {
            await SaveProgressAsync(totals, work, cancellationToken);
        }

        if (takesImages)
        {
            var planned = pages.Where(item => item.Booth.Images.Count > 0).ToList();
            work.PlanRequests(
                thumbnails: planned.Count,
                gallery: planned.Sum(item => item.Booth.Images.Count) - planned.Count,
                icons: shopIcons.Count);
        }

        // ── ③ 対応アバターの検出 ──
        //
        // 画像より先に置く。対応アバターを選ぶのは人の作業で、その候補が出揃っている
        // ことの方が、絵が見えていることより先に要る。
        // 通信が要るのは「対応表明で名前が出たが、手元に持っていないアバター」だけなので、
        // ここを④の前に置いても待ちはほとんど伸びない。
        if (_avatars is not null && pages.Count > 0)
        {
            using var detectPriority = BoothClient.Prioritize(BoothPriority.Detection);
            avatarDetectRan = true;

            try
            {
                var detected = await _avatars.DetectAsync(new DetectProgressAdapter(progress), cancellationToken);
                avatarItemsUpdated = detected.ItemsUpdated;
                avatarsFound = detected.AvatarsFound;
                // 問い合わせを打ち切った回は、止まった理由として言う（つながっていない／BOOTHが不調）
                avatarDetectError = Services.FailureText.Outage(detected.Outage);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取り込みは成立している。検出はアバター画面からやり直せるので、止めずに知らせるだけ
                Diagnostics.AppLog.Error("取り込みの後の対応アバターの検出", exception);
                avatarDetectError = Services.FailureText.Cause(exception);
            }
        }

        // ③の段は終わった（失敗しても、検出を使わない設定でも）。この周回の商品を編集に出す
        work.NoteDetectionDone(fetched.Select(item => item.Id));

        // ④1枚目 ⑤残りの画像 ⑥ショップのアイコン は、周回の外の画像の列で取る（U5・DrainImagesAsync）。
        // 周回の中で取り切ると、その間に積まれたフォルダは⑥が終わるまで何も始まらない。
        // 画像を取らない設定なら何も積まない（梯子は①②③で終わり。検索・絞り込み・統計は JSON だけで成立する）
        //
        // **並びは検索・編集の待ち行列と同じ規則にそろえる**（ItemOrder.ByAcquired。2026-09-21 ユーザ判断）。
        // 走査した順のままだと、絵が埋まっていく順と、人が上から片付けていく順が無関係になり、
        // 待ち行列の先頭の商品の絵だけがいつまでも来ない、という見え方になっていた
        var withImages = takesImages
            ? ItemOrder.ByAcquired(pages.Where(item => item.Booth.Images.Count > 0), descending: true).ToList()
            : [];

        return new FetchResult
        {
            Added = added,
            AlreadyKnown = alreadyKnown,
            NotFound = notFound,
            NotFoundFiles = notFoundFiles,
            TemporaryFailures = temporaryFailures,
            WithImages = withImages,
            ShopIcons = takesImages ? shopIcons : new Dictionary<string, string>(),
            AvatarItemsUpdated = avatarItemsUpdated,
            AvatarsFound = avatarsFound,
            AvatarDetectError = avatarDetectError,
            AvatarDetectRan = avatarDetectRan,
        };
    }

    /// <summary>
    /// どこまで進んだかを残す。閉じた時に何件残っていたかをユーザは覚えていない。
    /// ①の途中で閉じると「IDは分かったがまだ取得していない商品」の一覧は消えるので、
    /// 件数だけでも残しておかないと、中断したこと自体が黙って起きる。
    /// BOOTH の不調で取れなかった商品も一緒に書く（#10）
    /// </summary>
    /// <param name="scanning">走査・ID の特定に入るところか（<see cref="ImportState.Scanning"/>）。</param>
    private Task SaveProgressAsync(
        ImportTotals totals, ImportWorkSet work, CancellationToken cancellationToken, bool scanning = false)
        => _store.ImportState.SaveAsync(
            new ImportState
            {
                Done = totals.FetchedTotal,
                Total = totals.PendingTotal,
                StoppedAt = DateTimeOffset.Now,
                Targets = work.Accepted,
                Unfetched = totals.Unfetched,
                Stopped = totals.Outage.Stopped,
                Scanning = scanning,
            },
            cancellationToken);

    /// <summary>
    /// 次回の取得予定。全itemが同じ日に期限切れにならないよう、商品IDから決まるばらつきを足す。
    /// 乱数ではなくIDから決めているのは、同じitemなら何度計算しても（起動し直しても）同じ日になるようにするため（<see cref="RefreshJitter"/>）。
    /// </summary>
    private DateTimeOffset NextFetchDue(string itemId)
        => DateTimeOffset.Now.AddDays(_settings.RefreshIntervalDays + RefreshJitter.Days(itemId, _settings.RefreshJitterDays));

    private sealed class ResolutionResult
    {
        public required Dictionary<string, List<LocalFileRecord>> FilesByItemId { get; init; }

        /// <summary>手掛かりからは決まらず、同じ中身を持つ商品へ場所を足す物（商品ID → ファイル）。</summary>
        public required Dictionary<string, List<LocalFileRecord>> Relinked { get; init; }

        public required List<UnresolvedFile> Unresolved { get; init; }

        public int Hashed { get; init; }

        public int ReusedFromCache { get; init; }

        public int Excluded { get; init; }

        /// <summary>既にitemが持っていたので未確定へ流さなかった件数。</summary>
        public int AlreadyOwned { get; init; }

        /// <summary>中身を読めず（ハッシュを計算できず）飛ばした件数。</summary>
        public int Unreadable { get; init; }

        /// <summary>ハッシュを計算できなかったファイルの場所（今回見ていない場所として、前の未確定を残す）。</summary>
        public IReadOnlyList<string> Unhashed { get; init; } = [];

        /// <summary>zip として開けなかった物のうち、前から商品が持っている物（商品ID とハッシュ）。結果の数に入れる。</summary>
        public required List<(string ItemId, string Hash)> BrokenOwned { get; init; }

        /// <summary>記録の場所に、別の中身が来ていた物（その記録からこの場所を外す）。</summary>
        public required List<(string ItemId, string Hash, string Path)> Replaced { get; init; }
    }

    /// <summary>
    /// 商品へ紐付けたファイルを、未確定のファイルへ戻す。
    ///
    /// 手掛かりから決まった商品IDは<b>候補として載せる</b>。
    /// 「このファイルは 1234567 を指しているが、BOOTHには無い」と読める形にするため。
    /// </summary>
    private static UnresolvedFile ToUnresolved(LocalFileRecord file, string itemId)
    {
        var modified = DateTimeOffset.Now;
        var path = file.Paths.FirstOrDefault();
        string? hostUrl = null;
        string? referrerUrl = null;

        if (path is not null)
        {
            try
            {
                modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 日時が読めなくても未確定には出したいので、今の時刻で通す
            }

            // ふつうの未確定と同じくダウンロード元を読み直す（file-lifecycle.md 気になった所9）。
            // 商品の記録は Zone の欄を持たないので、前はここで落ち、毎回この道を通るので取り込み直しても付かず、
            // 展開した中身が元zip の束（ZoneReferrerUrl）に入らなかった。商品から外して戻すとき（DetachFile）も読み直している
            var zone = ZoneIdentifierReader.Read(path);
            hostUrl = zone.HostUrl;
            referrerUrl = zone.ReferrerUrl;
        }

        return new UnresolvedFile
        {
            Hash = file.Hash,
            Paths = file.Paths,
            SizeBytes = file.SizeBytes,
            ModifiedAtUtc = modified,
            FirstSeenAt = DateTimeOffset.Now,
            Contents = file.Contents,
            ZoneHostUrl = hostUrl,
            ZoneReferrerUrl = referrerUrl,
            CandidateItemIds = [itemId],

            // 開けなかった印は記録ごと引き継ぐ（前は商品の記録が印を持たず、戻すときに付け直していた）
            ArchiveBroken = file.ArchiveBroken,
        };
    }

    /// <summary>
    /// 画像の列を1件ずつ取る（U5）。④1枚目 → ⑤残り → ⑥ショップのアイコン の順。
    ///
    /// 1件ごとに <paramref name="work"/> を見て、積まれたフォルダがあればその場で戻る。
    /// 残りの問い合わせの見込み（U1）もここで減らす。
    /// 周回の中で取り切っていた頃は、積んだ分は⑥が終わるまで何も始まらなかった
    /// （設計では「積まれたら①②が最優先」と決めてあったのに、実装が合っていなかった）。
    /// 優先度は段ごとに切り替える。人が押した通信はどの段よりも上なので待たされない。
    /// </summary>
    /// <returns>落とせた画像の枚数。</returns>
    private async Task<int> DrainImagesAsync(
        ImageQueue queue,
        ImportWorkSet work,
        IProgress<ImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloaded = 0;

        // 画像の段でも、届かない失敗が3件続いたらこの回の残りは取りに行かない（ユーザ判断 2026-09-29）。
        // 取らなかった絵は印も置かないので、起動時の⑤か「足りない情報を取得」で取れる（ImageBacklog は手元の JSON とディスクの差で対象を決める）
        while (!queue.IsEmpty && !work.HasPending && !queue.Outage.IsStopped)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ④ 1枚目。一覧のカードは1枚目しか使わない（マウスを乗せて初めてギャラリーを組む）ので、1枚あれば一覧は完成する
            if (queue.Thumbnails.First is { } next)
            {
                queue.Thumbnails.RemoveFirst();
                var item = next.Value;
                Report(progress, ImportPhase.FetchingThumbnails, ++queue.ThumbnailsDone, queue.ThumbnailsTotal, item.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Thumbnail))
                {
                    // 取れなくても商品は画面に出す。出さないとその商品は永久に見えない
                    if (await _images.SyncOneAsync(item.Id, item.Booth.Images[0], queue.Outage, cancellationToken))
                    {
                        downloaded++;
                    }
                }

                // 見込みは取り終えてから減らす（①②と揃える）
                work.PlanRequests(thumbnails: -1);

                queue.Galleries.Enqueue(item);
                queue.GalleriesTotal++;
                continue;
            }

            // ⑤ 残りの画像。商品ごとにまとめて取ると、その商品を開いたときに揃っている確率が上がる
            if (queue.Galleries.TryDequeue(out var gallery))
            {
                Report(progress, ImportPhase.FetchingGallery, ++queue.GalleriesDone, queue.GalleriesTotal, gallery.Booth.Name);

                using (BoothClient.Prioritize(BoothPriority.Gallery))
                {
                    downloaded += (await _images.SyncAsync(gallery.Id, gallery.Booth.Images, queue.Outage, cancellationToken)).Downloaded;
                }

                work.PlanRequests(gallery: -(gallery.Booth.Images.Count - 1)); // 1枚目は④で数えた

                continue;
            }

            // ⑥ ショップのアイコン。使うのはショップ画面と作者名の横だけで、無くても名前で用は足りる
            var (subdomain, thumbnailUrl) = queue.ShopIcons.First();
            queue.ShopIcons.Remove(subdomain);
            Report(progress, ImportPhase.FetchingShopIcons, ++queue.IconsDone, queue.IconsTotal, subdomain);

            using (BoothClient.Prioritize(BoothPriority.ShopIcon))
            {
                await _images.SyncShopIconAsync(subdomain, thumbnailUrl, queue.Outage, cancellationToken);
            }

            work.PlanRequests(icons: -1);
        }

        return downloaded;
    }

    /// <summary>
    /// 周回の外で取る画像の列（U5）。
    ///
    /// 新しい周回の1枚目は**先頭**へ入れる。積んだ物が早く一覧に出ることの方が、
    /// 前の周回の2枚目より先に要る。件数は周回をまたいで数える（画面には1本の進み具合として出す）。
    /// </summary>
    private sealed class ImageQueue
    {
        public LinkedList<ItemRecord> Thumbnails { get; } = new();

        public Queue<ItemRecord> Galleries { get; } = new();

        public Dictionary<string, string> ShopIcons { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ThumbnailsDone { get; set; }

        public int ThumbnailsTotal { get; set; }

        public int GalleriesDone { get; set; }

        public int GalleriesTotal { get; set; }

        public int IconsDone { get; set; }

        public int IconsTotal { get; set; }

        public bool IsEmpty => Thumbnails.Count == 0 && Galleries.Count == 0 && ShopIcons.Count == 0;

        /// <summary>
        /// ④⑤⑥の結果を続けて数える。①②とは分けて持つ——画像は別の置き場（pximg）から来るので、
        /// 画像だけが落ちていても①②を止める理由にならず、その逆も同じ
        /// </summary>
        public BoothOutageWatch Outage { get; } = new();

        public void Enqueue(IReadOnlyList<ItemRecord> withImages, IReadOnlyDictionary<string, string> shopIcons)
        {
            // 並びは保ったまま先頭へ
            for (var index = withImages.Count - 1; index >= 0; index--)
            {
                Thumbnails.AddFirst(withImages[index]);
            }

            ThumbnailsTotal += withImages.Count;

            foreach (var (subdomain, url) in shopIcons)
            {
                if (ShopIcons.TryAdd(subdomain, url))
                {
                    IconsTotal++;
                }
            }
        }
    }

    private sealed class FetchResult
    {
        public int Added { get; init; }

        public int AlreadyKnown { get; init; }

        public int NotFound { get; init; }

        /// <summary>BOOTHに無かったので未確定へ戻すファイル。ここで捨てると手元から消える。</summary>
        public IReadOnlyList<UnresolvedFile> NotFoundFiles { get; init; } = [];

        public int TemporaryFailures { get; init; }

        /// <summary>画像の列（④⑤）に積む商品。画像を取らない設定なら空。</summary>
        public IReadOnlyList<ItemRecord> WithImages { get; init; } = [];

        /// <summary>画像の列（⑥）に積むショップのアイコン。サブドメイン → アイコンのURL。</summary>
        public IReadOnlyDictionary<string, string> ShopIcons { get; init; } = new Dictionary<string, string>();

        public int AvatarItemsUpdated { get; init; }

        public int AvatarsFound { get; init; }

        public string? AvatarDetectError { get; init; }

        public bool AvatarDetectRan { get; init; }
    }
}
