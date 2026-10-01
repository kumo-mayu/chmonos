using System.Text.RegularExpressions;
using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>商品に載っている YouTube の動画1本。</summary>
public sealed record VideoLink(string VideoId, string Url)
{
    /// <summary>動画の絵。動画の ID から決まる URL で、鍵も問い合わせも要らない（開いたときに画面が読みに行く）。</summary>
    public string ThumbnailUrl => $"https://i.ytimg.com/vi/{VideoId}/mqdefault.jpg";
}

/// <summary>
/// 商品に載っている YouTube の動画を拾う（ユーザ指示 2026-09-14：商品ページに動画の欄を作ってリンクを出す）。
///
/// 見る所は3つ：商品 JSON の <c>embeds</c>（埋め込みの HTML）・短い説明・見出しごとの本文。
/// 出品者は埋め込みを使わず本文に URL を貼ることが多い（確認用の保存先15件では、埋め込み0件・本文に2件）。
/// 同じ動画は1本にまとめ、見つけた順に並べる。URL は watch の形に揃える（再生・共有の形がどれでも同じ動画へ行く）。
/// </summary>
public static partial class VideoLinks
{
    /// <summary>
    /// watch?v=ID（間の引数・HTML の &amp;amp; を許す）・youtu.be/ID・embed/ID・shorts/ID・live/ID・v/ID。
    /// youtube-nocookie.com・m.youtube.com も同じ。ID は11文字
    /// </summary>
    [GeneratedRegex(
        @"(?:https?:)?//(?:www\.|m\.)?(?:youtube(?:-nocookie)?\.com/(?:watch\?(?:[^\s""'<>]*?[&;])?v=|embed/|shorts/|live/|v/)|youtu\.be/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])",
        RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static IReadOnlyList<VideoLink> Find(BoothBlock booth)
    {
        var texts = booth.Embeds
            .Append(booth.Description ?? string.Empty)
            .Concat(booth.H2Sections.Select(section => section.Text));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<VideoLink>();
        foreach (var text in texts)
        {
            foreach (Match match in Pattern().Matches(text))
            {
                var id = match.Groups[1].Value;
                if (seen.Add(id))
                {
                    links.Add(new VideoLink(id, $"https://www.youtube.com/watch?v={id}"));
                }
            }
        }

        return links;
    }
}
