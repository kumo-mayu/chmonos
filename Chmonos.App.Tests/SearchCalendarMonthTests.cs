using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Xml.Linq;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の日付の条件（公開日・入手日）のカレンダーは、開いたとき入力欄の日付の月を見せる（ユーザ判断 2026-10-06・メモ82・84。前は今月で開いた）。
/// </summary>
public class SearchCalendarMonthTests
{
    [Fact]
    public Task 選んである日の月を見せる() => UiThread.Run(() =>
    {
        var calendar = new Calendar { SelectedDate = new DateTime(2024, 6, 15) };
        Assert.NotEqual(new DateTime(2024, 6, 1), new DateTime(calendar.DisplayDate.Year, calendar.DisplayDate.Month, 1));

        CalendarMonth.ShowSelectedMonth(calendar);

        Assert.Equal((2024, 6), (calendar.DisplayDate.Year, calendar.DisplayDate.Month));
    });

    [Fact]
    public Task 別の月へ送ってから開き直しても_選んである日の月へ戻る() => UiThread.Run(() =>
    {
        var calendar = new Calendar { SelectedDate = new DateTime(2024, 6, 15) };
        calendar.DisplayDate = new DateTime(2023, 1, 1);
        calendar.DisplayMode = CalendarMode.Year;

        CalendarMonth.ShowSelectedMonth(calendar);

        Assert.Equal((2024, 6), (calendar.DisplayDate.Year, calendar.DisplayDate.Month));
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
    });

    [Fact]
    public Task 選んである日が無ければ今月() => UiThread.Run(() =>
    {
        var calendar = new Calendar { DisplayDate = new DateTime(2023, 1, 1) };

        CalendarMonth.ShowSelectedMonth(calendar);

        Assert.Equal((DateTime.Today.Year, DateTime.Today.Month), (calendar.DisplayDate.Year, calendar.DisplayDate.Month));
    });

    /// <summary>画面の元の文（XAML）を読む（試験では View を作れない。<c>DeleteIsRedTests</c> と同じ）。</summary>
    [Fact]
    public void 検索の日付のカレンダーの吹き出しは_どれも月を合わせる()
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), "SearchView.xaml"));
        var popups = document.Descendants()
            .Where(e => e.Name.LocalName == "Popup" && e.Elements().Any(child => child.Name.LocalName == "Calendar"))
            .ToList();

        Assert.Equal(2, popups.Count);
        Assert.All(popups, popup => Assert.Equal(
            "True",
            (string?)popup.Attributes().FirstOrDefault(a => a.Name.LocalName == "CalendarMonth.FollowsSelection")));
    }

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
