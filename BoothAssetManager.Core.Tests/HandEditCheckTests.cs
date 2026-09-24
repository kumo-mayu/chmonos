using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 手で直した JSON の食い違いを見つけて知らせる（J2・L6）。
/// **こちらから直さない**——公開前は、合っていないデータの方を問題にして直し方を聞く。
/// </summary>
public class HandEditCheckTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;

    public HandEditCheckTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-handedit-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task 食い違いが無ければ何も言わない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "衣装" }] });

        Assert.Empty(await HandEditCheck.FindAsync(_store));
    }

    [Fact]
    public async Task 同じ名前の大分類を見つける()
    {
        // 大文字小文字・かなの違いも同じ鍵として見る（照合がそうなっているので、画面はここで落ちていた）
        await _store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "衣装" }],
        });

        var issue = Assert.Single(await HandEditCheck.FindAsync(_store));
        Assert.Equal("userTags.json", issue.Where);
        Assert.Contains("衣装", issue.What);
    }

    [Fact]
    public async Task 登録簿の同じ商品IDを見つける()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "111" }, new AvatarRegistryEntry { ItemId = "111" }],
        });

        Assert.Contains(await HandEditCheck.FindAsync(_store), issue => issue.Where == "avatar-registry.json");
    }

    /// <summary>
    /// ファイル名と中の商品IDがずれていると、**以後その商品への保存が別のファイルに書かれる**（L6）。
    /// </summary>
    [Fact]
    public async Task ファイル名と商品IDのずれを見つける()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "テスト" },
        });

        // 手で id だけを書き換えた形にする
        var path = _store.Paths.ItemFile("111");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\"111\"", "\"222\""));

        var issue = Assert.Single(await HandEditCheck.FindAsync(_store));
        Assert.Equal("items/", issue.Where);
        Assert.Contains("222", issue.What);
    }

    /// <summary>同じ鍵が2つあっても、索引を作る所で落ちない（先に書いてある方を残す）。</summary>
    [Fact]
    public void 同じ鍵が2つあっても索引は作れる()
    {
        var entries = new[]
        {
            new AvatarRegistryEntry { ItemId = "111", DisplayName = "先" },
            new AvatarRegistryEntry { ItemId = "111", DisplayName = "後" },
        };

        var map = FirstWins.Map(entries, entry => entry.ItemId, StringComparer.Ordinal);

        Assert.Equal("先", Assert.Single(map).Value.DisplayName);
    }

    /// <summary>
    /// 同じ食い違いのまま確かめ直しても、知らせを作り直さない（既読が未読に戻らない）。
    /// 案内の文を足す前の文で比べていたので一致せず、確かめるたびに新しい知らせとして戻っていた。
    /// </summary>
    [Fact]
    public async Task 同じ食い違いのままなら知らせを作り直さない()
    {
        await _store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "衣装" }, new UserTagTop { Name = "衣装" }],
        });
        var notifications = new NotificationService(_store);

        Assert.Equal(1, await notifications.DetectHandEditIssuesAsync());
        var first = Assert.Single(_store.Notifications.Load()!, record => record.Id == "hand-edit");
        await _store.Notifications.SaveAsync([first with { IsRead = true }]);

        Assert.Equal(0, await notifications.DetectHandEditIssuesAsync());
        var again = Assert.Single(_store.Notifications.Load()!, record => record.Id == "hand-edit");
        Assert.True(again.IsRead);
        Assert.Equal(first.CreatedAt, again.CreatedAt);
    }
}
