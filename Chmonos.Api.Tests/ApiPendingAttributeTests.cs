using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Api.Tests;

/// <summary>
/// 外から使う API の属性の書き込みと、付いているが値はまだの属性（ユーザ判断 2026-11-20）。
/// 画面のまとめて編集と同じく、値を書けば値はまだから外れ、null は値はまだも外す。
/// </summary>
public sealed class ApiPendingAttributeTests
{
    [Fact]
    public void 値を書くと値はまだから外れ_nullは値はまだも外す()
    {
        var local = new LocalBlock { PendingAttributes = ["可愛さ", "派手さ", "落ち着き"] };

        var written = ApiClassify.ApplyAttributes(local, [("可愛さ", 0), ("派手さ", null)]);

        Assert.Equal(0, written.Attributes["可愛さ"]);
        Assert.False(written.Attributes.ContainsKey("派手さ"));
        Assert.Equal(["落ち着き"], written.PendingAttributes!);
    }

    /// <summary>
    /// 商品の答えに値はまだの名前を出す（欄は <c>pendingAttributes</c>）。<c>attributes</c> には入れず、0 と見分けが付く。
    /// 持たない商品には欄を出さない（前の版の答えと形が変わらない）。
    /// </summary>
    [Fact]
    public async Task 商品の答えは_値はまだの名前を別の欄で返し_持たない商品には欄を出さない()
    {
        const string token = "test-token-0123456789abcdef";
        var root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DataStore(new AppPaths(root));
            await store.Items.SaveAsync(new ItemRecord
            {
                Id = "9610001",
                Booth = new BoothBlock { Name = "作り物A" },
                Local = new LocalBlock { Attributes = new Dictionary<string, int> { ["可愛さ"] = 0 }, PendingAttributes = ["派手さ"] },
            });
            await store.Items.SaveAsync(new ItemRecord { Id = "9610002", Booth = new BoothBlock { Name = "作り物B" } });

            var (app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(root, 0, token, []));
            await app.StartAsync();
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(app.Urls.Single()).Port}") };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var with = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/v1/items/9610001")).RootElement;
                Assert.Equal(0, with.GetProperty("attributes").GetProperty("可愛さ").GetInt32());
                Assert.False(with.GetProperty("attributes").TryGetProperty("派手さ", out _));
                Assert.Equal("派手さ", Assert.Single(with.GetProperty("pendingAttributes").EnumerateArray()).GetString());

                var without = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/v1/items/9610002")).RootElement;
                Assert.False(without.TryGetProperty("pendingAttributes", out _));
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
