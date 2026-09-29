using System.Globalization;
using System.Text.RegularExpressions;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 色の表（App の <c>Themes/Light.xaml</c>・<c>Themes/Dark.xaml</c>）の決まり（docs/spec/ui-colors.md）。
/// App には試験の一式が無いので、XAML を文字として読んで確かめる。
/// </summary>
public class ThemeTableTests
{
    private static readonly string AppDir = FindAppDir();

    private static string FindAppDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var app = Path.Combine(dir.FullName, "BoothAssetManager.App");
            if (Directory.Exists(Path.Combine(app, "Themes")))
            {
                return app;
            }
        }

        throw new DirectoryNotFoundException("BoothAssetManager.App/Themes が見つからない");
    }

    private static Dictionary<string, (string Kind, string Value)> Table(string name)
    {
        var text = File.ReadAllText(Path.Combine(AppDir, "Themes", name));
        var table = new Dictionary<string, (string, string)>();
        foreach (Match match in Regex.Matches(text, """<(\w+) x:Key="([^"]+)"(?: Color="([^"]+)")?[^>]*>(?:([^<]+)</\1>)?"""))
        {
            table[match.Groups[2].Value] = (match.Groups[1].Value, match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value);
        }

        return table;
    }

    private static IEnumerable<string> ScreenXaml() => Directory
        .EnumerateFiles(AppDir, "*.xaml", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                       && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                       && Path.GetFileName(path) is not ("Light.xaml" or "Dark.xaml"));

    [Fact]
    public void 明るい表と暗い表は同じ鍵を同じ型で持つ()
    {
        var light = Table("Light.xaml");
        var dark = Table("Dark.xaml");

        Assert.NotEmpty(light);
        Assert.Equal(light.Keys.Order(), dark.Keys.Order());
        foreach (var key in light.Keys)
        {
            Assert.True(light[key].Kind == dark[key].Kind, $"{key}: {light[key].Kind} と {dark[key].Kind}");
        }
    }

    [Fact]
    public void 画面は色を直に書かない()
    {
        // 色の表の外で色を書くと、暗い表に切り替えてもそこだけ変わらない
        var direct = new Regex(@"=""#[0-9a-fA-F]{3,8}""|(?:Foreground|Background|BorderBrush|Fill|Stroke|Color|Value|CaretBrush|SelectionBrush)=""(?:White|Black|Red|Gray|Grey|Silver|Blue|Green|Orange|Yellow|LightGray|DarkGray|WhiteSmoke|Gainsboro)""");
        var found = ScreenXaml()
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(entry => direct.IsMatch(entry.line))
            .Select(entry => $"{Path.GetFileName(entry.path)}:{entry.index + 1}: {entry.line.Trim()}")
            .ToList();

        Assert.True(found.Count == 0, string.Join('\n', found));
    }

    [Fact]
    public void 色の鍵はDynamicResourceで指す()
    {
        // StaticResource は読み込んだ時の色を持ち続けるので、表を差し替えても変わらない
        var keys = Table("Light.xaml").Keys.ToHashSet();
        var found = ScreenXaml()
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\{StaticResource ([A-Za-z0-9_]+)\}")
                .Where(match => keys.Contains(match.Groups[1].Value))
                .Select(match => $"{Path.GetFileName(path)}: {match.Groups[1].Value}"))
            .ToList();

        Assert.True(found.Count == 0, string.Join('\n', found));
    }

    [Theory]
    // 本文（4.5:1）
    [InlineData("Text", "Bg", 4.5)]
    [InlineData("Text", "Surface", 4.5)]
    [InlineData("Text", "SurfaceAlt", 4.5)]
    [InlineData("Text", "InputBack", 4.5)]
    [InlineData("TextBody", "Surface", 4.5)]
    [InlineData("TextMuted", "Surface", 4.5)]
    [InlineData("TextMuted", "Bg", 4.5)]
    [InlineData("TextMuted", "SurfaceAlt", 4.5)]
    [InlineData("TextMuted", "AccentSoft", 4.5)]
    [InlineData("TextFaint", "Surface", 4.5)]
    [InlineData("TextFaint", "Bg", 4.5)]
    [InlineData("Accent", "Surface", 4.5)]
    [InlineData("Accent", "Bg", 4.5)]
    [InlineData("Accent", "AccentSoft", 4.5)]
    [InlineData("AccentText", "Surface", 4.5)]
    [InlineData("AccentText", "AccentSoft", 4.5)]
    [InlineData("AccentSoftText", "AccentSoft", 4.5)]
    [InlineData("OnAccent", "AccentFill", 4.5)]
    [InlineData("OnAccent", "AccentHover", 4.5)]
    [InlineData("OnAccent", "GoodFill", 4.5)]
    [InlineData("OnAccent", "BadFill", 4.5)]
    [InlineData("OnAccent", "UnreadFill", 4.5)]
    [InlineData("OnAccent", "NeedsWorkFill", 4.5)]
    [InlineData("Good", "Surface", 4.5)]
    [InlineData("Good", "GoodSoft", 4.5)]
    [InlineData("Warn", "Surface", 4.5)]
    [InlineData("Warn", "WarnSoft", 4.5)]
    [InlineData("Bad", "Surface", 4.5)]
    [InlineData("Bad", "BadSoft", 4.5)]
    [InlineData("DangerText", "Surface", 4.5)]
    [InlineData("Unread", "Surface", 4.5)]
    [InlineData("Unread", "UnreadSoft", 4.5)]
    [InlineData("ChipText", "ChipBack", 4.5)]
    [InlineData("RailText", "Rail", 4.5)]
    [InlineData("RailText", "RailActive", 4.5)]
    [InlineData("RailText", "RailHover", 4.5)]
    [InlineData("RailMuted", "Rail", 4.5)]
    [InlineData("RailSubtitle", "Rail", 4.5)]
    [InlineData("RailNoticeText", "RailNoticeBack", 4.5)]
    // 標準の部品の文字（Themes/Controls.xaml）
    [InlineData("MenuText", "MenuBack", 4.5)]
    [InlineData("MenuText", "MenuHover", 4.5)]
    [InlineData("ToolTipText", "ToolTipBack", 4.5)]
    [InlineData("Text", "ComboBack", 4.5)]
    [InlineData("Text", "ListHoverBack", 4.5)]
    [InlineData("Text", "ListSelectedBack", 4.5)]
    [InlineData("Text", "ColumnHeaderBack", 4.5)]
    // 部品と印（3:1）
    [InlineData("InputBorder", "InputBack", 3)]
    [InlineData("CheckBorder", "Surface", 3)]
    [InlineData("ScrollThumb", "ScrollTrack", 3)]
    [InlineData("SliderThumb", "SliderTrack", 3)]
    [InlineData("Star", "Surface", 3)]
    [InlineData("ChipRemove", "ChipBack", 3)]
    [InlineData("Accent", "BarTrack", 3)]
    [InlineData("RailLabel", "Rail", 3)]
    public void 暗い表の文字と地はAAを満たす(string foreground, string background, double required)
    {
        var dark = Table("Dark.xaml");
        var ratio = Contrast(dark[foreground].Value, dark[background].Value, dark["Surface"].Value);

        Assert.True(ratio >= required, $"{foreground} / {background}: {ratio:F2}（{required} 以上が要る）");
    }

    /// <summary>WCAG 2.x のコントラスト比。透ける色は、地は面（Surface）に、文字は地に重ねてから比べる。</summary>
    private static double Contrast(string foreground, string background, string surface)
    {
        var under = Rgb(surface, null);
        var back = Rgb(background, under);
        var fore = Rgb(foreground, back);
        var (a, b) = (Luminance(fore), Luminance(back));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double[] Rgb(string hex, double[]? under)
    {
        var digits = hex.TrimStart('#');
        var alpha = 1.0;
        if (digits.Length == 8)
        {
            alpha = int.Parse(digits[..2], NumberStyles.HexNumber) / 255.0;
            digits = digits[2..];
        }

        var rgb = new[] { 0, 2, 4 }.Select(i => (double)int.Parse(digits.Substring(i, 2), NumberStyles.HexNumber)).ToArray();
        return under is null ? rgb : rgb.Select((value, i) => (value * alpha) + (under[i] * (1 - alpha))).ToArray();
    }

    private static double Luminance(double[] rgb)
    {
        var linear = rgb.Select(value =>
        {
            var channel = value / 255;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }).ToArray();
        return (0.2126 * linear[0]) + (0.7152 * linear[1]) + (0.0722 * linear[2]);
    }
}
