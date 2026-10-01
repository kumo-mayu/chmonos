using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// サービス一式を作る前に、設定の表示の色だけを読むこと（ユーザ判断 2026-09-30）。
/// 「既に起動しています」の窓を設定の色で出すために使う。読めない場面（初回・壊れた設定）では null で、Windows に合わせる。
/// </summary>
public class ColorThemePeekTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-peek-" + Guid.NewGuid().ToString("N"));

    public ColorThemePeekTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string Settings(string json)
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>アプリが書いた設定そのものから読める（書く側と読む側で、欄の名前と値の書き方が揃っている）。</summary>
    [Theory]
    [InlineData(ColorThemeMode.Light)]
    [InlineData(ColorThemeMode.Dark)]
    [InlineData(ColorThemeMode.System)]
    public void ReadsWhatTheAppWrote(ColorThemeMode mode)
    {
        var path = Settings(JsonSerializer.Serialize(new AppSettings { ColorTheme = mode }, JsonStore.Options));

        Assert.Equal(mode, ColorThemePeek.Read(path));
    }

    /// <summary>欄が無い設定は既定（Windows に合わせる）。設定は在るので null ではない。</summary>
    [Fact]
    public void MissingFieldIsTheDefault()
        => Assert.Equal(ColorThemeMode.System, ColorThemePeek.Read(Settings("""{ "fetchIntervalMs": 1500 }""")));

    /// <summary>手で直した設定（コメント・末尾のカンマ）も、ほかの読み方と同じく読める。</summary>
    [Fact]
    public void ReadsAHandEditedFile()
        => Assert.Equal(ColorThemeMode.Dark, ColorThemePeek.Read(Settings("""
            {
              // 暗くした
              "colorTheme": "dark",
            }
            """)));

    /// <summary>色のほかの欄が読めない形でも、色は読める（設定の全部を読まない）。</summary>
    [Fact]
    public void IgnoresOtherFields()
        => Assert.Equal(ColorThemeMode.Light, ColorThemePeek.Read(Settings(
            """{ "colorTheme": "light", "fetchIntervalMs": "速く", "importFolders": 3 }""")));

    /// <summary>初回（設定がまだ無い）・保存先が無いときは null。呼ぶ側は Windows に合わせる。</summary>
    [Fact]
    public void NoSettingsIsNull()
    {
        Assert.Null(ColorThemePeek.Read(Path.Combine(_dir, "settings.json")));
        Assert.Null(ColorThemePeek.Read(Path.Combine(_dir, "無いフォルダ", "settings.json")));
    }

    /// <summary>壊れた設定・知らない値は、投げずに null（起動を色のために止めない）。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{ \"colorTheme\": ")]
    [InlineData("{ \"colorTheme\": \"purple\" }")]
    [InlineData("{ \"colorTheme\": { } }")]
    [InlineData("[]")]
    public void UnreadableIsNull(string json)
        => Assert.Null(ColorThemePeek.Read(Settings(json)));

    /// <summary>読むだけで、ファイルにもフォルダにも触らない。</summary>
    [Fact]
    public void DoesNotWrite()
    {
        var path = Settings("""{ "colorTheme": "dark" }""");
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        ColorThemePeek.Read(path);
        ColorThemePeek.Read(Path.Combine(_dir, "無いフォルダ", "settings.json"));

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(["settings.json"], Directory.EnumerateFileSystemEntries(_dir).Select(Path.GetFileName));
    }

    /// <summary>ほかの起動中のアプリが設定を書き込みで開いていても読める（2つ目の起動の窓のために読むので、これが本番の場面）。</summary>
    [Fact]
    public void ReadsWhileAnotherProcessHoldsTheFileForWriting()
    {
        var path = Settings("""{ "colorTheme": "light" }""");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(ColorThemeMode.Light, ColorThemePeek.Read(path));
    }
}
