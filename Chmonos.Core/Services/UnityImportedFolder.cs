namespace Chmonos.Core.Services;

/// <summary>
/// 1件だけ Unity へ送った後に、入った物として Unity で選ぶフォルダを決める（設定「1件だけ送った時、入った物を Unity で選ぶ」。ユーザ判断 2026-11-25）。
///
/// **中身が枝分かれする最初のフォルダを選ぶ。**unitypackage の多くは <c>Assets/作者/商品/…</c> の形で、
/// 利用者が見たいのは商品のフォルダ（ユーザ判断：「このProductNameが選択されたら良さそう」）。
/// 作者のフォルダは同じ作者の別の商品と重なるので、子が1つしか無い段はたどって下りる。
/// <c>Assets</c> の直下で分かれる（頭のフォルダが2つ以上）・<c>Assets</c> の外に入る物があるときは選ばない（ユーザ判断：「複数の時は選択しない」）。
/// </summary>
public static class UnityImportedFolder
{
    private const string AssetsFolder = "Assets";

    /// <summary>
    /// 選ぶフォルダ（<c>Assets/作者/商品</c> のような Unity の中のパス）。選ばないときは null。
    ///
    /// **拡張子でファイルかを見分けず、パスの木で決める。**unitypackage のパスにはフォルダの行（<c>Assets/作者</c>）も混ざり、
    /// 拡張子の無いファイルや、点の入ったフォルダの名前もある。下に何も無い物は、ファイルでも空のフォルダでも「葉」として同じに扱う。
    /// 子が1つでもそれが葉なら、そこで止めて今のフォルダを選ぶ（ファイルが1つだけの物は、そのファイルのあるフォルダ。
    /// ファイルそのものを選ばないのは、選ぶ物をいつもフォルダに揃え、検索をフォルダだけ（<c>t:Folder</c>）に絞れるようにするため）。
    /// </summary>
    public static string? Pick(IEnumerable<string> assetPaths)
    {
        var paths = assetPaths
            .Select(path => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Where(segments => segments.Length > 0)
            .ToList();

        // Packages の下やプロジェクトの直下に入る物が混ざると、入り先は Assets の外にも分かれている
        if (paths.Count == 0
            || paths.Any(segments => !string.Equals(segments[0], AssetsFolder, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var chosen = new List<string> { AssetsFolder };
        while (true)
        {
            var depth = chosen.Count;
            var below = paths.Where(segments => segments.Length > depth).ToList();
            var children = below
                .Select(segments => segments[depth])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (children.Count != 1)
            {
                break;
            }

            // 1つだけの子が葉（その下に何も無い）なら下りない。フォルダを選ぶ
            if (!below.Any(segments => segments.Length > depth + 1))
            {
                break;
            }

            chosen.Add(children[0]);
        }

        return chosen.Count == 1 ? null : string.Join('/', chosen);
    }

    /// <summary>
    /// 送った後に選びに行くか。1件だけ送った時（呼ぶ側が決める）に、設定が入っていて、取り込んだか既に全部入っていた時だけ。
    /// 取り消した・止めた・待ちきれなかった・Unity ごと閉じられた物は、何も入っていないか、どこまで入ったか分からないので選ばない。
    /// </summary>
    /// <param name="enabled">設定「1件だけ送った時、入った物を Unity で選ぶ」。</param>
    /// <param name="opened">取り込み画面を出せた。</param>
    /// <param name="cancelled">取り込み画面で Cancel された。</param>
    /// <param name="problem">止めた・待ちきれなかった・閉じられたときの理由（取り込んだ・既に入っていたなら null）。</param>
    public static bool ShouldSelect(bool enabled, bool opened, bool cancelled, string? problem)
        => enabled && opened && !cancelled && problem is null;
}
