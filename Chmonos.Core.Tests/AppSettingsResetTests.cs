using Chmonos.Core.Models;

namespace Chmonos.Core.Tests;

/// <summary>「すべての設定を既定に戻す」（メモ29）。使う人の環境に特有の物（取り込み元・監視するフォルダ）だけ残り、ほかは既定に戻る。</summary>
public class AppSettingsResetTests
{
    private static readonly string[] Kept = [nameof(AppSettings.ImportFolders), nameof(AppSettings.WatchedFolders)];

    private static AppSettings Customized() => new()
    {
        ImportFolders = ["D:\\作り物\\取り込み元"],
        WatchedFolders = ["D:\\作り物\\監視"],
        ShowAdult = false,
        ShowSortDividers = false,
        ShowAcquiredSortDividers = true,
        CardWidth = 300,
        ListRowHeight = 80,
        DisplayZoomPercent = 150,
        ColorTheme = ColorThemeMode.Dark,
        ProjectManager = ProjectManagerChoice.Alcom,
        CardAttributes = ["作り物の属性"],
        ReturnToSearchWhenEditDone = false,
        StartImportOnDrop = false,
        StartImportOnLaunch = true,
        RefreshIntervalDays = 30,
        FetchIntervalMs = 9000,
        SaveImages = false,
        ImageMaxEdgePixels = 1024,
        SearchHistoryCount = 3,
        AvatarSupportHeadings = ["作り物の見出し"],
        Shortcuts = new ShortcutSettings { SaveAndNext = "Ctrl+S", Back = "" },
    };

    [Fact]
    public void 戻すと_取り込み元と監視するフォルダだけ残り_ほかは既定に戻る()
    {
        var reset = Customized().ResetToDefaults();
        var defaults = new AppSettings();

        Assert.Equal(["D:\\作り物\\取り込み元"], reset.ImportFolders);
        Assert.Equal(["D:\\作り物\\監視"], reset.WatchedFolders);

        foreach (var property in typeof(AppSettings).GetProperties().Where(p => !Kept.Contains(p.Name)))
        {
            var actual = property.GetValue(reset);
            var expected = property.GetValue(defaults);
            if (actual is System.Collections.IEnumerable list && actual is not string)
            {
                Assert.True(
                    ((System.Collections.IEnumerable)expected!).Cast<object>().SequenceEqual(list.Cast<object>()),
                    $"{property.Name} が既定に戻っていません");
            }
            else
            {
                Assert.True(Equals(expected, actual), $"{property.Name} が既定に戻っていません");
            }
        }
    }

    [Fact]
    public void 戻した設定の通信の間隔は_約束の下限を割らない()
    {
        Assert.Equal(AppSettings.MinFetchIntervalMs, Customized().ResetToDefaults().FetchIntervalMs);
    }
}
