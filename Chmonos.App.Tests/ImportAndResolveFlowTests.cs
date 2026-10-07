using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.App.Tests;

/// <summary>
/// 取り込み・未確定の「押したら何が起きるか」のうち、これまで人が画面で確かめていた物
/// （手動確認 2026-10-02 の5。起動して撮らずに、ViewModel を通して確かめる）。
///
/// BOOTH は作り物（<see cref="FakeBooth"/>）で、待ちも差し替わっている。答えを止める（<c>Hold</c>）ことで
/// 「BOOTH の返事を待っている間」の画面の状態を確かめる。配置・線・フォーカスの絵は画面で見る物なので、ここでは確かめない。
/// </summary>
public class ImportAndResolveFlowTests
{
    private const string BoothMissing = "1999999";

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
        Assert.True(resolve.IsLoaded);
        return (main, resolve);
    }

    private static async Task PreviewAsync(TestApp app, ResolveViewModel resolve, string itemId)
    {
        resolve.ItemIdInput = itemId;
        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();
    }

    private static byte[] Png(byte red)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(red, 100, 100));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // ---- 前回途中の取り込み（5-2）----

    private static Task WriteInterruptedStateAsync(TestApp app, string target)
        => app.Store.ImportState.SaveAsync(new ImportState
        {
            Done = 1,
            Total = 3,
            Targets = [target],
            StoppedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        });

    [Fact]
    public Task 前回途中で閉じた取り込みが残っていると_どの画面でも続きからを言い_続きから進むで対象を積む() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"booth\a.zip"))!;
        await WriteInterruptedStateAsync(app, folder);

        var main = await app.StartAsync();
        await UiThread.Until(() => main.HasInterruptedImport, "続きの帯が出る");

        Assert.Equal("前回は 1 / 3 件まで進んで中断しました", main.InterruptedImportText);

        // 続きから進むは、前回の対象を積んで始める（積んだ物は今の取り込みの対象の一覧に載る）
        Assert.DoesNotContain(folder, main.Import.Folders, StringComparer.OrdinalIgnoreCase);
        main.Import.ResumeCommand.Execute(null);
        Assert.Contains(folder, main.Import.Folders, StringComparer.OrdinalIgnoreCase);
        await UiThread.Until(() => main.Import.Summary is not null, "続きの取り込みが終わる");
        await app.SettleAsync();

        // 最後まで走ったので、帯は消える
        await UiThread.Until(() => !main.HasInterruptedImport, "続きの帯が消える");
    });

    [Fact]
    public Task 起動時に自動で取り込む設定なら_前回途中の続きを窓で尋ねずに取り込み始める() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"booth\a.zip"))!;
        await WriteInterruptedStateAsync(app, folder);
        await app.ChangeSettingsAsync(settings => settings with { StartImportOnLaunch = true });

        var main = await app.StartAsync();

        // 押していないのに、続きの対象が積まれて走り、終わる
        await UiThread.Until(() => main.Import.Summary is not null, "起動時に続きの取り込みが走って終わる");
        Assert.Contains(folder, main.Import.Folders, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, main.Import.Summary!.FilesScanned);
        Assert.Empty(app.Notices);
        await app.SettleAsync();
    });

    [Fact]
    public Task 起動時に自動で取り込む設定でなければ_前回途中の続きがあっても取り込み始めない() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"booth\a.zip"))!;
        await WriteInterruptedStateAsync(app, folder);

        var main = await app.StartAsync();
        await UiThread.Until(() => main.HasInterruptedImport, "続きの帯が出る");

        Assert.Empty(main.Import.Folders);
        Assert.Null(main.Import.Summary);
    });

    // ---- BOOTH の URL を落として登録する間の帯（5-6）----

    [Fact]
    public Task BOOTHのURLを落として登録する間は_下の帯に登録していますを出し_終わると消す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1234567", "作り物の衣装");
        app.Answer = request => request.Caption == "BOOTHのURLを受け取りました"
            ? MessageBoxResult.Yes
            : MessageBoxResult.No;
        var main = await app.StartAsync();
        Assert.False(main.IsRegisteringDropped);

        // 登録は BOOTH の返事を待つので、止めておいて、待っている間の状態を見る
        app.Booth.Hold();
        Task drop;
        try
        {
            drop = main.HandleDropAsync(null, "https://booth.pm/ja/items/1234567");
            await UiThread.Until(() => app.Booth.Requests.Count > 0, "登録の問い合わせが BOOTH に届く");

            Assert.True(main.IsRegisteringDropped);
            Assert.Equal("商品を登録しています…", main.DroppedRegisteringText);
        }
        finally
        {
            app.Booth.Release();
        }

        await drop;
        await app.SettleAsync();

        Assert.False(main.IsRegisteringDropped);
        Assert.NotNull(await app.Store.Items.LoadAsync("1234567"));
    });

    [Fact]
    public Task 続けて2つ落とすと_帯は1つのまま件数で言う() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1234567", "作り物の衣装");
        app.Booth.HasItem("1234568", "作り物の髪型");
        app.Answer = request => request.Caption == "BOOTHのURLを受け取りました"
            ? MessageBoxResult.Yes
            : MessageBoxResult.No;
        var main = await app.StartAsync();

        app.Booth.Hold();
        Task first, second;
        try
        {
            first = main.HandleDropAsync(null, "https://booth.pm/ja/items/1234567");
            second = main.HandleDropAsync(null, "https://booth.pm/ja/items/1234568");
            await UiThread.Until(() => main.DroppedRegisteringText.StartsWith("2 件", StringComparison.Ordinal), "2件の登録中になる");

            Assert.Equal("2 件の商品を登録しています…", main.DroppedRegisteringText);
        }
        finally
        {
            app.Booth.Release();
        }

        await Task.WhenAll(first, second);
        await app.SettleAsync();

        Assert.False(main.IsRegisteringDropped);
    });

    // ---- 未確定：登録を押した後、終わるまで「登録しています…」（5-9）----

    [Fact]
    public Task 商品IDで確定を押した後_BOOTHの返事を待つ間は登録していますを出し_終わると消す() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        var (_, resolve) = await OpenResolveAsync(app, @"a\costume.zip");
        await PreviewAsync(app, resolve, "1000001");
        Assert.True(resolve.HasPreview);
        Assert.False(resolve.IsRegisteringInDecision);

        // 手元に無い商品の確定は BOOTH から取ってくる。その返事を止める
        app.Booth.Hold();
        try
        {
            var asked = app.Booth.Requests.Count;
            resolve.AssignCommand.Execute(null);
            await UiThread.Until(() => app.Booth.Requests.Count > asked, "確定の問い合わせが BOOTH に届く");

            Assert.True(resolve.IsRegisteringInDecision);
            Assert.False(resolve.IsRegisteringLocal);
            Assert.False(resolve.HasRegisteringTotal);

            // 手元に無い商品は、JSON と商品ページの2つが残っていると知らせてくる（メモ34）。
            // 画像の枚数は JSON を読むまで分からないので、始めはこの2つだけ
            await UiThread.Until(() => resolve.RegisteringText.Contains("BOOTHへあと 2 件", StringComparison.Ordinal), "残り2件が出る");
            Assert.StartsWith("登録しています…　", resolve.RegisteringText, StringComparison.Ordinal);

            // 登録は列で走る（メモ60）。行には札「登録中」、同じ行の登録はもう押せない
            Assert.Equal("登録中", resolve.Files.Single().QueueBadge);
            Assert.False(resolve.AssignCommand.CanExecute(null));
        }
        finally
        {
            app.Booth.Release();
        }

        await app.SettleAsync();

        Assert.False(resolve.IsRegisteringInDecision);
        Assert.NotNull(await app.Store.Items.LoadAsync("1000001"));
    });

    // ---- 未確定：BOOTHで見つからなかった商品IDのまま登録（5-10）----

    [Fact]
    public Task BOOTHで見つからなかった商品IDは_そのIDのまま登録でき_BOOTHの情報は空で販売終了と同じ扱いになる() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenResolveAsync(app, @"a\old-costume.zip");
        Assert.False(resolve.IsUnpublishedForm);
        Assert.False(resolve.AssignCommand.CanExecute(null));

        await PreviewAsync(app, resolve, BoothMissing);

        // BOOTH が「無い」と答えたときだけ、このIDのまま登録する形（説明・名前・画像）が出て、「このIDで登録」で登録できる
        Assert.True(resolve.IsUnpublishedForm);
        Assert.Equal("登録すると：BOOTHで非公開の商品として新しく作って、このファイルを紐付けます", resolve.AssignOutcomeText);
        Assert.True(resolve.AssignCommand.CanExecute(null));

        // 人が打ったIDは打ち間違いもあり得るので、欄の下の「IDが違うか、非公開です」を残し、枠の1行目は出さない
        Assert.Contains("見つかりませんでした", resolve.StatusText, StringComparison.Ordinal);
        Assert.False(resolve.HasUnpublishedLead);

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        // 1件の登録は窓を出さない（ふつうの「このIDで登録」と同じ。ユーザ判断 2026-10-06）
        Assert.Empty(app.Notices);

        var item = await app.Store.Items.LoadAsync(BoothMissing);
        Assert.NotNull(item);
        Assert.Equal("old-costume.zip", Path.GetFileName(Assert.Single(item!.Local.LocalFiles).Paths[0]));
        Assert.Null(item.Booth.FetchedAt);
        Assert.True(item.Local.IsDelisted);
        Assert.NotNull(item.Local.NextFetchDueAt);

        // 未確定から消え、ナビの数も合う
        Assert.Empty(resolve.Files);
        Assert.Empty(app.Store.Unresolved.Load());
        await UiThread.Until(() => main.UnresolvedCount == 0, "ナビの未確定の数が合う");
    });

    /// <summary>file-lifecycle.md「気になった所」14。前はここだけ一覧のチェックを見ず、チェックしていても選んでいる行だけを登録した。</summary>
    [Fact]
    public Task そのIDのまま登録は_一覧にチェックがあればチェックした物を対象にする() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\selected-only.zip", @"b\checked.zip");
        resolve.Selected = resolve.Files.Single(row => row.FileName == "selected-only.zip");
        await PreviewAsync(app, resolve, BoothMissing);
        resolve.Files.Single(row => row.FileName == "checked.zip").IsSelected = true;
        Assert.True(resolve.HasChecked);

        app.Answer = _ => MessageBoxResult.OK;
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(BoothMissing);
        Assert.Equal("checked.zip", Path.GetFileName(Assert.Single(item!.Local.LocalFiles).Paths[0]));
        Assert.Contains("選択した 1 件", Assert.Single(app.Notices).Text, StringComparison.Ordinal);
        Assert.Equal("selected-only.zip", Path.GetFileName(Assert.Single(app.Store.Unresolved.Load()).Paths[0]));
    });

    [Fact]
    public Task 読み取れなかったIDには_そのIDのまま登録する道を出さない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\costume.zip");

        // 読み取れないID（BOOTH へ問い合わせず、「無い」とも言えない）
        await PreviewAsync(app, resolve, "abc");

        Assert.False(resolve.HasNotOnBoothItemId);
        Assert.False(resolve.IsUnpublishedForm);
        Assert.False(resolve.AssignCommand.CanExecute(null));
    });

    // ---- 未確定：ファイルから読み取れた商品IDが BOOTH で非公開だったとき（ユーザ 2026-10-06）----

    private const string FromFileMissing = "9900001";

    /// <summary>取り込みの手掛かりから読み取れた商品IDを持つ未確定（BOOTH の答えが404で未確定へ送られた物と同じ形）。</summary>
    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveWithClueAsync(TestApp app, string name, string itemId)
    {
        var path = app.NewFile(name);
        await app.Store.Unresolved.SaveAsync([new UnresolvedFile
        {
            Hash = Make.HashOf(path),
            Paths = [path],
            SizeBytes = 3,
            ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            CandidateItemIds = [itemId],
        }]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        return (main, Assert.IsType<ResolveViewModel>(main.CurrentViewModel));
    }

    [Fact]
    public Task ファイルから読み取れたIDがBOOTHで非公開なら_非公開だと言い_名前の下書きが入っている() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithClueAsync(app, @"a\作り物の季節衣装.zip", FromFileMissing);

        await PreviewAsync(app, resolve, FromFileMissing);

        Assert.True(resolve.IsUnpublishedForm);
        Assert.Equal("このファイルから読み取れた商品IDは、BOOTHで非公開です。", resolve.UnpublishedLeadText);

        // 同じことを欄の下でも言わない
        Assert.Equal(string.Empty, resolve.StatusText);
        Assert.Equal("作り物の季節衣装", resolve.LocalNameInput);
    });

    [Fact]
    public Task 非公開のIDのまま登録すると_欄の名前と添えた画像で商品を作る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithClueAsync(app, @"a\gift-old.zip", FromFileMissing);
        await PreviewAsync(app, resolve, FromFileMissing);

        resolve.LocalNameInput = "作り物の季節の贈り物";
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200)), app.NewFile(@"pics\blue.png", Png(20))]);
        var asked = app.Booth.Requests.Count;
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        Assert.Empty(app.Notices);
        var item = await app.Store.Items.LoadAsync(FromFileMissing);
        Assert.Equal("作り物の季節の贈り物", item!.Local.DisplayName);
        Assert.Equal(2, item.Local.UserImages.Count);
        Assert.True(item.Local.IsDelisted);

        // 直前の確かめで見つからなかったIDなので、BOOTH へは問い合わせない。次の登録へ画像は持ち越さない
        Assert.Equal(asked, app.Booth.Requests.Count);
        Assert.Empty(resolve.LocalImages);
        Assert.Empty(resolve.Files);
    });

    [Fact]
    public Task 非公開のIDから欄を書き換えると_説明と名前と画像の枠は引っ込み_確かめてから登録する流れに戻る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithClueAsync(app, @"a\gift-old.zip", FromFileMissing);
        await PreviewAsync(app, resolve, FromFileMissing);
        Assert.True(resolve.IsUnpublishedForm);

        resolve.ItemIdInput = "9900002";

        Assert.False(resolve.IsUnpublishedForm);
        Assert.False(resolve.HasAssignOutcome);
        Assert.False(resolve.AssignCommand.CanExecute(null));

        // 戻せば、確かめ直さずにまた出る（同じIDの答えは出ている）
        resolve.ItemIdInput = $"https://booth.pm/ja/items/{FromFileMissing}";
        Assert.True(resolve.IsUnpublishedForm);
    });

    [Fact]
    public Task 非公開のIDの商品が確かめた後にできていれば_ファイルと画像を足すだけで名前は変えない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithClueAsync(app, @"a\gift-old.zip", FromFileMissing);
        await PreviewAsync(app, resolve, FromFileMissing);

        // 確かめた後に、別の道（取り込み・別の行）で同じIDの商品ができた
        await app.AddItemAsync(Make.Item(FromFileMissing, "作り物の先にあった名前"));

        resolve.LocalNameInput = "作り物の欄の名前";
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200))]);
        app.Answer = _ => MessageBoxResult.OK;
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(FromFileMissing);
        Assert.Equal("作り物の先にあった名前", item!.DisplayName);
        Assert.Contains(item.Local.LocalFiles, file => file.Paths.Any(path => path.EndsWith("gift-old.zip", StringComparison.Ordinal)));
        Assert.Single(item.Local.UserImages);
    });

    [Fact]
    public Task BOOTHに一時的に届かないときは_非公開のIDのまま登録する形を出さない() => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var (_, resolve) = await OpenResolveWithClueAsync(app, @"a\gift-old.zip", FromFileMissing);
        app.Booth.IsDown(FromFileMissing);

        await PreviewAsync(app, resolve, FromFileMissing);

        Assert.False(resolve.HasNotOnBoothItemId);
        Assert.False(resolve.IsUnpublishedForm);
        Assert.False(resolve.AssignCommand.CanExecute(null));
        Assert.Contains("問い合わせできませんでした", resolve.StatusText, StringComparison.Ordinal);
    });

    // ---- 未確定：取り込みが BOOTH から「無い」と言われた答えを残した行（ユーザ判断 2026-10-06）----

    /// <summary>取り込みが BOOTH に聞いて404だった行（<see cref="UnresolvedFile.NotOnBooth"/> 付き）と、ふつうの手掛かりの行を置いて未確定を開く。</summary>
    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenResolveWithNotOnBoothAsync(TestApp app)
    {
        var marked = app.NewFile(@"a\作り物の季節衣装.zip");
        var plain = app.NewFile(@"b\作り物の手掛かり付き.zip");
        await app.Store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = Make.HashOf(marked),
                Paths = [marked],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                CandidateItemIds = [FromFileMissing],
                NotOnBooth = new BoothNotFoundNote { ItemId = FromFileMissing, CheckedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) },
            },
            new UnresolvedFile
            {
                Hash = Make.HashOf(plain),
                Paths = [plain],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                CandidateItemIds = ["9900005"],
            },
        ]);
        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        resolve.Selected = resolve.Files.Single(row => row.FileName == "作り物の季節衣装.zip");
        await app.SettleAsync();
        return (main, resolve);
    }

    [Fact]
    public Task 取り込みでBOOTHに無いと答えられた行は_札がBOOTHで非公開になり_ほかの行は自動候補のまま() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);

        var marked = resolve.Files.Single(row => row.FileName == "作り物の季節衣装.zip");
        Assert.True(marked.IsNotOnBooth);
        Assert.False(marked.ShowsCandidateCount);

        var plain = resolve.Files.Single(row => row.FileName == "作り物の手掛かり付き.zip");
        Assert.False(plain.IsNotOnBooth);
        Assert.True(plain.ShowsCandidateCount);
        Assert.Equal("自動候補:1 件", plain.CandidateText);
    });

    [Fact]
    public Task 取り込みでBOOTHに無いと答えられた行を選ぶと_初めから欄にIDが入り_そのIDのまま登録する形が出て_BOOTHへは聞かない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);

        Assert.Equal(FromFileMissing, resolve.ItemIdInput);
        Assert.True(resolve.IsUnpublishedForm);
        Assert.Equal("このファイルから読み取れた商品IDは、BOOTHで非公開です。", resolve.UnpublishedLeadText);
        Assert.Equal("作り物の季節衣装", resolve.LocalNameInput);
        Assert.True(resolve.AssignCommand.CanExecute(null));
        Assert.Empty(app.Booth.Requests);

        // ほかの行を選ぶと引っ込む（その行には答えが無い）
        resolve.Selected = resolve.Files.Single(row => row.FileName == "作り物の手掛かり付き.zip");
        Assert.Equal(string.Empty, resolve.ItemIdInput);
        Assert.False(resolve.IsUnpublishedForm);
    });

    [Fact]
    public Task 選んだだけで出た非公開の形から_1件なら窓を出さずにそのIDのまま登録する() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200))]);

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        Assert.Empty(app.Notices);
        Assert.Empty(app.Booth.Requests);
        var item = await app.Store.Items.LoadAsync(FromFileMissing);
        Assert.Equal("作り物の季節衣装", item!.Local.DisplayName);
        Assert.True(item.Local.IsDelisted);
        Assert.Single(item.Local.UserImages);
    });

    [Fact]
    public Task 非公開のIDのまま登録も_一覧でチェックした物をまとめるときは窓で確かめる() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        resolve.Files.Single(row => row.FileName == "作り物の季節衣装.zip").IsSelected = true;

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        // 既定の答え（キャンセル）なので何も登録しない
        Assert.Equal("このIDのまま登録する", Assert.Single(app.Notices).Caption);
        Assert.Null(await app.Store.Items.LoadAsync(FromFileMissing));
    });

    [Fact]
    public Task 情報を確認で聞き直して今も無ければ_そのIDのまま登録する形のまま() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);

        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        Assert.NotEmpty(app.Booth.Requests);
        Assert.True(resolve.IsUnpublishedForm);
        Assert.True(resolve.Selected!.IsNotOnBooth);

        // 記録の日時は聞き直した時に新しくなる
        var row = app.Store.Unresolved.Load().Single(file => file.NotOnBooth is not null);
        Assert.True(row.NotOnBooth!.CheckedAt > new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
    });

    [Fact]
    public Task 情報を確認で聞き直して公開されていれば_伝えてふつうの登録に戻り_添えた画像を自分で足した画像として入れる() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200)), app.NewFile(@"pics\blue.png", Png(20))]);
        app.Booth.HasItem(FromFileMissing, "作り物の再公開された衣装");

        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(resolve.HasPreview);
        Assert.False(resolve.IsUnpublishedForm);
        Assert.Equal("BOOTHで公開されています。選んだ画像 2 枚も一緒に追加します。", resolve.StatusText);
        Assert.Equal("登録すると：この商品を新しく作って、このファイルを紐付けます", resolve.AssignOutcomeText);

        // 札も記録も「BOOTHで非公開」から外れる
        Assert.False(resolve.Selected!.IsNotOnBooth);
        Assert.All(app.Store.Unresolved.Load(), file => Assert.Null(file.NotOnBooth));

        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        Assert.Empty(app.Notices);
        var item = await app.Store.Items.LoadAsync(FromFileMissing);
        Assert.Equal("作り物の再公開された衣装", item!.Booth.Name);
        Assert.False(item.Local.IsDelisted);
        Assert.Equal(2, item.Local.UserImages.Count);
        Assert.DoesNotContain(app.Store.Unresolved.Load(), file => file.Paths.Any(path => path.EndsWith("作り物の季節衣装.zip", StringComparison.Ordinal)));
    });

    [Fact]
    public Task 聞き直して公開されていたら_画像の枠を出したまま外せて_全部外すと文から枚数が消え_登録で入る() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        Assert.False(resolve.ShowsOverturnedImages);
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200)), app.NewFile(@"pics\blue.png", Png(20))]);
        app.Booth.HasItem(FromFileMissing, "作り物の再公開された衣装");

        resolve.PreviewCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(resolve.ShowsOverturnedImages);
        resolve.RemoveLocalImageCommand.Execute(resolve.LocalImages[0]);
        Assert.True(resolve.ShowsOverturnedImages);
        Assert.Equal("BOOTHで公開されています。選んだ画像 1 枚も一緒に追加します。", resolve.StatusText);
        resolve.RemoveLocalImageCommand.Execute(resolve.LocalImages[0]);
        Assert.True(resolve.ShowsOverturnedImages);
        Assert.Equal("BOOTHで公開されています。", resolve.StatusText);

        resolve.AddLocalImages([app.NewFile(@"pics\gray.png", Png(120))]);
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(FromFileMissing);
        Assert.Single(item!.Local.UserImages);
    });

    [Fact]
    public Task はじめからふつうの登録の行には_公開後の画像の枠を出さない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        resolve.Selected = resolve.Files.Single(row => row.FileName == "作り物の手掛かり付き.zip");
        app.Booth.HasItem("9900005", "作り物のふつうの商品");
        await PreviewAsync(app, resolve, "9900005");
        Assert.True(resolve.HasPreview);
        Assert.False(resolve.ShowsOverturnedImages);
    });

    /// <summary>「BOOTHに無い商品」の欄で添えた画像は、ほかのIDのふつうの登録には持っていかない（非公開と思って添えた画像だけを持っていく）。</summary>
    [Fact]
    public Task 非公開ではない行のふつうの登録には_添えた画像を入れない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveWithNotOnBoothAsync(app);
        resolve.Selected = resolve.Files.Single(row => row.FileName == "作り物の手掛かり付き.zip");
        resolve.AddLocalImages([app.NewFile(@"pics\red.png", Png(200))]);
        app.Booth.HasItem("9900005", "作り物のふつうの商品");

        await PreviewAsync(app, resolve, "9900005");
        Assert.Equal(string.Empty, resolve.StatusText);
        resolve.AssignCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync("9900005");
        Assert.NotNull(item);
        Assert.Empty(item!.Local.UserImages);
    });

    // ---- 未確定：BOOTHに無い商品として登録するとき、画像も一緒に入れられる（5-10）----

    [Fact]
    public Task BOOTHに無い商品として登録するとき_選んだ画像も商品に入れる() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\gift.zip");
        var hash = resolve.Selected!.File.Hash;
        var red = app.NewFile(@"pics\red.png", Png(200));
        var blue = app.NewFile(@"pics\blue.png", Png(20));

        resolve.AddLocalImages([red, blue]);
        Assert.Equal("画像 2 枚を一緒に追加します。", resolve.LocalImagesText);

        resolve.LocalNameInput = "作り物の贈り物";
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        // 確認の窓にも、入れる枚数を書く
        Assert.Contains("選んだ画像 2 枚を追加します。", Assert.Single(app.Notices).Text, StringComparison.Ordinal);

        var item = await app.Store.Items.LoadAsync(LocalItemId.For(hash));
        Assert.Equal("作り物の贈り物", item!.Local.DisplayName);
        Assert.Equal(2, item.Local.UserImages.Count);

        // 次の登録へは持ち越さない
        Assert.Empty(resolve.LocalImages);
    });

    [Fact]
    public Task 添えた画像が画像として読めなくても_登録は取り消さず_入らなかった数を言う() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\gift.zip");
        var hash = resolve.Selected!.File.Hash;
        var good = app.NewFile(@"pics\good.png", Png(200));
        var broken = app.NewFile(@"pics\broken.png", [1, 2, 3, 4]);

        resolve.AddLocalImages([good, broken]);
        resolve.LocalNameInput = "作り物の贈り物";
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterLocalCommand.Execute(null);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync(LocalItemId.For(hash));
        Assert.NotNull(item);
        Assert.Single(item!.Local.UserImages);
        Assert.Equal("画像 1 枚を追加できませんでした。商品ページの「＋」から追加してください。", resolve.ListNoticeText);
    });

    [Fact]
    public Task 画像の拡張子でないファイルは_添える画像に入れない() => TestApp.Run(async app =>
    {
        var (_, resolve) = await OpenResolveAsync(app, @"a\gift.zip");

        resolve.AddLocalImages([app.NewFile(@"pics\note.txt"), app.NewFile(@"pics\red.png", Png(200))]);

        Assert.Equal("red.png", Assert.Single(resolve.LocalImages).Name);
    });

    // ---- 未確定：自動検索の結果は閉じるまで覚えている（5-11）----

    [Fact]
    public Task 自動検索の結果は_別のファイルを見て戻っても覚えている() => TestApp.Run(async app =>
    {
        // 名前は数字だけにする：語を持つ名前だと、検索語を広げるために辞書（JMdict）の索引を組み、1件で約7秒かかる
        var (_, resolve) = await OpenResolveAsync(app, @"a\1234.zip", @"b\5678.zip");
        var first = resolve.Files.Single(row => row.FileName == "1234.zip");
        var second = resolve.Files.Single(row => row.FileName == "5678.zip");
        resolve.Selected = first;
        Assert.False(resolve.HasSearched);

        resolve.ProposeCommand.Execute(null);
        await UiThread.Until(() => resolve.HasSearched, "自動検索が終わる");
        await app.SettleAsync();

        // 別のファイルへ移ると「まだ探していない」に戻り、戻ると結果がそのまま出る（選び直しは問い合わせを起こさない）
        resolve.Selected = second;
        Assert.False(resolve.HasSearched);
        resolve.Selected = first;

        Assert.True(resolve.HasSearched);
    });

    // ---- 「見つからないファイルを探す」：移したファイルを、同じ中身を持つ商品へ結び直す（5-15）----

    [Fact]
    public Task 見つからないファイルを探すと_移したファイルが同じ中身を持つ商品へ結び直り_結果を言う() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var original = app.NewFile(@"watched\costume.zip", [9, 8, 7, 6]);
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(original),
            Paths = [original],
            SizeBytes = 4,
        }));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();

        // 監視フォルダの中で移し、名前も変えた
        var moved = Path.Combine(watched, "sub", "costume_v2.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(original, moved);

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(
            () => main.Import.MissingSearchText.StartsWith("1 件を新しい場所に紐付け直しました", StringComparison.Ordinal),
            "探した結果が出る");
        await app.SettleAsync();

        Assert.Equal("1 件を新しい場所に紐付け直しました。", main.Import.MissingSearchText);
        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal(moved, Assert.Single(Assert.Single(item!.Local.LocalFiles).Paths));

        // 押せるように戻っている（探している間だけ押せない）
        Assert.True(main.Import.FindMissingFilesCommand.CanExecute(null));
    });

    [Fact]
    public Task 見つからないファイルを探すとき_探しても無かった物は移した先のフォルダを足すよう言う() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var original = app.NewFile(@"elsewhere\costume.zip", [9, 8, 7, 6]);
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(original),
            Paths = [original],
            SizeBytes = 4,
        }));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        File.Delete(original);

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.MissingSearchText.Contains("見つかりませんでした", StringComparison.Ordinal), "探した結果が出る");

        Assert.Equal("1 件を探しましたが、見つかりませんでした。", main.Import.MissingSearchText);
    });

    /// <summary>
    /// 探しても見つからなかった物に付けた日時が、検索の写しにもすぐ入る（file-lifecycle.md 気になった所16）。
    /// 前は結び直した数が1以上の時だけ読み直していたので、日時を付けただけの回はカードの印と条件に起動し直すまで出なかった。
    /// </summary>
    [Fact]
    public Task 見つからないファイルを探して見つからなければ_日時がすぐ検索のカードの印に出る() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var original = app.NewFile(@"elsewhere\costume.zip", [9, 8, 7, 6]);
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装").WithFiles(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(original),
            Paths = [original],
            SizeBytes = 4,
        }));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();
        File.Delete(original);
        Assert.False(main.Search.ListItems.Single().HasMissingFile);

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.MissingSearchText.Contains("見つかりませんでした", StringComparison.Ordinal), "探した結果が出る");
        await app.SettleAsync();

        Assert.NotNull(Assert.Single((await app.Store.Items.LoadAsync("9900001"))!.Local.LocalFiles).MissingSince);
        Assert.True(main.Search.ListItems.Single().HasMissingFile);
    });

    /// <summary>
    /// 取り込みが場所を全部外したファイル（場所が空）も中身で探す（ユーザ判断 2026-10-04）。
    /// 見つかれば場所が入り、検索の条件「見つからないファイルがある」から外れる。結果の文は付け替えと同じ数え方。
    /// </summary>
    [Fact]
    public Task 見つからないファイルを探すと_場所が空のファイルにも場所が入り_検索の条件から外れる() => TestApp.Run(async app =>
    {
        var moved = app.NewFile(@"watched\sub\costume_v2.zip", [9, 8, 7, 6]);
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(moved),
            Paths = [],
            SizeBytes = 4,
        }));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [Path.GetDirectoryName(Path.GetDirectoryName(moved))!] });
        var main = await app.StartAsync();
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.MissingFile);
        module.Selected = module.Options.Single(option => option.Key == "missing");
        Assert.Equal(["1000001"], main.Search.ListItems.Select(card => card.Item.Id));

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(
            () => main.Import.MissingSearchText.StartsWith("1 件を新しい場所に紐付け直しました", StringComparison.Ordinal),
            "探した結果が出る");
        await app.SettleAsync();

        Assert.Equal("1 件を新しい場所に紐付け直しました。", main.Import.MissingSearchText);
        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal([moved], Assert.Single(item!.Local.LocalFiles).Paths);
        await UiThread.Until(() => main.Search.ListItems.Count == 0, "条件「見つからないファイルがある」から外れる");
    });

    // ---- 未確定：元zipが無いフォルダ。フォルダのまま商品として登録できる（5-12）----

    /// <summary>
    /// フォルダを数えている間に行を選び直したら、登録せずにやめる（外部の点検 2026-10-06）。
    /// 前は待った後に今の下見を読み直し、選び直した別の商品へ登録していた（下見が空なら落ちていた）
    /// </summary>
    [Fact]
    public Task フォルダを数えている間に選び直したら_登録せずにやめる() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");
        app.Booth.HasItem("1000002", "作り物の別の衣装");
        const string inside = @"1\2\3\4\5";
        var texture = app.NewFile(inside + @"\outfit_v1\outfit\texture\t.png");
        var package = app.NewFile(inside + @"\outfit_v1\outfit\outfit.unitypackage");
        app.NewFile(inside + @"\other\readme.txt");
        var importFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(texture))))!;
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [importFolder] });
        await app.Store.Unresolved.SaveAsync([Unresolved(texture), Unresolved(package)]);

        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        var row = resolve.Files.First();
        await PreviewAsync(app, resolve, "1000001");

        using var counting = new ManualResetEventSlim();
        resolve.MeasureFolderForTest = (_, _) => { counting.Wait(); return (2, 10); };
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterFolderOfCommand.Execute(row.GroupKey);

        // 数えている間に、一覧で行を選び直し（下見が消える）、別の商品の下見を出す（作業中も押せる「候補を使う」と同じ道）
        resolve.Selected = resolve.Files.Last();
        Assert.Null(resolve.Preview);
        resolve.ItemIdInput = "1000002";
        resolve.PreviewCommand.Execute(null);
        await UiThread.Until(() => resolve.Preview?.Id == "1000002", "別の商品の下見");
        counting.Set();
        await app.SettleAsync();

        Assert.Empty(app.Notices);
        Assert.Empty((await app.Store.Items.LoadAsync("1000001"))?.Local.LocalFolders ?? []);
        Assert.Empty((await app.Store.Items.LoadAsync("1000002"))?.Local.LocalFolders ?? []);
    });

    [Fact]
    public Task 元zipが無い展開物は_フォルダのまま商品として登録でき_配下の未確定が片付く() => TestApp.Run(async app =>
    {
        app.Booth.HasItem("1000001", "作り物の衣装");

        // 親は6段までたどる。5段掘った中に置けば、置き場より上（実マシンの一時フォルダ）の中身に答えが左右されない
        const string inside = @"1\2\3\4\5";
        var texture = app.NewFile(inside + @"\outfit_v1\outfit\texture\t.png");
        var package = app.NewFile(inside + @"\outfit_v1\outfit\outfit.unitypackage");

        // 取り込み元に別の物もある（根は「中身がそのフォルダしか無い親」まで遡るので、並びが無いと取り込み元まで広がって使えなくなる）
        app.NewFile(inside + @"\other\readme.txt");
        var importFolder = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(texture))))!;
        var unpacked = Path.Combine(importFolder, "outfit_v1");
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [importFolder] });
        await app.Store.Unresolved.SaveAsync([Unresolved(texture), Unresolved(package)]);

        var main = await app.StartAsync();
        main.ShowResolveCommand.Execute(null);
        await app.SettleAsync();
        var resolve = Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
        var row = resolve.Files.First();

        // 展開元の zip が無い展開物：「その他」の枠が出る条件と、登録する対象のフォルダ
        Assert.True(row.IsArchiveContent);
        Assert.False(row.IsExpandedContent);
        Assert.Equal(unpacked, resolve.RegisterTargetFolder);
        Assert.Equal($"対象：{unpacked}（未確定 2 件）", resolve.RegisterTargetSummary);
        Assert.Equal("「outfit_v1」を商品として登録", resolve.RegisterFolderText);

        // 先に商品IDを確かめていないと、登録せずに押したボタンの横で確かめる欄へ案内する（画面は送らない）
        resolve.RegisterFolderOfCommand.Execute(row.GroupKey);
        await app.SettleAsync();
        Assert.Equal("先に「商品IDを決める」で商品IDを確認してください。", resolve.FolderStatusText);
        Assert.Empty(app.Notices);

        await PreviewAsync(app, resolve, "1000001");
        app.Answer = _ => MessageBoxResult.OK;
        resolve.RegisterFolderOfCommand.Execute(row.GroupKey);
        await app.SettleAsync();

        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal(unpacked, Assert.Single(item!.Local.LocalFolders).Path);
        Assert.Empty(resolve.Files);
        Assert.Empty(app.Store.Unresolved.Load());

        // ナビの未確定の数も、ほかの片付け方と同じく登録の直後に減る（前はフォルダの登録だけ古い数が残っていた）
        await UiThread.Until(() => main.UnresolvedCount == 0, "ナビの未確定の数が合う");
    });

    /// <summary>
    /// 下に出ている商品と違うIDに書き換えたら、その商品の確かめは引っ込み、登録できない（外部の点検 2026-10-07）。
    /// 前は欄が別のIDのまま「このIDで登録」を押すと、下に出ている（前に確かめた）商品へ登録した。同じ商品のURLを貼っただけなら残す
    /// </summary>
    [Fact]
    public Task IDを書き換えたら_前に確かめた商品へは登録しない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901001", "作り物の衣装A"));
        await app.AddItemAsync(Make.Item("9901002", "作り物の衣装B"));
        var (_, resolve) = await OpenResolveAsync(app, @"bootha.zip");

        await PreviewAsync(app, resolve, "9901001");
        Assert.True(resolve.HasPreview);

        resolve.ItemIdInput = "https://booth.pm/ja/items/9901001";
        Assert.True(resolve.HasPreview);

        resolve.ItemIdInput = "9901002";
        Assert.False(resolve.HasPreview);
        Assert.False(resolve.AssignCommand.CanExecute(null));
    });
}
