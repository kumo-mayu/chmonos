using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 未確定の画面の「このIDで登録」の順番待ち（ユーザ判断 2026-10-05 メモ60）。
///
/// 列は主画面が持ち、1件ずつ流す。BOOTH は作り物（<see cref="FakeBooth"/>）で、答えを止めて「登録の途中」を作る。
/// 時計には頼らない（見込みの時間は、数と間隔から出す関数を値で確かめる）。
/// </summary>
public class RegistrationQueueTests
{
    private const string ItemA = "9900601";
    private const string ItemB = "9900602";

    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveAsync(TestApp app, params string[] names)
    {
        await app.Store.Unresolved.SaveAsync([.. names.Select(name => Unresolved(app.NewFile(name)))]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        return (main, resolve);
    }

    private static UnresolvedRow Row(ResolveViewModel resolve, string fileName)
        => resolve.Files.Single(row => row.FileName == fileName);

    private static async Task PreviewAsync(TestApp app, ResolveViewModel resolve, string fileName, string itemId)
    {
        resolve.Selected = Row(resolve, fileName);
        resolve.ItemIdInput = itemId;
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(resolve.HasPreview);
    }

    private static bool IsPageOf(string url, string itemId) => url.EndsWith($"/items/{itemId}", StringComparison.Ordinal);

    // ---- 1件ずつ順に ----

    [Fact]
    public Task 二件を並べると_BOOTHへの問い合わせは交互にならず_一件ずつ順に終わる() => TestApp.Run(async app =>
    {
        // 画像の枚数があると、1件の登録の中に問い合わせが並ぶ（商品JSON・商品ページ・1枚目）
        await app.ChangeSettingsAsync(settings => settings with { SaveImages = true });
        app.Booth.HasItem(ItemA, "作り物の衣装", images: 2);
        app.Booth.HasItem(ItemB, "作り物の靴");
        var (main, first) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");

        // 2つ目の画面（フォルダビューの右と同じ組み込み）で、もう1件を確かめておく。答えを止めている間は確かめも門で待つため
        var second = new ResolveViewModel(app.Services, main);
        await app.SettleAsync();
        await PreviewAsync(app, first, "first.zip", ItemA);
        await PreviewAsync(app, second, "second.zip", ItemB);
        var before = app.Booth.Requests.Count;

        // A の商品ページで止める。止まっている間に B を積む
        app.Booth.Hold(url => IsPageOf(url, ItemA));
        try
        {
            first.AssignCommand.Execute(null);
            await UiThread.Until(() => app.Booth.Requests.Any(url => IsPageOf(url, ItemA)), "A の商品ページを問い合わせる");

            second.AssignCommand.Execute(null);

            // B は待つ（始まっていない）。行の札は順番
            var jobB = main.Registrations.JobFor(Row(second, "second.zip").File.Hash);
            Assert.NotNull(jobB);
            Assert.False(jobB!.IsRunning);
            Assert.Equal("登録待ち 1番目", Row(second, "second.zip").QueueBadge);
            Assert.Equal("登録中", Row(second, "first.zip").QueueBadge);
            Assert.Equal("「作り物の靴」として登録します。", Row(second, "second.zip").QueueTip);
            Assert.Equal("「作り物の衣装」として登録しています。", Row(second, "first.zip").QueueTip);
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();

        // A の登録（商品JSON・商品ページ・1枚目）が済んでから B の商品JSONへ（交互なら、A の登録の間に B の問い合わせが挟まる）。
        // A の2枚目は登録の後に⑤の段で裏に頼むので、登録の列の外（メモ60 案B）。B と門で居合わせた順に出るので、並びの中の場所は見ない
        var registering = app.Booth.Requests.Skip(before).ToList();
        var gallery = FakeBooth.ImageUrl(ItemA, 2);
        Assert.Equal(
            [
                $"https://booth.pm/ja/items/{ItemA}.json",
                $"https://booth.pm/ja/items/{ItemA}",
                FakeBooth.ImageUrl(ItemA, 1),
                $"https://booth.pm/ja/items/{ItemB}.json",
                $"https://booth.pm/ja/items/{ItemB}",
            ],
            registering.Where(url => url != gallery));
        Assert.Single(registering, gallery);
        Assert.True(registering.IndexOf(FakeBooth.ImageUrl(ItemA, 1)) < registering.IndexOf(gallery));
        Assert.NotNull(await app.Store.Items.LoadAsync(ItemA));
        Assert.NotNull(await app.Store.Items.LoadAsync(ItemB));
        Assert.Empty(app.Store.Unresolved.Load());
        Assert.False(main.Registrations.HasJobs);
        second.OnLeaving();
    });

    // ---- 待っている行 ----

    [Fact]
    public Task 待っている行は_開始までと所要の見込みを出し_登録と除外は押せず_ほかの行は押せる() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { SaveImages = true, FetchIntervalMs = 30000 });
        app.Booth.HasItem(ItemA, "作り物の衣装", images: 38);
        app.Booth.HasItem(ItemB, "作り物の靴", images: 8);
        var (main, first) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip");
        var second = new ResolveViewModel(app.Services, main);
        await app.SettleAsync();
        await PreviewAsync(app, first, "first.zip", ItemA);
        await PreviewAsync(app, second, "second.zip", ItemB);

        // 見込みは「商品JSON・商品ページ・1枚目」（ショップのアイコンは無い作り物）。残りの7枚は登録の後に⑤の段で取るので数えない（メモ60 案B）
        Assert.Equal(2 + 1, second.Preview!.RequestsToRegister);

        app.Booth.Hold(url => url.EndsWith($"/items/{ItemA}.json", StringComparison.Ordinal));
        try
        {
            first.AssignCommand.Execute(null);
            await UiThread.Until(() => app.Booth.Requests.Any(url => url.EndsWith($"/items/{ItemA}.json", StringComparison.Ordinal)), "A が始まる");
            second.AssignCommand.Execute(null);

            // B：前の A は残り 3 件（JSON の答えの前なので見込みのまま。画像が38枚でも1枚目だけ）× 30 秒 = 1 分 30 秒 → 約 2 分。B も 3 件 × 30 秒
            Assert.True(second.IsTargetWaiting);
            Assert.False(second.IsRegisteringInDecision);
            Assert.Equal("ほかの商品を登録しています。開始まで約 2 分、この商品は約 2 分です。", second.QueueWaitingText);
            Assert.False(second.AssignCommand.CanExecute(null));
            Assert.False(second.ExcludeCommand.CanExecute(null));
            Assert.True(second.CancelQueuedCommand.CanExecute(null));

            // 列にいない行は今までどおり扱える（登録が画面全体を止めない）
            second.Selected = Row(second, "third.zip");
            Assert.False(second.IsTargetWaiting);
            Assert.True(second.ExcludeCommand.CanExecute(null));
            Assert.Equal(string.Empty, Row(second, "third.zip").QueueBadge);

            // 走っている行はやめられない
            second.Selected = Row(second, "first.zip");
            Assert.True(second.IsTargetRunning);
            Assert.False(second.CancelQueuedCommand.CanExecute(null));

            // 閉じるときの確認：2件・6 件 × 30 秒 = 3 分
            Assert.Equal(2, main.Registrations.Jobs.Count);
            var confirm = RegistrationQueue.CloseConfirm(main.Registrations.Jobs.Count, main.Registrations.SecondsLeftAll());
            Assert.NotNull(confirm);
            Assert.Equal("登録が終わっていない商品が 2 件あります。閉じますか？", confirm!.Value.Question);
            Assert.Equal("終わるまであと約 3 分です。閉じても、次に起動したときに続きから登録します。", confirm.Value.Detail);
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();
        Assert.Null(RegistrationQueue.CloseConfirm(main.Registrations.Jobs.Count, main.Registrations.SecondsLeftAll()));
        second.OnLeaving();
    });

    [Fact]
    public Task 待っている登録をやめると_列と記録から外れ_ファイルは未確定に残る() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        app.Booth.HasItem(ItemB, "作り物の靴");
        var (main, first) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        var second = new ResolveViewModel(app.Services, main);
        await app.SettleAsync();
        await PreviewAsync(app, first, "first.zip", ItemA);
        await PreviewAsync(app, second, "second.zip", ItemB);

        app.Booth.Hold();
        try
        {
            first.AssignCommand.Execute(null);
            await UiThread.Until(() => main.Registrations.Jobs.Count == 1 && main.Registrations.Jobs[0].IsRunning, "A が始まる");
            second.AssignCommand.Execute(null);
            await main.Registrations.WhenWrittenAsync();
            Assert.Equal(2, app.Store.RegistrationQueue.Load().Count);

            second.CancelQueuedCommand.Execute(null);
            await main.Registrations.WhenWrittenAsync();

            Assert.Equal(ItemA, Assert.Single(main.Registrations.Jobs).ItemId);
            Assert.Equal(ItemA, Assert.Single(app.Store.RegistrationQueue.Load()).ItemId);
            Assert.Equal(string.Empty, Row(second, "second.zip").QueueBadge);
            Assert.True(second.AssignCommand.CanExecute(null));
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();
        Assert.Null(await app.Store.Items.LoadAsync(ItemB));
        Assert.Equal("second.zip", Path.GetFileName(Assert.Single(app.Store.Unresolved.Load()).Paths[0]));
        Assert.Empty(app.Store.RegistrationQueue.Load());
        second.OnLeaving();
    });

    // ---- 終わったとき ----

    [Fact]
    public Task 終わったときにその行を見ていれば_次の行へ移る() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);
        Assert.Equal("second.zip", resolve.Selected!.FileName);
        Assert.True(resolve.HasSettled);
    });

    [Fact]
    public Task 終わったときに別の行を見ていれば_見ている行のまま_一覧の上で知らせる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var (_, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip", @"c\third.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        app.Booth.Hold();
        resolve.AssignCommand.Execute(null);
        resolve.Selected = Row(resolve, "third.zip");
        resolve.ItemIdInput = "123";
        app.Booth.Release();
        await app.SettleAsync();

        Assert.Equal(["second.zip", "third.zip"], resolve.Files.Select(row => row.FileName));
        Assert.Equal("third.zip", resolve.Selected!.FileName);

        // 見ている行の入力は消さない（選び直していない）
        Assert.Equal("123", resolve.ItemIdInput);
        Assert.Equal("「作り物の衣装」を登録しました。", resolve.ListNoticeText);
    });

    [Fact]
    public Task 登録できなかったら_行に札と理由が残り_見ていれば欄の下に理由を出す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        // 確かめた後に BOOTH から消えた
        app.Booth.LosesItem(ItemA);
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var row = Row(resolve, "first.zip");
        Assert.Equal("登録に失敗", row.QueueBadge);
        Assert.True(row.IsQueueFailed);
        Assert.Equal(row.QueueFailure, row.QueueTip);
        Assert.Contains("確定できませんでした", row.QueueFailure, StringComparison.Ordinal);
        Assert.Equal(row.QueueFailure, resolve.StatusText);
        Assert.Same(row, resolve.Selected);
        Assert.False(main.Registrations.HasJobs);

        // 積み直せば札は消える（押せる）
        app.Booth.HasItem(ItemA, "作り物の衣装");
        Assert.True(resolve.AssignCommand.CanExecute(null));
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("second.zip", Assert.Single(resolve.Files).FileName);
    });

    // ---- 画面を離れても ----

    [Fact]
    public Task 画面を開き直しても_登録中の札と帯が映り_終われば新しい画面から行が消える() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        ResolveViewModel reopened;
        app.Booth.Hold();
        try
        {
            resolve.AssignCommand.Execute(null);
            await UiThread.Until(() => main.Registrations.HasJobs, "列に積まれる");

            // ほかの画面へ移って戻る（未確定の画面は作り直される）
            main.ShowStatsCommand.Execute(null);
            main.ShowResolveCommand.Execute(null);
            reopened = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
            Assert.NotSame(resolve, reopened);
            await UiThread.Until(() => reopened.IsLoaded, "一覧を読む");

            reopened.Selected = Row(reopened, "first.zip");
            Assert.Equal("登録中", reopened.Selected.QueueBadge);
            Assert.True(reopened.IsRegisteringInDecision);
            Assert.StartsWith("登録しています…", reopened.RegisteringText, StringComparison.Ordinal);
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();
        Assert.Equal("second.zip", Assert.Single(reopened.Files).FileName);
        Assert.True(reopened.HasSettled);
    });

    [Fact]
    public Task 未確定の画面を離れている間に終わった登録は_下の帯で知らせる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        app.Booth.Hold();
        resolve.AssignCommand.Execute(null);
        main.ShowStatsCommand.Execute(null);
        app.Booth.Release();
        await app.SettleAsync();

        Assert.True(main.HasRegisteredNotice);
        Assert.Equal("「作り物の衣装」を登録しました。", main.RegisteredNoticeText);
        Assert.Contains(ItemA, main.ResolveSettledItemIds);
    });

    // ---- 記録から再開 ----

    [Fact]
    public Task 前の起動で残った列は_記録から同じ順に続け_未確定に無い物は飛ばして知らせる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem(ItemA, "作り物の衣装");
        var file = Unresolved(app.NewFile(@"a\first.zip"));
        await app.Store.Unresolved.SaveAsync([file]);
        await app.Store.RegistrationQueue.SaveAsync(
        [
            new QueuedRegistration { ItemId = ItemB, ItemName = "作り物の靴", FileHashes = [Make.HashOf("消えたファイル")] },
            new QueuedRegistration { ItemId = ItemA, ItemName = "作り物の衣装", FileHashes = [file.Hash], EstimatedRequests = 2 },
        ]);

        // 人が開いて直せる形（計算で出せる進み具合は書かない）
        var text = File.ReadAllText(app.Store.RegistrationQueue.Path);
        Assert.Contains("\"itemId\": \"9900601\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("done", text, StringComparison.OrdinalIgnoreCase);

        // 未確定があるので、主画面は未確定の画面から始まる
        var main = await app.StartAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        await main.ResumeRegistrationsAsync();
        await app.SettleAsync();

        // 先頭は未確定に無い（済んだ・消えた）ので失敗として飛ばし、次を登録する
        var item = await app.Store.Items.LoadAsync(ItemA);
        Assert.NotNull(item);
        Assert.Empty(app.Store.Unresolved.Load());
        Assert.Empty(app.Store.RegistrationQueue.Load());
        Assert.Null(await app.Store.Items.LoadAsync(ItemB));

        // 登録できた行は開いている画面が受け止める。未確定に無かった物は、画面に行が無いので下の帯で知らせる（押すと未確定を開く）
        Assert.Empty(resolve.Files);
        Assert.True(resolve.HasSettled);
        Assert.Equal("「作り物の靴」を登録できませんでした。", main.RegisteredNoticeText);
        Assert.Equal("未確定を開く", main.RegisteredNoticeAction);
        Assert.False(main.HasRegisteredNoticeWaiting);

        // 再開した登録も、押した登録と同じ「人が押した」で BOOTH へ行く（裏の取得の設定とは別）
        Assert.Contains($"https://booth.pm/ja/items/{ItemA}.json", app.Booth.Requests);
    });

    // ---- 自動検索 ----

    [Fact]
    public Task 自動検索はほかの行を妨げ_進みは検索している行にだけ出す_登録と除外は妨げない() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip", @"b\second.zip");
        resolve.Selected = Row(resolve, "first.zip");

        app.Booth.Hold();
        try
        {
            resolve.ProposeCommand.Execute(null);
            await UiThread.Until(() => main.ResolveSearch.IsRunning, "検索が始まる");

            Assert.True(resolve.IsSearching);
            Assert.True(Row(resolve, "first.zip").IsSearching);
            Assert.False(Row(resolve, "second.zip").IsSearching);

            // ほかの行：進みは出ず、自動検索は押せず理由を言う。除外は押せる（前は検索の間、画面全体が止まっていた）
            resolve.Selected = Row(resolve, "second.zip");
            Assert.False(resolve.IsSearching);
            Assert.False(resolve.ProposeCommand.CanExecute(null));
            Assert.Equal(ResolveViewModel.OtherSearchHint, resolve.ProposeHint);
            Assert.True(resolve.ExcludeCommand.CanExecute(null));

            // 画面を開き直しても、検索している行に進みが出る
            main.ShowStatsCommand.Execute(null);
            main.ShowResolveCommand.Execute(null);
            var reopened = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
            await UiThread.Until(() => reopened.IsLoaded, "一覧を読む");
            reopened.Selected = Row(reopened, "first.zip");
            Assert.True(reopened.IsSearching);
            Assert.True(Row(reopened, "first.zip").IsSearching);

            main.StopLongJobCommand.Execute(null);
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();
        Assert.False(main.ResolveSearch.IsRunning);
    });

    // ---- 残りの画像は登録の後（メモ60 案B） ----

    [Fact]
    public Task 登録した直後の商品ページは_1枚目と未取得の枚数を出し_残りの画像が届くと並ぶ() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { SaveImages = true });
        app.Booth.HasItem(ItemA, "作り物の衣装", images: 3);
        var (main, resolve) = await OpenResolveAsync(app, @"a\first.zip");
        await PreviewAsync(app, resolve, "first.zip", ItemA);

        // 残りの画像の答えを止めて、「登録は済んだが残りがまだ来ていない」間を作る
        var rest = new[] { FakeBooth.ImageUrl(ItemA, 2), FakeBooth.ImageUrl(ItemA, 3) };
        app.Booth.Hold(url => rest.Contains(url));
        try
        {
            resolve.AssignCommand.Execute(null);
            await UiThread.Until(
                () => !main.Registrations.HasJobs && app.Booth.Requests.Contains(rest[0]),
                "登録が済み、残りの画像を裏で頼む");

            // 取り込みの⑤を待っている商品と同じ見え方：1枚目だけが並び、残りは「未取得の画像」の枚数で出る
            main.ShowItem((await app.Store.Items.LoadAsync(ItemA))!);
            var page = main.CurrentItemPage!;
            Assert.Single(page.Images);
            Assert.Equal(2, page.MissingImageCount);
            Assert.True(page.HasMissingImages);
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();

        // 届いた分は開いている商品ページに並ぶ（裏の取得が画像を置いた知らせで組み直す）
        Assert.Equal(3, main.CurrentItemPage!.Images.Count);
        Assert.Equal(0, main.CurrentItemPage.MissingImageCount);
    });

    // ---- 文 ----

    [Theory]
    [InlineData(90, 30.0, "ほかの商品を登録しています。開始まで約 2 分、この商品は1分以内です。")]
    [InlineData(10, 300.0, "ほかの商品を登録しています。開始まで1分以内、この商品は約 5 分です。")]
    [InlineData(200, null, "ほかの商品を登録しています。開始まで約 4 分です。")]
    public void 待っている行の文は_開始までと所要を言い_所要が分からなければ開始までだけ(double start, double? own, string expected)
        => Assert.Equal(expected, RegistrationQueue.WaitingText(start, own));

    [Theory]
    [InlineData(0, "登録中")]
    [InlineData(1, "登録待ち 1番目")]
    [InlineData(3, "登録待ち 3番目")]
    public void 行の札は_走っていれば登録中_待っていれば順番(int position, string expected)
        => Assert.Equal(expected, RegistrationQueue.BadgeText(position));
}
