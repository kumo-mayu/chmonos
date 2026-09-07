using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Booth;

public sealed class H2ExtractionResult
{
    public IReadOnlyList<H2Section> Sections { get; init; } = [];

    /// <summary>表示用に保存する説明コンテナのHTML。取れなければ null。</summary>
    public string? DescriptionHtml { get; init; }

    /// <summary>説明の本文が存在するか。「本文はあるのにセクション0件」の検知に使う。</summary>
    public bool HasDescriptionBody { get; init; }
}

/// <summary>
/// 商品ページのHTMLから説明文をセクション単位で取り出す。
///
/// 足場はTailwindのユーティリティクラス（<c>break-words font-bold ...</c>）ではなく、
/// 意味のある構造クラスに置いている。実ページの構成は次の通り：
/// <c>div.js-market-item-detail-description</c> が商品JSONの description と同じ短い説明を持ち、
/// その後ろに <c>section.shop__text</c> が見出し付きセクションとして並ぶ（両者は兄弟）。
/// 実測2ページで <c>section.shop__text</c> の数とセクション見出しの数が完全に一致し、
/// 商品名の見出しはこれらの外側にあるため混ざらない。
///
/// 実測（25件）では、セクションを持つ商品は19件、うち更新履歴系の見出しを持つのは6件だった。
/// 見出しは出品者の自由記述で装飾記号が付くため、判定前に <see cref="NormalizeHeading"/> で正規化する。
/// </summary>
public static class H2SectionExtractor
{
    /// <summary>商品JSONの description と同じ内容が入る短い説明のブロック。</summary>
    private const string ShortDescriptionSelector = "div.js-market-item-detail-description";

    /// <summary>見出し付きセクション。説明文の本体はこちらにある。</summary>
    private const string SectionSelector = "section.shop__text";

    /// <summary>説明文が置かれる列。セクションの探索範囲をここに絞れる場合は絞る。</summary>
    private const string ColumnSelector = "section.main-info-column";

    /// <summary>見出しの前後に付く装飾記号。丸括弧は本文の一部であることが多いので含めない。</summary>
    private static readonly char[] DecorationCharacters =
    [
        '●', '○', '◆', '◇', '■', '□', '▲', '△', '▼', '▽', '★', '☆', '◈', '◎', '〇',
        '▶', '◀', '▷', '◁', '►', '◄', '・', '•',
        '＋', '+', '＝', '=', '-', '−', '―', '─', '━', '～', '~', '〜',
        '｜', '|', '/', '\\', '＊', '*', '_', '＿',
        '【', '】', '≪', '≫', '《', '》', '＜', '＞', '<', '>',
        '※', '▌', '▍', '▎', '｛', '｝', '#', '＃',
        '：', ':', '、', '。', '　', ' ', '\t',
    ];

    /// <summary>更新履歴を表す見出しに現れる語。表記の揺れを吸収するため複数持つ。</summary>
    private static readonly string[] UpdateHistoryKeywords =
    [
        "アップデート", "更新", "変更履歴", "履歴", "update", "changelog", "change log",
    ];

    public static H2ExtractionResult Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new H2ExtractionResult();
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        var shortDescription = document.QuerySelector(ShortDescriptionSelector);

        // 説明文の列に絞れれば絞る。取れなければ文書全体から拾う（構造が変わっても止まらないように）。
        var sectionSource = (IParentNode?)document.QuerySelector(ColumnSelector) ?? document;
        var sectionElements = sectionSource.QuerySelectorAll(SectionSelector).ToList();
        if (sectionElements.Count == 0 && !ReferenceEquals(sectionSource, document))
        {
            sectionElements = document.QuerySelectorAll(SectionSelector).ToList();
        }

        var sections = new List<H2Section>();
        foreach (var element in sectionElements)
        {
            var headingElement = element.QuerySelector("h2");
            if (headingElement is null)
            {
                continue;
            }

            var heading = CollapseWhitespace(headingElement.TextContent);
            var body = ExtractBody(element, headingElement);

            // 見出しが空のセクションは実在する（見出しタグはあるが中身が空）。本文があるなら残す。
            if (heading.Length == 0 && body.Length == 0)
            {
                continue;
            }

            sections.Add(new H2Section
            {
                Heading = heading,
                NormalizedHeading = NormalizeHeading(heading),
                Text = body,
            });
        }

        var shortDescriptionText = shortDescription is null
            ? string.Empty
            : CollapseWhitespace(shortDescription.TextContent);

        // 表示用のHTMLは、短い説明とセクションだけを自分で組み立てる。
        // 列（main-info-column）ごと保存すると価格表示などの無関係な要素まで抱き込むため。
        var displayParts = new List<string>();
        if (shortDescription is not null)
        {
            displayParts.Add(shortDescription.OuterHtml);
        }

        displayParts.AddRange(sectionElements.Select(element => element.OuterHtml));

        return new H2ExtractionResult
        {
            Sections = sections,
            DescriptionHtml = displayParts.Count > 0 ? string.Join('\n', displayParts) : null,
            HasDescriptionBody = shortDescriptionText.Length > 0 || sections.Count > 0,
        };
    }

    /// <summary>
    /// 見出しから前後の装飾記号と空白を取り除く。「◈アップデート履歴◈」→「アップデート履歴」。
    /// 表示にはこの結果ではなく元の見出しを使う（あくまで判定用）。
    /// </summary>
    public static string NormalizeHeading(string heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return string.Empty;
        }

        return CollapseWhitespace(heading).Trim(DecorationCharacters).Trim();
    }

    /// <summary>更新履歴に相当するセクションか。ここに当たる変化だけを強い通知として扱う。</summary>
    public static bool IsUpdateHistoryHeading(string heading)
    {
        var normalized = NormalizeHeading(heading);
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var keyword in UpdateHistoryKeywords)
        {
            if (normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractBody(IElement section, IElement headingElement)
    {
        var builder = new StringBuilder();
        foreach (var child in section.Children)
        {
            if (ReferenceEquals(child, headingElement))
            {
                continue;
            }

            var text = child.TextContent.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        return NormalizeLineEndings(builder.ToString());
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;

        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\t' or ' ' or '　')
            {
                previousWasSpace = true;
                continue;
            }

            if (previousWasSpace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            previousWasSpace = false;
            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static string NormalizeLineEndings(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').Select(line => line.TrimEnd());
        return string.Join('\n', lines).Trim();
    }
}
