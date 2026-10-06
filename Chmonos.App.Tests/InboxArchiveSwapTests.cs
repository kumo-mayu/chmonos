using System.IO;
using System.IO.Compression;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 通知の「zipで登録し直す」で、zip に事情があるとき（2026-10-05・file-lifecycle.md「気になった所」7・ユーザ判断）。
/// 除外した zip は確かめの窓（既定はやめる側）で聞いてから除外を解いて付ける。ほかの商品が持つ zip は
/// 「その商品を開く」「この商品に付け直す」を選ばせる。
/// </summary>
public class InboxArchiveSwapTests
{
    private const string ItemId = "9900131";
    private const string OtherId = "9900132";

    private static async Task<(string Folder, string Hash)> PrepareAsync(TestApp app)
    {
        var folder = Path.Combine(app.Root, "files", "作り物_v1");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "a.txt"), new byte[5]);
        ZipFile.CreateFromDirectory(folder, folder + ".zip");
        var hash = await Core.Scanning.FileHasher.ComputeSha256Async(folder + ".zip");

        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item with
        {
            Local = new LocalBlock { LocalFolders = [new LocalFolderRecord { Path = folder, FileCount = 1, TotalBytes = 5 }] },
        });
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(new NotificationRecord
            {
                Id = $"archive-found:{folder}",
                Kind = NotificationKind.ArchiveFoundForFolder,
                ItemId = ItemId,
                Title = "作り物の衣装",
                Detail = string.Empty,
                CreatedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(9)),
            });
            return list;
        });

        return (folder, hash);
    }

    private static async Task<(MainViewModel Main, InboxViewModel Inbox, NotificationRow Row)> OpenAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count == 1, "通知の束が並ぶ");
        return (main, inbox, inbox.Groups[0].Rows[0]);
    }

    /// <summary>押して、「zipを読んで登録しています…」が別の文（か空）に替わるまで待つ。</summary>
    private static async Task PressAsync(TestApp app, NotificationRow row)
    {
        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => !row.ActionNotice.Text.EndsWith('…'), "押した結果が出る");
        await app.SettleAsync();
    }

    private static async Task<LocalBlock> LocalOfAsync(TestApp app, string id) => (await app.Store.Items.LoadAsync(id))!.Local;

    private static ExcludedEntry Exclusion(string folder, string hash)
        => new() { Hash = hash, Paths = [folder + ".zip"], ExcludedAt = DateTimeOffset.Now, Reason = "試験" };

    [Fact]
    public Task 除外したzipは窓で聞き_やめれば何も変えない() => TestApp.Run(async app =>
    {
        var (folder, hash) = await PrepareAsync(app);
        await app.Store.Excluded.SaveAsync([Exclusion(folder, hash)]);
        var (_, inbox, row) = await OpenAsync(app);

        await PressAsync(app, row);

        var asked = Assert.Single(app.Notices);
        Assert.Contains("除外を解除して、この商品に登録しますか？", asked.Text);
        Assert.Equal(MessageBoxResult.Cancel, asked.DefaultResult);
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.Single((await LocalOfAsync(app, ItemId)).LocalFolders);
        Assert.Single(app.Store.Excluded.Load());
        Assert.Equal(string.Empty, inbox.ListNotice.Text);
    });

    [Fact]
    public Task 除外したzipは_OKなら除外を解いて登録し直す() => TestApp.Run(async app =>
    {
        var (folder, hash) = await PrepareAsync(app);
        await app.Store.Excluded.SaveAsync([Exclusion(folder, hash)]);
        app.Answer = _ => MessageBoxResult.OK;
        var (_, inbox, row) = await OpenAsync(app);

        await PressAsync(app, row);

        Assert.Equal(hash, Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles).Hash);
        Assert.Empty(app.Store.Excluded.Load());
        Assert.StartsWith("「作り物_v1.zip」で登録し直しました。", inbox.ListNotice.Text);
    });

    [Fact]
    public Task ほかの商品が持つzipは選ばせ_付け直すとそちらから外してこちらに付ける() => TestApp.Run(async app =>
    {
        var (folder, hash) = await PrepareAsync(app);
        await app.AddItemAsync(Make.Item(OtherId, "作り物の別商品").WithFiles(
            new LocalFileRecord { Hash = hash, Paths = [folder + ".zip"], SizeBytes = 1 }));
        var (_, inbox, row) = await OpenAsync(app);

        ChoiceRequest? asked = null;
        ChoiceQuestion.Intercept = request =>
        {
            asked = request;
            return ChoiceDialogResult.Second;
        };
        try
        {
            await PressAsync(app, row);
        }
        finally
        {
            ChoiceQuestion.Intercept = null;
        }

        Assert.NotNull(asked);
        Assert.Equal("その商品を開く", asked!.First);
        Assert.Equal("この商品に付け直す", asked.Second);
        Assert.Contains("「作り物の別商品」に登録されています。", asked.Question);
        Assert.Contains("「作り物の別商品」にはファイルが残りません。", asked.Detail);
        Assert.False(Assert.Single((await LocalOfAsync(app, ItemId)).LocalFiles).Detached);
        Assert.True(Assert.Single((await LocalOfAsync(app, OtherId)).LocalFiles).Detached);
        Assert.StartsWith("「作り物_v1.zip」で登録し直しました。", inbox.ListNotice.Text);
    });

    [Fact]
    public Task ほかの商品が持つzipで_その商品を開くを選ぶと_何も変えずにその商品ページへ() => TestApp.Run(async app =>
    {
        var (folder, hash) = await PrepareAsync(app);
        await app.AddItemAsync(Make.Item(OtherId, "作り物の別商品").WithFiles(
            new LocalFileRecord { Hash = hash, Paths = [folder + ".zip"], SizeBytes = 1 },
            Make.File(@"D:\files\別のファイル.zip")));
        var (main, _, row) = await OpenAsync(app);

        ChoiceRequest? asked = null;
        ChoiceQuestion.Intercept = request =>
        {
            asked = request;
            return ChoiceDialogResult.First;
        };
        try
        {
            await PressAsync(app, row);
        }
        finally
        {
            ChoiceQuestion.Intercept = null;
        }

        Assert.DoesNotContain("ファイルが残りません", asked!.Detail);
        Assert.Equal(OtherId, Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id);
        Assert.Empty((await LocalOfAsync(app, ItemId)).LocalFiles);
        Assert.All((await LocalOfAsync(app, OtherId)).LocalFiles, file => Assert.False(file.Detached));
    });
}
