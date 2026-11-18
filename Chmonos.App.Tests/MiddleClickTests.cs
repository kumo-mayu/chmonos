using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 中クリックでブラウザ（BOOTH）を開く（ユーザ指摘 2026-11-18・open.md 136）。
/// 説明文の URL はライブラリにある商品のリンクでもブラウザで開き、押せる字（ショップ名・ID）は左クリックと別の行き先（BOOTH）を開く。
/// 本物のブラウザは開かず、外のアプリを起こす所（<see cref="Shell.StartOverride"/>）で受ける。
/// 差し替えは流れごとに持つので、画面のスレッドの中で入れる。
/// </summary>
public sealed class MiddleClickTests
{
    private sealed class LibraryNavigator : IInAppLinkNavigator
    {
        public List<Uri> Opened { get; } = [];

        public bool CanNavigate(Uri uri) => uri.Host == "booth.pm";

        public void Navigate(Uri uri) => Opened.Add(uri);
    }

    private static MouseButtonEventArgs Press(MouseButton button)
        => new(Mouse.PrimaryDevice, 0, button) { RoutedEvent = Mouse.MouseDownEvent };

    private static Hyperlink LinkIn(FlowDocument document, string url)
        => document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<Hyperlink>()
            .Single(link => link.NavigateUri?.AbsoluteUri == url);

    [Fact]
    public Task 説明文のリンクは_ライブラリにある商品のリンクでも中クリックでブラウザで開く() => UiThread.Run(() =>
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            var navigator = new LibraryNavigator();
            var document = SelectableText.Build(
                "持っている物：https://booth.pm/ja/items/9900001 手引き：https://example.invalid/guide", navigator);

            var inLibrary = Press(MouseButton.Middle);
            LinkIn(document, "https://booth.pm/ja/items/9900001").RaiseEvent(inLibrary);
            var outside = Press(MouseButton.Middle);
            LinkIn(document, "https://example.invalid/guide").RaiseEvent(outside);

            Assert.Equal(["https://booth.pm/ja/items/9900001", "https://example.invalid/guide"], started.Select(info => info.FileName));
            Assert.Empty(navigator.Opened);
            Assert.True(inLibrary.Handled && outside.Handled, "親（説明文の欄）へ流さない");
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    });

    [Fact]
    public Task 説明文のリンクは_左や右のボタンを下ろしただけでは開かない() => UiThread.Run(() =>
    {
        var started = new List<ProcessStartInfo>();
        Shell.StartOverride.Value = started.Add;
        try
        {
            var link = LinkIn(SelectableText.Build("https://example.invalid/guide", navigator: null), "https://example.invalid/guide");

            link.RaiseEvent(Press(MouseButton.Right));
            link.RaiseEvent(Press(MouseButton.XButton1));

            Assert.Empty(started);
        }
        finally
        {
            Shell.StartOverride.Value = null;
        }
    });

    [Fact]
    public Task IDの字は_左は写す命令のまま_中クリックでBOOTHの命令を行の値で呼ぶ() => UiThread.Run(() =>
    {
        object? opened = "未";
        var copied = 0;
        var link = new Hyperlink(new Run("ID 9900001")) { Command = new RelayCommand(() => copied++) };
        MiddleClick.SetCommand(link, new RelayCommand(parameter => opened = parameter));
        MiddleClick.SetCommandParameter(link, "行");
        _ = new TextBlock(link);

        var left = Press(MouseButton.Left);
        link.RaiseEvent(left);
        Assert.Equal("未", opened);

        var middle = Press(MouseButton.Middle);
        link.RaiseEvent(middle);
        Assert.Equal("行", opened);
        Assert.True(middle.Handled);
        Assert.Equal(0, copied);
    });

    [Fact]
    public Task 行き先の無い字は_中クリックを済ませずに親へ流す() => UiThread.Run(() =>
    {
        var button = new Button();
        MiddleClick.SetCommand(button, new RelayCommand(() => throw new InvalidOperationException("押せない命令を呼んだ"), () => false));

        var middle = Press(MouseButton.Middle);
        button.RaiseEvent(middle);

        Assert.False(middle.Handled);
    });

    [Fact]
    public Task なぞれるショップ名は_中クリックでBOOTHの命令を呼ぶ() => UiThread.Run(() =>
    {
        var shopScreen = 0;
        var booth = 0;
        // 字の上かは本物のマウスの位置で決まるので、試験では行全体で開く形（OpensOnBlank）にして配線だけを見る
        var link = new SelectableLink
        {
            OpensOnBlank = true,
            Command = new RelayCommand(() => shopScreen++),
            MiddleCommand = new RelayCommand(() => booth++),
        };

        var middle = Press(MouseButton.Middle);
        link.RaiseEvent(middle);

        Assert.Equal(1, booth);
        Assert.Equal(0, shopScreen);
        Assert.True(middle.Handled);
    });
}
