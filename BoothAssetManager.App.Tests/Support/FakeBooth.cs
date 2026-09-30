using System.Net;
using System.Net.Http;

namespace BoothAssetManager.App.Tests.Support;

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
    public void HasItem(string itemId, string name, string shop = "sample-shop")
    {
        lock (_gate)
        {
            _itemJson[itemId] = $$"""
                {
                  "id": {{itemId}},
                  "name": "{{name}}",
                  "shop": { "name": "{{shop}}", "subdomain": "{{shop}}", "url": "https://{{shop}}.booth.pm/" },
                  "images": [],
                  "variations": [ { "id": 1, "name": null, "price": 100 } ]
                }
                """;
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        lock (_gate)
        {
            _requests.Add(url);

            foreach (var (itemId, json) in _itemJson)
            {
                if (url.EndsWith($"/items/{itemId}.json", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
                }

                // 商品ページ（説明文の取得）。中身は空でよい
                if (url.EndsWith($"/items/{itemId}", StringComparison.Ordinal))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body></body></html>"),
                    });
                }
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
