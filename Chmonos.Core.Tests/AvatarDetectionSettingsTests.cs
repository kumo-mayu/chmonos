using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 見出し語を既定に足したとき、保存済みの設定を使う人にも届くこと。
///
/// 見出し語の一覧は settings.json に丸ごと保存されるので、既定に語を足しただけでは
/// 今の利用者の検出は古い一覧のまま動く。使うときに既定と合わせる。
/// </summary>
public class AvatarDetectionSettingsTests : IDisposable
{
    private const string AvatarId = "1111";
    private const string ItemId = "999";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public AvatarDetectionSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-headings-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    /// <summary>
    /// **設定から外した見出しは使わない**（ユーザ判断 2026-09-21・G13）。
    ///
    /// 前は既定の語を必ず混ぜていたので、`settings.json` から消しても次の検出で戻り、
    /// 足すことしかできなかった（対になる「読まない見出し」は消せるので、作りが揃っていなかった）。
    /// ここでは「🔍検索用🔍」を含まない一覧を設定にしているので、その見出しの下は拾わない
    /// （出どころは「確定」ではなく、本文中のリンク＝要確認になる）。
    /// </summary>
    [Fact]
    public async Task DoesNotBringBackHeadingsTheUserRemoved()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "オリジナル3Dモデル「マヌカ」",
                    DisplayName = "マヌカ",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                },
            ],
        });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ" },
            Local = new LocalBlock(),
        });

        File.WriteAllText(
            _paths.ItemHtmlFile(ItemId),
            $"<h2>🔍 検索用 🔍</h2><p>https://booth.pm/ja/items/{AvatarId}</p>");

        var withoutSearchHeading = new AppSettings
        {
            AvatarSupportHeadings = ["対応アバター", "対応モデル", "対応リスト", "対応表", "対応一覧", "Supported", "Compatible"],
        };

        await new AvatarService(_store, withoutSearchHeading).DetectAsync();

        var item = await _store.Items.LoadAsync(ItemId);
        var link = Assert.Single(item!.Local.Avatars);
        Assert.Equal(AvatarId, link.AvatarItemId);

        // 宣言の見出しとして読まないので、本文中のリンク（要確認）どまり
        Assert.Equal(AvatarLinkSource.H2Link, link.Source);
        Assert.False(link.Confirmed);
    }

    /// <summary>
    /// 見出しの一覧を丸ごと消した・壊した設定では既定に戻す（何も拾えなくなるより良い）。
    /// 手で書いた `null` は空として受けるので、この道を通る。
    /// </summary>
    [Fact]
    public async Task FallsBackToTheDefaultsWhenTheListIsEmpty()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "オリジナル3Dモデル「マヌカ」",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                },
            ],
        });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ" },
            Local = new LocalBlock(),
        });

        File.WriteAllText(
            _paths.ItemHtmlFile(ItemId),
            $"<h2>🔍 検索用 🔍</h2><p>https://booth.pm/ja/items/{AvatarId}</p>");

        await new AvatarService(_store, new AppSettings { AvatarSupportHeadings = [] }).DetectAsync();

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.Equal(AvatarLinkSource.SupportSection, Assert.Single(item!.Local.Avatars).Source);
    }
}
