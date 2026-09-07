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
        Commands = new CommandHandler(Import, Items);
    }

    public AppPaths Paths { get; }

    public DataStore Store { get; }

    public AppSettings Settings { get; }

    public IBoothClient Client { get; }

    public ImagePipeline Images { get; }

    public ImportPipeline Import { get; }

    public ItemService Items { get; }

    public FallbackResolver Resolver { get; }

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
