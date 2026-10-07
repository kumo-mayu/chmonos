using System.IO;

namespace Chmonos.App.Services;

/// <summary>
/// 実行ファイルの隣の <c>assets</c> に同梱する物（辞書2つとカテゴリ表）。
///
/// 無くてもアプリは起動するが、かな・漢字・英語をまたぐ検索とカテゴリの候補が黙って効かなくなる。
/// zip の中から直に開く・exe だけを取り出すと、この形で起動してしまい、使う人には理由が分からなかった（2026-10-07）。
/// 起動したときに1回だけ確かめ、主の窓の帯で知らせる。
/// </summary>
internal static class BundledAssets
{
    /// <summary>同梱する物の名前。読む所は <c>AppServiceContainer</c>（辞書）と <c>CategoryTable.Bundled</c>（カテゴリ表）。</summary>
    public static readonly IReadOnlyList<string> FileNames = ["JMdict_e.gz", "kanjidic2.xml.gz", "booth-categories.json"];

    /// <summary>
    /// 帯の文。何の名前が欠けたかは使う人の次の一手を変えないので出さず、ログにだけ書く。
    /// 「すべて展開」は Windows のエクスプローラの右クリックの項目名。
    /// </summary>
    public const string MissingText = "辞書などのファイルが見つかりません。zipを「すべて展開」してから、展開したフォルダのChmonos.exeを開いてください。";

    /// <summary>アプリの隣の <c>assets</c> に無い物の名前。</summary>
    public static IReadOnlyList<string> Missing()
        => MissingIn(Path.Combine(AppContext.BaseDirectory, "assets"));

    public static IReadOnlyList<string> MissingIn(string assetsDirectory)
        => FileNames.Where(name => !File.Exists(Path.Combine(assetsDirectory, name))).ToList();
}
