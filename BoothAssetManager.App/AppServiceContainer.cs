using System.IO;
using System.Net.Http;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
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
        Import = new ImportPipeline(Store, Client, Images, Settings);
        Items = new ItemService(Store, Client, Images, Settings);
        Resolver = new FallbackResolver(Client);
        Edit = new EditService(Store);
        Notifications = new NotificationService(Store, Settings);
        AppTags = new AppTagService(Store);
        Attributes = new AttributeService(Store);
        Shops = new ShopService(Store, Settings);
        Commands = new CommandHandler(
            Import, Items, Edit, new UnpackedFolderRemover(DeleteToRecycleBin), Resolver, Notifications, AppTags, Attributes);
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

    public AppSettings Settings { get; }

    public IBoothClient Client { get; }

    public ImagePipeline Images { get; }

    public ImportPipeline Import { get; }

    public ItemService Items { get; }

    public FallbackResolver Resolver { get; }

    public EditService Edit { get; }

    public NotificationService Notifications { get; }

    public AppTagService AppTags { get; }

    public AttributeService Attributes { get; }

    public ShopService Shops { get; }

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
