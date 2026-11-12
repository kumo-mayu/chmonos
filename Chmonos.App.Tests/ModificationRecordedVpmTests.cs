using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>改変の詳細の「記録した時のVPM」の出し分け（ユーザ判断 2026-11-12。docs/spec/unity-vpm.md「改変に控える」）。</summary>
public sealed class ModificationRecordedVpmTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 9, 25, 21, 0, 0, TimeSpan.FromHours(9));

    private static ModificationRecord Record(string? project, IReadOnlyList<ModificationVpmSnapshot?>? snapshots = null, DateTimeOffset? addedAt = null) => new()
    {
        Id = "mod-00000001",
        AvatarItemId = "9900201",
        Name = "夏の普段着",
        UnityProject = project,
        Members = [new ModificationMember { ItemId = "9900212", AddedAt = addedAt ?? Recorded }],
        VpmSnapshots = snapshots,
    };

    private static ModificationVpmSnapshot Snapshot(DateTimeOffset at, params ModificationVpmPackage[] packages) => new() { At = at, Packages = packages };

    private static VpmPackage Now(string name, string? display, string? version) => new(name, display, version, name, InManifest: true);

    private static VpmHistoryEntry At(string day, string? version, bool removed = false)
        => new(DateTimeOffset.Parse($"{day}T21:00:00+09:00", System.Globalization.CultureInfo.InvariantCulture), version, removed);

    [Fact]
    public void 最後の写しを出し_今と違う版_外した物_後から入った物に添え書きを付ける()
    {
        var record = Record(@"D:\Unity\Minato",
        [
            Snapshot(Recorded.AddDays(-10), new ModificationVpmPackage("com.example.shader", "作り物トゥーン", "2.3.1")),
            Snapshot(Recorded,
                new ModificationVpmPackage("com.example.shader", "作り物トゥーン", "2.3.2"),
                new ModificationVpmPackage("com.example.avatars", "作り物 SDK", "3.10.4"),
                new ModificationVpmPackage("com.example.oldtool", null, "1.4.0")),
        ]);

        var view = RecordedVpmView.Build(record,
        [
            Now("com.example.shader", "作り物トゥーン", "2.3.3"),
            Now("com.example.avatars", "作り物 SDK", "3.10.4"),
            Now("com.example.framework", "作り物フレームワーク", "1.13.1"),
        ], history: null);

        Assert.True(view.IsVisible);
        Assert.Equal("2026-09-25から", view.SinceText);
        Assert.Null(view.Caption);
        Assert.Equal(
            ["com.example.oldtool 1.4.0|今は入っていません", "作り物 SDK 3.10.4|", "作り物トゥーン 2.3.2|今は 2.3.3", "作り物フレームワーク 1.13.1|記録の後に入りました"],
            view.Rows.Select(row => $"{row.Text}|{row.NowText}"));
    }

    [Fact]
    public void 今が分からなければ比べない_版の読めない物は違うと言わない()
    {
        var record = Record(@"D:\Unity\Minato", [Snapshot(Recorded, new ModificationVpmPackage("a.b", null, null), new ModificationVpmPackage("c.d", null, "1.0"))]);

        Assert.All(RecordedVpmView.Build(record, now: null, history: null).Rows, row => Assert.False(row.Differs));
        Assert.All(RecordedVpmView.Build(record, [Now("a.b", null, "2.0"), Now("c.d", null, null)], history: null).Rows, row => Assert.False(row.Differs));
    }

    [Fact]
    public void 写しが空なら入っていなかったと言う()
    {
        var view = RecordedVpmView.Build(Record(@"D:\Unity\Minato", [Snapshot(Recorded)]), now: [], history: null);

        Assert.False(view.HasRows);
        Assert.Equal("記録した時は、VPMが入っていませんでした。", view.EmptyText);
    }

    [Fact]
    public void プロジェクトも写しも無ければ欄を出さない_写しがあればプロジェクトを外しても出す()
    {
        Assert.False(RecordedVpmView.Build(Record(null), now: null, history: null).IsVisible);
        Assert.True(RecordedVpmView.Build(Record(null, [Snapshot(Recorded, new ModificationVpmPackage("a.b", null, "1"))]), now: null, history: null).IsVisible);
    }

    [Fact]
    public void 写しの無い改変は_履歴から最後に足した時の姿を見つけた記録として出す()
    {
        IReadOnlyList<VpmPackageHistory> history =
        [
            new("com.example.shader", "作り物トゥーン", [At("2026-09-20", "2.3.2"), At("2026-11-02", "2.3.3")]),
            new("com.example.framework", "作り物フレームワーク", [At("2026-10-15", "1.13.1")]),
        ];

        var view = RecordedVpmView.Build(Record(@"D:\Unity\Minato"), [Now("com.example.shader", "作り物トゥーン", "2.3.3")], history);

        Assert.Null(view.SinceText);
        Assert.Equal("Chmonosが見つけたVPMの記録から、2026-09-25ごろの内容を表示しています。", view.Caption);
        Assert.Equal(["作り物トゥーン 2.3.2|今は 2.3.3"], view.Rows.Select(row => $"{row.Text}|{row.NowText}"));
    }

    [Fact]
    public void 履歴が記録より後から始まるなら補わずに残すと言う()
    {
        IReadOnlyList<VpmPackageHistory> history = [new("com.example.shader", "作り物トゥーン", [At("2026-11-02", "2.3.3")])];

        var view = RecordedVpmView.Build(Record(@"D:\Unity\Minato"), [Now("com.example.shader", "作り物トゥーン", "2.3.3")], history);

        Assert.False(view.HasRows);
        Assert.Null(view.Caption);
        Assert.Equal("使ったものを記録すると、その時のVPMも残します。", view.EmptyText);
    }
}
