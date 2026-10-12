using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Booth;

namespace Chmonos.Core.Resolution;

/// <summary>
/// ショップの商品一覧（<c>&lt;サブドメイン&gt;.booth.pm/items?page=N</c>）を全ページ取り、商品IDと名前を返す（<c>docs/spec/folder-layout.md</c>）。
/// 置き方の指定で分かったショップの中から、商品名のフォルダと同じ名前の商品を探すため。BOOTH 全体の検索は、検索語の割り方や
/// 記号入りの名前（「W.A.R.D」など）で外れるが、一覧を手元で比べればその影響を受けない（research §21：照らせた26本で正解20・外れ1）。
///
/// **起動している間だけ覚える。**同じショップの未確定が並ぶのが普通で、2件目から問い合わせずに済む。
/// 一覧は増えるので、ファイルには残さない（古い一覧で「無い」と言わないように）。
/// </summary>
public sealed partial class ShopItemList(IBoothClient client)
{
    public sealed record Entry(string ItemId, string Name);

    /// <summary>
    /// 1ショップで取るページの上限。1ページ数十件なので千件を超えるショップでも取り切れる。
    /// 一覧の作りが変わって「次のページ」を見誤ったときに、いつまでも取り続けないための止め
    /// </summary>
    public const int MaxPages = 50;

    private readonly ConcurrentDictionary<string, IReadOnlyList<Entry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    // 一覧の各商品は <li class="js-mount-point-shop-item-card" data-item="{…}"> に、id と name を持つ JSON を入れている（2026-10-12 に確かめた）
    [GeneratedRegex("data-item=\"([^\"]*)\"")]
    private static partial Regex CardRegex { get; }

    /// <summary>
    /// 一覧を返す。取れなかったページがあれば null（途中までの一覧で「無い」と言わないように）。覚えるのは取り切れた一覧だけ。
    /// <paramref name="onPage"/> は1ページ取るたびに呼ぶ（進み具合を出すため）。
    /// </summary>
    public async Task<IReadOnlyList<Entry>?> GetAsync(string subdomain, Action<int>? onPage = null, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(subdomain, out var cached))
        {
            return cached;
        }

        var entries = new List<Entry>();
        for (var page = 1; page <= MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onPage?.Invoke(page);
            var answer = await client.GetShopItemsPageAsync(subdomain, page, cancellationToken);
            if (!answer.IsSuccess || answer.Value is not { } html)
            {
                // 1ページ目が「無い」なら、ショップが消えた（名前を変えた）。空の一覧として覚え、同じショップのファイルのたびに聞き直さない。
                // 一時的な失敗は覚えない（次に押せば取れる）
                if (page == 1 && answer.Status == BoothFetchStatus.NotFound)
                {
                    _cache[subdomain] = [];
                    return [];
                }

                return null;
            }

            var cards = Parse(html);
            entries.AddRange(cards);

            // 商品の無いページか、次のページへのリンクが無いページで止める
            if (cards.Count == 0 || !html.Contains($"page={page + 1}", StringComparison.Ordinal))
            {
                break;
            }
        }

        var list = entries.DistinctBy(entry => entry.ItemId).ToList();
        _cache[subdomain] = list;
        return list;
    }

    /// <summary>一覧のページから商品を読む。読めないカードは飛ばす。</summary>
    public static IReadOnlyList<Entry> Parse(string html)
    {
        var entries = new List<Entry>();
        foreach (Match match in CardRegex.Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.String
                    && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    entries.Add(new Entry(id.ToString(), name.GetString() ?? string.Empty));
                }
            }
            catch (JsonException)
            {
                // ほかの部品の data-item かもしれない。読めない物は商品ではない
            }
        }

        return entries;
    }
}
