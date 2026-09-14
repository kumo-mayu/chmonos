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
    {
        Paths = AppPaths.Default;
        Paths.EnsureCreated();

        // 裏の作業で黙って飛ばした失敗も、後から追えるように書き残す（技術的負債 2-1）
        Core.Diagnostics.AppLog.Use(new Core.Diagnostics.LogFile(Paths.LogFile));

        _instanceLock = SingleInstanceLock.TryAcquire(Paths);
        IsSingleInstance = _instanceLock is not null;

        Store = new DataStore(Paths);

        // 書き込みの途中で落ちると .tmp が残る。本体を残したまま置き換えだけ失敗した物なので、消して困る物は無い
        JsonStore.DeleteStaleTemporaryFiles(Paths.Root);
        JsonStore.DeleteStaleTemporaryFiles(Paths.ItemsDir);

        // 前回閉じたときに消し残った一時展開（#56）。二重に起動した側が消すと、
        // 先に動いている方がエクスプローラで開いている中身を消してしまうので、1つ目のときだけ
        if (IsSingleInstance)
        {
            new TemporaryUnpacker().CleanUp();
        }
        // 設定を持つのは SettingsService だけ。画面は写しを持たず、書くときは UiCommand.ChangeSettings を通す（技術的負債 1-1）
        SettingsStore = new SettingsService(Store);

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // 設定は値ではなく「今の設定を返すもの」で渡す。値で渡すと、設定画面で保存しても
        // 起動し直すまで効かなかった（画像の長辺・画質・取得の間隔など。SettingsSource に理由）
        Client = new BoothClient(_httpClient, () => Settings);
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

        // 辞書に載っていない造語の読みは、漢字1字ごとの音訓から組み立てる
        KanjiReadings = new KanjiReadings(
            Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz"));
        Due = new DueRefresh(Store, Items);
        Resolver = new FallbackResolver(Client, Bridge, KanjiReadings);
        Edit = new EditService(Store);
        Notifications = new NotificationService(Store, () => Settings);
        UserTags = new UserTagService(Store);
        Attributes = new AttributeService(Store);
        Shops = new ShopService(Store, () => Settings, Client);
        Stats = new StatsService(Store);
        Recent =new Services.RecentTracker(Store);
        Modifications = new ModificationService(Store, Images);
        Commands = new CommandHandler(
            Import, Items, Edit, new UnpackedFolderRemover(DeleteToRecycleBin), Resolver, Notifications, UserTags, Attributes,
            Modifications, Avatars, UnityPackages, SettingsStore, Avatars, Shops, Images, Client);

        // ドラッグで変えた画面の幅（ユーザ判断 2026-09-14）。書くのは UiCommand.ChangeUiState
        PaneWidths = new Services.PaneWidths(SettingsStore, Commands);
    }

    /// <summary>ドラッグで変えられる画面の幅。</summary>
    public Services.PaneWidths PaneWidths { get; }

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

    public DataStore Store { get; }

    /// <summary>改変の記録を作る・消す・読む</summary>
    public IModificationService Modifications { get; }

    /// <summary>「最近」の足跡を打つ。itemのJSONではなく recent.json に集める</summary>
    public Services.RecentTracker Recent { get; }

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
