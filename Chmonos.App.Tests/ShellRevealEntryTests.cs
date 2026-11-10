using System.Diagnostics;
using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// エクスプローラで開くのは <see cref="Shell"/> 1つを通す（B7・2026-11-10・ui-rules.md「エクスプローラで開く」）。
/// 保存先を開く・写しかけのフォルダを開くは、前は自分で explorer を起こしていて、開き方も試験の差し替えも外れていた。
/// 差し替え（<see cref="Shell.StartOverride"/>）に届けば、Shell を通っている
/// </summary>
public class ShellRevealEntryTests
{
    private sealed class Crash : Exception;

    private sealed class CrashAtTheEnd : IProgress<StoreMoveProgress>
    {
        public void Report(StoreMoveProgress value)
        {
            if (value.Copied == value.Total)
            {
                throw new Crash();
            }
        }
    }

    [Fact]
    public Task 設定の保存先を開くは_Shellを通ってそのフォルダを開く() => TestApp.Run(async app =>
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            var main = await app.StartAsync();
            main.ShowSettingsCommand.Execute(null);
            var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);

            settings.OpenRootCommand.Execute(null);
            await UiThread.Until(() => started.Count > 0, "エクスプローラを起こす");

            var start = Assert.Single(started);
            Assert.Equal("explorer.exe", start.FileName);
            Assert.Equal($"\"{settings.RootPath}\"", start.Arguments);
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    });

    [Fact]
    public Task 写しかけのフォルダを開くは_Shellを通る() => TestApp.Run(async app =>
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            var source = Path.Combine(app.Root, "old-store");
            Directory.CreateDirectory(Path.Combine(source, "items"));
            File.WriteAllText(Path.Combine(source, "settings.json"), "{}");
            var folder = Path.Combine(app.Root, "picked", "Chmonos");
            Directory.CreateDirectory(folder);
            Assert.Throws<Crash>(() => StoreMover.Move(source, folder, new CrashAtTheEnd()));

            await app.StartAsync();
            app.Choose = request => request.Title == UnfinishedCopyPrompt.Title
                ? Views.ChoiceDialogResult.Second
                : Views.ChoiceDialogResult.Cancel;

            Assert.False(await UnfinishedCopyPrompt.ResolveAsync(folder));
            await UiThread.Until(() => started.Count > 0, "エクスプローラを起こす");

            var start = Assert.Single(started);
            Assert.Equal("explorer.exe", start.FileName);
            Assert.Equal($"\"{folder}\"", start.Arguments);
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    });
}
