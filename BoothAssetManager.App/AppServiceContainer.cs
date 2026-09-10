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

        _instanceLock = SingleInstanceLock.TryAcquire(Paths);
        IsSingleInstance = _instanceLock is not null;

        Store = new DataStore(Paths);
        Settings = Store.Settings.Load();

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Client = new BoothClient(_httpClient, Settings);
        Images = new ImagePipeline(Client, Paths, Settings);

        // 検出は梯子の③なので、取り込みより先に組み立てる
        Avatars = new AvatarService(Store, Settings, Client);
        Import = new ImportPipeline(Store, Client, Images, Settings, Avatars);
        Items = new ItemService(Store, Client, Images, Settings);
        Backlog = new ImageBacklog(Store, Images);
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
        Notifications = new NotificationService(Store, Settings);
        UserTags = new UserTagService(Store);
        Attributes = new AttributeService(Store);
        Shops = new ShopService(Store, Settings, Client);
        Stats = new StatsService(Store);
        SettingsStore = new SettingsService(Store);
        Recent = new Services.RecentTracker(Store);
        Commands = new CommandHandler(
            Import, Items, Edit, new UnpackedFolderRemover(DeleteToRecycleBin), Resolver, Notifications, UserTags, Attributes);
    }

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

    /// <summary>「最近」の足跡を打つ。itemのJSONではなく recent.json に集める</summary>
    public Services.RecentTracker Recent { get; }

    /// <summary>
    /// 現在の設定。設定画面から差し替わる。
    /// 起動時にサービスへ渡した値（通信間隔・画像の解像度など）は次の起動から効く。
    /// </summary>
    public AppSettings Settings { get; private set; }

    public IBoothClient Client { get; }

    public ImagePipeline Images { get; }

    public ImportPipeline Import { get; }

    /// <summary>前の取り込みで残った画像を、次の起動で取り直す（梯子の⑤の再開）。</summary>
    public ImageBacklog Backlog { get; }

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

    /// <summary>設定画面が保存した内容に差し替える。以後に作る画面はこちらを読む。</summary>
    public void ReplaceSettings(AppSettings settings) => Settings = settings;

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
