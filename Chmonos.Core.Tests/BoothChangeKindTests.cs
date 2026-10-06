using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>知らせの差の欄から、検索の条件「更新通知あり」の5つの種類を見分ける（ユーザ判断 2026-10-06）。</summary>
public class BoothChangeKindTests
{
    private static NotificationRecord Record(bool isStrong, params string[] fields) => new()
    {
        Id = "n",
        Kind = NotificationKind.ItemUpdated,
        Title = "作り物",
        Detail = string.Empty,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IsStrong = isStrong,
        Diffs = fields.Select(field => new NotificationDiff { Field = field }).ToList(),
    };

    [Theory]
    [InlineData(BoothChanges.SaleField, BoothChangeKind.Sale)]
    [InlineData(BoothChanges.PriceField, BoothChangeKind.Price)]
    [InlineData(BoothChanges.VariationsField, BoothChangeKind.Variations)]
    [InlineData(BoothChanges.NameField, BoothChangeKind.Page)]
    [InlineData(BoothChanges.ImagesField, BoothChangeKind.Page)]
    [InlineData(BoothChanges.DescriptionField, BoothChangeKind.Page)]
    [InlineData("使い方", BoothChangeKind.Page)]
    [InlineData("更新履歴", BoothChangeKind.Content)]
    public void 差の欄の名前で種類を見分ける(string field, BoothChangeKind expected)
        => Assert.Equal(expected, BoothChanges.KindsOf(Record(false, field)));

    [Fact]
    public void 複数の欄が変われば種類を重ね_強い知らせは差が無くても中身の更新_ほかの差の無い知らせはページ内容の変更()
    {
        Assert.Equal(BoothChangeKind.Price | BoothChangeKind.Page, BoothChanges.KindsOf(Record(false, BoothChanges.PriceField, "使い方")));
        Assert.Equal(BoothChangeKind.Content, BoothChanges.KindsOf(Record(true)));
        Assert.Equal(BoothChangeKind.Page, BoothChanges.KindsOf(Record(false)));
    }
}
