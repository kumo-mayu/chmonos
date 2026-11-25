using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 1件だけ送った後に Unity で選ぶフォルダ（<see cref="UnityImportedFolder"/>）。パスは作り物。
/// </summary>
public sealed class UnityImportedFolderTests
{
    public static TheoryData<string[], string?> Cases => new()
    {
        // よくある形：作者 → 商品 → 中身が分かれる。商品のフォルダを選ぶ
        { ["Assets/Author/Product/a.prefab", "Assets/Author/Product/Textures/b.png"], "Assets/Author/Product" },

        // unitypackage にはフォルダの行も混ざる。混ざっても同じ所で止まる
        { ["Assets/Author", "Assets/Author/Product", "Assets/Author/Product/a.prefab", "Assets/Author/Product/b.mat"], "Assets/Author/Product" },

        // 1つの子しか無い段は、何段でもたどる
        { ["Assets/A/B/C/D/x.png", "Assets/A/B/C/D/y.png"], "Assets/A/B/C/D" },

        // 作者のフォルダで分かれる（商品と共通の部品が並ぶ）なら、作者のフォルダ
        { ["Assets/Author/Product/a.prefab", "Assets/Author/Common/b.shader"], "Assets/Author" },

        // ファイルが1つだけ：そのファイルのあるフォルダ
        { ["Assets/Author/Product/only.prefab"], "Assets/Author/Product" },
        { ["Assets/Author", "Assets/Author/Product", "Assets/Author/Product/only.prefab"], "Assets/Author/Product" },

        // フォルダの中にファイルと子フォルダが1つずつ：そこで分かれている
        { ["Assets/Author/readme.txt", "Assets/Author/Product/a.prefab"], "Assets/Author" },

        // 拡張子の無いファイル・点の入ったフォルダも、木の形だけで決める
        { ["Assets/Author/v1.2/LICENSE", "Assets/Author/v1.2/a.prefab"], "Assets/Author/v1.2" },

        // 大文字小文字の違い・\ 区切り・末尾の / は同じフォルダ
        { ["Assets/Author/Product/a.prefab", "assets\\author\\product\\b.png", "Assets/Author/Product/"], "Assets/Author/Product" },

        // Assets の直下で分かれる（同梱の部品が別の頭のフォルダに入る）：選ばない
        { ["Assets/Author/Product/a.prefab", "Assets/Shader/b.shader"], null },

        // Assets の直下にファイルとフォルダが1つずつ：選ばない
        { ["Assets/readme.txt", "Assets/Author/Product/a.prefab"], null },

        // Assets の直下のファイルだけ：選ぶフォルダが Assets しか無いので選ばない
        { ["Assets/x.png"], null },

        // Assets の外（Packages）が混ざる・Packages だけ：選ばない
        { ["Assets/Author/Product/a.prefab", "Packages/com.example.tool/package.json"], null },
        { ["Packages/com.example.tool/package.json", "Packages/com.example.tool/Runtime/a.cs"], null },

        // 空
        { [], null },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Pick(string[] paths, string? expected)
        => Assert.Equal(expected, UnityImportedFolder.Pick(paths));

    [Theory]
    [InlineData(true, true, false, null, true)] // 取り込んだ・既に全部入っていた
    [InlineData(false, true, false, null, false)] // 設定が切
    [InlineData(true, false, false, "送れませんでした", false)] // 取り込み画面を出せなかった
    [InlineData(true, true, true, null, false)] // Cancel
    [InlineData(true, true, false, "止めました", false)] // 止めた・待ちきれなかった・Unity ごと閉じられた
    public void ShouldSelect(bool enabled, bool opened, bool cancelled, string? problem, bool expected)
        => Assert.Equal(expected, UnityImportedFolder.ShouldSelect(enabled, opened, cancelled, problem));
}
