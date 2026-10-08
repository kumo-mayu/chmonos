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
public sealed partial class ImportPipeline : IImportPipeline
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
}
