using System.Net;
using System.Text.RegularExpressions;
using BoothZipInspector;

namespace Chmonos.Core.Services;

/// <summary>
/// ブラウザで何行かを選んでコピーした（ドラッグした）物から、商品IDを全部拾う（計画 26・ユーザ判断 2026-11-11）。
///
/// 拾うのは**商品ページの URL だけ**。ショップの URL・商品の画像の URL・配布の置き場・購入履歴のダウンロードの行は数えない
/// （まとめて登録する候補に、ページの絵や注文の行が商品として並ばないように）。
///
/// 素の文と CF_HTML（ブラウザが置く HTML）の両方を見る。ショップの一覧・検索の結果・購入履歴は、
/// 画面に見える文に URL が無く、商品はリンク先（<c>href</c>）にしか無いので、素の文だけでは拾えない。
/// </summary>
public static class PastedItemIds
{
    private const string StartFragment = "<!--StartFragment-->";

    /// <summary>
    /// 素の文と CF_HTML から、商品IDを出てきた順に重ねずに返す。
    ///
    /// **HTML のリンク先を先に並べ、素の文にだけある物を後ろに足す。**HTML のリンクは文書の順に並ぶので、
    /// 一覧や購入履歴をコピーしたとき、ページで見た順のまま一覧に出る。素の文は、リンクの字（商品名）しか持たないことが多く、
    /// 素の文を先にすると、説明文に URL がそのまま書かれた商品だけが前に寄る。
    /// 素の文にだけある物は、リンクにしていない URL（HTML の無いアプリからのコピー）。
    /// </summary>
    public static IReadOnlyList<string> Collect(string? text, string? cfHtml)
    {
        var found = new List<string>();
        foreach (var id in LinkTargets(cfHtml).Select(BoothUrlExtractor.TryExtractItemPageId)
            .Concat(BoothUrlExtractor.FindItemPageIdsIn(text)))
        {
            if (id is not null && !found.Contains(id))
            {
                found.Add(id);
            }
        }

        return found;
    }

    /// <summary>
    /// CF_HTML の <c>&lt;a&gt;</c> のリンク先（<c>href</c>）を、文書の順に返す。
    ///
    /// 見出し（<c>SourceURL:</c>＝コピーした場所のページ）は落としてから読む。読むと、コピーした場所のページの商品が混ざる。
    /// <c>&lt;img src&gt;</c> などリンクでない URL は読まない。
    /// 相対のリンク（<c>/ja/items/123</c>）は拾わない：補う元の URL は見出しにしか無い。ブラウザはコピーのときに絶対の URL へ直して置くと見込んでいる（2026-11-11 時点で実機では未確認）。
    /// </summary>
    public static IReadOnlyList<string> LinkTargets(string? cfHtml)
    {
        if (string.IsNullOrWhiteSpace(cfHtml))
        {
            return [];
        }

        return AnchorHrefRegex.Matches(StripHtmlHeader(cfHtml))
            .Select(match => WebUtility.HtmlDecode(
                match.Groups["dq"].Success ? match.Groups["dq"].Value
                : match.Groups["sq"].Success ? match.Groups["sq"].Value
                : match.Groups["bare"].Value).Trim())
            .Where(href => href.Length > 0)
            .ToList();
    }

    /// <summary>
    /// CF_HTML の見出しを落として本体だけにする。
    ///
    /// 見出しには <c>SourceURL:</c>（掴んだ場所のページURL）が入っている。
    /// 落とさないと、掴んだリンクより先にそちらが当たり、**別の商品を開く**。
    /// </summary>
    public static string StripHtmlHeader(string html)
    {
        var start = html.IndexOf(StartFragment, StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            return html[(start + StartFragment.Length)..];
        }

        var body = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        return body >= 0 ? html[body..] : html;
    }

    // <a ...> の中の href だけ。引用符は " ' 無しのどれでもよい（ブラウザが書くのは " だが、手で書いた HTML も来る）。
    // 属性の名前の前は空白に限る（data-href などを拾わない）
    private static readonly Regex AnchorHrefRegex = new(
        @"<a\b[^>]*?\shref\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)'|(?<bare>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
