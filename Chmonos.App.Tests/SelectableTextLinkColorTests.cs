using System.Windows.Documents;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 説明文の中のリンクの色（2026-11-12）。コードで組む文書のリンクには App.xaml の Hyperlink の型のスタイルが届かず、
/// Windows の既定の #0066cc（暗い面で 2.9:1）で出ていた。色の表の試験は表の値しか見ないので、配線はここで見る。
/// </summary>
public class SelectableTextLinkColorTests
{
    [Fact]
    public Task 外へ出るリンクは色の表のLinkで描く() => UiThread.Run(() =>
    {
        var document = SelectableText.Build("導入の手引き：https://example.invalid/guide", navigator: null);
        var probe = new SolidColorBrush(Colors.Magenta);
        document.Resources["Link"] = probe;

        var link = Assert.Single(document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<Hyperlink>());

        Assert.Same(probe, link.Foreground);
    });
}
