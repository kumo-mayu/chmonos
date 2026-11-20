using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 配った版の settings.json に残った、今は無い項目（<c>showAcquiredSortDividers</c>。2026-11-20 に区切りの設定を1つにまとめて無くした）を読んでも、
/// 設定全体が既定へ戻らず、ほかの値（使う人の調整）が残る。古い値は引き継がず、全体の <c>showSortDividers</c> だけで決める。
/// </summary>
public sealed class SettingsLegacyKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-legacykey-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 無くなった入手日の区切りの項目が残っていても_ほかの値は読め_古い値は引き継がない(bool legacyAcquired, bool dividers)
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        File.WriteAllText(
            paths.SettingsFile,
            $$"""{ "cardWidth": 321, "showSortDividers": {{(dividers ? "true" : "false")}}, "showAcquiredSortDividers": {{(legacyAcquired ? "true" : "false")}} }""");

        var settings = new DataStore(paths).Settings.Load();

        Assert.Equal(321, settings.CardWidth);
        Assert.Equal(dividers, settings.ShowSortDividers);
    }
}
