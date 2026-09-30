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
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    /// <summary>
    /// 一時ファイルの名前は毎回違う。固定だと、同じ商品へ2本が同時に書いたときに
    /// 後から来た方が弾かれて保存ごと落ちていた（書き込み中のファイルは共有しない設定のため）。
    /// </summary>
    [Fact]
    public async Task WritesTheSameFileFromSeveralTasksWithoutFailing()
    {
        Directory.CreateDirectory(_directory);

        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => JsonStore.WriteAsync(FilePath, CreateItem()))));

        Assert.Equal("フリルニットセット", JsonStore.Read<ItemRecord>(FilePath)!.Booth.Name);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    /// <summary>
    /// **読んでいる最中の保存が落ちない。**前は読むのに削除の共有を許さない開き方をしていたので、
    /// 同時の保存の置き換えがアクセス拒否で落ちていた。読んでいた側は開いた時点の中身を読み切れる。
    /// </summary>
    [Fact]
    public async Task SavesWhileSomeoneIsReading()
    {
        JsonStore.Write(FilePath, CreateItem());

        await using (var reading = JsonStore.OpenShared(FilePath))
        {
            await JsonStore.WriteAsync(FilePath, CreateItem() with { Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow, Name = "更新後" } });
            JsonStore.Write(FilePath, CreateItem() with { Booth = new BoothBlock { FetchedAt = DateTimeOffset.UtcNow, Name = "もう一度" } });

            var old = await JsonSerializer.DeserializeAsync<ItemRecord>(reading, JsonStore.Options);
            Assert.Equal("フリルニットセット", old!.Booth.Name);
        }

        Assert.Equal("もう一度", JsonStore.Read<ItemRecord>(FilePath)!.Booth.Name);
    }

    /// <summary>
    /// アプリの外の読み手（セキュリティソフトなど）が削除の共有を許さずに少しだけ握っていても、
    /// 置き換えを少し待ってやり直すので保存ごと落とさない。
    /// </summary>
    [Fact]
    public async Task RetriesReplacingWhileAnOutsideReaderBrieflyHoldsTheFile()
    {
        JsonStore.Write(FilePath, CreateItem());

        var outside = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = Task.Run(async () =>
        {
            await Task.Delay(40);
            await outside.DisposeAsync();
        });

        await JsonStore.WriteTextAsync(FilePath, "{}");
        await release;

        Assert.Equal("{}", await JsonStore.ReadTextAsync(FilePath));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    /// <summary>
    /// 手で書いた <c>null</c> の配列は空として受ける（読んだ瞬間ではなく、後から画面が触って落ちていた）。
    /// </summary>
    [Fact]
    public void ReadsNullArraysAsEmpty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              "id": "123",
              "booth": { "fetchedAt": "2026-09-05T10:00:00+00:00", "name": "テスト", "tags": null, "images": null },
              "local": { "userTags": null, "attributes": null, "localFiles": null, "avatars": null }
            }
            """);

        var loaded = JsonStore.Read<ItemRecord>(FilePath);

        Assert.NotNull(loaded);
        Assert.Empty(loaded.Booth.Tags);
        Assert.Empty(loaded.Booth.Images);
        Assert.Empty(loaded.Local.UserTags);
        Assert.Empty(loaded.Local.Attributes);
        Assert.Empty(loaded.Local.LocalFiles);
        Assert.Empty(loaded.Local.Avatars);
    }

    /// <summary>
    /// <c>?</c> を付けた欄は別。「まだ調べていない」と「調べて0件だった」を区別している。
    /// </summary>
    [Fact]
    public void KeepsNullForFieldsThatAllowIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              "id": "123",
              "booth": { "fetchedAt": "2026-09-05T10:00:00+00:00", "name": "テスト" },
              "local": { "localFiles": [ { "hash": "abc", "sizeBytes": 12, "paths": ["a.zip"], "unityPackages": null } ] }
            }
            """);

        var loaded = JsonStore.Read<ItemRecord>(FilePath);

        Assert.Null(loaded!.Local.LocalFiles[0].UnityPackages);
    }

    [Fact]
    public void ComputedPropertiesAreNotPersisted()
    {
        JsonStore.Write(FilePath, CreateItem());

        var text = File.ReadAllText(FilePath);

        Assert.DoesNotContain("isDownloaded", text);
        Assert.DoesNotContain("logicalSizeBytes", text);
        Assert.DoesNotContain("hasBrokenArchive", text);
    }

    /// <summary>
    /// 置き換えが本体を退けた後で一時ファイルを据えられなかった（1176・1177）とき、**一時ファイルを消さずに本体の場所へ据える。**
    /// 前は一時ファイルまで片付けていて、その JSON は丸ごと消えていた。
    /// </summary>
    [Theory]
    [InlineData(1176)]
    [InlineData(1177)]
    public void PutsTheNewContentInPlaceWhenReplaceStrandsIt(int error)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "古い");
        var temporary = FilePath + ".x.tmp";
        File.WriteAllText(temporary, "新しい");

        JsonStore.MoveOver(temporary, FilePath, (_, target) =>
        {
            // ReplaceFile が本体を退けたところで失敗した形（1177 なら別の名前に残るが、本体の名前は空く）
            File.Move(target, target + ".退けた");
            throw new IOException("置き換えに失敗", unchecked((int)0x80070000) | error);
        });

        Assert.Equal("新しい", File.ReadAllText(FilePath));
        Assert.False(File.Exists(temporary));
    }

    /// <summary>それ以外の失敗は今までどおり投げる（据え直しで隠さない）。</summary>
    [Fact]
    public void OtherReplaceFailuresStillThrow()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "古い");
        var temporary = FilePath + ".y.tmp";
        File.WriteAllText(temporary, "新しい");

        Assert.Throws<IOException>(() => JsonStore.MoveOver(
            temporary, FilePath, (_, _) => throw new IOException("別の失敗", unchecked((int)0x80070000) | 5)));

        Assert.Equal("古い", File.ReadAllText(FilePath));
        Assert.True(File.Exists(temporary));
    }
}
