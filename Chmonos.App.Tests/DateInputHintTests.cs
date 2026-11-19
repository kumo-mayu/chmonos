using System.Text.RegularExpressions;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 日付の欄の案内に出す例は、どれも欄が実際に読める書き方である（ユーザのメモ 2026-11-18：使える区切りを正しく伝える）。
/// 読み方（<see cref="DateText.Parse"/>）を変えたときに、案内だけが古い書き方を勧めたまま残らないようにする。
/// </summary>
public class DateInputHintTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    /// <summary>案内の文から日付の例（数字で始まり、数字と区切りが続く所）を拾う。</summary>
    private static List<string> Examples(string text)
        => [.. Regex.Matches(text, @"\d[\d/\-.年月日]*\d日?").Select(match => match.Value)];

    private static void AllReadAsSeptemberFirst(IEnumerable<string> examples)
        => Assert.All(examples, example => Assert.Equal(new DateOnly(2026, 9, 1), DateText.Parse(example, isEnd: false, Today)));

    [Fact]
    public void 吹き出しは区切りを3通り見せ_どれも読める()
    {
        var examples = Examples(DateModule.InputHint);

        Assert.Equal(["2026-9-1", "2026/9/1", "2026年9月1日"], examples);
        AllReadAsSeptemberFirst(examples);
        Assert.True(DateModule.InputHint.Length <= 40, "吹き出しは40字まで");
    }

    [Fact]
    public void 検索の欄で読めなかったときの例も_どれも読める()
    {
        var examples = Examples(DateModule.UnreadableNote);

        Assert.Equal(["2026/9/1", "2026年9月1日", "9/1"], examples);
        AllReadAsSeptemberFirst(examples);
    }

    [Fact]
    public Task 編集画面で読めずに保存したときの例も_どれも読める() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900601", "作り物の衣装");
        await app.AddItemAsync(item);
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        await main.ShowEditAsync([item.Id]);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);

        edit.AcquiredAt = "きのう";
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        // 金額の例（「1200」）は除く
        var text = edit.StatusText;
        var examples = Examples(text[..text.IndexOf("金額", StringComparison.Ordinal)]);
        Assert.Equal(["2026-9-1", "2026/9/1", "2026年9月1日"], examples);
        AllReadAsSeptemberFirst(examples);
    });
}
