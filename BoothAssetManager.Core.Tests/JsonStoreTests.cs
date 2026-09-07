using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class JsonStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"booth-asset-manager-test-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "item.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static ItemRecord CreateItem() => new()
    {
        Id = "5813187",
        Booth = new BoothBlock
        {
            FetchedAt = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero),
            Name = "フリルニットセット",
            Tags = ["衣装", "VRChat"],
        },
        Local = new LocalBlock
        {
            Memo = "袖のフリルが貫通する",
            Attributes = new Dictionary<string, int> { ["かわいい"] = 82 },
        },
    };

    [Fact]
    public void RoundTripsAnItemRecord()
    {
        JsonStore.Write(FilePath, CreateItem());

        var loaded = JsonStore.Read<ItemRecord>(FilePath);

        Assert.NotNull(loaded);
        Assert.Equal("5813187", loaded.Id);
        Assert.Equal("フリルニットセット", loaded.Booth.Name);
        Assert.Equal(82, loaded.Local.Attributes["かわいい"]);
    }

    /// <summary>ユーザがJSONを直接開いて読める必要があるので、日本語をエスケープしない。</summary>
    [Fact]
    public void WritesJapaneseTextWithoutEscaping()
    {
        JsonStore.Write(FilePath, CreateItem());

        var text = File.ReadAllText(FilePath);

        Assert.Contains("フリルニットセット", text);
        Assert.DoesNotContain("\\u", text);
    }

    [Fact]
    public void WritesIndentedJson()
    {
        JsonStore.Write(FilePath, CreateItem());

        var text = File.ReadAllText(FilePath);

        Assert.Contains("\n", text);
    }

    /// <summary>手で編集されたJSONに末尾カンマやコメントが混ざっても読めるようにしている。</summary>
    [Fact]
    public void ReadsHandEditedJsonWithCommentsAndTrailingCommas()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              // 手で足したコメント
              "id": "123",
              "booth": { "fetchedAt": "2026-09-05T10:00:00+00:00", "name": "テスト", },
              "local": {},
            }
            """);

        var loaded = JsonStore.Read<ItemRecord>(FilePath);

        Assert.NotNull(loaded);
        Assert.Equal("123", loaded.Id);
    }

    [Fact]
    public void ReturnsNullWhenFileDoesNotExist()
    {
        Assert.Null(JsonStore.Read<ItemRecord>(FilePath));
    }

    /// <summary>壊れたファイルは黙って握り潰さず、呼び出し側に伝える。</summary>
    [Fact]
    public void ThrowsForCorruptJson()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ this is not json");

        Assert.Throws<JsonException>(() => JsonStore.Read<ItemRecord>(FilePath));
    }

    /// <summary>一時ファイルへ書いてから置き換えるので、既存ファイルが中途半端な状態にならない。</summary>
    [Fact]
    public void ReplacesExistingFileAndLeavesNoTemporaryFile()
    {
        JsonStore.Write(FilePath, CreateItem());

        var updated = new ItemRecord
        {
            Id = "5813187",
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow, Name = "更新後" },
            Local = new LocalBlock(),
        };
        JsonStore.Write(FilePath, updated);

        Assert.Equal("更新後", JsonStore.Read<ItemRecord>(FilePath)!.Booth.Name);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void ComputedPropertiesAreNotPersisted()
    {
        JsonStore.Write(FilePath, CreateItem());

        var text = File.ReadAllText(FilePath);

        Assert.DoesNotContain("isDownloaded", text);
        Assert.DoesNotContain("logicalSizeBytes", text);
    }
}
