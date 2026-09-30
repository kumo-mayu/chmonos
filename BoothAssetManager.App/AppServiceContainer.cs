using System.IO;
using System.Net.Http;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App;

/// <summary>
/// 構成ルート。DIコンテナは使わず、依存の順にここで組み立てる。
/// 何がどこに依存しているかをこの1ファイルで追えるようにするため。
/// </summary>
public sealed class AppServiceContainer : IDisposable
{
    private readonly HttpClient _httpClient;
    private SingleInstanceLock? _instanceLock;

    public AppServiceContainer()
        : this(AppPaths.Default)
    {
    }

    /// <summary>
    /// 保存先と、外へ出る所を渡して組む。**アプリは上の引数なしの方だけを使う**（既定の値がアプリの動き）。
    /// 試験（<c>BoothAssetManager.App.Tests</c> の <c>TestApp</c>）が、一時フォルダの保存先と通信しない作り物で
    /// ViewModel を組むための入口（ユーザ判断 2026-09-30：計算で決まる文言やボタンの出し分けを、起動して撮らずに確かめる）
    /// </summary>
    /// <param name="http">BOOTH への通信の出口。渡さなければ本物。試験は決まった応答を返す作り物を渡す。</param>
    /// <param name="boothDelay">問い合わせの間の待ち。渡さなければ本物の待ち。試験は待たない物を渡す（相手が作り物なので空ける意味が無い）。</param>
    /// <param name="cleanUpTemporaryUnpacks">
    /// 前回の一時展開を消すか。消す場所は保存先の外（利用者の一時フォルダ）で全部の起動が共有するので、
    /// 試験から消すと、隣で動いているアプリがエクスプローラで開いている中身を消してしまう
    /// </param>
    internal AppServiceContainer(
        AppPaths paths,
        HttpMessageHandler? http = null,
        Func<TimeSpan, CancellationToken, Task>? boothDelay = null,
        bool cleanUpTemporaryUnpacks = true)
    {
        Paths = paths;
        Paths.EnsureCreated();

        // 裏の作業で黙って飛ばした失敗も、後から追えるように書き残す（技術的負債 2-1）
        Core.Diagnostics.AppLog.Use(new Core.Diagnostics.LogFile(Paths.LogFile));

        _instanceLock = SingleInstanceLock.TryAcquire(Paths);
        IsSingleInstance = _instanceLock is not null;

        Store = new DataStore(Paths);

        // 前回閉じたときに消し残った一時展開（#56）。二重に起動した側が消すと、
        // 先に動いている方がエクスプローラで開いている中身を消してしまうので、1つ目のときだけ
        if (IsSingleInstance && cleanUpTemporaryUnpacks)
        {
            new TemporaryUnpacker().CleanUp();
        }
        // 設定を持つのは SettingsService だけ。画面は写しを持たず、書くときは UiCommand.ChangeSettings を通す（技術的負債 1-1）
        SettingsStore = new SettingsService(Store);

        _httpClient = http is null ? new HttpClient() : new HttpClient(http);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        // 設定は値ではなく「今の設定を返すもの」で渡す。値で渡すと、設定画面で保存しても
        // 起動し直すまで効かなかった（画像の長辺・画質・取得の間隔など。SettingsSource に理由）
        Client = new BoothClient(_httpClient, () => Settings, boothDelay);
        Images = new ImagePipeline(Client, Paths, () => Settings);

        // 検出は梯子の③なので、取り込みより先に組み立てる
        Avatars = new AvatarService(Store, () => Settings, Client);

        // unitypackage の中身は取り込みの裏で1度だけ読み、ハッシュごとの控えに置く（2026-09-13 ユーザ判断）。
        // 商品ページや改変の画面は zip を解く前に控えを見る
        var unityPackagePaths = new UnityPackagePathStore(Paths);
        UnityHandoff.UsePathStore(unityPackagePaths);
        UnityPackages = new UnityPackageCatalog(Store, unityPackagePaths);

        // 外付けはドライブ文字が変わる。取り込みとフォルダビューを開いた時に文字と通し番号の組を控える（2026-09-14 ユーザ判断）
        Volumes = new VolumeTable(Store, new Services.VolumeReader());
        Import = new ImportPipeline(Store, Client, Images, () => Settings, Avatars, UnityPackages, Volumes);
        Items = new ItemService(Store, Client, Images, () => Settings);
        Backlog = new ImageBacklog(Store, Images);
        AvatarImages = new AvatarImageSync(Store, Client, Images);
        Watch = new FolderWatch(Store);

        // 辞書は実行ファイルの隣に配られる。索引は最初に必要になったときだけ組む
        Bridge = new SearchBridge(new JapaneseDictionary(
            Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz"),
            Paths.SearchBridgeCacheFile));

        // 辞書に載っていない造語の読みは、漢字1字ごとの音訓から組み立てる。
        // 字の表は控えから読む（gz の XML から組むと 0.3秒・19.5MB、控えなら 14ms・4.1MB。2026-09-24 の実測）
        KanjiReadings = new KanjiReadings(
            Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz"),
            Paths.KanjiReadingsCacheFile);

        // 検索の「名前」「ショップ」の並べ替えの鍵（名前の読み）。名前ごとに1度だけ作って控える
        NameOrder = new Core.Services.NameCollation(KanjiReadings);
        Due = new DueRefresh(Store, Items);
        // 登録簿を渡すと、ファイル名の中のアバターの名前を検索語から外す（「商品名_アバター名」で AND 検索が0件になっていた。
        // 2026-09-29 の再調整で最も効いた分）。押すたびに読み直すので、検出で登録簿が増えればそのまま効く
        Resolver = new FallbackResolver(Client, Bridge, KanjiReadings, () => Store.Avatars.Load());
        Edit = new EditService(Store);
        Notifications = new NotificationService(Store, () => Settings);
        UserTags = new UserTagService(Store);
        Attributes = new AttributeService(Store);
        Shops = new ShopService(Store, () => Settings, Client);
        Stats = new StatsService(Store);
        Recent = new Services.RecentTracker(Store, BackgroundWrites);
        Modifications = new ModificationService(Store, Images);
        Commands = new CommandHandler(
            Import, Items, Edit, new UnpackedFolderRemover(DeleteToRecycleBin), Resolver, Notifications, UserTags, Attributes,
            Modifications, Avatars, UnityPackages, SettingsStore, Avatars, Shops, Images, Client, Store.VideoTitles, Store.ShopNotes,
            new MissingFileFinder(Store), Volumes, Store.ImportState);

        // 商品ページの動画の欄のタイトル。控えを読み、無いか30日を過ぎていれば YouTube に聞いて控える（ユーザ判断 2026-09-14）
        YouTube = new Services.YouTubeInfo(Store.VideoTitles, Commands);

        // ドラッグで変えた画面の幅（ユーザ判断 2026-09-14）。書くのは UiCommand.ChangeUiState
        PaneWidths = new Services.PaneWidths(SettingsStore, Commands);
    }

    /// <summary>
    /// Unity Hub・VCC・ALCOM が手元にあるかを調べる。レジストリと実マシンのファイルを見るので、
    /// 試験は決まった答えに差し替える（改変の画面のボタンと空の文・設定の説明の文が、これで決まる）
    /// </summary>
    internal Func<Services.UnityTools> DetectUnityTools { get; set; } = Services.UnityTools.Detect;

    /// <summary>Unity Hub・VCC の一覧から Unity のプロジェクトを集める。実マシンの一覧を読むので、試験は差し替える。</summary>
    internal Func<IReadOnlyList<UnityProjectCandidate>> DiscoverUnityProjects { get; set; } = () => UnityProjects.Discover();

    /// <summary>ドラッグで変えられる画面の幅。</summary>
    public Services.PaneWidths PaneWidths { get; }

    /// <summary>YouTube の動画のタイトル（商品ページの動画の欄）。</summary>
    public Services.YouTubeInfo YouTube { get; }

    /// <summary>unitypackage の中身を1度だけ読んで残す（取り込みの裏・手でファイルを付けた後）。</summary>
    public UnityPackageCatalog UnityPackages { get; }

    /// <summary>ドライブ文字と通し番号の組。フォルダビューが、文字の変わった外付けを今の文字で出すのに使う。</summary>
    public VolumeTable Volumes { get; }

    /// <summary>
    /// フォルダをごみ箱へ送る。完全削除にしないのは、判定を誤ったときに取り返しがつくようにするため。
    /// ごみ箱を使えない場所（ネットワークドライブなど）では完全削除にフォールバックする。
    /// </summary>
    private static Task DeleteToRecycleBin(string path, CancellationToken cancellationToken)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not IOException and not UnauthorizedAccessException)
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }

    public AppPaths Paths { get; }

    /// <summary>
    /// 書き込みの途中で落ちて残った <c>*.tmp</c> を片付ける。**窓を出した後に裏で呼ぶ**（2026-09-24）。
    ///
    /// 本体を残したまま置き換えだけ失敗した物なので、消して困る物は無い。
    /// 直下と items/ だけを見ていた頃は、images/&lt;商品&gt;/ や modifications/ に残った分が永久に消えなかったので、保存先の全体を見る。
    /// 全体を再帰でたどるので、画像が1万枚を超える保存先では窓が出るまでの待ちに乗っていた。
    /// 消すのは10分より古い物だけなので、起動直後に裏の作業が書き始めた分とはぶつからない。
    /// ファイルを消すのは書き込みなので、保存先を運ぶ間の門（書いている数）に入ってから行う
    /// </summary>
    public void SweepStaleTemporaryFilesLater()
        => Task.Run(async () =>
        {
            using var writing = await Core.Storage.StoreWriteGate.EnterAsync();
            var deleted = JsonStore.DeleteStaleTemporaryFiles(Paths.Root, includeSubdirectories: true);
            UiTrace.Write("速さ", $"起動時の片付け：書きかけ {deleted} 件を消した（窓を出した後に裏で）");
        }).Forget();

    public DataStore Store { get; }

    /// <summary>
    /// アバターの登録簿・ユーザタグ・属性の一覧の、ファイルが変わっていなければ読み直さない写し（<see cref="Services.StoreFileCache{T}"/>）。
    /// 商品ページ・編集の1件ごとのように、同じ物を何度も読む所で使う
    /// </summary>
    public Services.StoreFileCache<AvatarRegistry> CachedAvatars => _cachedAvatars ??= new(Store.Avatars);

    /// <inheritdoc cref="CachedAvatars"/>
    public Services.StoreFileCache<UserTagMaster> CachedUserTags => _cachedUserTags ??= new(Store.UserTags);

    /// <inheritdoc cref="CachedAvatars"/>
    public Services.StoreFileCache<AttributeMaster> CachedAttributes => _cachedAttributes ??= new(Store.Attributes);

    private Services.StoreFileCache<AvatarRegistry>? _cachedAvatars;
    private Services.StoreFileCache<UserTagMaster>? _cachedUserTags;
    private Services.StoreFileCache<AttributeMaster>? _cachedAttributes;

    /// <summary>改変の記録を作る・消す・読む</summary>
    public IModificationService Modifications { get; }

    /// <summary>「最近」の足跡を打つ。itemのJSONではなく recent.json に集める</summary>
    public Services.RecentTracker Recent { get; }

    /// <summary>
    /// 商品を開くたびの小さな書き込み（検索の履歴・足跡）を、画面のスレッドの外で順に書く列。
    /// 閉じる前と保存先を運ぶ前に、書き終わりを待つ（<c>MainViewModel.FlushPendingWritesAsync</c>）
    /// </summary>
    public BackgroundWriteQueue BackgroundWrites { get; } = new();

    /// <summary>
    /// 現在の設定（<see cref="SettingsService.Current"/>）。書くと差し替わる。
    /// サービスには値ではなく「今の設定を読む関数」を渡しているので、差し替えはすぐ効く。
    /// サムネイルの保持上限だけは起動時に決まる（読み込み器を作り直すと復号し直しになるため）。
    /// </summary>
    public AppSettings Settings => SettingsStore.Current;

    /// <summary>画面が覚えている状態（ui-state.json）。書くのは UiCommand.ChangeUiState。</summary>
    public UiState UiState => SettingsStore.UiState;

    public IBoothClient Client { get; }

    public ImagePipeline Images { get; }

    public ImportPipeline Import { get; }

    /// <summary>前の取り込みで残った画像を、次の起動で取り直す（梯子の⑤の再開）。</summary>
    public ImageBacklog Backlog { get; }

    /// <summary>持っていないアバターの1枚目を取って置く（U18）。起動時の裏の取得で、⑤の後に回す。</summary>
    public AvatarImageSync AvatarImages { get; }

    /// <summary>監視対象フォルダに新しいファイルが無いかを見る。起動時に走らせてよい唯一の走査。</summary>
    public FolderWatch Watch { get; }

    /// <summary>打った語から同じものの別表記を作る。通信はしない（同梱の辞書だけ）。</summary>
    public SearchBridge Bridge { get; }

    /// <summary>商品名の読みを組み立てる。造語の複合語はここでしか読めない。</summary>
    /// <summary>同梱したBOOTHのカテゴリ表。候補と、親の補完に使う。</summary>
    public Core.Services.CategoryTable Categories { get; } = Core.Services.CategoryTable.Bundled();

    public KanjiReadings KanjiReadings { get; }

    /// <summary>名前の読みの順（検索の並べ替え）。字の表は KanjiReadings と共有する。</summary>
    public Core.Services.NameCollation NameOrder { get; }

    /// <summary>⑦ 期限の来た商品を取り直す。梯子のいちばん下。</summary>
    public DueRefresh Due { get; }

    public ItemService Items { get; }

    public FallbackResolver Resolver { get; }

    public EditService Edit { get; }

    public NotificationService Notifications { get; }

    public UserTagService UserTags { get; }

    public AttributeService Attributes { get; }

    public ShopService Shops { get; }

    public StatsService Stats { get; }

    public AvatarService Avatars { get; }

    public SettingsService SettingsStore { get; }


    /// <summary>
    /// 多重起動のロックを放す。引越しのときだけ使う。
    ///
    /// ロックファイルは保存先の中にあり、握ったままだと元のフォルダを畳みきれない。
    /// 放してから再起動までの短い間だけ二重起動を許すことになるが、
    /// その間ユーザは引越しの確認ダイアログの中にいる。
    /// </summary>
    public void ReleaseInstanceLock()
    {
        _instanceLock?.Dispose();
        _instanceLock = null;
    }

    /// <summary>
    /// 運ぶのに失敗して元の保存先のまま続けるときに、放した錠を取り直す。
    /// 取り直さないと、失敗の後はずっと二重起動を許していた。
    /// </summary>
    public void ReacquireInstanceLock()
    {
        _instanceLock ??= SingleInstanceLock.TryAcquire(Paths);
    }

    public CommandHandler Commands { get; }

    /// <summary>ロックを取れたか。取れていなければ既に別のインスタンスが起動している。</summary>
    public bool IsSingleInstance { get; }

    public void Dispose()
    {
        _httpClient.Dispose();
        _instanceLock?.Dispose();
        _instanceLock = null;
    }
}
