using System.Net;
using System.Net.Http;

namespace Chmonos.App.Tests.Support;

/// <summary>
/// 通信しない作り物の BOOTH。**既定の一式は外へ出ない**（CLAUDE.md）ので、アプリの通信の出口をこれに差し替える。
///
/// 何も教えなければ、どの問い合わせにも「無い」（404）と答える。来た問い合わせは <see cref="Requests"/> に残るので、
/// 「この操作は BOOTH へ行かない」も確かめられる。
/// </summary>
internal sealed class FakeBooth : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<string> _requests = [];
    private readonly Dictionary<string, string> _itemJson = new(StringComparer.Ordinal);

    /// <summary>来た問い合わせの URL（来た順）。</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>その商品IDの JSON（<c>/items/{id}.json</c>）に、名前とショップだけの商品を答えるようにする。</summary>
    /// <param name="images">
    /// 商品の画像の枚数。画像の場所（<see cref="ImageUrl"/>）には 1×1 の PNG を答える。
    /// 画像を取るのは設定で画像を保存するときだけ（試験の既定は切ってある）。
    /// </param>
    public void HasItem(string itemId, string name, string shop = "sample-shop", int images = 0)
    {
        var imageList = string.Join(", ", Enumerable.Range(1, images).Select(index => $$"""{ "original": "{{ImageUrl(itemId, index)}}" }"""));
        lock (_gate)
        {
            _itemJson[itemId] = $$"""
                {
                  "id": {{itemId}},
                  "name": "{{name}}",
                  "shop": { "name": "{{shop}}", "subdomain": "{{shop}}", "url": "https://{{shop}}.booth.pm/" },
                  "images": [{{imageList}}],
                  "variations": [ { "id": 1, "name": null, "price": 100 } ]
                }
                """;
        }
    }

    /// <summary>教えた商品を忘れる（確かめた後に BOOTH から消えた、を作る）。</summary>
    public void LosesItem(string itemId)
    {
        lock (_gate)
        {
            _itemJson.Remove(itemId);
        }
    }

    private readonly Dictionary<string, string[]> _searches = new(StringComparer.Ordinal);

    /// <summary>
    /// BOOTH の検索（<c>/search/{語}</c>）に、教えた商品のカードを答えるようにする。名前とショップは <see cref="HasItem"/> で教えた物。
    /// 0件を作るときは商品を渡さずに呼ぶ。教えていない語には、ほかと同じく 404 を答える。
    /// </summary>
    public void SearchFinds(string query, params string[] itemIds)
    {
        lock (_gate)
        {
            _searches[BoothSearchUrl(query)] = itemIds;
        }
    }

    // 来た問い合わせは Uri.ToString() で比べるので（日本語は戻した形になる）、同じ形にして覚える
    private static string BoothSearchUrl(string query) => new Uri(Core.Booth.BoothClient.SearchUrl(query)).ToString();

    /// <summary>来た検索の数。</summary>
    public int SearchCount => Requests.Count(url => url.StartsWith("https://booth.pm/ja/search/", StringComparison.Ordinal));

    private readonly HashSet<string> _down = new(StringComparer.Ordinal);

    /// <summary>その商品IDの問い合わせに 503 を答える（一時的に届かない。「無い」とは言えない、を作る）。</summary>
    public void IsDown(string itemId)
    {
        lock (_gate)
        {
            _down.Add(itemId);
        }
    }

    public static string ImageUrl(string itemId, int index) => $"https://booth.pximg.net/fake/i/{itemId}/{index}.png";

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private TaskCompletionSource? _held;
    private Func<string, bool>? _holdOnly;

    /// <summary>
    /// 答えを止める。次に <see cref="Release"/> を呼ぶまで、来た問い合わせは答えずに待たせる
    /// （「BOOTH が答えていない間」の画面の守りを確かめるため）。
    /// </summary>
    /// <param name="only">止める問い合わせ（URL で選ぶ）。null なら全部。1件の登録の途中で止め、その間にほかの操作がどう並ぶかを見る。</param>
    public void Hold(Func<string, bool>? only = null)
    {
        lock (_gate)
        {
            _held ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _holdOnly = only;
        }
    }

    /// <summary>止めていた答えを返し始める。</summary>
    public void Release()
    {
        TaskCompletionSource? held;
        lock (_gate)
        {
            held = _held;
            _held = null;
            _holdOnly = null;
        }

        held?.TrySetResult();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 来た順は止めている間も残す（答える前に「問い合わせが来た」を確かめられるように）
        Task? waiting;
        lock (_gate)
        {
            var url = request.RequestUri!.ToString();
            _requests.Add(url);
            waiting = _holdOnly is null || _holdOnly(url) ? _held?.Task : null;
        }

        if (waiting is not null)
        {
            await waiting.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Answer(request);
    }

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        if (url.StartsWith("https://booth.pximg.net/fake/", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) };
        }

        lock (_gate)
        {
            if (_searches.TryGetValue(url, out var ids))
            {
                var cards = string.Concat(ids.Select(id =>
                {
                    var name = _itemJson.TryGetValue(id, out var json)
                        ? System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("name").GetString()
                        : id;
                    return $"""<li class="item-card l-card" data-product-id="{id}" data-product-name="{name}" data-product-brand="">""";
                }));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"<html><body><ul>{cards}</ul></body></html>") };
            }

            if (_down.Any(itemId => url.EndsWith($"/items/{itemId}.json", StringComparison.Ordinal)))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            foreach (var (itemId, json) in _itemJson)
            {
                if (url.EndsWith($"/items/{itemId}.json", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
                }

                // 商品ページ（説明文の取得）。中身は空でよい
                if (url.EndsWith($"/items/{itemId}", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body></body></html>"),
                    };
                }
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
