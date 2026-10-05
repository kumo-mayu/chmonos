using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// 「見つからないファイルを探す」で、どこを探すかを選ぶ窓（見つからない・移動の点検 11-A・ユーザ判断 2026-10-05）。
/// 前は監視フォルダの中だけを探し、ほかの場所へ移した物は監視フォルダに足すしかなかった。
/// </summary>
public class MissingSearchScopeTests
{
    // ---- 窓の中身（窓は出さずに ViewModel で確かめる）----

    [Fact]
    public void 監視フォルダは既定で選んである()
    {
        var model = new MissingSearchScopeViewModel([@"D:\watched", @"E:\more"]);

        Assert.Equal([@"D:\watched", @"E:\more"], model.SelectedFolders);
        Assert.All(model.Rows, row => Assert.Equal("監視フォルダ", row.KindText));
        Assert.True(model.CanSearch);
        Assert.Equal(string.Empty, model.CommitHint);
    }

    [Fact]
    public void 足したフォルダは選んだ状態で並び_今回だけと出る()
    {
        var model = new MissingSearchScopeViewModel([@"D:\watched"]);

        model.AddFolders([@"F:\moved"]);

        Assert.Equal([@"D:\watched", @"F:\moved"], model.SelectedFolders);
        Assert.Equal("今回だけ", model.Rows[1].KindText);
    }

    [Fact]
    public void 並んでいる場所を足しても2行にならず_外していたチェックが入る()
    {
        var model = new MissingSearchScopeViewModel([@"D:\watched"]);
        model.Rows[0].IsChecked = false;

        model.AddFolders([@"d:\Watched\"]);

        Assert.Single(model.Rows);
        Assert.True(model.Rows[0].IsChecked);
    }

    [Fact]
    public void 何も選んでいなければ探せず_理由を出す()
    {
        var model = new MissingSearchScopeViewModel([@"D:\watched"]);

        model.Rows[0].IsChecked = false;

        Assert.False(model.CanSearch);
        Assert.Equal("探すフォルダを1つ以上選んでください。", model.CommitHint);
    }

    [Fact]
    public void 監視フォルダが無ければ_空の一覧で次の手を言い_探せない()
    {
        var model = new MissingSearchScopeViewModel([]);

        Assert.False(model.HasRows);
        Assert.False(model.CanSearch);
        Assert.Equal("監視フォルダがありません。「フォルダを追加…」で探す場所を選んでください。", model.EmptyText);
    }

    // ---- 押したとき ----

    [Fact]
    public Task 足したフォルダの中からも探して結び直し_監視フォルダには足さない() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var original = app.NewFile(@"watched\costume.zip", [9, 8, 7, 6]);
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装").WithFiles(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(original),
            Paths = [original],
            SizeBytes = 4,
        }));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();

        // 監視していない場所へ移した
        var elsewhere = Path.Combine(app.Root, "files", "elsewhere");
        var moved = Path.Combine(elsewhere, "costume.zip");
        Directory.CreateDirectory(elsewhere);
        File.Move(original, moved);

        app.PickSearchScope = model =>
        {
            model.AddFolders([elsewhere]);
            return true;
        };

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(
            () => main.Import.MissingSearchText.StartsWith("1 件を新しい場所に紐付け直しました", StringComparison.Ordinal),
            "探した結果が出る");
        await app.SettleAsync();

        Assert.Equal([watched], Assert.Single(app.SearchScopes).Rows.Where(row => row.IsWatched).Select(row => row.Path));
        Assert.Equal([moved], Assert.Single((await app.Store.Items.LoadAsync("9900001"))!.Local.LocalFiles).Paths);
        Assert.Equal([watched], app.Services.Settings.WatchedFolders);
        Assert.Equal([watched], main.Import.Watched);
    });

    [Fact]
    public Task 窓でキャンセルすると探さず_何も書かない() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "library", "moved-away.zip");
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装").WithFiles(Make.File(gone)));
        var import = (await app.StartAsync()).Import;
        app.PickSearchScope = _ => false;

        import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Single(app.SearchScopes);
        Assert.Equal(string.Empty, import.MissingSearchText);
        Assert.Null(Assert.Single((await app.Store.Items.LoadAsync("9900001"))!.Local.LocalFiles).MissingSince);
        Assert.True(import.FindMissingFilesCommand.CanExecute(null));
    });
}
