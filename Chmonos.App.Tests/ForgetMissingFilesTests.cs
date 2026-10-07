using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定のデータの節の「見つからないファイル」の「記録をすべて削除…」（ユーザ判断 2026-10-07）。
/// 件数を見せる確かめ1回で、全部の商品から見つからない記録を消す。キャンセルなら何も変えない
/// </summary>
public sealed class ForgetMissingFilesTests
{
    private static ItemRecord WithMissingFile(string id, string name, string path)
        => Make.Item(id, name).WithFiles(Make.File(path) with { MissingSince = DateTimeOffset.Now.AddDays(-1) });

    [Fact]
    public Task 確かめでOKすると_見つからない記録を消して件数を言う() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(Path.GetTempPath(), "chmonos-gone-" + Guid.NewGuid().ToString("N")[..8]);
        await app.AddItemAsync(WithMissingFile("9900901", "作り物の消えた衣装", Path.Combine(gone, "a.zip")));
        await app.AddItemAsync(WithMissingFile("9900902", "作り物の消えた髪", Path.Combine(gone, "b.zip")));
        var main = await app.StartAsync();
        app.Answer = _ => MessageBoxResult.OK;

        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        settings.ForgetMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        var asked = Assert.Single(app.Notices, request => request.Caption == "見つからないファイルの記録を削除");
        Assert.StartsWith("2 商品の、見つからないファイル 2 件・フォルダ 0 件の記録を削除します。", asked.Text);
        Assert.Contains("2 商品は、未所持になります。", asked.Text);
        Assert.Equal("2 商品から、見つからないファイル 2 件・フォルダ 0 件の記録を削除しました。", settings.ForgetMissingNote);
        Assert.Empty((await app.Services.Store.Items.LoadAsync("9900901"))!.Local.LocalFiles);
        Assert.Empty((await app.Services.Store.Items.LoadAsync("9900902"))!.Local.LocalFiles);
    });

    [Fact]
    public Task キャンセルなら何も変えない() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(Path.GetTempPath(), "chmonos-gone-" + Guid.NewGuid().ToString("N")[..8]);
        await app.AddItemAsync(WithMissingFile("9900903", "作り物の消えた靴", Path.Combine(gone, "c.zip")));
        var main = await app.StartAsync();

        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        settings.ForgetMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Single(app.Notices, request => request.Caption == "見つからないファイルの記録を削除");
        Assert.Single((await app.Services.Store.Items.LoadAsync("9900903"))!.Local.LocalFiles);
        Assert.Equal(string.Empty, settings.ForgetMissingNote);
    });

    [Fact]
    public Task 見つからない記録が無ければ_確かめを出さずにそう言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900904", "作り物の衣装"));
        var main = await app.StartAsync();

        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        settings.ForgetMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.DoesNotContain(app.Notices, request => request.Caption == "見つからないファイルの記録を削除");
        Assert.Equal("見つからないファイルはありません。", settings.ForgetMissingNote);
    });
}
