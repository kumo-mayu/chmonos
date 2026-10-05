using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Chmonos.App.Tests;

/// <summary>
/// ナビのボタンにも、キーボードで止まったときの枠が付いていること（ユーザ判断 2026-10-05）。
/// 前は既定の点線のままで、暗いナビの上では見えなかった。窓の無い試験では描けないので、元の文（XAML）で型の付与を確かめる。
/// </summary>
public class RailFocusVisualTests
{
    [Theory]
    [InlineData("NavButton")]
    [InlineData("RailSmallButton")]
    public void ナビのボタンの型は_ナビ用の止まり枠を使う(string styleKey)
    {
        var document = XDocument.Load(MainWindowPath());
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var style = Assert.Single(document.Descendants(), e => e.Name.LocalName == "Style" && (string?)e.Attribute(x + "Key") == styleKey);
        var setter = Assert.Single(style.Elements(), e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "FocusVisualStyle");
        Assert.Contains("RailFocusVisual", (string?)setter.Attribute("Value"));
    }

    private static string MainWindowPath([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "MainWindow.xaml");
}
