using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 通知の「エクスプローラで開く」（手で直す物の場所。ユーザ指示 2026-11-18）と、同じ日の通知の文言の直し。
/// 開く先は計算で決まるので、押さずに <see cref="InboxViewModel.RevealTarget"/> の答えで確かめる
/// </summary>
public class InboxRevealTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "chmonos-reveal-test");

    private static NotificationRecord Record(NotificationKind kind, string id, string detail = "", bool resolved = false) => new()
    {
        Id = id,
        Kind = kind,
        Title = "作り物",
        Detail = detail,
        CreatedAt = DateTimeOffset.Now,
        IsResolved = resolved,
    };

    [Fact]
    public void 読めない商品の記録は_その商品のJSONを開く先にする()
    {
        var record = Record(NotificationKind.UnreadableItem, "unreadable-item:9900011");

        Assert.Equal(Path.Combine(Root, "items", "9900011.json"), InboxViewModel.RevealTarget(record, Root));
    }

    [Fact]
    public void 解消済みの行と_直す物でない種類には開く先を出さない()
    {
        Assert.Equal(string.Empty, InboxViewModel.RevealTarget(Record(NotificationKind.UnreadableItem, "unreadable-item:9900011", resolved: true), Root));
        Assert.Equal(string.Empty, InboxViewModel.RevealTarget(Record(NotificationKind.HandEditMismatch, "hand-edit", "・userTags.json：x", resolved: true), Root));
        Assert.Equal(string.Empty, InboxViewModel.RevealTarget(Record(NotificationKind.OrphanTag, "orphan"), Root));
    }

    [Fact]
    public void 食い違ったJSONは_本文の最初に挙げたファイルを開く先にする()
    {
        var detail = "・userTags.json：同じ名前の大分類が2つ以上あります：衣装（2 件）\n・attributes.json：同じ名前の属性が2つ以上あります：x\n\nJSONを開いて直してください。";

        Assert.Equal(Path.Combine(Root, "userTags.json"), InboxViewModel.RevealTarget(Record(NotificationKind.HandEditMismatch, "hand-edit", detail), Root));
    }

    [Fact]
    public void 食い違いがフォルダの話なら_そのフォルダを開く先にする()
    {
        var detail = "・items/：ファイル名と中の商品IDが違います：9900011.jsonの中のidが「9900012」";

        Assert.Equal(Path.Combine(Root, "items"), InboxViewModel.RevealTarget(Record(NotificationKind.HandEditMismatch, "hand-edit", detail), Root));
    }

    [Fact]
    public void 本文から場所を読めなければ_保存先のフォルダを開く先にする()
    {
        Assert.Equal(Root, InboxViewModel.RevealTarget(Record(NotificationKind.HandEditMismatch, "hand-edit", "JSONを開いて直してください。"), Root));
    }

    [Fact]
    public Task 上の帯の一言は_設定の件数を超えたら古い既読が保管へ移ると言う() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);

        // 既定は 200 件（AppSettings.NotificationRetentionCount）。「消えません」と言うと、上限を超えた既読が消える動きと食い違う
        Assert.Equal("既読の通知は、200 件を超えると古い方から「保管した通知」へ移ります", inbox.RetentionText);
    });

    [Fact]
    public void 登録し直すの文言は_漢字にそろっている()
    {
        var record = Record(NotificationKind.ArchiveFoundForFolder, "archive-found:x");

        Assert.Equal("zipで登録し直す", InboxViewModel.ActionLabel(record, new HashSet<string>()));
    }
}
