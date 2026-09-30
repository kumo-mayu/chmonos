using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 要確認の行の既読の丸の、読み上げ・自動操作の名前。丸は行ごとに並ぶので、どの行の物かと、今押すと起きることを言う
/// （ユーザ判断 2026-09-30。Tab で止まれるようにしたので、止まったときに何の丸かが読まれる）。
/// Tab で止まること・Enter と Space で切り替わることは <c>experiments/PeerProbe -- focus</c> で確かめる。
/// </summary>
public class InboxReadNameTests
{
    [Fact]
    public void 未読の行の丸は_行の題と_既読にする_を言う()
        => Assert.Equal("作り物の衣装が更新されましたを既読にする", NotificationRow.ReadNameFor("作り物の衣装が更新されました", isRead: false));

    [Fact]
    public void 既読の行の丸は_行の題と_未読に戻す_を言う()
        => Assert.Equal("作り物の衣装が更新されましたを未読に戻す", NotificationRow.ReadNameFor("作り物の衣装が更新されました", isRead: true));

    [Fact]
    public void 丸を押して既読が替わると_名前も替わったと知らせる()
    {
        var row = new NotificationRow
        {
            Record = new NotificationRecord
            {
                Id = "n1",
                Kind = NotificationKind.ItemUpdated,
                Title = "作り物の知らせ",
                Detail = string.Empty,
                CreatedAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9)),
            },
            KindText = "更新",
        };
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.Equal("作り物の知らせを既読にする", row.ReadButtonName);

        row.IsRead = true;

        Assert.Equal("作り物の知らせを未読に戻す", row.ReadButtonName);
        Assert.Contains(nameof(NotificationRow.ReadButtonName), changed);
    }
}
