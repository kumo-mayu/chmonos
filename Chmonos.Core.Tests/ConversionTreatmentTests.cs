using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 変換扱いの実質の答え（商品ページの「今の扱い」と、外から使う API の <c>conversion.effectiveMode</c> が同じ関数を使う）。
/// </summary>
public class ConversionTreatmentTests
{
    [Theory]
    [InlineData(null, true, ConversionTreatment.AutoProfile)]
    [InlineData(null, false, ConversionTreatment.AutoNotProfile)]
    [InlineData(ConversionMode.Auto, true, ConversionTreatment.AutoProfile)]
    [InlineData(ConversionMode.Auto, false, ConversionTreatment.AutoNotProfile)]
    [InlineData(ConversionMode.Profile, false, ConversionTreatment.ManualProfile)]
    [InlineData(ConversionMode.Profile, true, ConversionTreatment.ManualProfile)]
    [InlineData(ConversionMode.NotProfile, true, ConversionTreatment.ManualNotProfile)]
    [InlineData(ConversionMode.NotProfile, false, ConversionTreatment.ManualNotProfile)]
    public void 人が決めた扱いが自動の判定に勝つ(ConversionMode? mode, bool hasProfileFiles, ConversionTreatment expected)
    {
        var setting = mode is { } decided ? new ConversionSetting { Mode = decided } : null;
        Assert.Equal(expected, ConversionTreatments.Of(setting, hasProfileFiles));
    }

    [Fact]
    public void 扱うかは_どちらで決まったかを問わない()
    {
        Assert.True(ConversionTreatment.AutoProfile.IsProfile());
        Assert.True(ConversionTreatment.ManualProfile.IsProfile());
        Assert.False(ConversionTreatment.AutoNotProfile.IsProfile());
        Assert.False(ConversionTreatment.ManualNotProfile.IsProfile());
    }
}
