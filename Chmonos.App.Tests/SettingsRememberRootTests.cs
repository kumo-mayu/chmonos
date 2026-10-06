using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 運ばずに場所だけ変える（「場所だけ変える」「選んだ場所のデータを使う」）ときの location.json の書き換え（2026-10-06）。
/// 置き場は <see cref="AppPaths.DefaultRootVariable"/> で試験の保存先の中へ向ける（向けないと守りが止める。本番には書かない）。
/// 試験は並べて走らせないので、プロセスに1つの環境変数をこの中で入れて外せる
/// </summary>
public class SettingsRememberRootTests
{
    [Fact]
    public Task 場所を覚えられたら_次の起動はそこを指す() => WithDefaultHome(async (app, home) =>
    {
        var settings = await OpenSettingsAsync(app);
        var picked = Path.Combine(app.Root, "next", "Chmonos");

        Assert.True(settings.TryRememberRoot(picked));

        Assert.Contains(picked.Replace("\\", "\\\\"), File.ReadAllText(Path.Combine(home, "location.json")));
        Assert.Empty(app.Notices);
    });

    /// <summary>前は投げっぱなしで、書けないと何も言わずに止まっていた。読み取り専用の location.json で書けない場面を作る</summary>
    [Fact]
    public Task 場所を覚えられなければ_窓で知らせて_前の場所のまま() => WithDefaultHome(async (app, home) =>
    {
        var settings = await OpenSettingsAsync(app);
        var location = Path.Combine(home, "location.json");
        Directory.CreateDirectory(home);
        File.WriteAllText(location, "{\"root\":\"C:\\\\before\"}");
        File.SetAttributes(location, FileAttributes.ReadOnly);

        try
        {
            Assert.False(settings.TryRememberRoot(Path.Combine(app.Root, "next", "Chmonos")));

            var notice = Assert.Single(app.Notices);
            Assert.Equal("保存先を変えられませんでした", notice.Caption);
            Assert.StartsWith("新しい保存先の場所を記録できませんでした。", notice.Text);
            Assert.EndsWith("保存先は今のままです。", notice.Text);
            Assert.Equal("{\"root\":\"C:\\\\before\"}", File.ReadAllText(location));
            Assert.False(settings.HasPendingRoot);
        }
        finally
        {
            File.SetAttributes(location, FileAttributes.Normal);
        }
    });

    private static Task WithDefaultHome(Func<TestApp, string, Task> body) => TestApp.Run(async app =>
    {
        app.AllowLoggedFailures = true;
        var before = Environment.GetEnvironmentVariable(AppPaths.DefaultRootVariable);
        var home = Path.Combine(app.Root, "default-home");
        Environment.SetEnvironmentVariable(AppPaths.DefaultRootVariable, home);
        try
        {
            await body(app, home);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppPaths.DefaultRootVariable, before);
        }
    });

    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app)
    {
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
    }
}
