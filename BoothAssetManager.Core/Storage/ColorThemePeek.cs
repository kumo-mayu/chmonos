using System.Text.Json;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 設定の表示の色（<c>settings.json</c> の <c>colorTheme</c>）だけを、サービス一式を作る前に読む（ユーザ判断 2026-09-30）。
///
/// 起動の最初は Windows の色を当て、設定の色はサービス一式ができてから当て直していた。その間に出る窓
/// （「既に起動しています」）は、表示の色を「明るい」にしていても Windows が暗ければ暗く出た（大容量の確かめ 2026-09-30）。
/// サービス一式は保存先のフォルダを作り、2つ目の起動かどうかを確かめるので、色のためだけに先に作れない。
///
/// **読むだけで、何も書かない・作らない。**読めなければ null を返し、呼ぶ側は今までどおり Windows に合わせる。
/// </summary>
public static class ColorThemePeek
{
    /// <summary>今の保存先の設定から読む。保存先が無い・設定がまだ無い（初回）・読めないなら null。</summary>
    public static ColorThemeMode? Read()
    {
        string settingsFile;
        try
        {
            settingsFile = new AppPaths(StoreLocation.Resolve().Path).SettingsFile;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException
            or System.Security.SecurityException)
        {
            // 環境変数の保存先が道として読めない。色のために起動を止めない（保存先の確かめが同じ所で扱う）
            return null;
        }

        return Read(settingsFile);
    }

    /// <param name="settingsFile"><c>settings.json</c> の場所。</param>
    public static ColorThemeMode? Read(string settingsFile)
    {
        try
        {
            if (!File.Exists(settingsFile))
            {
                return null;
            }

            // アプリがもう1つ動いていて書いている最中でも読めるように、書き込みを妨げない開き方にする
            using var stream = new FileStream(
                settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // 設定の全部ではなく、この1欄だけを同じ読み方（名前の付け方・列挙の書き方・コメントと末尾のカンマ）で読む。
            // ほかの欄が手で直されて読めなくなっていても、色は読める。欄が無ければ既定（Windows に合わせる）
            return JsonSerializer.Deserialize<ThemeOnly>(stream, JsonStore.Options)?.ColorTheme;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            // 壊れた設定・知らない値・つながっていないドライブ。ここでは何も言わない——
            // 設定が読めないことは、この後でサービス一式が同じファイルを読むところで扱う
            return null;
        }
    }

    private sealed class ThemeOnly
    {
        public ColorThemeMode ColorTheme { get; init; } = ColorThemeMode.System;
    }
}
