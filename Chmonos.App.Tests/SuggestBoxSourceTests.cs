using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 候補付きの欄（<see cref="SuggestBox"/>）。選んで足したあとに候補の元が作り直されても、閉じた窓は開き直さない
/// （メモ73-①。素体の詳細でアバターを足すと、足した後の読み直しで窓が画面の左上に残った）。
/// </summary>
public sealed class SuggestBoxSourceTests
{
    private static (Window Window, SuggestBox Box, Popup Drop) Open(IReadOnlyList<string> source)
    {
        var box = new SuggestBox { Source = source, AllowNew = false, Width = 280 };
        // 画面の外に出して、欄にフォーカスを置く（窓が手前でないとフォーカスは入らない）
        var window = new Window
        {
            Content = box, Width = 400, Height = 300, ShowActivated = true,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = 0,
        };
        window.Show();
        window.Activate();
        var drop = (Popup)box.FindName("DropDown");
        return (window, box, drop);
    }

    [Fact]
    public Task 閉じている窓は_候補の元が差し替わっても開かない() => UiThread.Run(async () =>
    {
        var (window, box, drop) = Open(["あ", "い"]);
        try
        {
            box.FocusInput();
            await UiThread.Settle();
            Assert.True(box.IsKeyboardFocusWithin, "欄にフォーカスが入る");
            drop.IsOpen = false;

            box.Source = new List<string> { "う", "え" };
            await UiThread.Settle();

            Assert.False(drop.IsOpen);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task 開いている窓は_候補の元が差し替わると並びを引き直す() => UiThread.Run(async () =>
    {
        var (window, box, drop) = Open(["あ", "い"]);
        try
        {
            box.FocusInput();
            await UiThread.Settle();
            drop.IsOpen = true;

            box.Source = new List<string> { "う" };
            await UiThread.Settle();

            var candidates = (ListBox)box.FindName("Candidates");
            Assert.Single(candidates.Items);
        }
        finally
        {
            window.Close();
        }
    });
}
