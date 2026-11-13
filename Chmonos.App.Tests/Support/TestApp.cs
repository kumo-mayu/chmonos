using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Search;
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

        // 待たない：相手が作り物なので、1.5秒ずつ空ける意味が無い（空けると一式が分単位になる）。
        // 一時展開の掃除はしない：場所が利用者の一時フォルダで、隣で動いているアプリの分まで消す。
        // 漢字の読みの表は一式で使い回す（SharedKanjiReadings）
        Services = new AppServiceContainer(
            paths,
            Booth,
            (_, _) => Task.CompletedTask,
            cleanUpTemporaryUnpacks: false,
            kanjiReadings: SharedKanjiReadings.Value,
            searchBridge: SharedSearchBridge.Value)
        {
            // 実マシンに Unity Hub・VCC・ALCOM が入っているかで結果を変えない。要る試験が入れ直す
            DetectUnityTools = () => Tools,
            DiscoverUnityProjects = () => UnityProjects,
            OpenUnityEditors = () => OpenEditors,
        };

        // 開いている間の監視（plan 49）：本物の Windows の見張りを張らず、待ちもしない（出来事は試験が FolderEvents で起こす）。
        // 書き終わったかは本物で見る（試験のファイルは書き終えて閉じてあるので、答えは決まっている）
        Services.CreateLiveFolderWatch = watch => new Chmonos.Core.Scanning.LiveFolderWatch(
            FolderEvents.Create,
            watch.FindNewAsync,
            new Chmonos.Core.Scanning.FileSettleProbe(),
            (_, token) => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask,
            _ => true);

        // 幅・高さを書くまでの 0.4 秒を、試験では待たない（変える試験が1件ごとに 0.4 秒延びていた）
        Services.PaneWidths.SaveDelay = TimeSpan.Zero;

        // 本物のエクスプローラを開かない（使う人の画面に出る）。渡された道を控え、試験がそれを見る
        Services.RevealInFolder = path =>
        {
            Revealed.Add(path);
            return Task.CompletedTask;
        };

        // GitHub へ新しい版を聞きに行かない。試験が入れた版を返す
        Services.FetchLatestVersion = _ => Task.FromResult(LatestVersion);

        // 本物のブラウザを開かない。開いた先を控える
        Services.OpenUrl = url =>
        {
            OpenedUrls.Add(url);
            return true;
        };

        // 本物のクリップボードを書き換えない（使う人が写していた物が消える）。写した文字を控える
        Services.CopyText = text =>
        {
            Copied.Add(text);
            return true;
        };
    }

    /// <summary>試験1つ分の一時フォルダ。保存先は <c>store</c>、取り込むファイルは <see cref="NewFile"/> で <c>files</c> に作る。</summary>
    public string Root { get; }

    public AppServiceContainer Services { get; }

    public DataStore Store => Services.Store;

    /// <summary>開いている間の監視の作り物の見張り（plan 49）。</summary>
    public FakeFolderEventsHub FolderEvents { get; } = new();

    /// <summary>作り物の BOOTH。何も教えなければ、どの問い合わせにも「無い」（404）と答える。</summary>
    public FakeBooth Booth { get; }

    /// <summary>Unity Hub・VCC・ALCOM が手元にあるか（作り物）。既定はどれも無い。</summary>
    public UnityTools Tools { get; set; } = new(HasHub: false, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false);

    /// <summary>Unity Hub・VCC の一覧にあるプロジェクト（作り物）。既定は空。</summary>
    public IReadOnlyList<UnityProjectCandidate> UnityProjects { get; set; } = [];

    /// <summary>開いている Unity（既定は無し。実マシンで Unity が動いているかで結果を変えない）。</summary>
    public IReadOnlyList<Chmonos.App.Services.OpenUnityEditor> OpenEditors { get; set; } = [];

    /// <summary>エクスプローラで選んだ状態で開くはずだった道（開きはしない）。</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>クリップボードへ写すはずだった文字（写しはしない）。</summary>
    public List<string> Copied { get; } = [];

    /// <summary>新しい版の確認で返す版（tag の名前）。null は聞けなかったとき。</summary>
    public string? LatestVersion { get; set; }

    /// <summary>開いたページ。</summary>
    public List<string> OpenedUrls { get; } = [];

    /// <summary>出すはずだった知らせ・確認の窓（窓は出ない）。文言を確かめるのに使う。</summary>
    public List<NoticeRequest> Notices { get; } = [];

    /// <summary>出すはずだった Windows の通知（右下には出ない）。</summary>
    public List<ToastRequest> Toasts { get; } = [];

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
    /// 手で改変に足すときの「使ったファイル」の窓への答え（窓は出ない）。既定は、窓が最初に選んでいた物のまま「追加」。
    /// 選び直す試験は、ここで <see cref="MemberFileChoice.Selected"/> を変えてから true を返す
    /// </summary>
    public Func<MemberFilePickViewModel, bool> PickFiles { get; set; } = _ => true;

    /// <summary>出すはずだった「使ったファイル」の窓（窓は出ない）。</summary>
    public List<MemberFilePickViewModel> FilePicks { get; } = [];

    /// <summary>
    /// 送る unitypackage を選ぶ窓への答え（窓は出ない）。既定は「キャンセル」（答えを決めていない試験で、黙って送らない）。
    /// 選ぶ試験は、ここで行の <see cref="PackageChoiceRow.IsChecked"/> を変えてから true を返す
    /// </summary>
    public Func<PickPackagesDialogViewModel, bool> PickPackages { get; set; } = _ => false;

    /// <summary>出すはずだった、送る unitypackage を選ぶ窓（窓は出ない）。</summary>
    public List<PickPackagesDialogViewModel> PackagePicks { get; } = [];

    /// <summary>
    /// 「見つからないファイルを探す」の、どこを探すかの窓への答え（窓は出ない）。既定は、監視フォルダのまま「探す」。
    /// ほかの場所を足す試験は、ここで <see cref="MissingSearchScopeViewModel.AddFolders"/> を呼んでから true を返す
    /// </summary>
    public Func<MissingSearchScopeViewModel, bool> PickSearchScope { get; set; } = _ => true;

    /// <summary>
    /// コピーした商品をまとめて登録する窓への答え（窓は出ない）。既定は「キャンセル」（答えを決めていない試験で、黙って BOOTH に問い合わせない）。
    /// 選び直す試験は、ここで行の <see cref="PastedItemRow.IsChecked"/> を変えてから true を返す
    /// </summary>
    public Func<RegisterPastedItemsDialogViewModel, bool> AnswerPastedItems { get; set; } = _ => false;

    /// <summary>出すはずだった、まとめて登録する窓。</summary>
    public List<RegisterPastedItemsDialogViewModel> PastedItemDialogs { get; } = [];

    /// <summary>「すべての設定を既定に戻す」の窓への答え（窓は出ない）。既定は「キャンセル」。チェックは窓の既定のまま渡る。</summary>
    public Func<ResetSettingsDialogViewModel, bool> AnswerResetSettings { get; set; } = _ => false;

    /// <summary>出すはずだった、既定に戻す窓（窓は出ない）。</summary>
    public List<ResetSettingsDialogViewModel> ResetSettingsDialogs { get; } = [];

    /// <summary>出すはずだったサブ画面（ファイルの割り振り）の窓。窓は作らず、前に出した回数と閉じるを控える。</summary>
    public List<FakeSubWindow> SubWindows { get; } = [];

    /// <summary>サブ画面の窓の作り物。<see cref="Close"/> で窓の × と同じに閉じる。</summary>
    public sealed class FakeSubWindow(FilePoolViewModel model) : ISubWindowHandle
    {
        public FilePoolViewModel Model { get; } = model;

        public int BroughtToFront { get; private set; }

        public bool IsClosed { get; private set; }

        public void BringToFront() => BroughtToFront++;

        public void Close()
        {
            IsClosed = true;
            Model.NoteWindowClosed();
        }
    }

    /// <summary>出すはずだった、どこを探すかの窓（窓は出ない）。</summary>
    public List<MissingSearchScopeViewModel> SearchScopes { get; } = [];

    /// <summary>「IDを変える」の窓への答え（窓は出ない）。既定は「キャンセル」。</summary>
    public Func<ChangeItemIdDialogViewModel, bool> AnswerChangeId { get; set; } = _ => false;

    /// <summary>出すはずだった「IDを変える」の窓。</summary>
    public List<ChangeItemIdDialogViewModel> ChangeIdDialogs { get; } = [];

    /// <summary>選ぶ窓（<see cref="ChoiceQuestion"/>）への答え（窓は出ない）。既定はキャンセル——答えを決めていない問いで、書き換えが黙って走らないように</summary>
    public Func<ChoiceRequest, Views.ChoiceDialogResult> Choose { get; set; } = _ => Views.ChoiceDialogResult.Cancel;

    /// <summary>出すはずだった選ぶ窓（窓は出ない）。</summary>
    public List<ChoiceRequest> Choices { get; } = [];

    /// <summary>
    /// 「ファイルを選ぶ」窓への答え（窓は出ない）。既定は何も選ばずに閉じた（null）。
    /// </summary>
    public Func<IReadOnlyList<string>?> PickAttachFiles { get; set; } = () => null;

    /// <summary>
    /// 改変の画面の「場所を付け替える…」のフォルダを選ぶ窓への答え（窓は出ない）。引数は古い場所。既定は何も選ばずに閉じた（null）。
    /// </summary>
    public Func<string, string?> PickProjectFolder { get; set; } = _ => null;

    /// <summary>
    /// ログに「失敗」が残っても試験を落とさない。投げっぱなしの仕事（<c>Forget()</c>）の失敗はログにしか出ないので、
    /// 既定では残っていたら落とす。失敗する道そのものを確かめる試験だけが true にする
    /// </summary>
    public bool AllowLoggedFailures { get; set; }

    /// <summary>主画面。<see cref="StartAsync"/> の後で使う。</summary>
    public MainViewModel Main => _main ?? throw new InvalidOperationException("先に StartAsync を呼んでください。");

    /// <summary>
    /// 主画面を作り、最初の一覧が出るまでだけ待つ。起動のときに裏の作業を止めておいて、その間の見え方を確かめる試験用
    /// （<see cref="StartAsync"/> は裏の作業が全部済むまで待つので、止めたままだと終わらない）。
    /// </summary>
    public async Task<MainViewModel> StartWhileWorkRunsAsync()
    {
        _main ??= new MainViewModel(Services) { CountsInterval = TimeSpan.Zero };
        await UiThread.Until(() => !_main.Search.IsLoading, "検索の読み込みが済む");
        return _main;
    }

    /// <summary>
    /// 主画面を作り、検索の最初の読み込みが済むまで待つ。**商品・設定は、この前に保存先へ置く**
    /// （主画面は作るときに保存先を読んで、どの画面から始めるかを決める）
    /// </summary>
    public async Task<MainViewModel> StartAsync()
    {
        if (_main is null)
        {
            // ナビの数え直しをまとめる1秒を、試験では待たない（数を待つ試験が1件ごとに1秒延びていた）
            _main = new MainViewModel(Services) { CountsInterval = TimeSpan.Zero };
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
    public static Task Run(
        Func<TestApp, Task> body,
        [CallerMemberName] string test = "",
        [CallerFilePath] string file = "") => UiThread.Run(async () =>
    {
        var app = new TestApp(NewRoot());

        // 差し替えと初期設定の保存も、後始末を保証する範囲の中で行う（点検25：初期設定の保存が落ちると
        // StopAsync を通らず、差し替えとサービスが次の試験へ残った）
        var passed = false;
        try
        {
            Notice.Intercept = request =>
            {
                app.Notices.Add(request);
                return app.Answer(request);
            };
            WindowsToast.Intercept = app.Toasts.Add;
            RegisterPastedItemsDialogViewModel.Intercept = model =>
            {
                app.PastedItemDialogs.Add(model);
                return app.AnswerPastedItems(model);
            };
            PickPackagesDialogViewModel.Intercept = model =>
            {
                app.PackagePicks.Add(model);
                return app.PickPackages(model);
            };
            MemberFilePickViewModel.Intercept = model =>
            {
                app.FilePicks.Add(model);
                return app.PickFiles(model);
            };
            MissingSearchScopeViewModel.Intercept = model =>
            {
                app.SearchScopes.Add(model);
                return app.PickSearchScope(model);
            };
            ResetSettingsDialogViewModel.Intercept = model =>
            {
                app.ResetSettingsDialogs.Add(model);
                return app.AnswerResetSettings(model);
            };
            FilePoolViewModel.OpenIntercept = model =>
            {
                var window = new FakeSubWindow(model);
                app.SubWindows.Add(window);
                return window;
            };
            ChoiceQuestion.Intercept = request =>
            {
                app.Choices.Add(request);
                return app.Choose(request);
            };
            ItemViewModel.PickFilesToAttachIntercept = () => app.PickAttachFiles();
            ModificationHubViewModel.PickFolderIntercept = from => app.PickProjectFolder(from);
            ChangeItemIdDialogViewModel.Intercept = model =>
            {
                app.ChangeIdDialogs.Add(model);
                return app.AnswerChangeId(model);
            };

            // 裏の取得は既定で切る：作り物の BOOTH は「無い」としか答えないので、走らせると
            // 置いた商品に「BOOTHで見つからない」の印が付いて、確かめたい物と関係なく中身が変わる
            await app.ChangeSettingsAsync(settings => settings with { ResumeFetchInBackground = false, SaveImages = false });

            // 検索の条件は空から始める：条件を1つも積んでいなければ、置いた商品が全部並ぶ。
            // 既定の条件（保存された並びが無いときに出す3つ）のままにすると、既定を変えるたびに、絞り込みと関係の無い試験まで結果が変わる。
            // 既定の条件そのものを確かめる試験は、null に戻してから始める（SearchDefaultsTests）
            await app.Services.SettingsStore.UpdateUiStateAsync(state => state with { SearchModules = [] });

            await body(app);

            // 中身が投げたままの仕事を、ここで済ませる。済ませずに終えると、次の試験の最中に落ちて、次の試験のログに混ざる
            await UiThread.Settle();
            if (!app.AllowLoggedFailures && app.LoggedFailures() is { Count: > 0 } failures)
            {
                // 見捨てた仕事が後で落ちると、この試験のログに混ざり得るので、その印を添える
                var abandoned = UiThread.Abandoned.Note is { } note ? "\n" + note : "";
                Assert.Fail("ログに失敗が残りました：\n" + string.Join("\n", failures) + abandoned);
            }

            passed = true;
        }
        finally
        {
            await app.StopAsync(passed, $"{Path.GetFileNameWithoutExtension(file)}.{test}");
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

        // 全件の読み込みの後などに頼む詰め直し（止めて GC する）を、試験では走らせない（MemoryTrim.Enabled に理由）
        MemoryTrim.Enabled = false;

        // 保存の待ちを切る（JsonStore.FlushToDisk・WaitBeforeRetry に理由）。保存先は使い捨てで、電源断から守る物が無い。
        // MemoryTrim と同じくプロセスに効くので、TestApp を使わない試験も切れたまま走る（どの試験も待ちの長さを確かめていない）
        Chmonos.Core.Storage.JsonStore.FlushToDisk = false;
        Chmonos.Core.Storage.JsonStore.WaitBeforeRetry = false;
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
    /// 漢字の読みの表。一式（プロセス1つ）で1つを組み、全部の試験のアプリに渡す。
    ///
    /// 検索の読み込みは商品名の読みを作るのにこの表を使う。保存先ごとに持たせると、試験のたびに組み直しになる：
    /// 同梱の辞書から組むと1回 約0.3秒（2026-09-30。一式の時間のほとんどがこれだった）、
    /// 控えを保存先へ写して読んでも1回 約16ms・4MB（2026-10-05：商品を置く試験 361件で 5.8秒、割り当ての大半）。
    /// 表は読んだ後に書き換えないので、保存先が違っても同じ物を使える。
    /// 控えは持たせない：組むのは一式で1回だけで、控えを読むのと差が無い
    /// </summary>
    private static readonly Lazy<KanjiReadings> SharedKanjiReadings = new(
        () => new KanjiReadings(Path.Combine(AppContext.BaseDirectory, "assets", "kanjidic2.xml.gz")));

    /// <summary>
    /// 辞書の橋渡し（JMdict の索引）。一式で使い回し、控えは一時フォルダに残して次の回から読むだけにする——
    /// 試験ごとの保存先に組むと、英語の語を引く試験が1件 約3秒かかっていた（2026-10-14）。
    /// 控えの名前に辞書の大きさと更新日時を入れ、辞書が変われば別の控えを組む
    /// </summary>
    private static readonly Lazy<SearchBridge> SharedSearchBridge = new(() =>
    {
        var dictionary = new FileInfo(Path.Combine(AppContext.BaseDirectory, "assets", "JMdict_e.gz"));
        var stamp = dictionary.Exists ? $"{dictionary.Length}-{dictionary.LastWriteTimeUtc.Ticks}" : "none";
        var cache = Path.Combine(Path.GetTempPath(), "chmonos-app-tests", $"search-bridge-{stamp}.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        return new SearchBridge(new JapaneseDictionary(dictionary.FullName, cache));
    });

    private async Task StopAsync(bool passed, string test)
    {
        try
        {
            if (_main is { } main)
            {
                main.StopBackgroundWork();
                await main.FlushPendingWritesAsync();
            }
        }
        catch (TimeoutException) when (!passed)
        {
            // 中身が落ちた試験では、そちらの失敗をそのまま出す（後始末の待ちの失敗で上書きしない）
        }

        try
        {
            // 止めた・書き切ったことで投げられた分も済ませてから、通信の出口と錠を放す
            await UiThread.Settle();
        }
        catch (TimeoutException) when (!passed)
        {
            // 落ちた試験が裏の仕事を止めたまま終わると、その仕事はもう済まない。待ち続けると後の試験が全部
            // 起動と後片付けで10秒ずつ待って落ち、一式が何時間も止まった（2026-11-11・11-12）。
            // 残りを見捨てて後の試験に待たせない。この試験自体はもう落ちているので、失敗は隠れない
            UiThread.AbandonPending(test);
        }
        finally
        {
            // アプリ全体に効く静的な状態を、次の試験へ持ち越さない
            Notice.Intercept = null;
            WindowsToast.Intercept = null;
            PickPackagesDialogViewModel.Intercept = null;
            RegisterPastedItemsDialogViewModel.Intercept = null;
            MemberFilePickViewModel.Intercept = null;
            MissingSearchScopeViewModel.Intercept = null;
            ChangeItemIdDialogViewModel.Intercept = null;
            // Run で差し替えた口は全部ここで戻す（点検24：この2つだけ戻しておらず、TestApp を使わない試験が
            // 同じ入口を呼ぶと前の試験の答えを使い、走らせる順で結果が変わり得た）
            ChoiceQuestion.Intercept = null;
            ResetSettingsDialogViewModel.Intercept = null;

            // 開いたままのサブ画面は閉じる（置き場の知らせを外す。次の試験へ持ち越さない）
            foreach (var window in SubWindows.Where(window => !window.IsClosed))
            {
                window.Close();
            }

            FilePoolViewModel.OpenIntercept = null;
            ItemViewModel.PickFilesToAttachIntercept = null;
            ModificationHubViewModel.PickFolderIntercept = null;
            AppLog.Use(null);
            UnityHandoff.UsePathStore(null);
            Services.Dispose();
            TryDelete(Root);
        }
    }
}

