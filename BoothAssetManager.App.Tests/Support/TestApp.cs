using System.IO;
using System.Windows;
using BoothAssetManager.App.Services;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Diagnostics;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App.Tests.Support;

/// <summary>
/// 試験の中のアプリ1つ分。一時フォルダの保存先と、通信しない作り物の BOOTH で、アプリと同じ組み立て
/// （<see cref="AppServiceContainer"/> と <see cref="MainViewModel"/>）を作る。窓は作らない。
///
/// 使い方は <see cref="Run"/> の1つ：中身は画面のスレッドで走り、終わったら止めて一時フォルダを消す。
/// <code>
/// [Fact]
/// public Task 何々() => TestApp.Run(async app =>
/// {
///     await app.AddItemAsync(Make.Item("1000001", "作り物の商品"));   // 先に保存先へ置く
///     var main = await app.StartAsync();                               // 主画面を作り、検索の読み込みを待つ
///     Assert.Equal(1, main.Search.TotalCount);
/// });
/// </code>
/// </summary>
internal sealed class TestApp
{
    private MainViewModel? _main;

    private TestApp(string root)
    {
        Root = root;
        Booth = new FakeBooth();

        // 待たない：相手が作り物なので、1.5秒ずつ空ける意味が無い（空けると一式が分単位になる）。
        // 一時展開の掃除はしない：場所が利用者の一時フォルダで、隣で動いているアプリの分まで消す
        Services = new AppServiceContainer(
            new AppPaths(Path.Combine(root, "store")),
            Booth,
            (_, _) => Task.CompletedTask,
            cleanUpTemporaryUnpacks: false)
        {
            // 実マシンに Unity Hub・VCC・ALCOM が入っているかで結果を変えない。要る試験が入れ直す
            DetectUnityTools = () => Tools,
            DiscoverUnityProjects = () => UnityProjects,
        };
    }

    /// <summary>試験1つ分の一時フォルダ。保存先は <c>store</c>、取り込むファイルは <see cref="NewFile"/> で <c>files</c> に作る。</summary>
    public string Root { get; }

    public AppServiceContainer Services { get; }

    public DataStore Store => Services.Store;

    /// <summary>作り物の BOOTH。何も教えなければ、どの問い合わせにも「無い」（404）と答える。</summary>
    public FakeBooth Booth { get; }

    /// <summary>Unity Hub・VCC・ALCOM が手元にあるか（作り物）。既定はどれも無い。</summary>
    public UnityTools Tools { get; set; } = new(HasHub: false, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false);

    /// <summary>Unity Hub・VCC の一覧にあるプロジェクト（作り物）。既定は空。</summary>
    public IReadOnlyList<UnityProjectCandidate> UnityProjects { get; set; } = [];

    /// <summary>出すはずだった知らせ・確認の窓（窓は出ない）。文言を確かめるのに使う。</summary>
    public List<NoticeRequest> Notices { get; } = [];

    /// <summary>
    /// 確認の窓への答え。既定は、その窓の「やめる」側（OK だけの窓は OK）——
    /// 試験が答えを決めていない確認を「はい」で進めると、消す・上書きするが黙って走る
    /// </summary>
    public Func<NoticeRequest, MessageBoxResult> Answer { get; set; } = request => request.Button switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.Cancel,
    };

    /// <summary>
    /// ログに「失敗」が残っても試験を落とさない。投げっぱなしの仕事（<c>Forget()</c>）の失敗はログにしか出ないので、
    /// 既定では残っていたら落とす。失敗する道そのものを確かめる試験だけが true にする
    /// </summary>
    public bool AllowLoggedFailures { get; set; }

    /// <summary>主画面。<see cref="StartAsync"/> の後で使う。</summary>
    public MainViewModel Main => _main ?? throw new InvalidOperationException("先に StartAsync を呼んでください。");

    /// <summary>
    /// 主画面を作り、検索の最初の読み込みが済むまで待つ。**商品・設定は、この前に保存先へ置く**
    /// （主画面は作るときに保存先を読んで、どの画面から始めるかを決める）
    /// </summary>
    public async Task<MainViewModel> StartAsync()
    {
        if (_main is null)
        {
            _main = new MainViewModel(Services);
            await SearchSettledAsync();
        }

        return _main;
    }

    /// <summary>検索の読み込みが済むまで待つ（主画面を作った直後・読み直しを投げた後）。</summary>
    public Task SearchSettledAsync() => UiThread.Until(() => !Main.Search.IsLoading, "検索の読み込みが済む");

    /// <summary>設定を変える（アプリと同じ道：錠の中で今の値に当てる）。</summary>
    public Task ChangeSettingsAsync(Func<AppSettings, AppSettings> change) => Services.SettingsStore.UpdateAsync(change);

    /// <summary>商品を保存先へ置く。主画面を作った後なら、検索の写しは <see cref="SearchViewModel.ReloadAsync"/> まで変わらない。</summary>
    public Task AddItemAsync(ItemRecord item) => Store.Items.SaveAsync(item);

    /// <summary>手元のファイルを作る（商品のファイル・取り込む物）。返すのは絶対パス。</summary>
    public string NewFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(Root, "files", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [1, 2, 3]);
        return path;
    }

    /// <summary>ログに残った「失敗」の行（見出しの行だけ）。</summary>
    public IReadOnlyList<string> LoggedFailures()
    {
        var path = Services.Paths.LogFile;
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Contains(" 失敗 [", StringComparison.Ordinal)).ToList()
            : [];
    }

    /// <summary>
    /// 試験の中身を、画面のスレッドで、作りたてのアプリ1つに対して走らせる。
    /// 中身が通った後にログの「失敗」も見る（中身が落ちたときは、そちらの失敗をそのまま出す）
    /// </summary>
    public static Task Run(Func<TestApp, Task> body) => UiThread.Run(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-app-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = new TestApp(root);
        Notice.Intercept = request =>
        {
            app.Notices.Add(request);
            return app.Answer(request);
        };

        // 裏の取得は既定で切る：作り物の BOOTH は「無い」としか答えないので、走らせると
        // 置いた商品に「BOOTHで見つからない」の印が付いて、確かめたい物と関係なく中身が変わる
        await app.ChangeSettingsAsync(settings => settings with { ResumeFetchInBackground = false, SaveImages = false });

        // 検索の条件は空から始める：条件を1つも積んでいなければ、置いた商品が全部並ぶ。
        // 既定の条件（保存された並びが無いときに出す3つ）のままにすると、既定を変えるたびに、絞り込みと関係の無い試験まで結果が変わる。
        // 既定の条件そのものを確かめる試験は、null に戻してから始める（SearchDefaultsTests）
        await app.Services.SettingsStore.UpdateUiStateAsync(state => state with { SearchModules = [] });

        try
        {
            await body(app);

            if (!app.AllowLoggedFailures && app.LoggedFailures() is { Count: > 0 } failures)
            {
                Assert.Fail("ログに失敗が残りました：\n" + string.Join("\n", failures));
            }
        }
        finally
        {
            await app.StopAsync();
        }
    });

    private async Task StopAsync()
    {
        try
        {
            if (_main is { } main)
            {
                main.StopBackgroundWork();
                await main.FlushPendingWritesAsync();
            }
        }
        finally
        {
            // アプリ全体に効く静的な状態を、次の試験へ持ち越さない
            Notice.Intercept = null;
            AppLog.Use(null);
            UnityHandoff.UsePathStore(null);
            Services.Dispose();

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 裏の仕事がまだ握っていることがある。一時フォルダなので残しても害は無い
            }
        }
    }
}
