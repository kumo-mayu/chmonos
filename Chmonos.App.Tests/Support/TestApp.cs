using System.IO;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 試験の中のアプリ1つ分。一時フォルダの保存先と、通信しない作り物の BOOTH で、アプリと同じ組み立て
/// （<see cref="AppServiceContainer"/> と <see cref="MainViewModel"/>）を作る。窓は作らない。
///
/// 使い方は <see cref="Run"/> の1つ：中身は画面のスレッドで走り、終わったら裏の作業を止めて一時フォルダを消す。
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

        var paths = new AppPaths(Path.Combine(root, "store"));
        paths.EnsureCreated();
        SeedKanjiCache(paths);

        // 待たない：相手が作り物なので、1.5秒ずつ空ける意味が無い（空けると一式が分単位になる）。
        // 一時展開の掃除はしない：場所が利用者の一時フォルダで、隣で動いているアプリの分まで消す
        Services = new AppServiceContainer(
            paths,
            Booth,
            (_, _) => Task.CompletedTask,
            cleanUpTemporaryUnpacks: false)
        {
            // 実マシンに Unity Hub・VCC・ALCOM が入っているかで結果を変えない。要る試験が入れ直す
            DetectUnityTools = () => Tools,
            DiscoverUnityProjects = () => UnityProjects,
        };

        // 本物のエクスプローラを開かない（使う人の画面に出る）。渡された道を控え、試験がそれを見る
        Services.RevealInFolder = path =>
        {
            Revealed.Add(path);
            return Task.CompletedTask;
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

    /// <summary>エクスプローラで選んだ状態で開くはずだった道（開きはしない）。</summary>
    public List<string> Revealed { get; } = [];

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
            await SettleAsync();
        }

        return _main;
    }

    /// <summary>
    /// 投げっぱなしの読み込み・保存が済むまで待つ（主画面を作った直後・画面を開いた後・保存を押した後）。
    /// 検索の読み込みも済んでいることを確かめる
    /// </summary>
    public async Task SettleAsync()
    {
        await UiThread.Settle();
        await UiThread.Until(() => !Main.Search.IsLoading, "検索の読み込みが済む");
    }

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
        var app = new TestApp(NewRoot());
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

        var passed = false;
        try
        {
            await body(app);

            // 中身が投げたままの仕事を、ここで済ませる。済ませずに終えると、次の試験の最中に落ちて、次の試験のログに混ざる
            await UiThread.Settle();
            if (!app.AllowLoggedFailures && app.LoggedFailures() is { Count: > 0 } failures)
            {
                Assert.Fail("ログに失敗が残りました：\n" + string.Join("\n", failures));
            }

            passed = true;
        }
        finally
        {
            await app.StopAsync(passed);
        }
    });

    private const string RunRootPrefix = "chmonos-app-test-";

    /// <summary>
    /// この一式（プロセス1つ）の一時フォルダ。試験ごとの保存先はこの下に作る。**消すのは3段**：
    /// 試験の終わりにその試験の分、一式の終わりに残り、次の一式の始めに前の一式の残り。
    ///
    /// 1段では足りなかった（2026-09-30 に数えた）。アプリは待ってから書く物を持つ（検索の条件は、止まってから0.5秒後に書く）ので、
    /// 試験の終わりに消しても、遅れて来た書き込みがフォルダを作り直す（228件の一式で57個残った）。
    /// 一式の終わりにまとめて消すだけにすると、試験を走らせるプロセスが消し終わる前に終わらされ、15回ほどのうち12回で途中まで残った
    /// </summary>
    private static readonly string RunRoot = Path.Combine(
        Path.GetTempPath(), RunRootPrefix + Environment.ProcessId);

    /// <summary>
    /// 前の一式の残りとみなす古さ。一式は1分かからず、走っている間は試験ごとにフォルダを足して日時が新しくなるので、
    /// 10分触られていなければ、隣で走っている別の一式の物ではない
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private static int s_count;

    static TestApp()
    {
        foreach (var old in Directory.EnumerateDirectories(Path.GetTempPath(), RunRootPrefix + "*"))
        {
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(old) > StaleAfter)
            {
                TryDelete(old);
            }
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(RunRoot);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなくても試験は通る（次の一式の始めにもう一度試す）
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(RunRoot, Interlocked.Increment(ref s_count).ToString("000"));
        Directory.CreateDirectory(root);

        // 保存先は引数で渡しているが、画面の側には保存先の出どころを自分で聞く所がある（設定の画面など）。
        // 環境変数にも入れておくと、そこが本番の location.json を読みに行かない（指定が無いと StoreLocation が例外で止める）。
        // 試験は並べて走らせないので、プロセスに1つの環境変数で足りる
        Environment.SetEnvironmentVariable(AppPaths.RootVariable, root);
        return root;
    }

    /// <summary>
    /// 漢字の読みの表の控え。最初の試験が組んだ物を、後の試験の保存先へ写す。
    ///
    /// 検索の読み込みは商品名の読みを作るのにこの表を使い、控えが無ければ同梱の辞書から組む。
    /// 控えは保存先の中に置かれるので、保存先を試験ごとに作ると毎回組み直しになり、一式の時間のほとんどがこれだった
    /// （2026-09-30 に測った：主画面を作って検索が済むまで 171〜216ms → 写した控えがあれば 20〜38ms）。
    /// 控えは元の辞書の大きさと日時で確かめてから使われるので、写しても古い物が使われることは無い
    /// </summary>
    private static readonly string SharedKanjiCache = Path.Combine(RunRoot, "kanji-readings.cache");

    private static void SeedKanjiCache(AppPaths paths)
    {
        if (File.Exists(SharedKanjiCache))
        {
            File.Copy(SharedKanjiCache, paths.KanjiReadingsCacheFile, overwrite: true);
        }
    }

    /// <summary>組んだ控えを、次の試験のために取っておく。</summary>
    private void KeepKanjiCache()
    {
        var built = Services.Paths.KanjiReadingsCacheFile;
        try
        {
            if (File.Exists(built) && !File.Exists(SharedKanjiCache))
            {
                File.Copy(built, SharedKanjiCache, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 写せなくても試験は通る（次の試験が組み直すだけ）
        }
    }

    private async Task StopAsync(bool passed)
    {
        try
        {
            if (_main is { } main)
            {
                main.StopBackgroundWork();
                await main.FlushPendingWritesAsync();
            }

            // 止めた・書き切ったことで投げられた分も済ませてから、通信の出口と錠を放す
            await UiThread.Settle();
        }
        catch (TimeoutException) when (!passed)
        {
            // 中身が落ちた試験では、そちらの失敗をそのまま出す（後始末の待ちの失敗で上書きしない）
        }
        finally
        {
            // アプリ全体に効く静的な状態を、次の試験へ持ち越さない
            Notice.Intercept = null;
            AppLog.Use(null);
            UnityHandoff.UsePathStore(null);
            Services.Dispose();
            KeepKanjiCache();
            TryDelete(Root);
        }
    }
}

